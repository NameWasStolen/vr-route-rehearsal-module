using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using VRTutorial;

/// <summary>
/// The short questionnaire shown when a participant reaches the end zone, before they go back
/// to the main menu. Two questions, each answered on five faces from red/sad (1) to green/happy
/// (5), picked with the controller ray and confirmed with Next.
///
///   1. "How calm did you feel on the walk?"         -> calm (1-5), and stress = 6 - calm
///   2. "How confident did you feel finding the way?" -> confidence (1-5)
///
/// Stress is asked as calm so that the happy green face is the good answer on both questions;
/// the stress column is the calm answer reversed, so the data still reads as a stress score.
///
/// WHEN: only after a run that reached the end zone, and only for the run types listed in
/// Run Types (Unguided 1 and Guided by default). Leaving a run from the pause menu skips it.
///
/// NO SETUP: RunSystemController adds one of these if RunSystem has none, and the panel is built
/// when it is first needed, styled from the run's pause menu (RunHelp > PauseMenuPanel) so it
/// matches it. Add a PostRunSurvey to RunSystem by hand only to change its settings - which runs
/// get it, the wording, sizes.
///
/// WHILE IT IS UP: walking and turning are off and the pause button does nothing, as with the
/// pause menu. The help button still works; the survey steps aside while the help panels are up
/// and comes back when the participant presses Resume.
///
/// DATA: one row per survey in Data/RunData/survey_responses.csv (SurveyResponseWriter), plus
/// survey_started / survey_answer / survey_completed in the session log.
/// </summary>
[DisallowMultipleComponent]
public class PostRunSurvey : MonoBehaviour
{
    [Serializable]
    public class Question
    {
        [Tooltip("Column name for the answer in survey_responses.csv. Keep it short, lower case, " +
                 "no spaces or commas.")]
        public string id;

        [Tooltip("The question, in plain English. Short words read best for this group.")]
        [TextArea(2, 4)] public string prompt;

        [Tooltip("Under the red face (answer 1).")]
        public string lowLabel;

        [Tooltip("Under the green face (answer 5).")]
        public string highLabel;

        [Tooltip("Optional. Also saves 6 - answer under this column name. Used to turn the calm " +
                 "answer into a stress score.")]
        public string reversedColumn;

        public Question(string id, string prompt, string lowLabel, string highLabel, string reversedColumn = "")
        {
            this.id = id;
            this.prompt = prompt;
            this.lowLabel = lowLabel;
            this.highLabel = highLabel;
            this.reversedColumn = reversedColumn;
        }
    }

    public const int ScaleSize = 5;

    [Header("When")]
    [Tooltip("Run types that end with the survey. The module run types are unguided_1, guided, " +
             "unguided_2a and unguided_2b.")]
    [SerializeField] private string[] runTypes = { MenuController.ModuleUnguided1, MenuController.ModuleGuided };

    [Tooltip("Seconds between reaching the end zone and the survey appearing, so the arrival " +
             "registers before something new appears.")]
    [SerializeField] private float delayBeforeSurvey = 1f;

    [Header("Questions")]
    [SerializeField] private Question[] questions =
    {
        new Question("calm", "How calm did you feel on the walk?", "Not calm", "Very calm", "stress"),
        new Question("confidence", "How confident did you feel finding the way?", "Not confident", "Very confident"),
    };

    [Header("Words")]
    [SerializeField] private string nextLabel = "Next";
    [SerializeField] private string doneLabel = "Done";
    [SerializeField] private string thanksText = "Thank you!";
    [Tooltip("Seconds the thank-you shows before going back to the menu.")]
    [SerializeField] private float thanksSeconds = 2f;

    [Header("Layout (canvas units: 1 unit = 1 mm)")]
    [Tooltip("Metres in front of the participant.")]
    [SerializeField] private float distance = 1.5f;
    [SerializeField] private Vector2 panelSize = new Vector2(1000f, 700f);
    [SerializeField] private float faceSize = 150f;
    [SerializeField] private float faceSpacing = 180f;
    [SerializeField] private float questionFontSize = 54f;
    [SerializeField] private float labelFontSize = 34f;

