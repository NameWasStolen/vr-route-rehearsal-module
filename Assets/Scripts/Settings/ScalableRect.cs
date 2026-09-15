using UnityEngine;

/// <summary>
/// Grows a panel's frame in step with the font scale, so larger text does not overflow or clip.
///
/// Why sizeDelta rather than localScale: if the glyphs grow by 1.4x (ScalableText) and the
/// frame grows by 1.4x too, the text occupies exactly the same fraction of the panel as before,
/// so every line break lands in the same place and the layout you authored is preserved -
/// it is simply bigger. Scaling localScale instead would grow the frame AND the already-grown
/// text, applying the setting twice.
///
/// DO NOT put this on the tutorial panel. TutorialFlow owns that RectTransform's sizeDelta and
/// rewrites it on every step change, so the two would fight and the loser would be whichever
/// ran last. TutorialFlow folds the font scale in itself - see its patch.
/// Use this on MainMenuScreen and any other standalone panel.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class ScalableRect : MonoBehaviour
{
    [SerializeField] private bool scaleWidth = true;
    [SerializeField] private bool scaleHeight = true;

    [Tooltip("Ceiling on how much the frame may grow, independent of how far the font scale " +
             "goes. A menu panel that grows without limit will eventually extend past " +
             "comfortable head-turn range, at which point bigger text is harder to read, not " +
             "easier. 1.0 pins the frame and lets the text grow within it.")]
    [Range(1f, 2f)]
    [SerializeField] private float maxGrowth = 1.5f;

    private RectTransform _rect;
    private Vector2 _authoredSize;
    private bool _captured;

    private void Awake() => Capture();

    private void Capture()
    {
        if (_captured) return;
        _rect = transform as RectTransform;
        if (_rect == null) return;
        _authoredSize = _rect.sizeDelta;
        _captured = true;
    }

    private void OnEnable()
    {
        Capture();
        Apply(AccessibilitySettings.CurrentOrDefault(AccessibilitySettings.DefaultFontScale));
        AccessibilitySettings.FontScaleChanged += Apply;
    }

    private void OnDisable()
    {
        AccessibilitySettings.FontScaleChanged -= Apply;
    }

    private void Apply(float scale)
    {
        if (!_captured || _rect == null) return;

        float g = Mathf.Clamp(scale, 1f, maxGrowth);

        // Below 1x the frame is left alone. Shrinking a panel because someone reduced the text
        // size makes the target smaller for a person who may also be reaching less accurately.
        Vector2 size = _authoredSize;
        if (scaleWidth) size.x = _authoredSize.x * g;
        if (scaleHeight) size.y = _authoredSize.y * g;
        _rect.sizeDelta = size;
    }
}
