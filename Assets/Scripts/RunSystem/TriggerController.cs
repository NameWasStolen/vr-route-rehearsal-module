using UnityEngine;

public class TriggerController : MonoBehaviour
{
    enum TriggerType
    {
        START,
        END,
        WRONG_TURN
    }

    [Header("Trigger Type")]
    [SerializeField] private TriggerType triggerType;

    public TimerController timerController;
    public WrongTurnController wrongTurnController;

    private void OnTriggerExit(Collider other)
    {
        if (isPlayer(other))
        {
            // Start Trigger
            if (isPlayer(other) && triggerType == TriggerType.START)
            {
                Debug.Log("Player left a Start Trigger");

                if (timerController != null)
                {
                    timerController.startTimer();
                    // TODO
                } else
                {
                    Debug.LogError("TimerController not set");
                }
            }
        }
    }



    private void OnTriggerEnter(Collider other)
    {
        if (isPlayer(other))
        {
            // End Trigger
            if (triggerType == TriggerType.END)
            {
                Debug.Log("Player entered an End Trigger");
                if (timerController != null)
                {
                    timerController.endTimer();
                    // TODO
                } else
                {
                    Debug.LogError("TimerController not set");
                }
            }

            // Wrong Turn Trigger
            else if (triggerType == TriggerType.WRONG_TURN)
            {
                Debug.Log("Player entered a Wrong Turn Trigger");

                if (wrongTurnController != null)
                {
                    wrongTurnController.logWrongTurn();
                    // TODO
                } else
                {
                    Debug.LogError("WrongTurnController not set");
                }
            }
        }
    }

    private bool isPlayer(Collider collider)
    {
        return collider.CompareTag("Player") || collider.CompareTag("MainCamera");
    }
}
