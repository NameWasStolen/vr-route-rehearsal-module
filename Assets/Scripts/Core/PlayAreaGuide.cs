using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using VRTutorial;

/// <summary>
/// Keeps the participant near the spot they started on, so they never reach the Quest's own
/// boundary - where the headset fades the app out and switches to the passthrough cameras, which
/// is startling, and more so for an older participant who does not know why it happened.
///
/// Walking in the app is done with the grip, so nobody needs to step in the real room at all.
/// People drift anyway: a step towards something they want to look at, a shuffle while turning.
/// This notices the drift early and guides them back:
///
///   - a ring with two shoe prints appears on the floor at the starting spot;
///   - a small card in front of them shows an arrow pointing the way to step, with the words
///     "Step back to the circle";
///   - if they keep going, the neutral "try again" chime plays once (the same one the tutorial
///     uses for a wrong turn), so it is noticed even while looking somewhere else.
/// Both fade away once they are back near the middle.
///
/// MEASURED IN THE REAL ROOM, not the virtual world. The distance is between where the headset is
/// now and where it was at the start, both in the rig's own tracking space - so walking with the
/// grip, snap turning and being placed at a spawn point never count as drift. Only real steps do.
///
/// THE STARTING SPOT is taken again:
///   - every time the participant is placed - the tutorial, every module, the Map and the main
///     menu (XRPlayerTeleport.AlignHeadTo calls Recentre). So if the app was opened from a desk
///     and the headset then handed to a participant across the room, the spot is wherever they
///     stand when their module starts;
///   - when the Quest's own view reset is used (hold the Meta button), which moves the tracking
///     space;
///   - on demand: hold BOTH thumbsticks pressed in for 3 seconds anywhere outside the main menu
///     (on the main menu the same hold opens the researcher screen). A confirmation tick plays.
///     F3 does the same on a keyboard, for desktop testing.
/// Each one is written to the session log, as is every time the guide appears and clears.
///
/// OFF while the main menu area is showing (MenuController holds it off: there is nothing to walk
/// to, and the participant may still be being handed the headset), and while the screen is faded
/// for a scene change.
///
/// SITTING uses tighter distances than standing: a seated participant's play area is smaller.
///
/// BUILT AT RUNTIME, with no scene setup: created once when the app starts and kept for the whole
/// session. Icons are Resources/PlayArea/StepBackArrow.png and HomeSpot.png.
/// </summary>
public class PlayAreaGuide : MonoBehaviour
{
    public const string ArrowResource = "PlayArea/StepBackArrow";
    public const string SpotResource = "PlayArea/HomeSpot";

    public static PlayAreaGuide Instance { get; private set; }

    [Header("Distances (metres from the starting spot, in the real room)")]
    [Tooltip("Standing: the guide appears once the participant has stepped this far away.")]
    [SerializeField] private float standingShowDistance = 0.5f;

    [Tooltip("Standing: the chime plays if they carry on this far.")]
    [SerializeField] private float standingChimeDistance = 0.75f;

    [Tooltip("Sitting: the guide appears this far from the spot. Smaller, because a seated play " +
             "area is smaller - but not so small that leaning to look at the controller sets it off.")]
    [SerializeField] private float sittingShowDistance = 0.4f;

    [Tooltip("Sitting: the chime plays this far from the spot.")]
    [SerializeField] private float sittingChimeDistance = 0.55f;

    [Tooltip("The guide goes away once they are back this much inside the show distance, so it " +
             "does not flicker on and off at the edge.")]
    [SerializeField] private float hideMargin = 0.15f;

    [Header("Look")]
    [SerializeField] private string label = "Step back to the circle";

    [Tooltip("Diameter of the floor circle, metres.")]
    [SerializeField] private float spotDiameter = 0.8f;

    [Tooltip("Head-local placement of the card: X right, Y up, Z forward (metres).")]
    [SerializeField] private Vector3 cardOffset = new Vector3(0f, -0.2f, 1.2f);

    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.3f;

