using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

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

    [SerializeField] private GameObject mainMenuRoot;
    [SerializeField] private Transform menuStandingPoint;
    private bool isLoadingRunSystem;

    private void Start()
    {
        TeleportPlayerToMenu();
    }

    public void onRunSystemButtonClick()
    {
        Debug.Log("Run System Button Clicked");
    }

    public void onGuidedButtonClick()
    {
        if (!isLoadingRunSystem)
        {
            Debug.Log("Guided Button Clicked");
            StartCoroutine(LoadRunSystem("guided"));
        }
    }

    public void onUnguidedButtonClick()
    {
        if (!isLoadingRunSystem)
        {
            Debug.Log("Unguided Button Clicked");
            StartCoroutine(LoadRunSystem("unguided"));
        }
    }

    public void ShowMainMenu()
    {
        TeleportPlayerToMenu();

        if (mainMenuRoot != null)
            mainMenuRoot.SetActive(true);
        else
            Debug.LogWarning("MenuController has no main menu root assigned.", this);
    }

    private void TeleportPlayerToMenu()
    {
        XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();

        if (XRPlayerTeleport.MoveToStandingPoint(
                xrOrigin,
                menuStandingPoint,
                this))
        {
            Debug.Log("Player returned to the main menu.", this);
        }
    }

    private IEnumerator LoadRunSystem(string runType)
    {
        Debug.Log("Loading Run System.", this);
        isLoadingRunSystem = true;

        AsyncOperation loadOperation =
            SceneManager.LoadSceneAsync("RunSystem", LoadSceneMode.Additive);

        if (loadOperation == null)
        {
            Debug.LogError("RunSystem could not be loaded.", this);
            isLoadingRunSystem = false;
            yield break;
        }

        yield return loadOperation;
        Debug.Log("RunSystem scene loaded.", this);

        RunSystemController runSystemController =
            FindFirstObjectByType<RunSystemController>();

        if (runSystemController == null)
        {
            Debug.LogError("RunSystemController could not be found.", this);
            isLoadingRunSystem = false;
            yield break;
        }

        runSystemController.StartRun(runType);

        if (mainMenuRoot != null)
            mainMenuRoot.SetActive(false);

        isLoadingRunSystem = false;
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
