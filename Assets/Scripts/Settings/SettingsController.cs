using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SettingsController : MonoBehaviour
{
    public List<Toggle> usageModeToggles = new();
    public List<Toggle> handToggles = new();
    public List<Toggle> rotationToggles = new();

    void Start()
    {
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
        foreach (var toggle in usageModeToggles)
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

        // Roation Toggle Setup
        foreach (var toggle in usageModeToggles)
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
}
