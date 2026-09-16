using UnityEngine;

/// <summary>
/// Puts a banner on the mirrored desktop view - and nowhere else - so whoever is running the
/// session sees that something has happened without the participant seeing anything extra.
///
/// Drawn with OnGUI, and that choice is the whole trick. IMGUI renders straight to the game view
/// or the desktop window, after the XR mirror blit and outside the XR camera stack entirely, so
/// it cannot reach the headset's eye textures. The obvious alternatives are all worse: a
/// screen-space Canvas renders through a camera and therefore shows up in the headset, which is
/// exactly the thing that must not happen, and a separate spectator camera with
/// stereoTargetEye None is fiddly, configuration-dependent, and needs a layer excluded from
/// every headset camera to work at all.
///
/// Scope, stated plainly: this only means anything on PCVR or Quest Link, where a desktop window
/// exists. In a standalone Android build on the headset there is no second screen for it to draw
/// to, and the session log is the whole of the record.
///
/// OnGUI costs a little per frame, so it early-outs to nothing while hidden.
/// </summary>
[DisallowMultipleComponent]
public class SpectatorMarker : MonoBehaviour
{
    [Header("Message")]
    [Tooltip("Shown when Show() is called with no argument.")]
    [SerializeField] private string defaultMessage = "ASSISTANCE REQUESTED";

    [Header("Appearance")]
    [Tooltip("Band height as a fraction of the window height.")]
    [Range(0.04f, 0.3f)]
    [SerializeField] private float heightFraction = 0.09f;

    [Tooltip("Text height as a fraction of the band. Kept relative so the banner stays readable " +
             "whatever the researcher has sized their window to.")]
    [Range(0.2f, 0.9f)]
    [SerializeField] private float textFraction = 0.45f;

    [SerializeField] private Color background = new Color(0.72f, 0.10f, 0.10f, 0.92f);
    [SerializeField] private Color textColour = Color.white;

    [Tooltip("Pulse the band so it catches the eye of somebody not looking directly at the " +
             "screen. Set to 0 for a steady band.")]
    [Range(0f, 3f)]
    [SerializeField] private float blinksPerSecond = 0.8f;

    [Header("Console")]
    [Tooltip("Also write a console line when the marker appears. Useful when the Editor is open " +
             "but the game view is not the window being watched.")]
    [SerializeField] private bool logToConsole = true;

    private bool _visible;
    private string _message;
    private Texture2D _fill;
    private GUIStyle _style;

    public bool IsVisible => _visible;

    public void Show() => Show(defaultMessage);

    public void Show(string message)
    {
        _message = string.IsNullOrEmpty(message) ? defaultMessage : message;
        _visible = true;
        if (logToConsole) Debug.LogWarning($"[SpectatorMarker] {_message}");
    }

    public void Hide() => _visible = false;

    private void OnDisable()
    {
        _visible = false;
    }

    private void OnDestroy()
    {
        if (_fill != null) Destroy(_fill);
    }

    private void OnGUI()
    {
        if (!_visible) return;

        if (_fill == null)
        {
            _fill = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _fill.SetPixel(0, 0, Color.white);
            _fill.Apply();
        }

        float bandHeight = Mathf.Max(24f, Screen.height*heightFraction);
        var rect = new Rect(0f, 0f, Screen.width, bandHeight);

        Color tint = background;
        if (blinksPerSecond > 0f)
        {
            // Never fades all the way out: a banner that disappears between pulses can be missed
            // entirely by somebody glancing at the screen at the wrong moment.
            float pulse = 0.65f + 0.35f*Mathf.Abs(Mathf.Sin(Time.unscaledTime*Mathf.PI*blinksPerSecond));
            tint.a *= pulse;
        }

        Color previous = GUI.color;
        GUI.color = tint;
        GUI.DrawTexture(rect, _fill);
        GUI.color = previous;

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
        }

        _style.fontSize = Mathf.RoundToInt(bandHeight*textFraction);
        _style.normal.textColor = textColour;

        GUI.Label(rect, _message, _style);
    }
}
