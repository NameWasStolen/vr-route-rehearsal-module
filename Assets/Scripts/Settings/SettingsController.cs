using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The settings menu. This is now a thin front end: it reads the current state, positions its
/// controls to match, and forwards changes to AccessibilitySettings. It no longer owns any
/// setting itself.
///
/// That split matters because the menu lives in a prefab that is only loaded some of the time,
/// while the settings have to hold for the whole session and reach scenes the menu has never
/// met. Anything stored here would be lost the moment the menu closed.
///
/// Brightness is the exception and stays here: it drives a VolumeProfile asset directly, which
/// is already global by nature.
/// </summary>
public class SettingsController : MonoBehaviour
{
    [Header("Sliders")]
    public Slider brightnessSlider;
    public Slider volumeSlider;
    public Slider fontSizeSlider;

    [Tooltip("Optional extra sliders for the individual audio buses. Leave any of them empty " +
             "if the menu only offers a single master volume.")]
    public Slider ambienceVolumeSlider;
    public Slider uiVolumeSlider;
    public Slider movementVolumeSlider;

    [Header("Toggles")]
    public List<Toggle> usageModeToggles = new();
    public List<Toggle> handToggles = new();
    public List<Toggle> rotationToggles = new();

    [Header("Brightness")]
    [Tooltip("Brightness.asset - the dedicated profile on the global Volume in Bootstrap. " +
             "Note that moving this slider in the editor writes to that asset on disk, so " +
             "expect it to show up as a local change in git after a play session.")]
    public VolumeProfile brightnessProfile;

    [Header("Operator")]
    [Tooltip("Optional. Restores every accessibility setting to its authored default - for " +
             "handing the headset to the next participant. Keep it somewhere a participant " +
             "will not press by accident.")]
    public Button resetToDefaultsButton;

    private ColorAdjustments _colorAdjustments;
    private bool _applying;

    private void Start()
    {
        CacheBrightnessOverride();
        BindSliders();
        BindToggles();

        if (resetToDefaultsButton != null)
            resetToDefaultsButton.onClick.AddListener(OnResetPressed);

        SyncControlsToCurrentState();
    }

    private void OnDestroy()
    {
        // Removing what we added keeps this safe if the menu prefab is ever pooled or
        // re-opened rather than recreated.
        if (brightnessSlider != null) brightnessSlider.onValueChanged.RemoveListener(SetBrightness);
        if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
        if (fontSizeSlider != null) fontSizeSlider.onValueChanged.RemoveListener(OnFontSizeChanged);
        if (ambienceVolumeSlider != null) ambienceVolumeSlider.onValueChanged.RemoveListener(OnAmbienceChanged);
        if (uiVolumeSlider != null) uiVolumeSlider.onValueChanged.RemoveListener(OnUiVolumeChanged);
        if (movementVolumeSlider != null) movementVolumeSlider.onValueChanged.RemoveListener(OnMovementVolumeChanged);
        if (resetToDefaultsButton != null) resetToDefaultsButton.onClick.RemoveListener(OnResetPressed);
    }

    // ------------------------------------------------------------------ binding

    /// <summary>
    /// Each listener is registered exactly once, here.
    ///
    /// The previous version registered SetBrightness from *inside* the brightness slider's own
    /// value-changed callback, so every movement of the slider added another permanent
    /// listener - one drag registered dozens, and they were never removed. It also mutated the
    /// event's invocation list while that event was being invoked.
    /// </summary>
    private void BindSliders()
    {
        if (brightnessSlider != null) brightnessSlider.onValueChanged.AddListener(SetBrightness);
        if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        if (fontSizeSlider != null) fontSizeSlider.onValueChanged.AddListener(OnFontSizeChanged);
        if (ambienceVolumeSlider != null) ambienceVolumeSlider.onValueChanged.AddListener(OnAmbienceChanged);
        if (uiVolumeSlider != null) uiVolumeSlider.onValueChanged.AddListener(OnUiVolumeChanged);
        if (movementVolumeSlider != null) movementVolumeSlider.onValueChanged.AddListener(OnMovementVolumeChanged);
    }

