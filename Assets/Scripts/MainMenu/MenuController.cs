using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuController : MonoBehaviour
{
    [SerializeField] private GameObject mainMenuRoot;
    private bool isLoadingRunSystem;

    public void onRunSystemButtonClick()
    {
        Debug.Log("Run System Button Clicked");
    }

    public void onGuidedButtonClick()
    {
        if (!isLoadingRunSystem)
            StartCoroutine(LoadGuidedRun());
    }

    private IEnumerator LoadGuidedRun()
    {
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

        RunSystemController runSystemController =
            FindFirstObjectByType<RunSystemController>();

        if (runSystemController == null)
        {
            Debug.LogError("RunSystemController could not be found.", this);
            isLoadingRunSystem = false;
            yield break;
        }

        runSystemController.StartGuidedRun();

        if (mainMenuRoot != null)
            mainMenuRoot.SetActive(false);

        isLoadingRunSystem = false;
    }

    public void onTutorialButtonClick()
    {
        Debug.Log("Tutorial Button Clicked");
        //SceneManager.LoadScene("Tutorial");
    }
}
