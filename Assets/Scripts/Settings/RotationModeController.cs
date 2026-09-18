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

    [SerializeField] private SnapTurnProvider _snapTurnProvider;
    [SerializeField] private ContinuousTurnProvider _continuousTurnProvider;
    [SerializeField] private PlayerRotationMode _defaultMode =
        PlayerRotationMode.Continuous;

    public PlayerRotationMode CurrentMode { get; private set; }

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

        // Only one artificial rotation provider may be active.
        _snapTurnProvider.enabled = mode == PlayerRotationMode.Snap;
        _continuousTurnProvider.enabled =
            mode == PlayerRotationMode.Continuous;

        CurrentMode = mode;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}