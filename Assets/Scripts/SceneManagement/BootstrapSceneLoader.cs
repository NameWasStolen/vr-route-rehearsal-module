using UnityEngine;
using UnityEngine.SceneManagement;

public class BootstrapSceneLoader : MonoBehaviour
{
    private async void Start()
    {
        if (!SceneManager.GetSceneByName("MainMenu").isLoaded)
        {
            await SceneManager.LoadSceneAsync(
                "MainMenu",
                LoadSceneMode.Additive
            );
        }
    }
}