    [Header("Timing")]
    [Tooltip("Clicks are ignored for this long after each question appears, so a click meant " +
             "for Next cannot also pick a face on the next question.")]
    [SerializeField] private float ignoreClicksSeconds = 0.4f;
    [SerializeField] private float fadeSeconds = 0.25f;

    /// <summary>True from the moment the survey starts until the participant finishes it.</summary>
    public bool IsRunning { get; private set; }

    // ------------------------------------------------------------------ built UI
    private GameObject _root;
    private Canvas _canvas;
    private CanvasGroup _group;
    private HeadLockedUI _headLocked;
    private GameObject _questionPage;
    private TMP_Text _prompt;
    private TMP_Text _lowLabel;
    private TMP_Text _highLabel;
    private TMP_Text _thanks;
    private Button _next;
    private CanvasGroup _nextGroup;
    private TMP_Text _nextText;
    private readonly List<Image> _dots = new List<Image>();
    private readonly SurveyFaceOption[] _faces = new SurveyFaceOption[ScaleSize];

    // ------------------------------------------------------------------ state
    private PauseController _pause;
    private bool _holdingPause;
    private bool _pauseWasAvailable;
    private bool _hiddenForHelp;
    private int _selected;              // 0 = nothing picked yet
    private int _changes;
    private bool _nextPressed;
    private float _clickableFrom;

