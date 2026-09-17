using UnityEngine;

/// <summary>
/// Scales an entire UI panel uniformly with the accessibility font scale.
///
/// This is the right tool for a panel whose contents are hand-positioned - fixed sizeDelta,
/// fixed anchoredPosition, no layout groups. MainMenuScreen is exactly that: its settings rows
/// sit at y = 40, 0, -40, -110, -150, -190, a 40-unit pitch that was measured by eye against
/// 24 pt text. Growing the text inside that layout does not move the rows, so at around 1.4x
/// the lines are taller than the gap between them and rows collide. Nothing reflows, because
/// there is no layout group anywhere in the prefab to do the reflowing.
///
/// Scaling the root instead multiplies positions, sizes and glyphs by the same factor, so the
/// design holds together exactly as authored at every scale. Overlap becomes impossible rather
/// than merely unlikely.
///
/// It also picks up things ScalableText cannot touch at all - the seven toggle labels here are
/// legacy UnityEngine.UI.Text rather than TMP, so they can never take a ScalableText. As
/// children of this transform they scale anyway, for free.
///
/// The trade-off, stated plainly: this magnifies the whole panel, chrome included. You do not
/// get more readable text in the same space, you get the same design larger. For a menu that
/// is read once at the start of a session that is the right call. For dense content you would
/// want real layout groups instead.
///
/// Do NOT combine with ScalableRect on the same panel - it would apply the same scale a second
/// time, and this component warns if it finds one. A ScalableText beneath it is harmless: it
/// notices this component and stands down.
/// </summary>
[DisallowMultipleComponent]
public class ScalableUIRoot : MonoBehaviour
{
    [Tooltip("Ceiling on total magnification. In VR a panel that grows without limit " +
             "eventually extends past comfortable head-turn range, at which point it is " +
             "harder to read, not easier. Check the widest row - Rotation, with three " +
             "toggles - still sits inside view at whatever you set here.")]
    [Range(1f, 2f)]
    [SerializeField] private float maxScale = 1.6f;

    [Tooltip("Allow the panel to shrink below its authored size when the scale is under 1. " +
             "Off by default: making the menu smaller for someone who reduced the text also " +
             "makes every button a smaller target.")]
    [SerializeField] private bool allowShrink = false;

    private Vector3 _authoredScale;
    private bool _captured;

    private void Awake()
    {
        Capture();
        WarnAboutDoubleScaling();
    }

    private void Capture()
    {
        if (_captured) return;
        _authoredScale = transform.localScale;
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
        if (!_captured) return;

        float s = Mathf.Min(scale, maxScale);
        if (!allowShrink) s = Mathf.Max(s, 1f);

        transform.localScale = _authoredScale * s;
    }

    private void WarnAboutDoubleScaling()
    {
        // ScalableText defers to this component automatically, so only ScalableRect can still
        // double up.
        var rects = GetComponentsInChildren<ScalableRect>(true);
        if (rects.Length == 0) return;

        Debug.LogWarning(
            $"[ScalableUIRoot] '{name}' scales this whole panel, but found {rects.Length} " +
            "ScalableRect beneath it. That applies the same scale a second time, so those rects " +
            "will grow roughly twice as fast as the panel around them. Remove them from this panel.", this);
    }
}
