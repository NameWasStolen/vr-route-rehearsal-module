using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Sends the player back to the main menu. Wire <see cref="ReturnToMenu"/> to a Button's
/// OnClick - it is the mirror image of MenuController.onTutorialButtonClick, and deliberately
/// shares that script's shape so the two ends of the journey behave the same way.
///
/// Lives on an object in the CONTENT scene (Tutorial), not in Bootstrap: the thing being
/// unloaded is this scene, and defaulting the unload target to gameObject.scene.name means the
/// same component can be dropped into Training later without re-typing a scene name.
///
/// The heavy lifting - fade out, preload, reposition the rig, unload, fade in - belongs to
/// SceneTransitionController in Bootstrap. This is only the doorbell.
/// </summary>
[DisallowMultipleComponent]
public class ReturnToMainMenu : MonoBehaviour
{
    [Header("Scenes")]
    [Tooltip("The menu scene to load. Must be listed in File > Build Settings, or " +
             "LoadSceneAsync cannot find it by name at runtime.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Tooltip("Scene to unload on the way out. Leave empty to unload whichever scene this " +
             "component happens to live in, which is almost always what you want.")]
    [SerializeField] private string sceneToUnload = "";

    [Header("Button")]
    [Tooltip("Optional. Greyed out the moment it is pressed, so the player gets immediate " +
             "feedback and cannot fire a second request into the dark. A 65+ user pressing " +
             "again because 'nothing happened yet' is the normal case, not the edge case.")]
    [SerializeField] private Selectable buttonToDisable;

    // Belt and braces: SceneTransitionController guards itself, but the fallback path below
    // does not, and this also covers the frames before the fade has visibly started.
    private bool _requested;

    /// <summary>
    /// Hook this to Button.onClick. Safe to call more than once - every call after the first
    /// is ignored until the scene actually goes away.
    /// </summary>
    public void ReturnToMenu()
    {
        if (_requested) return;
        if (string.IsNullOrEmpty(mainMenuSceneName))
        {
            Debug.LogError("[ReturnToMainMenu] No main menu scene name set.", this);
            return;
        }

        _requested = true;
        if (buttonToDisable != null) buttonToDisable.interactable = false;

        string unloadName = string.IsNullOrEmpty(sceneToUnload)
            ? gameObject.scene.name
            : sceneToUnload;

        if (SceneTransitionController.Instance != null)
        {
            SceneTransitionController.Instance.SwitchTo(mainMenuSceneName, unloadName);
            return;
        }

        Debug.LogWarning("[ReturnToMainMenu] No SceneTransitionController found in Bootstrap - " +
                         "switching without a fade.", this);
        SwitchUnfaded(mainMenuSceneName, unloadName);
    }

    /// <summary>
    /// Fallback, matching MenuController's. Still ADDITIVE: a plain SceneManager.LoadScene
    /// would unload Bootstrap too, destroying XRPlayerRig, the XR Interaction Manager, the
    /// EventSystem and the ControllerHandednessManager - and MainMenu has no rig of its own.
    ///
    /// Note this path does NOT fade, so the player is moved while they can see it. It exists so
    /// a misconfigured Bootstrap degrades to "abrupt but works" rather than "button does nothing",
    /// which is much harder to diagnose in a headset.
    /// </summary>
    private async void SwitchUnfaded(string loadSceneName, string unloadSceneName)
    {
        try
        {
            if (!SceneManager.GetSceneByName(loadSceneName).isLoaded)
            {
                await SceneManager.LoadSceneAsync(loadSceneName, LoadSceneMode.Additive);
            }

            Scene loaded = SceneManager.GetSceneByName(loadSceneName);
            if (loaded.IsValid() && loaded.isLoaded) SceneManager.SetActiveScene(loaded);

            if (!string.IsNullOrEmpty(unloadSceneName) && unloadSceneName != loadSceneName)
            {
                Scene old = SceneManager.GetSceneByName(unloadSceneName);
                if (old.IsValid() && old.isLoaded) await SceneManager.UnloadSceneAsync(old);
            }
        }
        finally
        {
            // Only reached if the unload failed - if it succeeded this object is already gone.
            _requested = false;
            if (buttonToDisable != null) buttonToDisable.interactable = true;
        }
    }
}
