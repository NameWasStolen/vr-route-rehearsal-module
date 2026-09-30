using System;
using UnityEngine;

/// <summary>
/// Collects wrong turns for the run. WRONG_TURN triggers call in here; RunSystemController
/// listens to the events and hands them to PlayerPositionTracker for the CSV.
///
/// A wrong turn is one excursion into a side street: counted once when the participant crosses
/// a WRONG_TURN trigger heading into the side street, however long they spend there. Walking
/// back out is reported separately (ReturnedToRoute) and never counts as a second error.
/// TriggerController works out which way the crossing went.
/// </summary>
public class WrongTurnController : MonoBehaviour
{
    /// <summary>A wrong turn was taken. The argument is the trigger's name (the side street).</summary>
    public event Action<string> WrongTurnRecorded;

    /// <summary>The participant walked back out of a side street they had turned into.</summary>
    public event Action<string> ReturnedToRoute;

    public int WrongTurnCount { get; private set; }

    /// <summary>Kept for any existing wiring that calls it without a name.</summary>
    public void logWrongTurn()
    {
        logWrongTurn(string.Empty);
    }

    public void logWrongTurn(string triggerName)
    {
        WrongTurnCount++;
        Debug.Log($"User made a Wrong Turn ({triggerName}), {WrongTurnCount} this run");
        SessionLog.Record("wrong_turn", triggerName);
        WrongTurnRecorded?.Invoke(triggerName);
    }

    public void logReturnToRoute(string triggerName)
    {
        Debug.Log($"User came back from a wrong turn ({triggerName})");
        SessionLog.Record("wrong_turn_return", triggerName);
        ReturnedToRoute?.Invoke(triggerName);
    }
}