    [Tooltip("How quickly the card follows the head (seconds to catch up). Slow enough to read " +
             "comfortably, quick enough that it never ends up behind them.")]
    [SerializeField, Min(0f)] private float cardFollowSeconds = 0.25f;

    [Header("Researcher re-centre")]
    [Tooltip("Seconds both thumbsticks must be held in to take the starting spot again.")]
    [SerializeField, Min(0.5f)] private float recentreHoldSeconds = 3f;

    // ------------------------------------------------------------------ state
    private XROrigin _origin;
    private Vector3 _homeLocal;          // head position in the rig's tracking space, at the start
    private bool _hasHome;
    private bool _showing;
    private bool _chimed;

    private static readonly HashSet<object> Suppressors = new HashSet<object>();

    private InputAction _leftStick, _rightStick;
    private float _heldFor;
    private readonly List<XRInputSubsystem> _subsystems = new List<XRInputSubsystem>();

    // ------------------------------------------------------------------ visuals
    private GameObject _card;
    private CanvasGroup _cardGroup;
    private RectTransform _arrow;
    private TMP_Text _cardText;
    private GameObject _spot;
    private CanvasGroup _spotGroup;
    private float _alpha;
    private bool _fontResolved;

    // ------------------------------------------------------------------ public API

    /// <summary>
    /// Takes the participant's current spot in the real room as the new starting spot. Safe to
    /// call from anywhere, including before the guide exists.
    /// </summary>
    public static void Recentre(string reason = "placed")
    {
        if (Instance != null) Instance.CaptureHome(reason);
    }

    /// <summary>Holds the guide off on behalf of an owner (the main menu). Held per owner.</summary>
    public static void Suppress(object owner)
    {
        if (owner != null) Suppressors.Add(owner);
    }

    public static void Release(object owner)
    {
        if (owner != null) Suppressors.Remove(owner);
    }

    public bool IsShowing => _showing;

    /// <summary>How far, in metres, the participant is from the starting spot in the real room.</summary>
    public float Drift { get; private set; }

    // ------------------------------------------------------------------ lifetime

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOnStartup()
    {
        if (Instance != null) return;
        var go = new GameObject("PlayAreaGuide (runtime)");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayAreaGuide>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // The same thumbstick-press bindings as the researcher screen.
        _leftStick = new InputAction("PlayAreaLeftStick", InputActionType.Button);
        _leftStick.AddBinding("<XRController>{LeftHand}/{Primary2DAxisClick}");
        _leftStick.AddBinding("<XRController>{LeftHand}/thumbstickClicked");
        _rightStick = new InputAction("PlayAreaRightStick", InputActionType.Button);
        _rightStick.AddBinding("<XRController>{RightHand}/{Primary2DAxisClick}");
        _rightStick.AddBinding("<XRController>{RightHand}/thumbstickClicked");

        Build();
    }

    private void OnEnable()
    {
        _leftStick?.Enable();
        _rightStick?.Enable();
    }

    private void OnDisable()
    {
        _leftStick?.Disable();
        _rightStick?.Disable();
        UnhookTrackingSpace();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _leftStick?.Dispose();
        _rightStick?.Dispose();
    }

    // ------------------------------------------------------------------ per frame

