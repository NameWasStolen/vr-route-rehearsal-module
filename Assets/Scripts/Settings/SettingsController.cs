using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Audio;
using TMPro;

public class SettingsController : MonoBehaviour
{
    public Slider brightnessSlider;
    public Slider volumeSlider;
    public Slider fontSizeSlider;
    public List<Toggle> usageModeToggles = new();
    public List<Toggle> handToggles = new();
    public List<Toggle> rotationToggles = new();
    public VolumeProfile brightnessProfile;
    private ColorAdjustments colorAdjustments;
    public AudioMixer audioMixer;
    public List<TMP_Text> textElements = new();
    private List<float> originalFontSizes = new();

    void Start()
    {
        // Brightness Slider Setup
        brightnessSlider.onValueChanged.AddListener(value =>
        {
            brightnessProfile.TryGet(out colorAdjustments);
            brightnessSlider.onValueChanged.AddListener(SetBrightness);
            SetBrightness(brightnessSlider.value);
            Debug.Log(brightnessSlider.name + " changed value to: " + value);
        });

        // Volume Slider Setup
        if (audioMixer == null)
        {
            Debug.LogError("Audio Mixer is not assigned.");
        }
        else
        {
            volumeSlider.onValueChanged.AddListener(SetVolume);
            SetVolume(volumeSlider.value);
        }

        // Font Size Slider Setup
        originalFontSizes.Clear();

        foreach (TMP_Text textElement in textElements)
        {
            originalFontSizes.Add(textElement.fontSize);
        }

        fontSizeSlider.onValueChanged.AddListener(SetFontSize);
        SetFontSize(fontSizeSlider.value);

        // Usage Mode Toggle Setup
        foreach (var toggle in usageModeToggles)
        {
            toggle.onValueChanged.AddListener(state =>
            {
                Debug.Log(toggle.name + " changed to: " + state);

                if (state)
                {
                    if (toggle.name == "StandingToggle")
                    {
                        // TODO
                    } else if (toggle.name == "SittingToggle")
                    {
                        // TODO
                    }
                }
            });
        }

        // Hand Toggle Setup
        foreach (var toggle in handToggles)
        {
            toggle.onValueChanged.AddListener(state =>
            {
                Debug.Log(toggle.name + " changed to: " + state);

                if (state)
                    {
                        ControllerHandednessManager handednessManager =
                            ControllerHandednessManager.Instance;

                        if (handednessManager == null)
                        {
                            Debug.LogWarning(
                                "ControllerHandednessManager was not found."
                            );

                            return;
                        }

                        if (toggle.name == "LeftToggle")
                        {
                            handednessManager.SelectHand(ControllerHand.Left);
                        }
                        else if (toggle.name == "RightToggle")
                        {
                            handednessManager.SelectHand(ControllerHand.Right);
                        }
                    }
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
            toggle.onValueChanged.AddListener(state =>
            {
                Debug.Log(toggle.name + " changed to: " + state);

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

    void SetBrightness(float sliderValue)
    {
        float exposure = Mathf.Lerp(-2f, 2f, sliderValue);
        colorAdjustments.postExposure.value = exposure;
    }

    void SetVolume(float sliderValue)
    {
        float decibels = Mathf.Lerp(-20f, 0f, sliderValue);
        audioMixer.SetFloat("MasterVolume", decibels);
    }

    void SetFontSize(float sliderValue)
    {
        float sizeMultiplier = Mathf.Lerp(0.8f, 1.4f, sliderValue);

        for (int index = 0; index < textElements.Count; index++)
        {
            if (textElements[index] != null)
            {
                textElements[index].fontSize =
                    originalFontSizes[index] * sizeMultiplier;
            }
        }
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
    private void OnEnable()
    {
        ControllerHandednessManager.HandChanged += SyncHandToggles;
    }

    private void OnDisable()
    {
        ControllerHandednessManager.HandChanged -= SyncHandToggles;
    }
}


