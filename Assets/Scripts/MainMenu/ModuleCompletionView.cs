using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows where the current participant is in the module order (ModuleProgress.Sequence:
/// Map -> 1 -> 2 -> 3 -> 4) on the Select Module buttons:
///
///   DONE    - soft green with a green tick on the right; can't be pressed.
///   NEXT    - the only one that can be pressed, with a warm outline that gently pulses.
///   LOCKED  - greyed out with a grey padlock on the right (where the tick goes once done).
///   SKIPPED - greyed out with no icon (the researcher skipped it; it was not completed).
///
/// With no participant ID set, every button is open and plain: nothing is recorded, so testing
/// is never blocked. Changing the ID on the researcher screen, or resetting or skipping there,
/// updates the buttons straight away.
///
/// Added at runtime by MenuController, so there is no setup. The buttons are found by the
/// MenuController method their On Click calls (onMapButtonClick, onUnguidedButtonClick ...),
/// falling back to the names Tools > VR Full Route > Set Up Module Buttons gives them.
/// </summary>
[DisallowMultipleComponent]
public class ModuleCompletionView : MonoBehaviour
{
    private static readonly (string method, string objectName, string module)[] Modules =
    {
        ("onMapButtonClick",        "ModuleButton0_Map",        ModuleProgress.MapModule),
        ("onUnguidedButtonClick",   "ModuleButton1_Unguided",   MenuController.ModuleUnguided1),
        ("onGuidedButtonClick",     "ModuleButton2_Guided",     MenuController.ModuleGuided),
        ("onUnguided2aButtonClick", "ModuleButton3_Unguided2a", MenuController.ModuleUnguided2a),
        ("onUnguided2bButtonClick", "ModuleButton4_Unguided2b", MenuController.ModuleUnguided2b),
    };

    [Tooltip("Button colour once its module is done.")]
    [SerializeField] private Color completedColour = new Color(0.78f, 0.92f, 0.78f, 1f);

    [Tooltip("Size of the tick, in menu canvas units (the buttons are 64 tall).")]
    [SerializeField] private float tickSize = 42f;

    [Tooltip("Gap between the tick and the button's right edge, in canvas units.")]
    [SerializeField] private float tickInset = 10f;

    [Tooltip("Outline round the next module's button.")]
    [SerializeField] private Color nextOutlineColour = new Color(0.98f, 0.70f, 0.15f, 1f);

    [Tooltip("Thickness of that outline, in canvas units.")]
    [SerializeField] private float nextOutlineWidth = 5f;

    [Tooltip("Seconds for one slow pulse of the outline (bright -> soft -> bright).")]
    [SerializeField] private float pulseSeconds = 1.6f;

    [Tooltip("How far the outline fades at the soft end of the pulse (0 = it vanishes).")]
    [Range(0f, 1f)] [SerializeField] private float pulseLow = 0.35f;

    [Tooltip("Label opacity on locked and skipped buttons.")]
    [Range(0f, 1f)] [SerializeField] private float dimmedLabelAlpha = 0.45f;

    private class Entry
    {
        public string module;
        public Button button;
        public Image image;
        public Color colour;
        public ColorBlock colours;
        public TMP_Text label;
        public Vector4 margin;
        public Color labelColour;
        public GameObject tick;
        public GameObject padlock;
        public Outline outline;
        public State state;
    }

    private enum State { Open, Done, Next, Locked, Skipped }

    private readonly List<Entry> _entries = new List<Entry>();
    private string _shownFor;
    private int _shownVersion = -1;
    private Sprite _tickSprite;
    private Sprite _lockSprite;
    private Entry _next;

    /// <summary>Called by MenuController. Adds the view to the menu once.</summary>
    public static ModuleCompletionView Attach(GameObject menuRoot)
    {
        if (menuRoot == null) return null;
        ModuleCompletionView view = menuRoot.GetComponent<ModuleCompletionView>();
        if (view == null) view = menuRoot.AddComponent<ModuleCompletionView>();
        return view;
    }

    private void OnEnable()
    {
        if (_entries.Count == 0) FindButtons();
        Refresh();
    }

    private void Update()
    {
        if (StudySession.ParticipantId != _shownFor || ModuleProgress.Version != _shownVersion)
            Refresh();

        // The next module's outline: a slow, gentle pulse.
        if (_next != null && _next.outline != null)
        {
            float k = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.2f, pulseSeconds));
            Color c = nextOutlineColour;
            c.a *= Mathf.Lerp(pulseLow, 1f, k);
            _next.outline.effectColor = c;
        }
    }

    /// <summary>Redraws every button for the current participant.</summary>
    public void Refresh()
    {
        string id = StudySession.ParticipantId;
        _shownFor = id;
        _shownVersion = ModuleProgress.Version;
        bool tracked = ModuleProgress.Tracks(id);
        string next = ModuleProgress.NextModule(id);
        _next = null;

        foreach (Entry e in _entries)
        {
            State state;
            if (!tracked) state = State.Open;
            else if (ModuleProgress.IsCompleted(id, e.module)) state = State.Done;
            else if (ModuleProgress.IsSkipped(id, e.module)) state = State.Skipped;
            else if (e.module == next) state = State.Next;
            else state = State.Locked;
            Show(e, state);
            if (state == State.Next) _next = e;
        }
    }

    private void FindButtons()
    {
        var found = new HashSet<string>();
        foreach (Button b in GetComponentsInChildren<Button>(true))
        {
            string module = ModuleFor(b);
            if (module == null || !found.Add(module)) continue;

            var e = new Entry { module = module, button = b, image = b.targetGraphic as Image };
            if (e.image == null) e.image = b.GetComponent<Image>();
            if (e.image != null) e.colour = e.image.color;
            e.colours = b.colors;
            e.label = b.GetComponentInChildren<TMP_Text>(true);
            if (e.label != null) { e.margin = e.label.margin; e.labelColour = e.label.color; }
            _entries.Add(e);
        }

        if (found.Count < Modules.Length)
        {
            var missing = new List<string>();
            foreach (var m in Modules)
                if (!found.Contains(m.module)) missing.Add(m.module);
            Debug.LogWarning("[ModuleCompletionView] No menu button found for: " + string.Join(", ", missing) +
                             ". Those modules will not show their tick or padlock, and are not locked on the menu (starting them out of order is still refused). Re-run Tools > VR Full Route > Set Up Module Buttons.", this);
        }
    }

    private static string ModuleFor(Button b)
    {
        for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
        {
            string method = b.onClick.GetPersistentMethodName(i);
            foreach (var m in Modules)
                if (m.method == method) return m.module;
        }
        foreach (var m in Modules)
            if (b.gameObject.name == m.objectName) return m.module;
        return null;
    }

    private void Show(Entry e, State state)
    {
        if (e.button == null) return;
        e.state = state;
        bool done = state == State.Done;
        bool locked = state == State.Locked;
        bool dimmed = locked || state == State.Skipped;

        e.button.interactable = state == State.Open || state == State.Next;

        if (e.image != null) e.image.color = done ? completedColour : e.colour;

        // A disabled button is greyed by its colour tint (locked, skipped); keep the green when done.
        ColorBlock c = e.colours;
        if (done) c.disabledColor = Color.white;
        e.button.colors = c;

        if (e.label != null)
        {
            // Leave room for the tick or padlock so the label never runs under it.
            bool icon = done || locked;
            e.label.margin = icon ? e.margin + new Vector4(0f, 0f, tickSize + tickInset + 4f, 0f) : e.margin;
            Color lc = e.labelColour;
            if (dimmed) lc.a *= dimmedLabelAlpha;
            e.label.color = lc;
        }

        if (done && e.tick == null) e.tick = MakeIcon(e.button.transform, "DoneTick", ref _tickSprite, "Survey/SurveyTick",
                                                     new Color(0.30f, 0.69f, 0.31f));
        if (e.tick != null) e.tick.SetActive(done);

        if (locked && e.padlock == null) e.padlock = MakeIcon(e.button.transform, "LockedPadlock", ref _lockSprite, "Menu/MenuPadlock",
                                                             new Color(0.55f, 0.58f, 0.62f));
        if (e.padlock != null) e.padlock.SetActive(locked);

        // The next module's outline. Outline draws behind the button's own image.
        if (state == State.Next)
        {
            if (e.outline == null && e.image != null)
            {
                e.outline = e.image.gameObject.AddComponent<Outline>();
                e.outline.useGraphicAlpha = false;
            }
            if (e.outline != null)
            {
                e.outline.effectDistance = new Vector2(nextOutlineWidth, -nextOutlineWidth);
                e.outline.effectColor = nextOutlineColour;
                e.outline.enabled = true;
            }
        }
        else if (e.outline != null)
        {
            e.outline.enabled = false;
        }
    }

    /// <summary>An icon on the right of the button: the done tick or the locked padlock.</summary>
    private GameObject MakeIcon(Transform button, string name, ref Sprite sprite, string resource, Color fallback)
    {
        if (sprite == null) sprite = LoadSprite(resource);

        var go = new GameObject(name, typeof(RectTransform));
        go.layer = button.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(button, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-tickInset, 0f);
        rt.sizeDelta = new Vector2(tickSize, tickSize);

        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        if (sprite == null) img.color = fallback;   // plain coloured square if the image is missing
        return go;
    }

    private static Sprite LoadSprite(string resource)
    {
        Sprite s = Resources.Load<Sprite>(resource);
        if (s != null) return s;
        Texture2D t = Resources.Load<Texture2D>(resource);
        if (t != null) return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
        Debug.LogWarning($"[ModuleCompletionView] Missing Resources/{resource}.png.");
        return null;
    }
}