    private void Update()
    {
        if (!ResolveOrigin()) { SetShowing(false); ApplyAlpha(); return; }
        HookTrackingSpace();

        // First frame with a headset: take the spot, so there is always one.
        if (!_hasHome) CaptureHome("start");
        if (_originRecentreInFrames > 0 && --_originRecentreInFrames == 0) CaptureHome("view_reset");

        bool suppressed = Suppressors.Count > 0 || IsFaded();
        HandleResearcherRecentre(suppressed);

        Vector3 here = HeadLocal();
        Vector3 d = here - _homeLocal;
        d.y = 0f;
        Drift = d.magnitude;

        bool sitting = UsageModeController.Instance != null &&
                       UsageModeController.Instance.CurrentMode == PlayerUsageMode.Sitting;
        float show = sitting ? sittingShowDistance : standingShowDistance;
        float chime = sitting ? sittingChimeDistance : standingChimeDistance;

        if (suppressed)
        {
            SetShowing(false);
        }
        else if (!_showing && Drift >= show)
        {
            SetShowing(true);
        }
        else if (_showing && Drift <= Mathf.Max(0.05f, show - hideMargin))
        {
            SetShowing(false);
        }

        if (_showing && !_chimed && Drift >= chime)
        {
            _chimed = true;
            UiCuePlayer.Instance?.PlayGentleRetry();
            SessionLog.Record("play_area_far", Drift.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "m");
        }

        ApplyAlpha();
    }

    private void LateUpdate()
    {
        if (_alpha <= 0f || _origin == null || _origin.Camera == null) return;
        PlaceSpot();
        PlaceCard();
    }

    // ------------------------------------------------------------------ home spot

    private void CaptureHome(string reason)
    {
        if (!ResolveOrigin()) return;
        _homeLocal = HeadLocal();
        _homeLocal.y = 0f;
        _hasHome = true;
        _chimed = false;
        if (_showing) SetShowing(false);

        // Only the deliberate ones are logged. Placement happens at every load, mostly in the
        // dark or on the main menu, and logging those would open a session file for the menu.
        if (reason == "researcher" || reason == "view_reset")
            SessionLog.Record("play_area_recentred", reason);
    }

    /// <summary>The head's position in the rig's own space: real-room position only.</summary>
    private Vector3 HeadLocal()
    {
        return _origin.transform.InverseTransformPoint(_origin.Camera.transform.position);
    }

    private float _nextOriginSearch;

    private bool ResolveOrigin()
    {
        if (_origin != null && _origin.Camera != null) return true;

        // Searched for at most once a second, so a scene without a rig costs nothing per frame.
        if (Time.unscaledTime < _nextOriginSearch) return false;
        _nextOriginSearch = Time.unscaledTime + 1f;
        _origin = FindFirstObjectByType<XROrigin>();
        return _origin != null && _origin.Camera != null;
    }

    private static bool IsFaded()
    {
        if (SceneTransitionController.Instance != null && SceneTransitionController.Instance.IsTransitioning)
            return true;
        return ScreenFader.Instance != null && ScreenFader.Instance.IsCovering;
    }

    private void HandleResearcherRecentre(bool suppressed)
    {
        bool key = Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame;

        // On the main menu the same hold opens the researcher screen, so it is left to that.
        bool both = !suppressed && _leftStick != null && _rightStick != null &&
                    _leftStick.IsPressed() && _rightStick.IsPressed();
        _heldFor = both ? _heldFor + Time.unscaledDeltaTime : 0f;

        if (_heldFor >= recentreHoldSeconds || key)
        {
            _heldFor = float.NegativeInfinity;     // once per hold: let go to arm it again
            CaptureHome("researcher");
            UiCuePlayer.Instance?.PlayActionAccepted();
        }
    }

    // The Quest's own "reset view" moves the tracking space; the old spot is meaningless after it.
    private void HookTrackingSpace()
    {
        if (_subsystems.Count > 0) return;
        SubsystemManager.GetSubsystems(_subsystems);
        foreach (XRInputSubsystem s in _subsystems)
            s.trackingOriginUpdated += OnTrackingOriginUpdated;
    }

    private void UnhookTrackingSpace()
    {
        foreach (XRInputSubsystem s in _subsystems)
            if (s != null) s.trackingOriginUpdated -= OnTrackingOriginUpdated;
        _subsystems.Clear();
    }

    private void OnTrackingOriginUpdated(XRInputSubsystem _)
    {
        // Taken a couple of frames later, once the camera has the new pose.
        _originRecentreInFrames = 2;
    }