    private void BindToggles()
    {
        foreach (var toggle in usageModeToggles)
        {
            if (toggle == null) continue;
            Toggle captured = toggle;
            captured.onValueChanged.AddListener(state =>
            {
                if (!state || _applying) return;

                if (state)
                {
                    UsageModeController usageController =
                        UsageModeController.Instance;

                    if (usageController == null)
                    {
                        Debug.LogWarning("UsageModeController was not found.");
                        return;
                    }

                    if (toggle.name == "StandingToggle")
                    {
                        usageController.SetUsageMode(PlayerUsageMode.Standing);
                    }
                    else if (toggle.name == "SittingToggle")
                    {
                        usageController.SetUsageMode(PlayerUsageMode.Sitting);
                    }
                }
            });
        }
        // Sync usage mode toggles
        SyncUsageModeToggles();

        foreach (var toggle in handToggles)
        {
            if (toggle == null) continue;
            Toggle captured = toggle;
            captured.onValueChanged.AddListener(state =>
            {
                if (!state || _applying) return;

                var manager = ControllerHandednessManager.Instance;
                if (manager == null)
                {
                    Debug.LogWarning("[Settings] ControllerHandednessManager was not found.");
                    return;
                }

                if (captured.name == "LeftToggle") manager.SelectHand(ControllerHand.Left);
                else if (captured.name == "RightToggle") manager.SelectHand(ControllerHand.Right);
            });
        }

        // Sync hand toggles
        ControllerHandednessManager handednessManager =
            ControllerHandednessManager.Instance;

        if (handednessManager != null &&
            handednessManager.HasResolvedHand)
        {
            SyncHandToggles(handednessManager.ActiveHand);
        }

        // Rotation Toggle Setup
        foreach (var toggle in rotationToggles)
        {
            if (toggle == null) continue;
            Toggle captured = toggle;
            captured.onValueChanged.AddListener(state =>
            {
                if (!state || _applying) return;

                // NOTE (from feat/guided-tutorial, still unresolved): SnapTurnTask
                // detects a single-frame yaw jump, which by design never fires under
                // continuous rotation. With this setting live, a participant who
                // chooses continuous reaches the camera step of the tutorial and
                // cannot complete it. Either make the task mode-aware, or force snap
                // turn for the tutorial's duration and restore the preference after.
                if (state)
                {
                    RotationModeController rotationController =
                        RotationModeController.Instance;

                    if (rotationController == null)
                    {
                        Debug.LogWarning("RotationModeController was not found.");
                        return;
                    }

                    if (toggle.name == "ContinuousToggle")
                    {
                        rotationController.SetRotationMode(
                            PlayerRotationMode.Continuous
                        );
                    }
                    else if (toggle.name == "SnapToggle")
                    {
                        rotationController.SetRotationMode(
                            PlayerRotationMode.Snap
                        );
                    }
                    else if (toggle.name == "RawToggle")
                    {
                        rotationController.SetRotationMode(
                            PlayerRotationMode.HeadOnly
                        );
                    }
                }
            });
        }
        // Sync toggles
        SyncRotationToggles();
    }

    /// <summary>
    /// Moves the controls to match the settings that were restored at startup, without those
    /// moves being read as the participant changing anything. Set a slider's value and Unity
    /// raises onValueChanged, so without the guard flag this would immediately write the
    /// control's default straight back over the restored value.
    /// </summary>
    private void SyncControlsToCurrentState()
    {
        var settings = AccessibilitySettings.Instance;
        if (settings == null) return;

        _applying = true;

        if (volumeSlider != null) volumeSlider.value = settings.MasterVolume01;
        if (ambienceVolumeSlider != null) ambienceVolumeSlider.value = settings.AmbienceVolume01;
        if (uiVolumeSlider != null) uiVolumeSlider.value = settings.UiVolume01;
        if (movementVolumeSlider != null) movementVolumeSlider.value = settings.MovementVolume01;
        if (fontSizeSlider != null) fontSizeSlider.value = settings.FontScaleAsSlider01();

        foreach (var toggle in handToggles)
        {
            if (toggle == null) continue;
            bool isLeft = toggle.name == "LeftToggle";
            bool handIsLeft = ControllerHandednessManager.CurrentOrDefault(ControllerHand.Right)
                              == ControllerHand.Left;
            toggle.isOn = isLeft == handIsLeft;
        }

        _applying = false;
    }

    // ------------------------------------------------------------------ handlers

