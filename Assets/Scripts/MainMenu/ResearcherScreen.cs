using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Researcher-only screen for setting the participant ID in the headset (standalone Quest
/// builds, where the Editor's Tools > VR Study > Participant ID window is not available).
///
/// OPEN: on the main menu, hold BOTH thumbsticks pressed in for 3 seconds (F2 on a keyboard, for
/// desktop testing). Nothing on the main menu hints that it exists, so participants never see it.
///
/// USE: the ID is a fixed prefix ("P") plus digits typed on a keypad, e.g. P01. Save stores it
/// (StudySession, PlayerPrefs on the headset) until it is changed. Cancel leaves it as it was.
/// The screen also shows the current ID and which run number that participant is on.
///
/// BUILT AT RUNTIME, not in the scene: MenuController adds it. It is a separate world-space canvas
/// placed exactly where the main menu is, reusing the menu's raycasters (so the controller
/// pointers work), font and button look. The main menu is hidden while it is open, so a
/// participant cannot start a run from behind it.
/// </summary>
public class ResearcherScreen : MonoBehaviour
{
    [Tooltip("Fixed start of every participant ID.")]
    [SerializeField] private string prefix = "P";
    [Tooltip("Most digits that can be typed after the prefix.")]
    [SerializeField, Min(1)] private int maxDigits = 4;
    [Tooltip("How long both thumbsticks must be held in to open the screen (seconds).")]
    [SerializeField, Min(0.5f)] private float holdSeconds = 3f;

    private GameObject menuRoot;
    private InputAction leftStick;
    private InputAction rightStick;
    private float heldFor;

    private GameObject screen;
    private TMP_Text currentLabel;
    private TMP_Text entryLabel;
    private Button saveButton;
    private string digits = "";

    public bool IsOpen => screen != null && screen.activeSelf;

    /// <summary>Called by MenuController. Adds the screen to the main menu's scene once.</summary>
    public static ResearcherScreen Attach(GameObject mainMenuRoot)
    {
        if (mainMenuRoot == null) return null;
        foreach (ResearcherScreen existing in FindObjectsByType<ResearcherScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (existing.menuRoot == mainMenuRoot) return existing;

        // Its own object, not on the menu: the menu is switched off while this is open.
        var host = new GameObject("ResearcherScreen (runtime)");
        SceneManager.MoveGameObjectToScene(host, mainMenuRoot.scene);
        var r = host.AddComponent<ResearcherScreen>();
        r.menuRoot = mainMenuRoot;
        return r;
    }

    private void Awake()
    {
        // "{Primary2DAxisClick}" is the thumbstick press on any XR controller; the second binding
        // names the Quest Touch control directly in case a profile does not tag the usage.
        leftStick = new InputAction("ResearcherLeftStick", InputActionType.Button);
        leftStick.AddBinding("<XRController>{LeftHand}/{Primary2DAxisClick}");
        leftStick.AddBinding("<XRController>{LeftHand}/thumbstickClicked");
        rightStick = new InputAction("ResearcherRightStick", InputActionType.Button);
        rightStick.AddBinding("<XRController>{RightHand}/{Primary2DAxisClick}");
        rightStick.AddBinding("<XRController>{RightHand}/thumbstickClicked");
    }

    private void OnEnable()
    {
        leftStick?.Enable();
        rightStick?.Enable();
    }

    private void OnDisable()
    {
        leftStick?.Disable();
        rightStick?.Disable();
    }

    private void OnDestroy()
    {
        leftStick?.Dispose();
        rightStick?.Dispose();
        if (screen != null) Destroy(screen);
    }

    private void Update()
    {
        if (IsOpen || menuRoot == null || !menuRoot.activeInHierarchy)
        {
            heldFor = 0f;
            return;
        }

        bool both = leftStick.IsPressed() && rightStick.IsPressed();
        heldFor = both ? heldFor + Time.unscaledDeltaTime : 0f;

        bool key = Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame;
        if (heldFor >= holdSeconds || key)
        {
            heldFor = 0f;
            Open();
        }
    }

    // ------------------------------------------------------------------ open / close
    public void Open()
    {
        if (menuRoot == null) return;
        if (screen == null) Build();
        if (screen == null) return;

        string current = StudySession.ParticipantId;
        digits = StudySession.HasParticipant && current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? current.Substring(prefix.Length)
            : "";
        if (digits.Length > maxDigits || !IsDigits(digits)) digits = "";

        PlaceOverMenu();
        menuRoot.SetActive(false);
        screen.SetActive(true);
        Refresh();
        Debug.Log("[ResearcherScreen] Opened.", this);
    }

    private void Close()
    {
        if (screen != null) screen.SetActive(false);
        if (menuRoot != null) menuRoot.SetActive(true);
    }

    private void Save()
    {
        if (digits.Length == 0) return;
        string id = prefix + digits;
        StudySession.SetParticipantId(id);
        SessionLog.Record("participant_set", id);
        Debug.Log($"[ResearcherScreen] Participant ID set to {id}.", this);
        Close();
    }

    private void Press(char c)
    {
        if (digits.Length >= maxDigits) return;
        digits += c;
        Refresh();
    }

    private void DeleteLast()
    {
        if (digits.Length > 0) digits = digits.Substring(0, digits.Length - 1);
        Refresh();
    }

    private void Refresh()
    {
        string current = StudySession.ParticipantId;
        currentLabel.text = StudySession.HasParticipant
            ? $"Current: {current}   (next run: {StudySession.NextRunIndex(current)})"
            : "Current: none set";
        entryLabel.text = prefix + (digits.Length > 0 ? digits : "_");
        saveButton.interactable = digits.Length > 0;
    }

    private static bool IsDigits(string s)
    {
        foreach (char c in s) if (c < '0' || c > '9') return false;
        return true;
    }

    // ------------------------------------------------------------------ building
    private Canvas sourceCanvas;
    private TMP_FontAsset font;
    private Color textColour = Color.white;
    private Image buttonTemplate;
    private Button buttonTemplateButton;

    private void Build()
    {
        sourceCanvas = menuRoot.GetComponentInParent<Canvas>(true);
        if (sourceCanvas == null) sourceCanvas = menuRoot.GetComponentInChildren<Canvas>(true);
        if (sourceCanvas == null)
        {
            Debug.LogWarning("[ResearcherScreen] The main menu has no Canvas to copy, so the researcher screen " +
                             "cannot be shown.", this);
            return;
        }

        // Look and feel from the menu itself.
        foreach (Button b in menuRoot.GetComponentsInChildren<Button>(true))
        {
            buttonTemplateButton = b;
            buttonTemplate = b.targetGraphic as Image;
            TMP_Text t = b.GetComponentInChildren<TMP_Text>(true);
            if (t != null) { font = t.font; textColour = t.color; }
            break;
        }
        if (font == null)
        {
            TMP_Text anyText = menuRoot.GetComponentInChildren<TMP_Text>(true);
            if (anyText != null) { font = anyText.font; textColour = anyText.color; }
        }

        screen = new GameObject("ResearcherScreen", typeof(RectTransform), typeof(Canvas));
        SceneManager.MoveGameObjectToScene(screen, gameObject.scene);
        screen.SetActive(false);

        var canvas = screen.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = sourceCanvas.worldCamera;
        canvas.sortingLayerID = sourceCanvas.sortingLayerID;
        canvas.sortingOrder = sourceCanvas.sortingOrder;

        var srcScaler = sourceCanvas.GetComponent<CanvasScaler>();
        if (srcScaler != null)
        {
            var scaler = screen.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = srcScaler.dynamicPixelsPerUnit;
            scaler.referencePixelsPerUnit = srcScaler.referencePixelsPerUnit;
        }

        // Same raycasters as the menu (the XR tracked-device one included), so the controller
        // pointers can press these buttons exactly as they press the menu's.
        var added = new HashSet<Type>();
        foreach (BaseRaycaster rc in sourceCanvas.GetComponents<BaseRaycaster>())
            if (added.Add(rc.GetType()))
                screen.AddComponent(rc.GetType());
        if (added.Count == 0)
            screen.AddComponent<GraphicRaycaster>();

        RectTransform root = (RectTransform)screen.transform;
        Vector2 size = ((RectTransform)menuRoot.transform).rect.size;
        if (size.x < 100f || size.y < 100f) size = new Vector2(600f, 400f);
        root.sizeDelta = size;

        // Background, same as the menu's if it has one.
        Image menuBackground = null;
        foreach (Image img in menuRoot.GetComponentsInChildren<Image>(true))
            if (img.gameObject.name == "Background" && img.transform.parent == menuRoot.transform) { menuBackground = img; break; }
        Image bg = MakeRect("Background", root, Vector2.zero, size).gameObject.AddComponent<Image>();
        if (menuBackground != null)
        {
            bg.sprite = menuBackground.sprite;
            bg.type = menuBackground.type;
            bg.color = menuBackground.color;
        }
        else bg.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);

        // Layout in the menu's own units (600 x 400 by default), scaled to its size.
        float sx = size.x / 600f, sy = size.y / 400f;
        Func<float, float, Vector2> P = (x, y) => new Vector2(x * sx, y * sy);
        Func<float, float, Vector2> S = (w, h) => new Vector2(w * sx, h * sy);

        MakeText("Title", root, "Researcher: participant ID", P(0, 165), S(560, 44), 30 * sy);
        currentLabel = MakeText("Current", root, "", P(0, 125), S(560, 32), 20 * sy);
        entryLabel = MakeText("Entry", root, "", P(-90, 78), S(250, 56), 44 * sy);

        string keys = "123456789";
        for (int i = 0; i < 9; i++)
        {
            char c = keys[i];
            int col = i % 3, row = i / 3;
            MakeButton("Key" + c, root, c.ToString(), P(-170 + col * 80, 18 - row * 62), S(70, 54), 30 * sy, () => Press(c));
        }
        MakeButton("KeyDelete", root, "Delete", P(-170, 18 - 3 * 62), S(70, 54), 18 * sy, DeleteLast);
        MakeButton("Key0", root, "0", P(-90, 18 - 3 * 62), S(70, 54), 30 * sy, () => Press('0'));

        saveButton = MakeButton("Save", root, "Save", P(165, 0), S(190, 70), 28 * sy, Save);
        MakeButton("Cancel", root, "Cancel", P(165, -100), S(190, 70), 28 * sy, Close);
    }

    private void PlaceOverMenu()
    {
        Transform src = menuRoot.transform;
        Transform dst = screen.transform;
        dst.SetPositionAndRotation(src.position, src.rotation);
        dst.localScale = src.lossyScale;
    }

    private static RectTransform MakeRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    private TMP_Text MakeText(string name, Transform parent, string text, Vector2 pos, Vector2 size, float fontSize)
    {
        RectTransform rt = MakeRect(name, parent, pos, size);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = fontSize;
        t.color = textColour;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }

    private Button MakeButton(string name, Transform parent, string label, Vector2 pos, Vector2 size, float fontSize, Action onClick)
    {
        RectTransform rt = MakeRect(name, parent, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        if (buttonTemplate != null)
        {
            img.sprite = buttonTemplate.sprite;
            img.type = buttonTemplate.type;
            img.color = buttonTemplate.color;
            img.pixelsPerUnitMultiplier = buttonTemplate.pixelsPerUnitMultiplier;
        }
        else img.color = new Color(0.25f, 0.45f, 0.85f);

        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        if (buttonTemplateButton != null)
        {
            button.colors = buttonTemplateButton.colors;
            button.transition = buttonTemplateButton.transition;
            button.spriteState = buttonTemplateButton.spriteState;
        }
        button.onClick.AddListener(() => onClick());

        TMP_Text t = MakeText("Label", rt, label, Vector2.zero, size, fontSize);
        // Keep the label readable on the button whatever colour the menu's text is.
        TMP_Text templateLabel = buttonTemplateButton != null ? buttonTemplateButton.GetComponentInChildren<TMP_Text>(true) : null;
        if (templateLabel != null) t.color = templateLabel.color;
        return button;
    }
}