    /// <summary>Whether a run of this type ends with the survey.</summary>
    public bool AppliesTo(string runType)
    {
        if (string.IsNullOrEmpty(runType) || runTypes == null || questions == null || questions.Length == 0)
            return false;
        foreach (string t in runTypes)
            if (string.Equals(t?.Trim(), runType, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Shows the survey and returns when the participant has answered every question and the
    /// thank-you has shown. Run it from the coroutine that ends the run.
    /// </summary>
    public IEnumerator Run(string runType, string participantId, int runIndex)
    {
        if (IsRunning || questions == null || questions.Length == 0)
            yield break;
        IsRunning = true;

        // Hold everything still straight away (the participant is standing in the end zone),
        // then give the arrival a moment before the panel appears.
        HoldPause();
        yield return WaitUnscaled(delayBeforeSurvey);

        if (!EnsureBuilt())
        {
            ReleasePause();
            IsRunning = false;
            yield break;
        }

        int n = questions.Length;
        var answers = new int[n];
        var seconds = new float[n];
        var changes = new int[n];
        DateTime startedAt = DateTime.Now;
        SessionLog.Record("survey_started", runType);

        AssistanceRequest.Changed += HandleAssistanceChanged;
        try
        {
            _root.SetActive(true);
            _canvas.enabled = true;
            _hiddenForHelp = AssistanceRequest.IsActive;
            _headLocked.SnapToTarget();

            for (int q = 0; q < n; q++)
            {
                ShowQuestion(q);
                if (q == 0)
                    yield return FadeTo(_hiddenForHelp ? 0f : 1f);
                else
                    UiCuePlayer.Instance?.PlayStepAdvance();

                // Wait for Next. Time with the help panels up is not counted as answering time.
                float answering = 0f;
                while (!_nextPressed)
                {
                    if (!_hiddenForHelp) answering += Time.unscaledDeltaTime;
                    KeepPauseButtonOff();
                    yield return null;
                }

                answers[q] = _selected;
                seconds[q] = answering;
                changes[q] = _changes;
                SessionLog.Record("survey_answer", $"{questions[q].id}={_selected}");
            }

            // Saved before the thank-you, so the answers are on disk even if the headset comes
            // off now.
            SurveyResponseWriter.Write(participantId, runType, runIndex, startedAt, questions, answers, seconds, changes);
            SessionLog.Record("survey_completed", runType);

            _questionPage.SetActive(false);
            _thanks.gameObject.SetActive(true);
            UiCuePlayer.Instance?.PlayFlowComplete();
            yield return WaitUnscaled(thanksSeconds);
            yield return FadeTo(0f);
        }
        finally
        {
            AssistanceRequest.Changed -= HandleAssistanceChanged;
            if (_root != null) _root.SetActive(false);
            ReleasePause();
            IsRunning = false;
        }
    }

    private void OnDisable()
    {
        // The run scene unloading part-way through: give locomotion and the pause flag back.
        AssistanceRequest.Changed -= HandleAssistanceChanged;
        ReleasePause();
        IsRunning = false;
    }

    // ------------------------------------------------------------------ flow

    private void ShowQuestion(int index)
    {
        Question q = questions[index];
        _prompt.text = q.prompt;
        _lowLabel.text = q.lowLabel;
        _highLabel.text = q.highLabel;

        _selected = 0;
        _changes = 0;
        _nextPressed = false;
        _clickableFrom = Time.unscaledTime + ignoreClicksSeconds;

        foreach (SurveyFaceOption face in _faces)
            face.SetState(false, false, true);

        bool last = index == questions.Length - 1;
        _nextText.text = last ? doneLabel : nextLabel;
        SetNextEnabled(false);

        for (int i = 0; i < _dots.Count; i++)
            _dots[i].color = new Color(1f, 1f, 1f, i == index ? 1f : 0.35f);

        _questionPage.SetActive(true);
        _thanks.gameObject.SetActive(false);
    }

    private void OnFaceClicked(int value)
    {
        if (!IsRunning || _hiddenForHelp || Time.unscaledTime < _clickableFrom)
            return;
        if (value == _selected)
            return;

        if (_selected != 0) _changes++;
        _selected = value;
        for (int i = 0; i < _faces.Length; i++)
            _faces[i].SetState(i + 1 == value, true, false);

        SetNextEnabled(true);
        UiCuePlayer.Instance?.PlayActionAccepted();
    }

    private void OnNextClicked()
    {
        if (!IsRunning || _selected == 0 || _hiddenForHelp || Time.unscaledTime < _clickableFrom)
            return;
        SetNextEnabled(false);     // one press only
        _nextPressed = true;
    }

    private void SetNextEnabled(bool enabled)
    {
        _next.interactable = enabled;
        _nextGroup.alpha = enabled ? 1f : 0.4f;
    }

    /// <summary>The survey steps aside while the help panels are up - never two panels at once.</summary>
    private void HandleAssistanceChanged(AssistanceState state)
    {
        if (_group == null) return;
        bool hide = state != AssistanceState.Idle;
        if (hide == _hiddenForHelp) return;
        _hiddenForHelp = hide;

        _group.alpha = hide ? 0f : 1f;
        _group.interactable = !hide;
        _group.blocksRaycasts = !hide;
        if (!hide)
        {
            _headLocked.SnapToTarget();          // back in front of them, wherever they now face
            _clickableFrom = Time.unscaledTime + ignoreClicksSeconds;
        }
    }

    // ------------------------------------------------------------------ pausing

    private void HoldPause()
    {
        if (_holdingPause) return;
        _holdingPause = true;

        _pause = FindInScene<PauseController>();
        if (_pause != null)
        {
            if (_pause.IsOpen) _pause.Close();
            _pauseWasAvailable = _pause.IsAvailable;
            _pause.SetAvailable(false);
        }

        // Same as the pause menu: no walking or turning, head-locked panels hold still.
        TutorialPause.Hold(this);
        ControllerHandednessManager.Instance?.SuspendLocomotion(this);
    }

    /// <summary>
    /// The help button's Resume puts the pause button back as it found it. If help was asked
    /// for before the survey began, that would switch it back on mid-survey.
    /// </summary>
    private void KeepPauseButtonOff()
    {
        if (_pause != null && _pause.IsAvailable) _pause.SetAvailable(false);
    }

    private void ReleasePause()
    {
        if (!_holdingPause) return;
        _holdingPause = false;

        TutorialPause.Release(this);
        ControllerHandednessManager.Instance?.ResumeLocomotion(this);
        if (_pause != null) _pause.SetAvailable(_pauseWasAvailable);
    }

    // ------------------------------------------------------------------ helpers

    private static IEnumerator WaitUnscaled(float seconds)
    {
        float end = Time.unscaledTime + Mathf.Max(0f, seconds);
        while (Time.unscaledTime < end) yield return null;
    }

    private IEnumerator FadeTo(float target)
    {
        float start = _group.alpha;
        _group.interactable = target > 0f;
        _group.blocksRaycasts = target > 0f;
        for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / fadeSeconds);
            _group.alpha = Mathf.Lerp(start, target, k * k * (3f - 2f * k));
            yield return null;
        }
        _group.alpha = target;
    }

    private T FindInScene<T>() where T : Component
    {
        T fallback = null;
        foreach (T c in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c.gameObject.scene == gameObject.scene) return c;
            if (fallback == null) fallback = c;
        }
        return fallback;
    }

