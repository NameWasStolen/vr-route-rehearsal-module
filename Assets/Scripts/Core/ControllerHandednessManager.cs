using System;
using System.Collections.Generic;
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

    [Header("Interactors")]
    [Tooltip("The Ray Interactor under Controller > Left Hand. Hidden while the right hand " +
             "is active, so only the chosen controller shows a ray.")]
    [SerializeField] private GameObject _leftRayInteractor;

    [Tooltip("The Ray Interactor under Controller > Right Hand.")]
    [SerializeField] private GameObject _rightRayInteractor;

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

        // While locomotion is suspended - the pause menu is open - the choice is recorded but no
        // map is enabled. Otherwise changing hands from the pause menu would hand movement back
        // mid-pause, and the participant would walk off while reading the settings.
        if (IsLocomotionSuspended)
        {
            SetMapEnabled(_leftHandActions, false);
            SetMapEnabled(_rightHandActions, false);
        }
        else
        {
            SetMapEnabled(_leftHandActions, useLeftHand);
            SetMapEnabled(_rightHandActions, !useLeftHand);
        }

        // Deliberately outside the suspension branch above: which hand owns the ray is a
        // setting, but the pause menu still has to be clickable while locomotion is off.
        ApplyInteractorVisibility();

        Debug.Log($"Active controller: {selectedHand}");

        // Fired last, so every listener sees a fully-applied state (maps already switched).
        // Deliberately fires even when the hand did not actually change - a listener that has
        // only just enabled relies on this to sync, and re-applying the same hand is harmless.
        HandChanged?.Invoke(selectedHand);
    }

    /// <summary>
    /// Shows the ray on the chosen controller only.
    ///
    /// Disabling the GameObject rather than just the line visual is deliberate: a hidden
    /// interactor still hovers and selects, so a participant could click a menu item with
    /// the hand they are not using and never see what did it.
    /// </summary>
    private void ApplyInteractorVisibility()
    {
        bool useLeftHand = ActiveHand == ControllerHand.Left;

        if (_leftRayInteractor != null) _leftRayInteractor.SetActive(useLeftHand);
        if (_rightRayInteractor != null) _rightRayInteractor.SetActive(!useLeftHand);
    }

    /// <summary>True while anything holds locomotion off, e.g. the pause menu is open.</summary>
    public bool IsLocomotionSuspended => _suspenders.Count > 0;

    private readonly HashSet<object> _suspenders = new HashSet<object>();

    // Locomotion provider components this manager switched off, so resuming turns back on only
    // those - ContinuousTurnProvider ships disabled and must stay that way.
    private readonly List<Behaviour> _providersWeDisabled = new List<Behaviour>();

    /// <summary>
    /// Turns locomotion off without forgetting which hand the participant chose.
    ///
    /// This is how pausing is done. It is deliberately not Time.timeScale: the tutorial's
    /// coroutines all run on unscaled time so a zero timescale would not stop them, and freezing
    /// the world while head tracking carries on is unpleasant in a headset.
    ///
    /// Two layers, so walking and snap turning are reliably off while a menu is up:
    ///   - both locomotion action maps are disabled (the same mechanism that switches hands), and
    ///   - the rig's locomotion provider components (move, snap turn, continuous turn) are
    ///     disabled, so nothing that still reads an action - or a provider enabled from some
    ///     other asset - can move the rig.
    ///
    /// Suspension is held per owner. The pause menu and a help request both suspend, and closing
    /// the menu while a request panel is opening must not hand movement back.
    ///
    /// The pause button itself must live outside these two maps, or it would disable itself.
    /// </summary>
    public void SuspendLocomotion(object owner = null)
    {
        bool was = IsLocomotionSuspended;
        _suspenders.Add(owner ?? this);
        if (was) return;

        SetMapEnabled(_leftHandActions, false);
        SetMapEnabled(_rightHandActions, false);
        SetProvidersEnabled(false);
    }

    /// <summary>
    /// Releases an owner's suspension, and hands locomotion back to whichever controller is
    /// currently selected once nobody else is holding it off.
    /// </summary>
    public void ResumeLocomotion(object owner = null)
    {
        if (!_suspenders.Remove(owner ?? this)) return;
        if (IsLocomotionSuspended) return;

        SetProvidersEnabled(true);
        SelectHand(ActiveHand);
    }

    private void SetProvidersEnabled(bool enable)
    {
        if (!enable)
        {
            _providersWeDisabled.Clear();
            foreach (Behaviour provider in FindLocomotionProviders())
            {
                if (!provider.enabled) continue;
                provider.enabled = false;
                _providersWeDisabled.Add(provider);
            }
            return;
        }

        foreach (Behaviour provider in _providersWeDisabled)
            if (provider != null) provider.enabled = true;
        _providersWeDisabled.Clear();
    }

    /// <summary>
    /// Every XRI LocomotionProvider on this rig. Matched by base-type name rather than by type,
    /// so this script does not take a hard dependency on the XR Interaction Toolkit assembly
    /// and keeps compiling if the package version moves the class between namespaces.
    /// </summary>
    private IEnumerable<Behaviour> FindLocomotionProviders()
    {
        Behaviour[] all = transform.root.GetComponentsInChildren<Behaviour>(true);
        foreach (Behaviour b in all)
        {
            if (b == null) continue;
            for (Type t = b.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                if (t.Name == "LocomotionProvider")
                {
                    yield return b;
                    break;
                }
            }
        }
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
