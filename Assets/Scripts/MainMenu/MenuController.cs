using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

public class MenuController : MonoBehaviour
{
    [SerializeField] private GameObject mainMenuRoot;
    [SerializeField] private Transform menuReturnPoint;
    private bool isLoadingRunSystem;

    public void onRunSystemButtonClick()
    {
        Debug.Log("Run System Button Clicked");
    }

    public void onGuidedButtonClick()
    {
        if (!isLoadingRunSystem)
        {
            Debug.Log("Guided Button Clicked");
            StartCoroutine(LoadRunSystem());
        }
    }

    public void onUnguidedButtonClick()
    {
        if (!isLoadingRunSystem)
        {
            Debug.Log("Unguided Button Clicked");
            StartCoroutine(LoadRunSystem());
        }
    }

    public void ShowMainMenu()
    {
        XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();

        if (xrOrigin != null && menuReturnPoint != null)
        {
            CharacterController characterController =
                xrOrigin.GetComponent<CharacterController>();

            if (characterController != null)
                characterController.enabled = false;

            xrOrigin.transform.SetPositionAndRotation(
                menuReturnPoint.position,
                Quaternion.Euler(0f, menuReturnPoint.eulerAngles.y, 0f)
            );

            if (characterController != null)
                characterController.enabled = true;

            Debug.Log("Player returned to the main menu.", this);
        }
        else if (menuReturnPoint == null)
        {
            Debug.LogError("MenuController has no menu return point assigned.", this);
        }

        if (mainMenuRoot != null)
            mainMenuRoot.SetActive(true);
        else
            Debug.LogWarning("MenuController has no main menu root assigned.", this);
    }

    private IEnumerator LoadRunSystem()
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

        runSystemController.StartRun();

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