    // ------------------------------------------------------------------ building the panel

    private bool EnsureBuilt()
    {
        if (_root != null) return true;
        try
        {
            Build();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[PostRunSurvey] Could not build the survey panel, so the survey was skipped: {e}", this);
            if (_root != null) Destroy(_root);
            _root = null;
            return false;
        }
    }

    private struct Style
    {
        public int layer;
        public TMP_FontAsset font;
        public Sprite panelSprite;
        public Image.Type panelType;
        public Color panelColor;
        public Sprite buttonSprite;
        public Image.Type buttonType;
        public Color buttonColor;
        public ColorBlock buttonColors;
        public Color buttonTextColor;
        public Color textColor;
    }

    /// <summary>
    /// The look of the run's pause menu, so the survey is the same family: its translucent dark
    /// panel, light buttons with dark text, and its font. Sensible defaults if there is no pause
    /// menu in RunSystem.
    /// </summary>
    private Style ReadStyle()
    {
        var s = new Style
        {
            layer = LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 0,
            font = TMP_Settings.defaultFontAsset,
            panelType = Image.Type.Simple,
            panelColor = new Color(0.078f, 0.078f, 0.098f, 0.82f),
            buttonType = Image.Type.Simple,
            buttonColor = new Color(0.93f, 0.93f, 0.93f, 1f),
            buttonColors = ColorBlock.defaultColorBlock,
            buttonTextColor = new Color(0.078f, 0.078f, 0.094f, 1f),
            textColor = Color.white,
        };

        PauseController pause = FindInScene<PauseController>();
        GameObject template = pause != null ? pause.MenuRoot : null;
        if (template == null)
        {
            Debug.LogWarning("[PostRunSurvey] No pause menu in RunSystem to copy the look from; using " +
                             "default colours. (Tools > VR Full Route > Add Help and Pause Menu to RunSystem.)", this);
            return s;
        }

        s.layer = template.layer;
        foreach (Transform child in template.transform)
        {
            if (child.GetComponent<Button>() != null) continue;
            var img = child.GetComponent<Image>();
            var rt = child as RectTransform;
            if (img != null && rt != null && rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one)
            {
                s.panelSprite = img.sprite;
                s.panelType = img.type;
                // A little more opaque than the pause menu: these are reading questions, and the
                // street behind is busy.
                s.panelColor = new Color(img.color.r, img.color.g, img.color.b, Mathf.Max(img.color.a, 0.82f));
                break;
            }
        }

        Button button = template.GetComponentInChildren<Button>(true);
        if (button != null)
        {
            var img = button.GetComponent<Image>();
            if (img != null)
            {
                s.buttonSprite = img.sprite;
                s.buttonType = img.type;
                s.buttonColor = img.color;
            }
            s.buttonColors = button.colors;
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                if (label.font != null) s.font = label.font;
                s.buttonTextColor = label.color;
            }
        }

