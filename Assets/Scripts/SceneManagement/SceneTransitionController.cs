using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns swapping one content scene for another while Bootstrap stays loaded, with the view
/// faded out for the whole thing.
///
/// Sequence: fade out -> stop locomotion -> preload (activation held) -> activate ->
/// reposition the player -> unload the old scene -> settle -> fade in -> restore locomotion.
///
/// Deliberately a coroutine rather than async/await. Holding allowSceneActivation false means
/// the AsyncOperation never reaches isDone, so awaiting it would deadlock; coroutines can poll
/// progress, which is exactly what this needs.
///
/// Lives in Bootstrap, next to the rig - anything in a content scene would be unloaded halfway
/// through its own transition.
/// </summary>
[DisallowMultipleComponent]
public class SceneTransitionController : MonoBehaviour
{
    public static SceneTransitionController Instance { get; private set; }

    [Header("References")]
    [Tooltip("Leave empty to use ScreenFader.Instance.")]
    [SerializeField] private ScreenFader fader;

    [Tooltip("The rig root that gets moved - XRPlayerRig. Leave empty to walk up from the camera.")]
    [SerializeField] private Transform rigRoot;

    [Tooltip("The XR camera. Leave empty to use Camera.main.")]
    [SerializeField] private Transform head;

    [Tooltip("Disabled before moving the rig and re-enabled after. A CharacterController will " +
             "otherwise fight the teleport and drag the player back.")]
    [SerializeField] private CharacterController characterController;

    [Header("Input during the transition")]
    [Tooltip("Locomotion providers to switch off while the view is dark. Without this a held " +
             "thumbstick walks the player around blind, and they can arrive facing a fence.")]
    [SerializeField] private MonoBehaviour[] locomotionToSuspend;

    [Header("Timing")]
    [Tooltip("Seconds to fade out. Slower than a flatscreen game would use.")]
    [SerializeField] private float fadeOutDuration = 0.45f;

    [Tooltip("Extra seconds held fully dark after the new scene is ready, so the change never " +
             "feels like a jump cut even when loading was instant.")]
    [SerializeField] private float holdDarkDuration = 0.25f;

    [Tooltip("Seconds to fade back in. Longer than the fade out on purpose - arriving should " +
             "feel gentler than leaving.")]
    [SerializeField] private float fadeInDuration = 0.7f;

    [Tooltip("Frames to wait after activating the scene before fading in, letting the first " +
             "heavy frame and any Start() work land while still hidden.")]
    [SerializeField] private int settleFrames = 3;

    [Header("Audio")]
    [Tooltip("Duck the AudioListener alongside the picture. A sound cutting dead mid-fade is " +
             "jarring on its own. Replace with an AudioMixer snapshot once settings land.")]
    [SerializeField] private bool fadeAudio = true;

    public bool IsTransitioning { get; private set; }

