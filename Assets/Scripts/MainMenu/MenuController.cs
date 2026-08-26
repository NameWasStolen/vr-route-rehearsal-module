using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuController : MonoBehaviour
{
    public void onRunSystemButtonClick()
    {
        Debug.Log("Run System Button Clicked");
        //SceneManager.LoadScene("RunSystem");
    }

    public void onTutorialButtonClick()
    {
        Debug.Log("Tutorial Button Clicked");
        //SceneManager.LoadScene("Tutorial");
    }
}
