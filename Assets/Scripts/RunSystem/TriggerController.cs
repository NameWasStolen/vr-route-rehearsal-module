using UnityEngine;

public class TriggerController : MonoBehaviour
{
    enum TriggerType
    {
        START,
        END,
        WRONG_TURN
    }

    [SerializeField]
    [Tooltip("The type of trigger this is\n- START and END triggers require a set Timer Controller\n- WRONG_TURN triggers require a set Wrong Turn Controller")]
    private TriggerType triggerType;

    public TimerController timerController;
    public WrongTurnController wrongTurnController;

    private void Start()
    {
        Debug.Log(
            $"Trigger '{name}' ready. Type: {triggerType}, " +
            $"Timer assigned: {timerController != null}.",
            this
        );
    }

    private void OnTriggerExit(Collider other)
    {
        if (isPlayer(other))
        {
            Debug.Log($"Player collider left trigger '{name}'.", this);

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
            Debug.Log($"Player collider entered trigger '{name}'.", this);

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
