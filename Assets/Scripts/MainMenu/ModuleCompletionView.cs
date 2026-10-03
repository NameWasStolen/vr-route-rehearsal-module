using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ticks off the Select Module buttons the current participant has completed (ModuleProgress):
/// the button turns soft green, gets a green tick on its right, and can no longer be pressed.
/// Modules not yet done stay as they are, in any order.
///
/// With no participant ID set, nothing is ticked. Changing the ID on the researcher screen, or
/// resetting a participant's progress there, updates the buttons straight away.
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

    private class Entry
    {
        public string module;
        public Button button;
        public Image image;
        public Color colour;
        public ColorBlock colours;
        public TMP_Text label;
        public Vector4 margin;
        public GameObject tick;
        public bool done;
    }

    private readonly List<Entry> _entries = new List<Entry>();
    private string _shownFor;
    private int _shownVersion = -1;
    private Sprite _tickSprite;

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
    }

    /// <summary>Redraws every button for the current participant.</summary>
    public void Refresh()
    {
        string id = StudySession.ParticipantId;
        _shownFor = id;
        _shownVersion = ModuleProgress.Version;
        foreach (Entry e in _entries)
            Show(e, ModuleProgress.IsCompleted(id, e.module));
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
            if (e.label != null) e.margin = e.label.margin;
            _entries.Add(e);
        }

        if (found.Count < Modules.Length)
        {
            var missing = new List<string>();
            foreach (var m in Modules)
                if (!found.Contains(m.module)) missing.Add(m.module);
            Debug.LogWarning("[ModuleCompletionView] No menu button found for: " + string.Join(", ", missing) +
                             ". Those modules will not show a tick. Re-run Tools > VR Full Route > Set Up Module Buttons.", this);
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

    private void Show(Entry e, bool done)
    {
        if (e.button == null) return;
        e.done = done;
        e.button.interactable = !done;

        if (e.image != null) e.image.color = done ? completedColour : e.colour;

        // A disabled button is normally greyed by its colour tint; keep the green instead.
        ColorBlock c = e.colours;
        if (done) c.disabledColor = Color.white;
        e.button.colors = c;

        // Leave room for the tick so the label never runs under it.
        if (e.label != null)
            e.label.margin = done ? e.margin + new Vector4(0f, 0f, tickSize + tickInset + 4f, 0f) : e.margin;

        if (done && e.tick == null) e.tick = MakeTick(e.button.transform);
        if (e.tick != null) e.tick.SetActive(done);
    }

    private GameObject MakeTick(Transform button)
    {
        if (_tickSprite == null) _tickSprite = LoadTick();

        var go = new GameObject("DoneTick", typeof(RectTransform));
        go.layer = button.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(button, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-tickInset, 0f);
        rt.sizeDelta = new Vector2(tickSize, tickSize);

        var img = go.AddComponent<Image>();
        img.sprite = _tickSprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        if (_tickSprite == null) img.color = new Color(0.30f, 0.69f, 0.31f);   // plain green square if missing
        return go;
    }

    private static Sprite LoadTick()
    {
        Sprite s = Resources.Load<Sprite>("Survey/SurveyTick");
        if (s != null) return s;
        Texture2D t = Resources.Load<Texture2D>("Survey/SurveyTick");
        if (t != null) return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
        Debug.LogWarning("[ModuleCompletionView] Missing Resources/Survey/SurveyTick.png.");
        return null;
    }
}
