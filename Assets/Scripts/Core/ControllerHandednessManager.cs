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

        // Walking stays off if something still holds it (the main menu), and comes back here if
        // its hold was released while everything was suspended.
        if (!IsWalkingSuspended) RestoreWalking();

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
        {
            if (provider == null) continue;

            // Walking is still held off: hand the move provider to that hold instead, so it comes
            // back when the hold is released rather than now.
            if (IsWalkingSuspended && IsMoveProvider(provider))
            {
                if (!_walkersWeDisabled.Contains(provider)) _walkersWeDisabled.Add(provider);
                continue;
            }

            provider.enabled = true;
        }
        _providersWeDisabled.Clear();
    }

    // ------------------------------------------------------------------ walking only

    /// <summary>True while anything holds walking off, e.g. the main menu is showing.</summary>
    public bool IsWalkingSuspended => _walkingSuspenders.Count > 0;

    private readonly HashSet<object> _walkingSuspenders = new HashSet<object>();

    // Move providers switched off by the walking hold, so releasing it turns back on only those.
    private readonly List<Behaviour> _walkersWeDisabled = new List<Behaviour>();

    /// <summary>
    /// Turns walking off but leaves snap turning (and head look) alone. Used by the main menu,
    /// where there is nothing to walk to and a stray grip only carries the participant away
    /// from the menu they are meant to be reading.
    ///
    /// Only the move provider is switched off; the action maps stay on, so turning and every
    /// button keep working. Held per owner like SuspendLocomotion, and the two stack: while
    /// either holds, the participant cannot walk.
    ///
    /// Other code switches the move provider back on after a fade (SceneTransitionController's
    /// own locomotion list, for one). Rather than teach each of them about this hold, LateUpdate
    /// switches it straight back off while the hold is on - coroutines run before LateUpdate, so
    /// the provider never gets an Update in which to move the rig.
    /// </summary>
    public void SuspendWalking(object owner = null)
    {
        bool was = IsWalkingSuspended;
        _walkingSuspenders.Add(owner ?? this);
        if (!was) DisableWalking();
    }

    /// <summary>
    /// Releases an owner's walking hold. Walking returns once nobody holds it - unless
    /// locomotion as a whole is suspended (a fade, the pause menu), in which case it returns
    /// when that ends.
    /// </summary>
    public void ResumeWalking(object owner = null)
    {
        if (!_walkingSuspenders.Remove(owner ?? this)) return;
        if (IsWalkingSuspended) return;
        if (IsLocomotionSuspended) return;   // ResumeLocomotion hands it back

        RestoreWalking();
    }

    private void DisableWalking()
    {
        foreach (Behaviour provider in MoveProviders())
        {
            if (provider == null || !provider.enabled) continue;
            provider.enabled = false;
            if (!_walkersWeDisabled.Contains(provider)) _walkersWeDisabled.Add(provider);
        }
    }

    // The rig persists for the whole session, so its move providers are looked up once rather
    // than searched for every frame the menu is open.
    private List<Behaviour> _moveProviders;

    private List<Behaviour> MoveProviders()
    {
        // Unity's == (not List.Contains) is what notices a destroyed component.
        bool stale = _moveProviders == null;
        if (!stale)
            foreach (Behaviour provider in _moveProviders)
                if (provider == null) { stale = true; break; }

        if (stale)
        {
            _moveProviders = new List<Behaviour>();
            foreach (Behaviour provider in FindLocomotionProviders())
                if (IsMoveProvider(provider)) _moveProviders.Add(provider);
        }
        return _moveProviders;
    }

    private void RestoreWalking()
    {
        foreach (Behaviour provider in _walkersWeDisabled)
            if (provider != null) provider.enabled = true;
        _walkersWeDisabled.Clear();
    }

    private void LateUpdate()
    {
        if (IsWalkingSuspended) DisableWalking();
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
    /// The walking provider (XRI's ContinuousMoveProvider, or anything derived from it), matched
    /// by name for the same reason as above. Turn providers are left out on purpose.
    /// </summary>
    private static bool IsMoveProvider(Behaviour provider)
    {
        for (Type t = provider.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
        {
            if (t.Name == "ContinuousMoveProvider") return true;
        }
        return false;
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
