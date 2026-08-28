using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        volumeSlider.onValueChanged.AddListener(value =>
        {
            Debug.Log(volumeSlider.name + " changed value to: " + value);
        });

        // Font Size Slider Setup
        fontSizeSlider.onValueChanged.AddListener(value =>
        {
            Debug.Log(fontSizeSlider.name + " changed value to: " + value);
        });

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
}
