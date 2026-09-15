using TMPro;
using UnityEngine;

/// <summary>
/// Makes one TMP text follow the accessibility font scale. Drop it on a text once and it is
/// covered forever, in any scene, with no Inspector list to maintain.
///
/// The authored size is captured in Awake and every application multiplies THAT, never the
/// current size. This is the same trick HandedVisual uses for mirroring, and for the same
/// reason: applying a scale twice must not compound. A component that multiplied the live
/// value would creep upward every time the slider moved and never find its way back.
///
/// Note this changes fontSize, not localScale. Scaling the transform would blur the glyphs,
/// because TMP renders from a signed distance field sized for the font size it was given.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class ScalableText : MonoBehaviour
{
    [Tooltip("If this text uses TMP auto-sizing, scale its min/max bounds too. Without this " +
             "auto-sizing would simply re-shrink the text back to where it started and the " +
             "setting would appear to do nothing.")]
    [SerializeField] private bool scaleAutoSizeBounds = true;

    [Tooltip("Extra multiplier for this specific text. Use it where one label should grow " +
             "more or less than the rest - a title that is already large, say.")]
    [Range(0.5f, 1.5f)]
    [SerializeField] private float localWeight = 1f;

    private TMP_Text _text;
    private float _authoredSize;
    private float _authoredMin;
    private float _authoredMax;
    private bool _captured;

    private void Awake() => Capture();

    private void Capture()
    {
        if (_captured) return;

        _text = GetComponent<TMP_Text>();
        if (_text == null) return;

        _authoredSize = _text.fontSize;
        _authoredMin = _text.fontSizeMin;
        _authoredMax = _text.fontSizeMax;
        _captured = true;
    }

    private void OnEnable()
    {
        Capture();

        // Read the current value before subscribing. In an additively-loaded scene the event
        // fired long ago, during Bootstrap.
        Apply(AccessibilitySettings.CurrentOrDefault(AccessibilitySettings.DefaultFontScale));
        AccessibilitySettings.FontScaleChanged += Apply;
    }

    private void OnDisable()
    {
        AccessibilitySettings.FontScaleChanged -= Apply;
    }

    private void Apply(float scale)
    {
        if (!_captured || _text == null) return;

        float s = scale * localWeight;
        _text.fontSize = _authoredSize * s;

        if (scaleAutoSizeBounds && _text.enableAutoSizing)
        {
            _text.fontSizeMin = _authoredMin * s;
            _text.fontSizeMax = _authoredMax * s;
        }
    }
}