    private void OnFontSizeChanged(float t)
    {
        if (_applying) return;
        if (AccessibilitySettings.Instance != null)
            AccessibilitySettings.Instance.SetFontScaleFromSlider01(t);
    }

    private void OnMasterVolumeChanged(float v)
    {
        if (_applying) return;
        if (AccessibilitySettings.Instance != null)
            AccessibilitySettings.Instance.SetMasterVolume01(v);
    }

    private void OnAmbienceChanged(float v)
    {
        if (_applying) return;
        if (AccessibilitySettings.Instance != null)
            AccessibilitySettings.Instance.SetAmbienceVolume01(v);
    }

    private void OnUiVolumeChanged(float v)
    {
        if (_applying) return;
        if (AccessibilitySettings.Instance != null)
            AccessibilitySettings.Instance.SetUiVolume01(v);
    }

    private void OnMovementVolumeChanged(float v)
    {
        if (_applying) return;
        if (AccessibilitySettings.Instance != null)
            AccessibilitySettings.Instance.SetMovementVolume01(v);
    }

    private void OnResetPressed()
    {
        if (AccessibilitySettings.Instance == null) return;
        AccessibilitySettings.Instance.ResetToDefaults();
        SyncControlsToCurrentState();
    }

    // ------------------------------------------------------------------ brightness

    private void CacheBrightnessOverride()
    {
        if (brightnessProfile == null)
        {
            Debug.LogWarning("[Settings] No brightness profile assigned.", this);
            return;
        }

        if (!brightnessProfile.TryGet(out _colorAdjustments))
        {
            Debug.LogWarning("[Settings] Brightness profile has no ColorAdjustments override, " +
                             "so the brightness slider will do nothing.", this);
        }
    }

    private void SetBrightness(float sliderValue)
    {
        if (_applying || _colorAdjustments == null) return;

        // Exposure in EV. The range is deliberately narrower than +/-2: at the extremes the
        // baked lighting in the tutorial scene is either washed out or crushed to the point
        // that kerbs and path edges stop being readable, which is a safety-relevant detail in
        // a route-rehearsal module.
        _colorAdjustments.postExposure.value = Mathf.Lerp(-1.2f, 1.2f, Mathf.Clamp01(sliderValue));
    }

    // TOGGLE METHODS
    private static void SetToggleState(
        List<Toggle> toggles,
        string toggleName,
        bool isOn
    )
    /**
        Sets the state of a toggle in the provided list by its name.
        Needed so that the UI settings are actually synced to the current setup when checking the page again.
    */
    {
        Toggle toggle = toggles.Find(item => item.name == toggleName);

        if (toggle != null)
        {
            toggle.SetIsOnWithoutNotify(isOn);
        }
    }

    private void SyncRotationToggles()
    {
        RotationModeController controller =
            RotationModeController.Instance;

        if (controller == null)
        {
            return;
        }

        SetToggleState(
            rotationToggles,
            "ContinuousToggle",
            controller.CurrentMode == PlayerRotationMode.Continuous
        );

        SetToggleState(
            rotationToggles,
            "SnapToggle",
            controller.CurrentMode == PlayerRotationMode.Snap
        );

        SetToggleState(
            rotationToggles,
            "RawToggle",
            controller.CurrentMode == PlayerRotationMode.HeadOnly
        );
    }

    private void SyncHandToggles(ControllerHand activeHand)
    {
        SetToggleState(
            handToggles,
            "LeftToggle",
            activeHand == ControllerHand.Left
        );

        SetToggleState(
            handToggles,
            "RightToggle",
            activeHand == ControllerHand.Right
        );
    }

    private void SyncUsageModeToggles()
    {
        UsageModeController controller =
            UsageModeController.Instance;

        if (controller == null)
        {
            return;
        }

        SetToggleState(
            usageModeToggles,
            "StandingToggle",
            controller.CurrentMode == PlayerUsageMode.Standing
        );

        SetToggleState(
            usageModeToggles,
            "SittingToggle",
            controller.CurrentMode == PlayerUsageMode.Sitting
        );
    }

    private void OnEnable()
    {
        ControllerHandednessManager.HandChanged += SyncHandToggles;
    }

    private void OnDisable()
    {
        ControllerHandednessManager.HandChanged -= SyncHandToggles;
    }
}


