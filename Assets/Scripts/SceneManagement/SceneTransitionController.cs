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

    [Tooltip("Told to forget its accumulated stride distance whenever the rig is placed at a " +
             "spawn point, so the jump is not heard as footsteps. Leave empty to find it on " +
             "the rig automatically.")]
    [SerializeField] private FootstepAudio footstepAudio;

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

        if (footstepAudio == null && rigRoot != null)
        {
            // May sit on the rig root or on a child (MovementAudio); either is supported.
            footstepAudio = rigRoot.GetComponentInChildren<FootstepAudio>(true);
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

        // 2. Stop the player moving while they cannot see. Held under this controller's own name
        //    as well, as the other two routines do: the main menu releases its walking hold as it
        //    unloads, and without this, walking would come back while the view is still dark.
        SetLocomotionEnabled(false);
        ControllerHandednessManager.Instance?.SuspendLocomotion(this);

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
                ControllerHandednessManager.Instance?.ResumeLocomotion(this);
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

        ControllerHandednessManager.Instance?.ResumeLocomotion(this);
        SetLocomotionEnabled(true);
        IsTransitioning = false;
    }

    /// <summary>
    /// Runs <paramref name="work"/> with the view dark - the same fade out, held darkness, sound
    /// dip and fade in as <see cref="SwitchTo"/>, around whatever loading or moving the caller
    /// needs to do.
    ///
    /// For the module runs, which do not swap scenes the SwitchTo way: the main menu stays loaded
    /// (hidden) and RunSystem is added on top, then later unloaded again. The work runs on this
    /// controller, in Bootstrap, so it carries on even when it unloads the scene that asked for it
    /// or hides the menu that started it.
    ///
    /// Walking and turning are stopped from the start (before the fade, not after it), so nobody
    /// moves while the view is going dark. If the work throws, the error is logged and the view
    /// still comes back - a participant is never left in the dark.
    /// </summary>
    /// <returns>False if a transition is already running; the caller should then do the work itself.</returns>
    public bool RunInDark(IEnumerator work)
    {
        if (IsTransitioning || work == null) return false;
        StartCoroutine(DarkRoutine(work));
        return true;
    }

    private IEnumerator DarkRoutine(IEnumerator work)
    {
        IsTransitioning = true;
        if (fader == null) ResolveReferences();

        // 1. Movement off first, then hide the world.
        SetLocomotionEnabled(false);
        ControllerHandednessManager.Instance?.SuspendLocomotion(this);
        if (fader != null) yield return fader.FadeTo(1f, fadeOutDuration);
        float audioFrom = AudioListener.volume;
        if (fadeAudio) AudioListener.volume = 0f;

        // 2. The caller's loading, unloading and moving, stepped by hand so an exception in it
        //    cannot end this routine with the view still black.
        while (true)
        {
            object step;
            try
            {
                if (!work.MoveNext()) break;
                step = work.Current;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
                break;
            }
            yield return step;
        }

        // 3. The work usually moves the rig (to the run's start, or back to the menu); a jump
        //    must not be heard as footsteps.
        if (footstepAudio == null) ResolveReferences();
        if (footstepAudio != null) footstepAudio.ResetStride();

        // 4. Let the first heavy frames pass while still hidden, then reveal.
        for (int i = 0; i < settleFrames; i++) yield return null;
        if (holdDarkDuration > 0f) yield return new WaitForSecondsRealtime(holdDarkDuration);

        if (fadeAudio) AudioListener.volume = audioFrom;
        if (fader != null) yield return fader.FadeTo(0f, fadeInDuration);

        ControllerHandednessManager.Instance?.ResumeLocomotion(this);
        SetLocomotionEnabled(true);
        IsTransitioning = false;
    }

    /// <summary>
    /// Unloads a content scene and loads a fresh copy of it, behind a fade - "start again".
    ///
    /// SwitchTo cannot do this: asked to load a scene that is already loaded, it keeps the
    /// existing copy and skips the unload, so nothing would reset. Here the old copy goes first,
    /// then a new one comes up, so every lesson, trigger and timer starts from its authored state
    /// and the player is placed on the scene's spawn point again.
    /// </summary>
    /// <returns>False if a transition is already running, so the caller can re-enable its button.</returns>
    public bool ReloadScene(string sceneName)
    {
        if (IsTransitioning || string.IsNullOrEmpty(sceneName)) return false;
        StartCoroutine(ReloadRoutine(sceneName));
        return true;
    }

    private IEnumerator ReloadRoutine(string sceneName)
    {
        IsTransitioning = true;
        if (fader == null) ResolveReferences();

        // 1. Dark first.
        if (fader != null) yield return fader.FadeTo(1f, fadeOutDuration);
        float audioFrom = AudioListener.volume;
        if (fadeAudio) AudioListener.volume = 0f;

        // 2. Hold movement off under this controller's own name. The pause menu releases its own
        //    hold as the scene unloads; without this, walking would come back while still dark.
        SetLocomotionEnabled(false);
        ControllerHandednessManager.Instance?.SuspendLocomotion(this);

        // 3. Out with the old copy. Bootstrap is made active first - Unity will not unload the
        //    active scene while deciding what replaces it.
        Scene old = SceneManager.GetSceneByName(sceneName);
        if (old.IsValid() && old.isLoaded)
        {
            if (SceneManager.GetActiveScene() == old) SceneManager.SetActiveScene(gameObject.scene);
            AsyncOperation unload = SceneManager.UnloadSceneAsync(old);
            while (unload != null && !unload.isDone) yield return null;
        }

        // 4. In with a fresh one.
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (load == null)
        {
            Debug.LogError($"[SceneTransitionController] '{sceneName}' could not be reloaded. " +
                           "Is it in File > Build Settings?", this);
        }
        else
        {
            while (!load.isDone) yield return null;
            Scene fresh = SceneManager.GetSceneByName(sceneName);
            if (fresh.IsValid() && fresh.isLoaded) SceneManager.SetActiveScene(fresh);
        }

        // 5. Back to the start position, while still unseen.
        MoveToSpawnPoint();

        for (int i = 0; i < settleFrames; i++) yield return null;
        if (holdDarkDuration > 0f) yield return new WaitForSecondsRealtime(holdDarkDuration);

        // 6. Reveal, then hand control back.
        if (fadeAudio) AudioListener.volume = audioFrom;
        if (fader != null) yield return fader.FadeTo(0f, fadeInDuration);

        ControllerHandednessManager.Instance?.ResumeLocomotion(this);
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
        if (spawn == null) return;

        // Head and rigRoot are not wired in the Inspector; they are found from Camera.main in
        // Awake, and the rig's camera is not guaranteed to be enabled by then. Without this retry
        // the move below would be skipped silently, and the participant would arrive facing
        // whichever way they happened to be facing.
        if (rigRoot == null || head == null || characterController == null) ResolveReferences();
        if (rigRoot == null || head == null)
        {
            Debug.LogWarning("[SceneTransitionController] Could not find the XR rig or camera, so the " +
                             "player was not placed at the spawn point.", this);
            return;
        }

        // Same placement as the runs, the Map and the main menu: the head is turned to face the
        // spawn's forward and stood on it, whichever way the participant is turned in the room.
        XRPlayerTeleport.AlignHeadTo(rigRoot, head, characterController,
                                     spawn.transform.position, spawn.transform.eulerAngles.y);

        // The rig persists across scene loads, so FootstepAudio is never disabled and never
        // re-syncs itself. Left alone it measures this jump as travel and spends it as a burst
        // of footsteps the moment the participant next moves - the "repeat run" rattle.
        if (footstepAudio != null) footstepAudio.ResetStride();
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
