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
/// to the main menu.
///
/// THE SEQUENCE, all on one panel so there is never more than one on screen:
///   arrival   - a green tick and "You have arrived!" with the completion sound, so the
///               participant knows why they have stopped (about 2.5 s);
///   questions - the page cross-fades to each question in turn;
///   thank you - then the panel fades away and the view fades to the menu.
/// The panel eases in (fades while growing from 95%) rather than appearing at once.
///
/// Up to three questions, each answered on five faces from red/sad (1) to green/happy
/// (5), picked with the controller ray and confirmed with Next.
///
///   1. "How calm did you feel on the walk?"          -> calm (1-5), and stress = 6 - calm      (every module)
///   2. "How confident did you feel finding the way?" -> confidence (1-5)                       (every module)
///   3. Depends on the module:
///      Unguided 1:   "How easy was it to find your own way?" -> ease (1-5), and difficulty = 6 - ease
///      Guided:       "How much did the blue line help you navigate?" -> guide_help (1-5); only asked
///                    if the guide appeared at least once, so a Guided run with no taps and no wrong
///                    turns gets two questions.
///      Unguided 2.a: "How much did the guidance help you find the way?" -> guidance_help (1-5). No
///                    line in this run: it asks how much the earlier Guided module helped. Asked of
///                    everyone, whether or not the line appeared in their Guided run.
///      Unguided 2.b: none (two questions).
///
/// Stress and difficulty are asked as calm and ease so that the happy green face is the good
/// answer on every question; the reversed columns keep the data reading as the original measure.
///
/// WHEN: only after a run that reached the end zone, and only for the run types listed in
/// Run Types (all four modules by default). Leaving a run from the pause menu skips it.
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
                 "answer into a stress score, and ease into a difficulty score.")]
        public string reversedColumn;

        [Tooltip("Only asked after these run types. Leave empty to ask after every run that has the survey.")]
        public string[] askAfter = new string[0];

        [Tooltip("Only asked if the guide (blue line, or the Turn around sign) appeared at least once " +
                 "during the run. Otherwise it is skipped and its cells are left blank.")]
        public bool onlyIfGuideShown;

        public Question(string id, string prompt, string lowLabel, string highLabel, string reversedColumn = "",
                        string[] askAfter = null, bool onlyIfGuideShown = false)
        {
            this.id = id;
            this.prompt = prompt;
            this.lowLabel = lowLabel;
            this.highLabel = highLabel;
            this.reversedColumn = reversedColumn;
            this.askAfter = askAfter ?? new string[0];
            this.onlyIfGuideShown = onlyIfGuideShown;
        }

        /// <summary>Whether this question is asked after a run of this type.</summary>
        public bool AskedAfter(string runType, int guideShownCount)
        {
            if (onlyIfGuideShown && guideShownCount <= 0)
                return false;
            if (askAfter == null || askAfter.Length == 0)
                return true;
            foreach (string t in askAfter)
                if (string.Equals(t?.Trim(), runType, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }

    public const int ScaleSize = 5;

    [Header("When")]
    [Tooltip("Run types that end with the survey. The module run types are unguided_1, guided, " +
             "unguided_2a and unguided_2b.")]
    [SerializeField] private string[] runTypes =
    {
        MenuController.ModuleUnguided1, MenuController.ModuleGuided,
        MenuController.ModuleUnguided2a, MenuController.ModuleUnguided2b,
    };

    [Tooltip("Seconds between reaching the end zone and the arrival panel easing in. Short: the " +
             "panel is what tells the participant why they have stopped.")]
    [SerializeField] private float delayBeforeSurvey = 0.3f;

    [Header("Questions")]
    [Tooltip("Asked in this order. Each run type gets only the questions whose Ask After includes it " +
             "(or is empty). survey_responses.csv has a column for every question here, left blank " +
             "where a question was not asked.")]
    [SerializeField] private Question[] questions =
    {
        new Question("calm", "How calm did you feel on the walk?", "Not calm", "Very calm", "stress"),
        new Question("confidence", "How confident did you feel finding the way?", "Not confident", "Very confident"),
        // Unguided 1 only: "how difficult" asked as "how easy", so the green face is the good answer.
        new Question("ease", "How easy was it to find your own way?", "Very hard", "Very easy", "difficulty",
                     new[] { MenuController.ModuleUnguided1 }),
        // Guided: only if the line (or Turn around sign) actually appeared - someone who never
        // tapped and never went wrong has nothing to rate.
        new Question("guide_help", "How much did the blue line help you navigate?", "Not helpful", "Very helpful", "",
                     new[] { MenuController.ModuleGuided }, onlyIfGuideShown: true),
        // Unguided 2.a, the first walk after Guided: how much the earlier guidance helped. There is
        // no line in this run, so it is asked of everyone.
        new Question("guidance_help", "How much did the guidance help you find the way?", "Not helpful", "Very helpful", "",
                     new[] { MenuController.ModuleUnguided2a }),
    };

    [Header("Words")]
    [Tooltip("Shown with a green tick before the questions.")]
    [SerializeField] private string arrivalText = "You have arrived!";
    [Tooltip("Seconds the arrival message shows before the first question.")]
    [SerializeField] private float arrivalSeconds = 2.5f;
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
    [Tooltip("Seconds for the panel to ease in (fade while growing from 95%) and out.")]
    [SerializeField] private float appearSeconds = 0.6f;
    [Tooltip("Seconds for each half of the cross-fade between pages (out, then in).")]
    [SerializeField] private float pageFadeSeconds = 0.25f;

    /// <summary>True from the moment the survey starts until the participant finishes it.</summary>
    public bool IsRunning { get; private set; }

    // ------------------------------------------------------------------ built UI
    private GameObject _root;
    private Canvas _canvas;
    private CanvasGroup _group;
    private HeadLockedUI _headLocked;
    private RectTransform _content;      // scaled for the ease-in; the root's scale belongs to ScalableUIRoot
    private CanvasGroup _arrivalPage;
    private CanvasGroup _questionPage;
    private CanvasGroup _thanksPage;
    private TMP_Text _prompt;
    private TMP_Text _lowLabel;
    private TMP_Text _highLabel;
    private Button _next;
    private CanvasGroup _nextGroup;
    private TMP_Text _nextText;
    private readonly List<Image> _dots = new List<Image>();
    private int _layer;
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
    /// <param name="guideShownCount">Times the guide appeared during the run (Guided), or -1 when
    /// the run has no guide. Decides whether guide questions are asked, and is saved with the row.</param>
    public IEnumerator Run(string runType, string participantId, int runIndex, int guideShownCount = -1)
    {
        if (IsRunning || questions == null || questions.Length == 0)
            yield break;

        // The questions this run gets, in order.
        var asked = new List<int>();
        for (int i = 0; i < questions.Length; i++)
        {
            if (questions[i].AskedAfter(runType, guideShownCount))
                asked.Add(i);
            else if (questions[i].onlyIfGuideShown && questions[i].AskedAfter(runType, 1))
                SessionLog.Record("survey_skipped", $"{questions[i].id} (guide never shown)");
        }
        if (asked.Count == 0)
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

        // Full-length arrays, one slot per question in the list; a question not asked keeps 0 and
        // is written as blank cells.
        int n = questions.Length;
        var answers = new int[n];
        var seconds = new float[n];
        var changes = new int[n];
        DateTime startedAt = DateTime.Now;
        SessionLog.Record("survey_started", runType);
        BuildDots(asked.Count);

        AssistanceRequest.Changed += HandleAssistanceChanged;
        try
        {
            _root.SetActive(true);
            _canvas.enabled = true;
            _hiddenForHelp = AssistanceRequest.IsActive;
            _headLocked.SnapToTarget();

            // Arrival: tells them why everything has stopped, before asking anything.
            ShowOnly(_arrivalPage);
            UiCuePlayer.Instance?.PlayStepComplete();
            yield return Appear();
            yield return WaitWhileVisible(arrivalSeconds);

            for (int step = 0; step < asked.Count; step++)
            {
                int q = asked[step];
                int position = step;
                yield return SwitchPage(step == 0 ? _arrivalPage : _questionPage, _questionPage,
                                        () => ShowQuestion(q, position, asked.Count));
                if (step > 0) UiCuePlayer.Instance?.PlayStepAdvance();
                _clickableFrom = Time.unscaledTime + ignoreClicksSeconds;

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
            SurveyResponseWriter.Write(participantId, runType, runIndex, startedAt, questions, answers, seconds, changes,
                                       guideShownCount);
            SessionLog.Record("survey_completed", runType);

            yield return SwitchPage(_questionPage, _thanksPage, null);
            UiCuePlayer.Instance?.PlayFlowComplete();
            yield return WaitWhileVisible(thanksSeconds);
            yield return Disappear();
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

    /// <param name="index">The question in the list.</param>
    /// <param name="position">Its place among the questions this run gets (for Next/Done and the dots).</param>
    private void ShowQuestion(int index, int position, int count)
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

        bool last = position == count - 1;
        _nextText.text = last ? doneLabel : nextLabel;
        SetNextEnabled(false);

        for (int i = 0; i < _dots.Count; i++)
            _dots[i].color = new Color(1f, 1f, 1f, i == position ? 1f : 0.35f);
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

    /// <summary>Counts down only while the survey is on screen (not while help is up).</summary>
    private IEnumerator WaitWhileVisible(float seconds)
    {
        float left = seconds;
        while (left > 0f)
        {
            if (!_hiddenForHelp) left -= Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static float EaseOut(float k) => 1f - (1f - k) * (1f - k) * (1f - k);
    private static float Smooth(float k) => k * k * (3f - 2f * k);

    /// <summary>The panel eases in: fades up while growing from 95% to full size.</summary>
    private IEnumerator Appear()
    {
        _group.interactable = false;
        _group.blocksRaycasts = false;
        for (float t = 0f; t < appearSeconds; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / appearSeconds);
            _content.localScale = Vector3.one * Mathf.Lerp(0.95f, 1f, EaseOut(k));
            if (!_hiddenForHelp) _group.alpha = Smooth(k);
            yield return null;
        }
        _content.localScale = Vector3.one;
        if (!_hiddenForHelp)
        {
            _group.alpha = 1f;
            _group.interactable = true;
            _group.blocksRaycasts = true;
        }
    }

    /// <summary>The reverse, a little quicker and smaller in movement.</summary>
    private IEnumerator Disappear()
    {
        _group.interactable = false;
        _group.blocksRaycasts = false;
        float start = _group.alpha;
        float duration = appearSeconds * 0.7f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / duration);
            _content.localScale = Vector3.one * Mathf.Lerp(1f, 0.97f, Smooth(k));
            _group.alpha = Mathf.Lerp(start, 0f, Smooth(k));
            yield return null;
        }
        _group.alpha = 0f;
    }

    /// <summary>
    /// Cross-fades the panel's contents: the old page fades out, the content changes while it is
    /// blank, then the new page fades in. The panel itself (background) stays put throughout.
    /// </summary>
    private IEnumerator SwitchPage(CanvasGroup from, CanvasGroup to, Action change)
    {
        if (from != null && from.gameObject.activeSelf)
        {
            from.interactable = false;
            from.blocksRaycasts = false;
            float start = from.alpha;
            for (float t = 0f; t < pageFadeSeconds; t += Time.unscaledDeltaTime)
            {
                from.alpha = Mathf.Lerp(start, 0f, Smooth(Mathf.Clamp01(t / pageFadeSeconds)));
                yield return null;
            }
            from.alpha = 0f;
        }

        change?.Invoke();
        ShowOnly(to);
        to.alpha = 0f;
        to.interactable = false;
        to.blocksRaycasts = false;
        for (float t = 0f; t < pageFadeSeconds; t += Time.unscaledDeltaTime)
        {
            to.alpha = Smooth(Mathf.Clamp01(t / pageFadeSeconds));
            yield return null;
        }
        to.alpha = 1f;
        to.interactable = true;
        to.blocksRaycasts = true;
    }

    private void ShowOnly(CanvasGroup page)
    {
        foreach (CanvasGroup p in new[] { _arrivalPage, _questionPage, _thanksPage })
            if (p != null) p.gameObject.SetActive(p == page);
        page.alpha = 1f;
        page.interactable = true;
        page.blocksRaycasts = true;
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

        // Everything sits in Content, which the ease-in scales. The root's own scale belongs to
        // ScalableUIRoot (the text-size setting), so it is left alone.
        _content = (RectTransform)NewChild("Content", _root.transform, style.layer).transform;
        Stretch(_content);

        // Panel
        Image bg = NewImage("Background", _content, style.panelSprite, style.panelColor, style.layer);
        bg.type = style.panelSprite != null ? style.panelType : Image.Type.Simple;
        Stretch(bg.rectTransform);

        float top = panelSize.y * 0.5f;
        float bottom = -panelSize.y * 0.5f;

        // Arrival: a green tick and "You have arrived!".
        _arrivalPage = NewPage("Arrival", style.layer);
        Image tick = NewImage("Tick", _arrivalPage.transform, LoadSprite("Survey/SurveyTick"), Color.white, style.layer);
        tick.raycastTarget = false;
        tick.preserveAspect = true;
        Place(tick.rectTransform, new Vector2(0f, 90f), new Vector2(180f, 180f));
        TMP_Text arrived = NewText("ArrivalText", _arrivalPage.transform, style, style.textColor, 76f, FontStyles.Bold);
        arrived.text = arrivalText;
        Place(arrived.rectTransform, new Vector2(0f, -110f), new Vector2(panelSize.x - 80f, 160f));

        _questionPage = NewPage("Question", style.layer);

        // The position dots are made per survey (BuildDots), as the number of questions depends
        // on the run type.
        _layer = style.layer;

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
        _thanksPage = NewPage("Thanks", style.layer);
        TMP_Text thanks = NewText("ThanksText", _thanksPage.transform, style, style.textColor, 80f, FontStyles.Bold);
        thanks.text = thanksText;
        Place(thanks.rectTransform, Vector2.zero, new Vector2(panelSize.x - 80f, 300f));

        ShowOnly(_arrivalPage);
    }

    /// <summary>
    /// Which question this is: one dot per question this run gets, the current one bright. No
    /// words needed. None for a single question.
    /// </summary>
    private void BuildDots(int count)
    {
        foreach (Image d in _dots)
            if (d != null) Destroy(d.gameObject);
        _dots.Clear();
        if (count < 2) return;

        Sprite dot = LoadSprite("Survey/SurveyDot");
        float pitch = 44f;
        float x0 = -(count - 1) * pitch * 0.5f;
        float top = panelSize.y * 0.5f;
        for (int i = 0; i < count; i++)
        {
            Image d = NewImage($"Dot_{i + 1}", _questionPage.transform, dot, Color.white, _layer);
            d.raycastTarget = false;
            Place(d.rectTransform, new Vector2(x0 + i * pitch, top - 50f), new Vector2(24f, 24f));
            _dots.Add(d);
        }
    }

    /// <summary>A full-panel page with its own CanvasGroup, for cross-fading.</summary>
    private CanvasGroup NewPage(string name, int layer)
    {
        GameObject page = NewChild(name, _content, layer);
        Stretch((RectTransform)page.transform);
        return page.AddComponent<CanvasGroup>();
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
