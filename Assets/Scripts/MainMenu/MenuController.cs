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

    // Guards against a second button press while a load is still in flight - without it an
    // impatient double-press loads the scene twice.
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
    /// Swaps the menu out for a content scene, leaving Bootstrap loaded.
    ///
    /// Deliberately ADDITIVE. A plain SceneManager.LoadScene would unload Bootstrap along with
    /// the menu, destroying XRPlayerRig, the XR Interaction Manager, the EventSystem and the
    /// ControllerHandednessManager - the player would lose their rig and their hand preference
    /// mid-transition, and the tutorial scene has no rig of its own to fall back on.
    ///
    /// Load first, unload second, so there is never a frame with no content scene present.
    /// </summary>
    private async void SwitchToContentScene(string sceneName)
    {
        if (_isSwitching || string.IsNullOrEmpty(sceneName)) return;

        _isSwitching = true;

        try
        {
            if (!SceneManager.GetSceneByName(sceneName).isLoaded)
            {
                await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            }

            // The active scene supplies lighting and skybox settings, and receives anything
            // instantiated without an explicit scene. Leaving MainMenu active and then
            // unloading it drops that role to Bootstrap, which has no environment of its own.
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
