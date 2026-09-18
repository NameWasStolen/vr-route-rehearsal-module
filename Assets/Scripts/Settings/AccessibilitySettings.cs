using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The single home for accessibility state that has to reach every scene.
///
/// Lives on Bootstrap alongside ControllerHandednessManager and deliberately mirrors its
/// shape - singleton, static change event, and a static accessor with a fallback - because
/// the hard problem here is the same one: content scenes load additively AFTER Bootstrap has
/// already applied its settings, so a component that only listens for the event would never
/// hear the one that mattered. Anything that cares reads CurrentOrDefault first, then
/// subscribes.
///
/// This is what replaces SettingsController's hand-populated List&lt;TMP_Text&gt;. That list could
/// only ever hold objects inside MainMenuScreen.prefab, because Unity cannot serialise a
/// reference from a prefab in one scene to an object in another. The tutorial text was
/// therefore unreachable by design, not by oversight. Inverting it - text finds the setting
/// rather than the setting finding the text - is the only thing that works across additive
/// scene loads, and it means new UI is covered the moment it gets a ScalableText.
///
/// Execution order is forced late so that whatever ControllerHandednessManager does in its own
/// Start has already happened before the saved hand is restored on top of it.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class AccessibilitySettings : MonoBehaviour
{
    public static AccessibilitySettings Instance { get; private set; }

    /// <summary>
    /// Raised whenever the text scale changes, and once during startup after saved values load.
    /// Subscribers that may enable later should still call CurrentOrDefault on enable - by the
    /// time an additive scene loads, this has usually already fired.
    /// </summary>
    public static event Action<float> FontScaleChanged;

    public const float DefaultFontScale = 1f;
    public const float MinFontScale = 0.8f;
    public const float MaxFontScale = 1.8f;

    [Header("Audio mixer")]
    [Tooltip("MainAudioMixer. Each bus below needs its volume parameter exposed on the mixer " +
             "with the matching name, or that slider silently does nothing.")]
    [SerializeField] private AudioMixer mixer;

    [SerializeField] private string masterParam = "MasterVolume";
    [SerializeField] private string ambienceParam = "AmbienceVolume";
    [SerializeField] private string uiParam = "UIVolume";
    [SerializeField] private string movementParam = "MovementVolume";

    [Header("Defaults")]
    [Tooltip("Used on a fresh install, and restored by ResetToDefaults between participants.")]
    [Range(MinFontScale, MaxFontScale)]
    [SerializeField] private float defaultFontScale = DefaultFontScale;

    [Range(0f, 1f)] [SerializeField] private float defaultMasterVolume = 0.85f;

    [Tooltip("Street ambience defaults lower than the rest. It is the one bus that is pure " +
             "atmosphere, and it is also the one most likely to mask an instruction.")]
    [Range(0f, 1f)] [SerializeField] private float defaultAmbienceVolume = 0.45f;

    [Range(0f, 1f)] [SerializeField] private float defaultUiVolume = 0.9f;
    [Range(0f, 1f)] [SerializeField] private float defaultMovementVolume = 0.7f;

    [Header("Persistence")]
    [Tooltip("Untick to make every launch start from the defaults above, ignoring anything " +
             "previously saved. Useful if you ever want guaranteed-identical study conditions.")]
    [SerializeField] private bool persistBetweenSessions = true;

    [Tooltip("Also remember the controller hand chosen in settings. Handedness itself stays " +
             "owned by ControllerHandednessManager; this only saves and restores the choice.")]
    [SerializeField] private bool persistHandChoice = true;

    // PlayerPrefs keys. Versioned so that changing the meaning of a value later can be done by
    // bumping the prefix rather than by trying to migrate whatever is on a headset already.
    private const string K = "access.v1.";
    private const string KeyFont = K + "fontScale";
    private const string KeyMaster = K + "master";
    private const string KeyAmbience = K + "ambience";
    private const string KeyUi = K + "ui";
    private const string KeyMovement = K + "movement";
    private const string KeyHand = K + "hand";

    private float _fontScale = DefaultFontScale;
    private float _master, _ambience, _ui, _movement;
    private bool _loaded;

    public float FontScale => _fontScale;
    public float MasterVolume01 => _master;
    public float AmbienceVolume01 => _ambience;
    public float UiVolume01 => _ui;
    public float MovementVolume01 => _movement;

    /// <summary>
    /// Text scale for a component that has just enabled. Falls back when this has not run yet,
    /// which is the case when a content scene is opened standalone without Bootstrap.
    /// </summary>
    public static float CurrentOrDefault(float fallback)
    {
        return Instance != null && Instance._loaded ? Instance._fontScale : fallback;
    }

    public static float CurrentFontScale => CurrentOrDefault(DefaultFontScale);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Two of these means two sources of truth for the same sliders.
            Debug.LogWarning("[AccessibilitySettings] A second instance was found on " +
                             $"'{name}'. Destroying it; keep exactly one, on Bootstrap.", this);
            Destroy(this);
            return;
        }

        Instance = this;
        Load();
    }

    private void Start()
    {
        // Push everything outward once, after the rest of Bootstrap has had its own Start.
        ApplyAll();

        if (persistHandChoice) RestoreSavedHand();

        ControllerHandednessManager.HandChanged += OnHandChanged;
    }

    private void OnDestroy()
    {
        ControllerHandednessManager.HandChanged -= OnHandChanged;
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ loading

    private void Load()
    {
        _fontScale = defaultFontScale;
        _master = defaultMasterVolume;
        _ambience = defaultAmbienceVolume;
        _ui = defaultUiVolume;
        _movement = defaultMovementVolume;

        if (persistBetweenSessions)
        {
            _fontScale = PlayerPrefs.GetFloat(KeyFont, _fontScale);
            _master = PlayerPrefs.GetFloat(KeyMaster, _master);
            _ambience = PlayerPrefs.GetFloat(KeyAmbience, _ambience);
            _ui = PlayerPrefs.GetFloat(KeyUi, _ui);
            _movement = PlayerPrefs.GetFloat(KeyMovement, _movement);
        }

        Clamp();
        _loaded = true;
    }

    private void Clamp()
    {
        _fontScale = Mathf.Clamp(_fontScale, MinFontScale, MaxFontScale);
        _master = Mathf.Clamp01(_master);
        _ambience = Mathf.Clamp01(_ambience);
        _ui = Mathf.Clamp01(_ui);
        _movement = Mathf.Clamp01(_movement);
    }

    private void ApplyAll()
    {
        PushVolume(masterParam, _master);
        PushVolume(ambienceParam, _ambience);
        PushVolume(uiParam, _ui);
        PushVolume(movementParam, _movement);
        FontScaleChanged?.Invoke(_fontScale);
    }

    // ------------------------------------------------------------------ setters

    public void SetFontScale(float scale)
    {
        scale = Mathf.Clamp(scale, MinFontScale, MaxFontScale);
        if (Mathf.Approximately(scale, _fontScale)) return;

        _fontScale = scale;
        Save(KeyFont, _fontScale);
        FontScaleChanged?.Invoke(_fontScale);
    }

    /// <summary>Maps a 0-1 slider straight onto the font scale range, for UI convenience.</summary>
    public void SetFontScaleFromSlider01(float t)
    {
        SetFontScale(Mathf.Lerp(MinFontScale, MaxFontScale, Mathf.Clamp01(t)));
    }

    /// <summary>Inverse of the above, for positioning the slider from a restored value.</summary>
    public float FontScaleAsSlider01()
    {
        return Mathf.InverseLerp(MinFontScale, MaxFontScale, _fontScale);
    }

    public void SetMasterVolume01(float v) { _master = Mathf.Clamp01(v); PushVolume(masterParam, _master); Save(KeyMaster, _master); }
    public void SetAmbienceVolume01(float v) { _ambience = Mathf.Clamp01(v); PushVolume(ambienceParam, _ambience); Save(KeyAmbience, _ambience); }
    public void SetUiVolume01(float v) { _ui = Mathf.Clamp01(v); PushVolume(uiParam, _ui); Save(KeyUi, _ui); }
    public void SetMovementVolume01(float v) { _movement = Mathf.Clamp01(v); PushVolume(movementParam, _movement); Save(KeyMovement, _movement); }

    /// <summary>
    /// Back to the authored defaults, for handing the headset to the next participant.
    /// Wire this to a button the operator can reach, not one a participant will find.
    /// </summary>
    public void ResetToDefaults()
    {
        _fontScale = defaultFontScale;
        _master = defaultMasterVolume;
        _ambience = defaultAmbienceVolume;
        _ui = defaultUiVolume;
        _movement = defaultMovementVolume;
        Clamp();

        PlayerPrefs.DeleteKey(KeyFont);
        PlayerPrefs.DeleteKey(KeyMaster);
        PlayerPrefs.DeleteKey(KeyAmbience);
        PlayerPrefs.DeleteKey(KeyUi);
        PlayerPrefs.DeleteKey(KeyMovement);
        PlayerPrefs.DeleteKey(KeyHand);
        PlayerPrefs.Save();

        ApplyAll();
        Debug.Log("[AccessibilitySettings] Reset to defaults.", this);
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>
    /// A 0-1 slider is not a volume. Loudness is roughly logarithmic, so a linear map to
    /// decibels - which is what the original Lerp(-20, 0, v) did - spends most of the travel
    /// on changes nobody can hear and never actually reaches silence. This maps the slider to
    /// dB properly, so halfway sounds halfway and zero is off.
    /// </summary>
    private void PushVolume(string parameterName, float linear01)
    {
        if (mixer == null || string.IsNullOrEmpty(parameterName)) return;

        float dB = linear01 <= 0.0001f ? -80f : Mathf.Log10(linear01) * 20f;

        if (!mixer.SetFloat(parameterName, dB))
        {
            // Nearly always means the parameter was never exposed in the Audio Mixer window.
            Debug.LogWarning($"[AccessibilitySettings] Mixer has no exposed parameter named " +
                             $"'{parameterName}'. That bus's slider will do nothing.", this);
        }
    }

    private void Save(string key, float value)
    {
        if (!persistBetweenSessions) return;
        PlayerPrefs.SetFloat(key, value);
        PlayerPrefs.Save();
    }

    private void OnHandChanged(ControllerHand hand)
    {
        if (!persistHandChoice || !persistBetweenSessions) return;
        PlayerPrefs.SetInt(KeyHand, (int)hand);
        PlayerPrefs.Save();
    }

    private void RestoreSavedHand()
    {
        if (!persistBetweenSessions || !PlayerPrefs.HasKey(KeyHand)) return;

        var saved = (ControllerHand)PlayerPrefs.GetInt(KeyHand);
        if (ControllerHandednessManager.Instance != null)
            ControllerHandednessManager.Instance.SelectHand(saved);
    }
}
