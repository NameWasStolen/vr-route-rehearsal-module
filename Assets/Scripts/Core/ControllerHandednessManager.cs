using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum ControllerHand
{
    Left,
    Right
}

public class ControllerHandednessManager : MonoBehaviour
{
    public static ControllerHandednessManager Instance { get; private set; }

    /// <summary>
    /// Raised whenever the active hand is set - including the initial call from Start().
    /// Subscribers that may enable AFTER this manager has already run Start() (anything in an
    /// additively-loaded scene, e.g. the tutorial UI) must ALSO read <see cref="ActiveHand"/>
    /// on enable; the event alone will have already fired and they would never hear it.
    /// </summary>
    public static event Action<ControllerHand> HandChanged;

    /// <summary>
    /// True once SelectHand has run at least once, so subscribers can tell "right by default,
    /// nothing chosen yet" apart from "the user actively chose right".
    /// </summary>
    public bool HasResolvedHand { get; private set; }

    [SerializeField] private InputActionAsset _locomotionActions; // Contains the action maps for both left and right hand locomotion
    [SerializeField] private ControllerHand _defaultHand = ControllerHand.Right;

    private InputActionMap _leftHandActions;
    private InputActionMap _rightHandActions;

    public ControllerHand ActiveHand { get; private set; } // Creates a public property to get the currently active controller hand

    private void Awake()
    {
        Instance = this;

        _leftHandActions = FindActionMap("Left Locomotion");
        _rightHandActions = FindActionMap("Right Locomotion");
    }

    private void Start()
    {
        SelectHand(_defaultHand);
    }

    public void SelectHand(ControllerHand selectedHand)
    /**
     * Selects the active controller hand and enables/disables the corresponding action maps.
     *
     * @param selectedHand The hand to select (Left or Right).
     */
    {
        ActiveHand = selectedHand;
        HasResolvedHand = true;

        bool useLeftHand = selectedHand == ControllerHand.Left;

        SetMapEnabled(_leftHandActions, useLeftHand);
        SetMapEnabled(_rightHandActions, !useLeftHand);

        Debug.Log($"Active controller: {selectedHand}");

        // Fired last, so every listener sees a fully-applied state (maps already switched).
        // Deliberately fires even when the hand did not actually change - a listener that has
        // only just enabled relies on this to sync, and re-applying the same hand is harmless.
        HandChanged?.Invoke(selectedHand);
    }

    /// <summary>
    /// Convenience for UnityEvents / toggles that can't pass an enum from the Inspector.
    /// </summary>
    public void SelectLeftHand() => SelectHand(ControllerHand.Left);

    /// <summary>
    /// Convenience for UnityEvents / toggles that can't pass an enum from the Inspector.
    /// </summary>
    public void SelectRightHand() => SelectHand(ControllerHand.Right);

    /// <summary>
    /// The hand to use when no manager exists yet - lets scenes be opened and tested standalone.
    /// </summary>
    public static ControllerHand CurrentOrDefault(ControllerHand fallback)
    {
        return Instance != null && Instance.HasResolvedHand ? Instance.ActiveHand : fallback;
    }

    private InputActionMap FindActionMap(string mapName)
    /**
     * Finds an action map by name in the locomotion actions asset.
     *
     * @param mapName The name of the action map to find.
     * @return The found InputActionMap.
     */
    {
        return _locomotionActions.FindActionMap(
            mapName,
            throwIfNotFound: true
        );
    }

    private static void SetMapEnabled(
        InputActionMap actionMap,
        bool shouldEnable
    )
    /**
     * Enables or disables the specified action map.
     *
     * @param actionMap The action map to enable or disable.
     * @param shouldEnable True to enable the action map, false to disable it.
     */
    {
        if (shouldEnable)
        {
            actionMap.Enable();
        }
        else
        {
            actionMap.Disable();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