    private int _originRecentreInFrames;

    // ------------------------------------------------------------------ show / hide

    private void SetShowing(bool show)
    {
        if (show == _showing) return;
        _showing = show;
        if (show)
        {
            _chimed = false;
            if (!_fontResolved) ResolveFont();
            SnapCard();
            SessionLog.Record("play_area_guide_shown",
                Drift.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "m");
        }
        else if (_hasHome)
        {
            SessionLog.Record("play_area_guide_cleared",
                Drift.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "m");
        }
    }

    private void ApplyAlpha()
    {
        float target = _showing ? 1f : 0f;
        _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));

        bool visible = _alpha > 0f;
        if (_card != null && _card.activeSelf != visible) _card.SetActive(visible);
        if (_spot != null && _spot.activeSelf != visible) _spot.SetActive(visible);
        if (_cardGroup != null) _cardGroup.alpha = _alpha;
        if (_spotGroup != null) _spotGroup.alpha = _alpha;
    }

    // ------------------------------------------------------------------ placement

    private void PlaceSpot()
    {
        // On the floor of the rig (its origin is the floor), a couple of centimetres up.
        Transform rig = _origin.transform;
        Vector3 world = rig.TransformPoint(new Vector3(_homeLocal.x, 0f, _homeLocal.z));
        world += rig.up * 0.02f;

        // Lying flat, with the shoe prints pointing the way the participant is facing.
        Vector3 fwd = Vector3.ProjectOnPlane(_origin.Camera.transform.forward, rig.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = rig.forward;
        _spot.transform.SetPositionAndRotation(world,
            Quaternion.LookRotation(-rig.up, fwd.normalized));
    }

    private Vector3 CardTarget(out Quaternion rotation)
    {
        Transform head = _origin.Camera.transform;
        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(head.up, Vector3.up);
        fwd.Normalize();
        Quaternion yaw = Quaternion.LookRotation(fwd, Vector3.up);
        Vector3 pos = head.position + yaw * cardOffset;
        rotation = Quaternion.LookRotation(pos - head.position, Vector3.up);
        return pos;
    }

    private void SnapCard()
    {
        if (_card == null || !ResolveOrigin()) return;
        Vector3 pos = CardTarget(out Quaternion rot);
        _card.transform.SetPositionAndRotation(pos, rot);
    }

    private void PlaceCard()
    {
        Vector3 pos = CardTarget(out Quaternion rot);
        float k = cardFollowSeconds <= 0f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime / cardFollowSeconds);
        _card.transform.SetPositionAndRotation(
            Vector3.Lerp(_card.transform.position, pos, k),
            Quaternion.Slerp(_card.transform.rotation, rot, k));

        // The arrow points the way to step: up on the card is straight ahead, down is behind.
        Transform head = _origin.Camera.transform;
        Transform rig = _origin.transform;
        Vector3 home = rig.TransformPoint(new Vector3(_homeLocal.x, 0f, _homeLocal.z));
        Vector3 toHome = Vector3.ProjectOnPlane(home - head.position, Vector3.up);
        Vector3 facing = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (toHome.sqrMagnitude > 1e-4f && facing.sqrMagnitude > 1e-4f && _arrow != null)
        {
            float angle = Vector3.SignedAngle(facing, toHome, Vector3.up);   // + is to the right
            Quaternion want = Quaternion.Euler(0f, 0f, -angle);              // UI: + Z is anticlockwise
            _arrow.localRotation = Quaternion.Slerp(_arrow.localRotation, want, k);
        }
    }

    // ------------------------------------------------------------------ building

    private void Build()
    {
        Texture arrowTex = Resources.Load<Texture2D>(ArrowResource);
        Texture spotTex = Resources.Load<Texture2D>(SpotResource);
        if (arrowTex == null || spotTex == null)
            Debug.LogWarning($"[PlayAreaGuide] Icons missing from Resources ({ArrowResource}, {SpotResource}); " +
                             "the guide will show without them.", this);

        // ---- card in front of the participant
        _card = new GameObject("PlayAreaGuide_Card", typeof(RectTransform));
        _card.transform.SetParent(transform, false);
        _card.SetActive(false);
        var canvas = _card.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 50;                       // above the other panels
        _cardGroup = _card.AddComponent<CanvasGroup>();
        _cardGroup.interactable = false;
        _cardGroup.blocksRaycasts = false;
        var cardRt = (RectTransform)_card.transform;
        cardRt.sizeDelta = new Vector2(560f, 560f);
        _card.transform.localScale = Vector3.one * 0.001f;   // 0.56 m square

        var bg = NewImage("Background", cardRt, Vector2.zero, Vector2.zero, stretch: true);
        bg.color = new Color(0.12f, 0.14f, 0.18f, 0.92f);

        if (arrowTex != null)
        {
            var arrow = NewRawImage("Arrow", cardRt, new Vector2(0f, 55f), new Vector2(330f, 330f), arrowTex);
            _arrow = arrow.rectTransform;
        }

        var textGo = new GameObject("Label", typeof(RectTransform));
        var textRt = (RectTransform)textGo.transform;
        textRt.SetParent(cardRt, false);
        textRt.anchorMin = textRt.anchorMax = textRt.pivot = new Vector2(0.5f, 0.5f);
        textRt.anchoredPosition = new Vector2(0f, arrowTex != null ? -185f : 0f);
        textRt.sizeDelta = new Vector2(520f, 140f);
        _cardText = textGo.AddComponent<TextMeshProUGUI>();
        _cardText.text = label;
        _cardText.fontSize = 54f;
        _cardText.fontStyle = FontStyles.Bold;
        _cardText.color = Color.white;
        _cardText.alignment = TextAlignmentOptions.Center;
        _cardText.textWrappingMode = TextWrappingModes.Normal;
        _cardText.raycastTarget = false;

        // ---- circle on the floor at the starting spot
        _spot = new GameObject("PlayAreaGuide_Spot", typeof(RectTransform));
        _spot.transform.SetParent(transform, false);
        _spot.SetActive(false);
        var spotCanvas = _spot.AddComponent<Canvas>();
        spotCanvas.renderMode = RenderMode.WorldSpace;
        spotCanvas.sortingOrder = 49;
        _spotGroup = _spot.AddComponent<CanvasGroup>();
        _spotGroup.interactable = false;
        _spotGroup.blocksRaycasts = false;
        var spotRt = (RectTransform)_spot.transform;
        spotRt.sizeDelta = new Vector2(512f, 512f);
        _spot.transform.localScale = Vector3.one * (spotDiameter / 512f);
        if (spotTex != null)
            NewRawImage("Spot", spotRt, Vector2.zero, new Vector2(512f, 512f), spotTex);

        // Both drawn over scenery: a hedge or kerb must never hide the way back. Applied while
        // still inactive; UIDrawOnTop swaps the materials as each one first switches on.
        _card.AddComponent<UIDrawOnTop>().OnTop = true;
        _spot.AddComponent<UIDrawOnTop>().OnTop = true;
    }

    /// <summary>The project's own panel font, taken from whichever panel is loaded first.</summary>
    private void ResolveFont()
    {
        _fontResolved = true;
        foreach (TMP_Text t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == _cardText || t.font == null) continue;
            _cardText.font = t.font;
            return;
        }
    }

    private static Image NewImage(string name, RectTransform parent, Vector2 pos, Vector2 size, bool stretch = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        if (stretch)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    private static RawImage NewRawImage(string name, RectTransform parent, Vector2 pos, Vector2 size, Texture tex)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var raw = go.AddComponent<RawImage>();
        raw.texture = tex;
        raw.raycastTarget = false;
        return raw;
    }
}
