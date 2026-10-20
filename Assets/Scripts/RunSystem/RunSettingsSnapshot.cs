using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public readonly struct RunSettingsSnapshot
{
    public RunSettingsSnapshot(
        float brightness,
        float volume,
        float fontSize,
        string usageMode,
        string handedness,
        string rotationMode)
    {
        Brightness = brightness;
        Volume = volume;
        FontSize = fontSize;
        UsageMode = usageMode;
        Handedness = handedness;
        RotationMode = rotationMode;
    }

    public float Brightness { get; }
    public float Volume { get; }
    public float FontSize { get; }
    public string UsageMode { get; }
    public string Handedness { get; }
    public string RotationMode { get; }

    public static RunSettingsSnapshot Capture(SettingsController settingsController)
    {
        ControllerHand fallbackHand = ControllerHand.Right;
        ControllerHand activeHand = ControllerHandednessManager.CurrentOrDefault(fallbackHand);

        return new RunSettingsSnapshot(
            settingsController != null && settingsController.brightnessSlider != null
                ? settingsController.brightnessSlider.value
                : 0f,
            settingsController != null && settingsController.volumeSlider != null
                ? settingsController.volumeSlider.value
                : 0f,
            settingsController != null && settingsController.fontSizeSlider != null
                ? settingsController.fontSizeSlider.value
                : 0f,
            GetSelectedToggleName(settingsController?.usageModeToggles),
            activeHand.ToString(),
            GetSelectedToggleName(settingsController?.rotationToggles)
        );
    }

    private static string GetSelectedToggleName(List<Toggle> toggles)
    {
        if (toggles == null)
            return string.Empty;

        Toggle selectedToggle = toggles.FirstOrDefault(toggle =>
            toggle != null && toggle.isOn);

        return selectedToggle != null ? selectedToggle.name : string.Empty;
    }
}