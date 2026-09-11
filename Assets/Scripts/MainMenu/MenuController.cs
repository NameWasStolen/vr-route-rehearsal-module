using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuController : MonoBehaviour
{
    [Header("Scenes")]
    [Tooltip("Content scene loaded by the tutorial button. Must be listed in " +
             "File > Build Settings, or LoadSceneAsync cannot find it by name at runtime.")]
    [SerializeField] private string tutorialSceneName = "Tutorial";

    [Tooltip("This menu's own scene. Unloaded once the content scene is up.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    // Only used by the fallback path below; the transition controller has its own guard.
    private bool _isSwitching;

    public void onRunSystemButtonClick()
    {
        Debug.Log("Run System Button Clicked");
        //SceneManager.LoadScene("RunSystem");
    }

    public void onTutorialButtonClick()
    {
        Debug.Log("Tutorial Button Clicked");
        SwitchToContentScene(tutorialSceneName);
    }

    /// <summary>
    /// Hands the swap to SceneTransitionController in Bootstrap, which fades the view out,
    /// preloads, places the player at the scene's spawn point and fades back in.
    ///
    /// Falls back to an unfaded additive swap if that controller is missing, so a misconfigured
    /// Bootstrap degrades to "works but looks abrupt" rather than "button does nothing".
    /// </summary>
    private void SwitchToContentScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;

        if (SceneTransitionController.Instance != null)
        {
            SceneTransitionController.Instance.SwitchTo(sceneName, mainMenuSceneName);
            return;
        }

        Debug.LogWarning("[MenuController] No SceneTransitionController found in Bootstrap - " +
                         "switching without a fade.", this);
        SwitchUnfaded(sceneName);
    }

    /// <summary>
    /// Fallback. Still ADDITIVE: a plain SceneManager.LoadScene would unload Bootstrap along
    /// with the menu, destroying XRPlayerRig, the XR Interaction Manager, the EventSystem and
    /// the ControllerHandednessManager - and the tutorial scene has no rig of its own.
    /// </summary>
    private async void SwitchUnfaded(string sceneName)
    {
        if (_isSwitching) return;
        _isSwitching = true;

        try
        {
            if (!SceneManager.GetSceneByName(sceneName).isLoaded)
            {
                await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            }

            Scene loaded = SceneManager.GetSceneByName(sceneName);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(loaded);
            }

            Scene menu = SceneManager.GetSceneByName(mainMenuSceneName);
            if (menu.IsValid() && menu.isLoaded)
            {
                await SceneManager.UnloadSceneAsync(menu);
            }
        }
        finally
        {
            _isSwitching = false;
        }
    }
}
