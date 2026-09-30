using System.Collections.Generic;
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

    // The player can have more than one collider tagged Player/MainCamera (the rig's
    // CharacterController and a head collider, for instance). Each one fires its own
    // enter/exit, so the trigger acts only when the FIRST player collider enters and when the
    // LAST one leaves - otherwise one step through the trigger is counted twice.
    private readonly HashSet<Collider> playerCollidersInside = new HashSet<Collider>();

    // WRONG_TURN only. A wrong-turn trigger is a thin slab across a side street, with the
    // route on one side and the side street on the other. What matters is which way the
    // participant CROSSES it:
    //   route side -> side street : one wrong turn
    //   side street -> route side : they came back (not another wrong turn)
    //   in and out the same side  : dithering on the line, nothing counted
    // The route side is the side they first touch it from - every run starts on the route, so
    // the first contact with any side-street trigger is always from the route side.
    private float entrySide;
    private float routeSide;
    private bool routeSideKnown;
    private bool inSideStreet;

    private void Start()
    {
        Debug.Log(
            $"Trigger '{name}' ready. Type: {triggerType}, " +
            $"Timer assigned: {timerController != null}.",
            this
        );
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isPlayer(other))
            return;

        // Colliders switched off while inside never send an exit (the rig's CharacterController
        // is disabled for a moment whenever the player is teleported), so drop them first.
        playerCollidersInside.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);

        bool first = playerCollidersInside.Count == 0;
        playerCollidersInside.Add(other);
        if (!first)
            return;

        Debug.Log($"Player entered trigger '{name}'.", this);

        switch (triggerType)
        {
            case TriggerType.END:
                Debug.Log("Player entered an End Trigger");
                if (timerController != null)
                    timerController.endTimer();
                else
                    Debug.LogError("TimerController not set", this);
                break;

            case TriggerType.WRONG_TURN:
                entrySide = SideOf(other);
                break;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!isPlayer(other))
            return;

        if (!playerCollidersInside.Remove(other) || playerCollidersInside.Count > 0)
            return;

        Debug.Log($"Player left trigger '{name}'.", this);

        switch (triggerType)
        {
            case TriggerType.START:
                Debug.Log("Player left a Start Trigger");
                if (timerController != null)
                    timerController.startTimer();
                else
                    Debug.LogError("TimerController not set", this);
                break;

            case TriggerType.WRONG_TURN:
                HandleWrongTurnCrossing(SideOf(other));
                break;
        }
    }

    private void HandleWrongTurnCrossing(float exitSide)
    {
        if (!routeSideKnown)
        {
            routeSide = entrySide;
            routeSideKnown = true;
        }

        if (entrySide == exitSide)
            return;   // stepped onto the line and back off the same side

        if (wrongTurnController == null)
        {
            Debug.LogError("WrongTurnController not set", this);
            return;
        }

        if (exitSide != routeSide)
        {
            if (inSideStreet)
                return;
            inSideStreet = true;
            Debug.Log("Player crossed a Wrong Turn Trigger into the side street");
            wrongTurnController.logWrongTurn(name);
        }
        else if (inSideStreet)
        {
            inSideStreet = false;
            wrongTurnController.logReturnToRoute(name);
        }
    }

    /// <summary>Which side of the slab (its local z) a collider's centre is on: +1 or -1.</summary>
    private float SideOf(Collider collider)
    {
        Vector3 local = transform.InverseTransformPoint(collider.bounds.center);
        return local.z >= 0f ? 1f : -1f;
    }

    private bool isPlayer(Collider collider)
    {
        return collider.CompareTag("Player") || collider.CompareTag("MainCamera");
    }
}
