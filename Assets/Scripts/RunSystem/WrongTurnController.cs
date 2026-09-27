using System;
using UnityEngine;

public class WrongTurnController : MonoBehaviour
{
    public event Action WrongTurnRecorded;

    public void logWrongTurn()
    {
        Debug.Log("User made a Wrong Turn");
        WrongTurnRecorded?.Invoke();
    }
}
