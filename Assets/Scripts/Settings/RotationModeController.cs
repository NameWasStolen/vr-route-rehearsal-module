using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

public enum PlayerRotationMode
{
    Continuous,
    Snap,
    HeadOnly
}

public class RotationModeController : MonoBehaviour
{
    public static RotationModeController Instance { get; private set; }

    /// <summary>
    /// Raised whenever the mode actually in force changes, including when it is changed for the
    /// participant rather than by them (switching to seated moves Raw to Snap). The settings menu
    /// listens so its toggles never show a mode that is not the real one.
    /// </summary>
    public static event Action<PlayerRotationMode> ModeChanged;

    [SerializeField] private SnapTurnProvider _snapTurnProvider;
    [SerializeField] private ContinuousTurnProvider _continuousTurnProvider;

    [Tooltip("Snap by default (changed Sept 2026, was Continuous). Snap turning has no smooth " +
             "rotation, which is the most comfortable option for most people new to VR.")]
    [SerializeField] private PlayerRotationMode _defaultMode =
        PlayerRotationMode.Snap;

    [Tooltip("What a seated participant is moved to if they were on Raw (turn your body) when " +
             "they chose seated. Raw needs room to turn the whole body, which a chair - " +
             "especially one with arms - does not reliably give.")]
    [SerializeField] private PlayerRotationMode _seatedFallbackMode =
        PlayerRotationMode.Snap;

    public PlayerRotationMode CurrentMode { get; private set; }

    /// <summary>
    /// The turning mode in force, or <paramref name="fallback"/> when no controller exists -
    /// i.e. when a scene is opened standalone for testing without Bootstrap.
    ///
    /// Mirrors ControllerHandednessManager.CurrentOrDefault, and exists for the same reason: a
    /// lesson in an additively-loaded scene needs the current setting without holding a scene
    /// reference across scenes, which does not serialise.
    /// </summary>
    public static PlayerRotationMode CurrentOrDefault(PlayerRotationMode fallback)
    {
        return Instance != null ? Instance.CurrentMode : fallback;
    }

    /// <summary>
    /// Whether a mode may be chosen right now. Raw (HeadOnly) is unavailable while seated.
    /// The settings menu uses this to grey out the Raw toggle.
    /// </summary>
    public static bool IsAllowed(PlayerRotationMode mode)
    {
        return !(mode == PlayerRotationMode.HeadOnly && IsSeated);
    }

    private static bool IsSeated =>
        UsageModeController.Instance != null &&
        UsageModeController.Instance.CurrentMode == PlayerUsageMode.Sitting;

    public PlayerRotationMode SeatedFallbackMode =>
        _seatedFallbackMode == PlayerRotationMode.HeadOnly ? PlayerRotationMode.Snap : _seatedFallbackMode;

    private void Awake()
    {
        Instance = this;
        SetRotationMode(_defaultMode);
    }

    public void SetRotationMode(PlayerRotationMode mode)
    {
        // Check if the rotation providers are assigned before enabling/disabling them
        if (_snapTurnProvider == null || _continuousTurnProvider == null)
        {
            Debug.LogError("Rotation providers are not assigned.", this);
            return;
        }

        // The rule lives here, not only in the menu, so nothing - a default, a reset, a future
        // caller - can put a seated participant on Raw.
        if (!IsAllowed(mode))
        {
            Debug.Log($"[RotationMode] {mode} is not available while seated; using {SeatedFallbackMode}.", this);
            mode = SeatedFallbackMode;
        }

        // Only one artificial rotation provider may be active.
        _snapTurnProvider.enabled = mode == PlayerRotationMode.Snap;
        _continuousTurnProvider.enabled =
            mode == PlayerRotationMode.Continuous;

        bool changed = mode != CurrentMode;
        CurrentMode = mode;
        if (changed || !_announcedOnce)
        {
            _announcedOnce = true;
            ModeChanged?.Invoke(mode);
        }
    }

    private bool _announcedOnce;

    /// <summary>
    /// Called by UsageModeController when the participant sits down. Moves them off Raw if they
    /// were on it; any other mode is left alone.
    /// </summary>
    public void EnforceSeatedRules()
    {
        if (CurrentMode == PlayerRotationMode.HeadOnly) SetRotationMode(SeatedFallbackMode);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
