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
                    if (toggle.name == "LeftToggle")
                    {
                        // TODO
                    }
                    else if (toggle.name == "RightToggle")
                    {
                        // TODO
                    }
                }
            });
        }

        // Rotation Toggle Setup
        foreach (var toggle in rotationToggles)
        {
            toggle.onValueChanged.AddListener(state =>
            {
                Debug.Log(toggle.name + " changed to: " + state);

                if (state)
                {
                    if (toggle.name == "ContinuousToggle")
                    {
                        // TODO
                    }
                    else if (toggle.name == "SnapToggle")
                    {
                        // TODO
                    }
                    else if (toggle.name == "RawToggle")
                    {
                        // TODO
                    }
                }
            });
        }
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
}
