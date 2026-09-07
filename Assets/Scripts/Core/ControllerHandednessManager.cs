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

        bool useLeftHand = selectedHand == ControllerHand.Left;

        SetMapEnabled(_leftHandActions, useLeftHand);
        SetMapEnabled(_rightHandActions, !useLeftHand);

        Debug.Log($"Active controller: {selectedHand}");
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