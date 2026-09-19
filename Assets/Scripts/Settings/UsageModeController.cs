using Unity.XR.CoreUtils;
using UnityEngine;

public enum PlayerUsageMode
{
    Standing,
    Sitting
}

public class UsageModeController : MonoBehaviour
{
    public static UsageModeController Instance { get; private set; }

    [SerializeField] private XROrigin _xrOrigin;

    [Tooltip("Virtual eye height used while sitting.")]
    [SerializeField] private float _sittingCameraHeight = 1.1176f;

    [SerializeField] private PlayerUsageMode _defaultMode =
        PlayerUsageMode.Standing;

    public PlayerUsageMode CurrentMode { get; private set; }

    private void Awake()
    {
        Instance = this;
        SetUsageMode(_defaultMode);
    }

    public void SetUsageMode(PlayerUsageMode mode)
    {
        if (_xrOrigin == null)
        {
            Debug.LogError("XR Origin is not assigned.", this);
            return;
        }

        if (mode == PlayerUsageMode.Standing)
        {
            _xrOrigin.RequestedTrackingOriginMode =
                XROrigin.TrackingOriginMode.Floor;

            // Floor tracking reports real head height, so no virtual offset is wanted.
            // Clearing it matters if the platform cannot provide Floor and quietly falls
            // back to Device - otherwise the seated offset stays applied while standing.
            _xrOrigin.CameraYOffset = 0f;
        }
        else
        {
            // Mode first: changing it re-runs XROrigin's camera setup, so the offset is
            // applied against the mode that will actually be in effect.
            _xrOrigin.RequestedTrackingOriginMode =
                XROrigin.TrackingOriginMode.Device;

            _xrOrigin.CameraYOffset = _sittingCameraHeight;
        }

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