        foreach (TMP_Text t in template.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t.GetComponentInParent<Button>(true) != null || t.gameObject == template) continue;
            s.textColor = t.color;
            if (t.font != null && s.font == null) s.font = t.font;
            break;
        }
        return s;
    }

    private void Build()
    {
        Style style = ReadStyle();

        // Built inactive, so nothing wakes up half-configured; moved into RunSystem so it goes
        // when the run unloads.
        _root = new GameObject("PostRunSurveyPanel", typeof(RectTransform));
        _root.SetActive(false);
        if (_root.scene != gameObject.scene) SceneManager.MoveGameObjectToScene(_root, gameObject.scene);
        _root.layer = style.layer;

        var rect = (RectTransform)_root.transform;
        rect.sizeDelta = panelSize;
        rect.localScale = Vector3.one * 0.001f;

        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 |
                                           AdditionalCanvasShaderChannels.Normal |
                                           AdditionalCanvasShaderChannels.Tangent;
        _root.AddComponent<GraphicRaycaster>();
        _root.AddComponent<TrackedDeviceGraphicRaycaster>();
        _group = _root.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        _headLocked = _root.AddComponent<HeadLockedUI>();
        _headLocked.LocalOffset = new Vector3(0f, 0f, distance);

        // Grows with the text-size setting, as the pause menu does.
        _root.AddComponent<ScalableUIRoot>();

        // Panel
        Image bg = NewImage("Background", _root.transform, style.panelSprite, style.panelColor, style.layer);
        bg.type = style.panelSprite != null ? style.panelType : Image.Type.Simple;
        Stretch(bg.rectTransform);

        float top = panelSize.y * 0.5f;
        float bottom = -panelSize.y * 0.5f;

        _questionPage = NewChild("Question", _root.transform, style.layer);
        Stretch((RectTransform)_questionPage.transform);

        // Which question this is: one dot per question, the current one bright. No words needed.
        _dots.Clear();
        if (questions.Length > 1)
        {
            Sprite dot = LoadSprite("Survey/SurveyDot");
            float pitch = 44f;
            float x0 = -(questions.Length - 1) * pitch * 0.5f;
            for (int i = 0; i < questions.Length; i++)
            {
                Image d = NewImage($"Dot_{i + 1}", _questionPage.transform, dot, Color.white, style.layer);
                d.raycastTarget = false;
                Place(d.rectTransform, new Vector2(x0 + i * pitch, top - 50f), new Vector2(24f, 24f));
                _dots.Add(d);
            }
        }

        _prompt = NewText("Prompt", _questionPage.transform, style, style.textColor, questionFontSize, FontStyles.Bold);
        Place(_prompt.rectTransform, new Vector2(0f, top - 160f), new Vector2(panelSize.x - 80f, 170f));

        // Faces, 1 (red) to 5 (green), left to right.
        float faceY = top - 340f;
        Sprite ring = LoadSprite("Survey/SurveyRing");
        for (int i = 0; i < ScaleSize; i++)
        {
            int value = i + 1;
            float x = (i - (ScaleSize - 1) * 0.5f) * faceSpacing;
            _faces[i] = NewFace(value, new Vector2(x, faceY), LoadSprite($"Survey/SurveyFace_{value}"), ring, style.layer);
        }

        float labelY = faceY - faceSize * 0.5f - 22f;
        float edgeX = (ScaleSize - 1) * 0.5f * faceSpacing;
        _lowLabel = NewText("LowLabel", _questionPage.transform, style, style.textColor, labelFontSize, FontStyles.Normal);
        _highLabel = NewText("HighLabel", _questionPage.transform, style, style.textColor, labelFontSize, FontStyles.Normal);
        foreach (TMP_Text label in new[] { _lowLabel, _highLabel })
        {
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.verticalAlignment = VerticalAlignmentOptions.Top;
        }
        Place(_lowLabel.rectTransform, new Vector2(-edgeX, labelY), new Vector2(faceSpacing + 70f, 90f));
        Place(_highLabel.rectTransform, new Vector2(edgeX, labelY), new Vector2(faceSpacing + 70f, 90f));

        // Next / Done
        Image nextImage = NewImage("NextButton", _questionPage.transform, style.buttonSprite, style.buttonColor, style.layer);
        nextImage.type = style.buttonSprite != null ? style.buttonType : Image.Type.Simple;
        Place(nextImage.rectTransform, new Vector2(0f, bottom + 100f), new Vector2(420f, 120f));
        _next = nextImage.gameObject.AddComponent<Button>();
        _next.targetGraphic = nextImage;
        _next.colors = style.buttonColors;
        _next.onClick.AddListener(OnNextClicked);
        _nextGroup = nextImage.gameObject.AddComponent<CanvasGroup>();
        _nextText = NewText("Text (TMP)", nextImage.transform, style, style.buttonTextColor, 50f, FontStyles.Normal);
        Stretch(_nextText.rectTransform);

        // Thank you
        _thanks = NewText("Thanks", _root.transform, style, style.textColor, 80f, FontStyles.Bold);
        _thanks.text = thanksText;
        Place(_thanks.rectTransform, Vector2.zero, new Vector2(panelSize.x - 80f, 300f));
        _thanks.gameObject.SetActive(false);
    }

    private SurveyFaceOption NewFace(int value, Vector2 position, Sprite face, Sprite ring, int layer)
    {
        GameObject go = NewChild($"Face_{value}", _questionPage.transform, layer);
        Place((RectTransform)go.transform, position, new Vector2(faceSize, faceSize));

        // Behind the face; shown on the chosen one.
        Image ringImage = NewImage("Ring", go.transform, ring, Color.white, layer);
        ringImage.raycastTarget = false;
        Place(ringImage.rectTransform, Vector2.zero, Vector2.one * faceSize * 1.18f);

        Image icon = NewImage("Icon", go.transform, face, Color.white, layer);
        icon.preserveAspect = true;
        Place(icon.rectTransform, Vector2.zero, Vector2.one * faceSize);

        // No colour tint: tinting would change the face colours, which carry the meaning.
        var button = go.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = icon;
        var nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
        button.onClick.AddListener(() => OnFaceClicked(value));

        var option = go.AddComponent<SurveyFaceOption>();
        option.Init(icon, ringImage.gameObject);
        return option;
    }

    private static GameObject NewChild(string name, Transform parent, int layer)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = layer;
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image NewImage(string name, Transform parent, Sprite sprite, Color color, int layer)
    {
        Image img = NewChild(name, parent, layer).AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        return img;
    }

    private static TMP_Text NewText(string name, Transform parent, Style style, Color color, float size, FontStyles fontStyle)
    {
        var t = NewChild(name, parent, style.layer).AddComponent<TextMeshProUGUI>();
        if (style.font != null) t.font = style.font;
        t.color = color;
        t.fontStyle = fontStyle;
        t.fontSize = size;
        t.enableAutoSizing = true;          // long wording shrinks rather than overflows
        t.fontSizeMax = size;
        t.fontSizeMin = Mathf.Round(size * 0.65f);
        t.horizontalAlignment = HorizontalAlignmentOptions.Center;
        t.verticalAlignment = VerticalAlignmentOptions.Middle;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.raycastTarget = false;
        return t;
    }

    private static void Place(RectTransform r, Vector2 position, Vector2 size)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = position;
        r.sizeDelta = size;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    /// <summary>Sprites live in Resources/Survey. Falls back to the texture if it was not imported as a sprite.</summary>
    private static Sprite LoadSprite(string path)
    {
        Sprite sprite = Resources.Load<Sprite>(path);
        if (sprite != null) return sprite;
        Texture2D tex = Resources.Load<Texture2D>(path);
        if (tex != null)
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        Debug.LogWarning($"[PostRunSurvey] Missing Resources/{path}.png");
        return null;
    }
}