    private void Awake()
    {
        Instance = this;
        ResolveReferences();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void ResolveReferences()
    {
        if (fader == null) fader = ScreenFader.Instance;
        if (fader == null) fader = FindFirstObjectByType<ScreenFader>();

        if (head == null && Camera.main != null) head = Camera.main.transform;

        // Same fallback shape SnapTurnTask uses: Camera -> Camera Offset -> XR Origin.
        if (rigRoot == null && head != null)
        {
            rigRoot = head.parent != null && head.parent.parent != null
                ? head.parent.parent
                : head.root;
        }

        if (characterController == null && rigRoot != null)
        {
            characterController = rigRoot.GetComponent<CharacterController>();
        }
    }

    /// <summary>Swaps to a scene, unloading the one named in <paramref name="unloadSceneName"/>.</summary>
    public void SwitchTo(string loadSceneName, string unloadSceneName)
    {
        if (IsTransitioning)
        {
            Debug.Log("[SceneTransitionController] Already transitioning - request ignored.", this);
            return;
        }
        if (string.IsNullOrEmpty(loadSceneName)) return;

        StartCoroutine(SwitchRoutine(loadSceneName, unloadSceneName));
    }

    private IEnumerator SwitchRoutine(string loadSceneName, string unloadSceneName)
    {
        IsTransitioning = true;

        if (fader == null) ResolveReferences();

        // 1. Hide the world before anything changes.
        if (fader != null) yield return fader.FadeTo(1f, fadeOutDuration);
        float audioFrom = AudioListener.volume;
        if (fadeAudio) AudioListener.volume = 0f;

        // 2. Stop the player moving while they cannot see.
        SetLocomotionEnabled(false);

        // 3. Preload with activation held, so the expensive frame lands inside the darkness
        //    instead of halfway through the fade.
        Scene target = SceneManager.GetSceneByName(loadSceneName);
        if (!target.isLoaded)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(loadSceneName, LoadSceneMode.Additive);
            if (load == null)
            {
                Debug.LogError($"[SceneTransitionController] '{loadSceneName}' could not be loaded. " +
                               "Is it in File > Build Settings?", this);
                SetLocomotionEnabled(true);
                if (fadeAudio) AudioListener.volume = audioFrom;
                if (fader != null) yield return fader.FadeTo(0f, fadeInDuration);
                IsTransitioning = false;
                yield break;
            }

            load.allowSceneActivation = false;
            // Held activation caps progress at 0.9 and never sets isDone, so poll instead.
            while (load.progress < 0.9f) yield return null;

            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;

            target = SceneManager.GetSceneByName(loadSceneName);
        }

        // 4. Active scene drives lighting and skybox, and receives anything instantiated
        //    without an explicit scene.
        if (target.IsValid() && target.isLoaded) SceneManager.SetActiveScene(target);

        // 5. Place the player deliberately, before they can see where they are.
        MoveToSpawnPoint();

        // 6. Drop the old scene only once its replacement is up.
        if (!string.IsNullOrEmpty(unloadSceneName) && unloadSceneName != loadSceneName)
        {
            Scene old = SceneManager.GetSceneByName(unloadSceneName);
            if (old.IsValid() && old.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(old);
                while (unload != null && !unload.isDone) yield return null;
            }
        }

        // 7. Let the first heavy frames pass while still hidden.
        for (int i = 0; i < settleFrames; i++) yield return null;
        if (holdDarkDuration > 0f) yield return new WaitForSecondsRealtime(holdDarkDuration);

        // 8. Reveal, then hand control back - not before, or they can move while half blind.
        if (fadeAudio) AudioListener.volume = audioFrom;
        if (fader != null) yield return fader.FadeTo(0f, fadeInDuration);

        SetLocomotionEnabled(true);
        IsTransitioning = false;
    }

    /// <summary>
    /// Moves the rig so the player's HEAD lands on the spawn point, not the rig root. Those are
    /// different: the player's physical position inside their playspace offsets the camera from
    /// the root, so moving the root alone lands them off-target by however far they had walked.
    /// </summary>
    private void MoveToSpawnPoint()
    {
        SceneSpawnPoint spawn = SceneSpawnPoint.Active;
        if (spawn == null || rigRoot == null || head == null) return;

        bool hadController = characterController != null && characterController.enabled;
        if (hadController) characterController.enabled = false;

        // Yaw first, pivoting about the head so the player's own position does not shift.
        Vector3 currentForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        Vector3 desiredForward = Vector3.ProjectOnPlane(spawn.transform.forward, Vector3.up);
        if (currentForward.sqrMagnitude > 0.0001f && desiredForward.sqrMagnitude > 0.0001f)
        {
            float yaw = Vector3.SignedAngle(currentForward, desiredForward, Vector3.up);
            rigRoot.RotateAround(head.position, Vector3.up, yaw);
        }

        // Then translate: head over the spawn in XZ, rig floor at the spawn's height.
        Vector3 delta = spawn.transform.position - head.position;
        delta.y = spawn.transform.position.y - rigRoot.position.y;
        rigRoot.position += delta;

        if (hadController) characterController.enabled = true;
    }

    private void SetLocomotionEnabled(bool enabled)
    {
        if (locomotionToSuspend == null) return;

        foreach (MonoBehaviour behaviour in locomotionToSuspend)
        {
            if (behaviour != null) behaviour.enabled = enabled;
        }
    }
}
