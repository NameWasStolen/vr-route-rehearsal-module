using UnityEngine;
using UnityEngine.SceneManagement;

// Class to determine when end zone of guided tutorial has been reached and display UI result
// Author: Kade Lucy
// Date Last Modified: 26/08/26
public class EndZoneTrigger: MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        // Check if user enters end zone (contains 'Player' tag)
        if (other.CompareTag("Player"))
        {
            Debug.Log("End Zone reached");
        }
    }
}
