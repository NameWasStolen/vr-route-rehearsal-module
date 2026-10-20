using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace VRTutorial
{
    /// <summary>
    /// Makes a head-locked panel see-through while the participant is moving, so the panel does
    /// not cover the street at the very moment they need to see it. It comes back to full as soon
    /// as they stop.
    ///
    /// Put it on the root of the tutorial panel, beside HeadLockedUI. The pause and help panels do
    /// not need it: the pause lock stops all movement while they are open.
    ///
    /// How it fades without fighting anything else
    /// -------------------------------------------
    /// TutorialFlow owns the panel root's CanvasGroup alpha (dismiss / restore), TutorialStep owns
    /// each step's CanvasGroup alpha (step cross-fades), and TaskProgressIndicator owns its own
    /// CanvasGroup alpha (appear / hide). Writing any of those would mean two scripts setting the
    /// same number. So this fades through each Graphic's own colour alpha instead. Unity
    /// multiplies that with the CanvasGroup alphas above it, so every fade composes with every
    /// other and none of them has to know this exists. The original alpha is remembered and put
    /// back exactly when the fade ends.
    ///
    /// Two layers fade by different amounts:
    ///   - Backing: everything outside a TutorialStep, i.e. the Background and frame on the panel
    ///     root. This is what actually hides the scene, so it goes furthest.
    ///   - Content: everything inside a step (text, diagrams). It dims less, so the words are
    ///     still there as a shape and do not seem to vanish.
    ///
    /// Progress bars
    /// -------------
    /// The bars are how the panel says "keep going". Fading them would make a lesson that is
    /// working look like one that has stalled. So:
    ///   1. A TaskProgressIndicator and its children never fade.
    ///   2. While any showing bar is filling, or finished less than Hold After Complete seconds
    ///      ago, everything but the bar fades while moving - backing to Bar Backing Alpha (clear
    ///      by default), text and diagrams to Bar Content Alpha (half by default). The bar stays
    ///      at full strength as the 'keep going' signal. This covers the movement lesson. It does
    ///      NOT cover the turning lesson, whose bar returns to zero between directions; see below.
    ///   3. A bar's track that is a SIBLING of the fill, not its parent, is not covered by rule 1.
    ///      Put a MotionFadeRule (Never Fade This) on the track.
    ///
    /// Other times the panel stays solid
    /// ---------------------------------
    ///   - For Read Window seconds after a step changes, so new instructions are always seen at
    ///     full strength even if they arrive while walking.
    ///   - While the tutorial is paused, or the panel is frozen for pointing.
    ///   - For Prompt Change Read seconds after the wording of any showing text changes, so a
    ///     mid-step instruction ("Now turn left", "Other way - try again") is seen at full strength.
    ///   - While a step with MotionFadeRule (Hold Panel While Showing) is showing. The turning
    ///     lesson (StepCamera) needs this in every mode - Snap, Continuous and Raw. Its bar goes
    ///     back to zero after each direction and stays at zero on a wrong-way turn, so the bar
    ///     alone would let the panel fade mid-lesson while the participant is turning.
    ///
    /// Movement is the rig walking or turning (stick walking, snap and continuous turning), and
    /// optionally the headset walking in the room or turning steadily (Raw turning mode).
    /// </summary>
    [DisallowMultipleComponent]
    public class PanelMotionFade : MonoBehaviour
    {
        [Header("Sources")]
        [Tooltip("The XR camera. Leave empty to use Camera.main.")]
        [SerializeField] private Transform headTransform;

        [Tooltip("The object that locomotion moves and turns - the XR Origin. Leave empty to use " +
                 "the camera's grandparent (Main Camera -> Camera Offset -> XR Origin).")]
        [SerializeField] private Transform rigTransform;

        [Tooltip("Optional. When set, each step change starts a Read Window, and the list of " +
                 "graphics is refreshed. Without it, call NotifyContentChanged yourself.")]
        [SerializeField] private TutorialFlow flow;

        [Header("What counts as moving")]
        [Tooltip("Rig speed across the ground, metres per second, that counts as walking with the stick.")]
        [SerializeField] private float moveSpeedThreshold = 0.25f;

        [Tooltip("Rig turn rate, degrees per second, that counts as turning. Snap turns spike far " +
                 "above this for one frame. A single snap is too short to fade the panel (see " +
                 "Move Onset Delay); a run of snaps does fade it.")]
        [SerializeField] private float turnSpeedThreshold = 30f;

        [Tooltip("Also count walking in the room, measured from the headset itself.")]
        [SerializeField] private bool includePhysicalWalking = true;

        [Tooltip("Headset speed across the ground, m/s, that counts as walking in the room. Kept " +
                 "well above the sway of standing still and the drift of a fast glance around.")]
        [SerializeField] private float physicalWalkThreshold = 0.45f;

        [Tooltip("Also count turning the body under the Raw turning mode, where the rig never " +
                 "rotates and only the headset does. Measured as sustained head turn rate, so a " +
                 "quick glance (shorter than Move Onset Delay) does not count.")]
        [SerializeField] private bool includeHeadTurning = true;

        [Tooltip("Head turn rate, degrees per second, that counts as turning under Raw. Kept above " +
                 "the slow drift of looking about while reading.")]
        [SerializeField] private float headTurnThreshold = 60f;

        [Tooltip("Seconds of continued movement before the panel starts to fade. Stops a small " +
                 "nudge of the stick, or a single snap turn, from making the panel flicker.")]
        [SerializeField] private float moveOnsetDelay = 0.4f;

        [Tooltip("Seconds of stillness before the panel comes back. Short: someone who has " +
                 "stopped usually wants to read.")]
        [SerializeField] private float stillDelay = 0.5f;

        [Header("How far to fade")]
        [Tooltip("Opacity of the backing (Background and frame) while moving. This layer does most " +
                 "of the hiding.")]
        [Range(0f, 1f)]
        [SerializeField] private float backingAlpha = 0.2f;

        [Tooltip("Opacity of the backing while moving DURING a lesson whose progress bar is filling " +
                 "(the movement lesson). The bar itself always stays fully visible. 0 = backing " +
                 "fully clear.")]
        [Range(0f, 1f)]
        [SerializeField] private float barBackingAlpha = 0f;

        [Tooltip("Opacity of step content (text, diagrams) while moving during a lesson whose " +
                 "progress bar is filling. Everything but the bar dims; the bar stays at full " +
                 "strength as the 'keep going' signal.")]
        [Range(0f, 1f)]
        [SerializeField] private float barContentAlpha = 0.5f;

        [Tooltip("Opacity of step content (text, diagrams) while moving.")]
        [Range(0f, 1f)]
        [SerializeField] private float contentAlpha = 0.5f;

        // Renamed from fadeOutDuration / fadeInDuration (0.6 / 0.3) on 24 Sept, on purpose without
        // FormerlySerializedAs: the old values saved in Tutorial.unity would otherwise override
        // the new, slower defaults.
        [Tooltip("Seconds to fade down, whatever the depth of the fade - fading to clear and " +
                 "fading to half both take this long. Slow on purpose: a panel that suddenly " +
                 "vanishes reads as something going wrong.")]
        [SerializeField] private float fadeOutSeconds = 1.5f;

        [Tooltip("Seconds to come back to full once still. Quicker than the fade out: someone " +
                 "who has stopped usually wants to read.")]
        [SerializeField] private float fadeInSeconds = 0.6f;

        [Header("When to stay solid")]
        [Tooltip("Seconds after a step changes during which the panel will not fade, so a new " +
                 "instruction is always seen at full strength.")]
        [SerializeField] private float readWindowSeconds = 5f;

        [Tooltip("Seconds the panel stays solid after the wording of any showing text changes " +
                 "within a step - the turn lesson's 'Now turn left', or its 'Other way - try " +
                 "again'. A progress bar cannot cover these: the turn lesson's bar goes back to " +
                 "zero after each direction and stays at zero on a wrong-way turn, which is " +
                 "exactly when the new words matter.")]
        [SerializeField] private float promptChangeReadSeconds = 3f;

        [Tooltip("Seconds a finished progress bar keeps the panel solid, so the congratulation is " +
                 "seen. Match TutorialFlow's Default Advance Delay.")]
        [SerializeField] private float holdAfterCompleteSeconds = 2.5f;

        [Header("Diagnostics")]
        [Tooltip("Live readout while playing: whether movement is detected, the measured speeds, " +
                 "and - if the panel is being held solid - exactly why. Read-only; anything typed " +
                 "here is overwritten.")]
        [SerializeField, TextArea(2, 4)] private string status;

        [Tooltip("Also write the readout to the Console each time the reason changes.")]
        [SerializeField] private bool logChanges = false;

        // ------------------------------------------------------------------ state

        private class Entry
        {
            public Graphic graphic;
            public CanvasRenderer renderer;   // cached; looked up directly, never created
            public bool isContent;
            public float baseline;   // the alpha somebody else wants; we multiply it down
            public float written;    // what we last wrote, to tell our writes from theirs
            public bool tracking;
        }

        private readonly Dictionary<Graphic, Entry> _entries = new Dictionary<Graphic, Entry>();
        private readonly List<Graphic> _scratch = new List<Graphic>();
        private readonly List<Graphic> _dead = new List<Graphic>();
        private TaskProgressIndicator[] _indicators = new TaskProgressIndicator[0];
        private readonly List<MotionFadeRule> _holdRules = new List<MotionFadeRule>();

        private HeadLockedUI _headLocked;
        private CanvasGroup _rootGroup;

        private Vector3 _lastRigPos;
        private float _lastRigYaw;
        private float _lastHeadYaw;
        private float _headTurn;   // smoothed
        private Vector3 _lastHeadPos;
        private bool _haveSamples;
        private float _rigSpeed, _rigTurn, _headSpeed;   // smoothed

        private float _motionStart = -1f;   // start of the current burst of movement
        private float _lastMotion = -999f;

        private enum HoldLevel { None, BarLesson, Solid }

        // Current multipliers for each layer. 1 = as authored. Each eases toward its own target, so
        // switching between a full fade and a backing-only fade never pops.
        private float _backingNow = 1f;
        private float _contentNow = 1f;
        private float _backingLow = 1f;   // lowest point of the current fade, for timing the return
        private float _contentLow = 1f;
        private bool _applied;              // anything currently written below full
        private float _readUntil;
        private string _readReason = "new step";
        private string _holdReason = "";
        private string _lastLogged;

        private readonly Dictionary<TMP_Text, string> _lastText = new Dictionary<TMP_Text, string>();
        private readonly List<TMP_Text> _textScratch = new List<TMP_Text>();

        /// <summary>True while the participant is moving by this component's definition.</summary>
        public bool IsMoving { get; private set; }

        /// <summary>1 when the panel is solid, 0 when fully faded. For debugging in the Inspector.</summary>
        public float Visibility => Mathf.Min(_backingNow, _contentNow);

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            _headLocked = GetComponent<HeadLockedUI>();
        }

        private void OnEnable()
        {
            if (flow != null) flow.onStepChanged.AddListener(OnStepChanged);
            _haveSamples = false;
            _motionStart = -1f;
            _backingNow = _contentNow = 1f;
            _backingLow = _contentLow = 1f;
            Refresh();
            NotifyContentChanged();
        }

        private void OnDisable()
        {
            if (flow != null) flow.onStepChanged.RemoveListener(OnStepChanged);
            RestoreAll();
        }

        private void OnStepChanged(int _) => NotifyContentChanged();

        /// <summary>
        /// Call when the panel shows something new. Starts a Read Window and picks up any graphics
        /// added since last time. Wired automatically when Flow is set.
        /// </summary>
        public void NotifyContentChanged()
        {
            _readUntil = Time.unscaledTime + readWindowSeconds;
            _readReason = "new step";
            Refresh();
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;

            SampleMotion(now, dt);
            CheckTextChanges(now);

            if (_rootGroup == null) _rootGroup = GetComponent<CanvasGroup>();
            bool panelHidden = _rootGroup != null && _rootGroup.alpha < 0.01f;

            _holdReason = "";
            HoldLevel hold = EvaluateHold(now);   // every frame, so the readout is always live

            float backingGoal = 1f, contentGoal = 1f;
            if (IsMoving && !panelHidden)
            {
                if (hold == HoldLevel.None) { backingGoal = backingAlpha; contentGoal = contentAlpha; }
                else if (hold == HoldLevel.BarLesson) { backingGoal = barBackingAlpha; contentGoal = barContentAlpha; }
            }
            UpdateStatus(hold, panelHidden);

            if ((backingGoal < _backingNow || contentGoal < _contentNow) && !_applied) Refresh();

            _backingNow = Step(_backingNow, backingGoal, ref _backingLow, dt);
            _contentNow = Step(_contentNow, contentGoal, ref _contentLow, dt);

            if (_backingNow >= 1f && _contentNow >= 1f)
            {
                if (_applied) RestoreAll();
                return;
            }

            Apply(_backingNow, _contentNow);
        }

        /// <summary>
        /// Moves a layer's opacity toward its goal. Rates are per full 0-1 range, so a fade to 0.2
        /// takes 80% of Fade Out Duration - consistent speed whichever target is in play.
        /// </summary>
        private float Step(float current, float goal, ref float low, float dt)
        {
            // Speed is set so the whole transition takes the stated time whatever its depth:
            // going down, the span is 1 -> goal; coming back, it is the lowest point reached -> 1.
            bool down = goal < current;
            float duration = down ? fadeOutSeconds : fadeInSeconds;
            if (duration <= 0f) { low = Mathf.Min(low, goal); return goal; }

            float span = down ? 1f - goal : 1f - low;
            float next = Mathf.MoveTowards(current, goal, Mathf.Max(span, 0.05f) * dt / duration);

            low = next >= 1f ? 1f : Mathf.Min(low, next);
            return next;
        }


        private void SampleMotion(float now, float dt)
        {
            ResolveSources();
            if (headTransform == null || dt <= 0f) { IsMoving = false; return; }

            Vector3 rigPos = rigTransform != null ? rigTransform.position : headTransform.position;
            float rigYaw = rigTransform != null ? rigTransform.eulerAngles.y : 0f;
            Vector3 headPos = headTransform.position;
            float headYaw = headTransform.eulerAngles.y;

            if (!_haveSamples)
            {
                _lastRigPos = rigPos; _lastRigYaw = rigYaw; _lastHeadPos = headPos; _lastHeadYaw = headYaw;
                _rigSpeed = _rigTurn = _headSpeed = _headTurn = 0f;
                _haveSamples = true;
                IsMoving = false;
                return;
            }

            float rigSpeed = Horizontal(rigPos - _lastRigPos).magnitude / dt;
            float rigTurn = Mathf.Abs(Mathf.DeltaAngle(_lastRigYaw, rigYaw)) / dt;
            float headSpeed = Horizontal(headPos - _lastHeadPos).magnitude / dt;
            // Head yaw relative to the rig, so a snap or stick turn is not counted twice.
            float headTurn = Mathf.Abs(Mathf.DeltaAngle(_lastHeadYaw - _lastRigYaw, headYaw - rigYaw)) / dt;
            _lastRigPos = rigPos; _lastRigYaw = rigYaw; _lastHeadPos = headPos; _lastHeadYaw = headYaw;

            // Light smoothing, ~0.15 s, so tracking jitter does not count as walking. A snap turn
            // or teleport still shows up, but only for a frame or two.
            float a = 1f - Mathf.Exp(-dt / 0.15f);
            _rigSpeed = Mathf.Lerp(_rigSpeed, rigSpeed, a);
            _rigTurn = Mathf.Lerp(_rigTurn, rigTurn, a);
            _headSpeed = Mathf.Lerp(_headSpeed, headSpeed, a);
            _headTurn = Mathf.Lerp(_headTurn, headTurn, a);

            bool movingNow = _rigSpeed > moveSpeedThreshold || _rigTurn > turnSpeedThreshold ||
                             (includePhysicalWalking && _headSpeed > physicalWalkThreshold) ||
                             (includeHeadTurning && _headTurn > headTurnThreshold);

            if (movingNow)
            {
                if (_motionStart < 0f || now - _lastMotion > stillDelay) _motionStart = now;
                _lastMotion = now;
            }

            // Moving means a burst that has lasted long enough, and has not gone quiet. A single
            // snap turn or a teleport is a burst of length zero, so it never counts.
            bool sustained = _motionStart >= 0f && _lastMotion - _motionStart >= moveOnsetDelay;
            bool recent = now - _lastMotion <= stillDelay;
            IsMoving = sustained && recent;
        }

        /// <summary>
        /// New wording inside a step is new information, just like a new step. Compared by
        /// reference: assigning .text always gives a new string, and nothing is allocated here.
        /// </summary>
        private void CheckTextChanges(float now)
        {
            if (_lastText.Count == 0) return;

            _textScratch.Clear();
            _textScratch.AddRange(_lastText.Keys);
            foreach (TMP_Text t in _textScratch)
            {
                if (t == null) { _lastText.Remove(t); continue; }
                string current = t.text;
                if (ReferenceEquals(current, _lastText[t])) continue;

                bool changed = current != _lastText[t];
                _lastText[t] = current;
                if (changed && IsShowing(t.transform, includeSelf: true) &&
                    now + promptChangeReadSeconds > _readUntil)
                {
                    _readUntil = now + promptChangeReadSeconds;
                    _readReason = $"wording changed on '{t.name}'";
                }
            }
        }

        /// <summary>
        /// How much of the panel has to stay while moving.
        ///   Solid       - nothing fades.
        ///   BarLesson   - a lesson's bar is filling: everything but the bar fades to the Bar alphas.
        ///                 Wins over a read window, because a bar that is filling means they have
        ///                 already read the instruction and are carrying it out.
        ///   None        - normal fade.
        /// </summary>
        private HoldLevel EvaluateHold(float now)
        {
            if (TutorialPause.IsPaused) { Hold("tutorial is paused"); return HoldLevel.Solid; }
            if (_headLocked != null && _headLocked.IsFrozen) { Hold("panel is frozen (HeadLockedUI)"); return HoldLevel.Solid; }

            for (int i = 0; i < _holdRules.Count; i++)
            {
                MotionFadeRule rule = _holdRules[i];
                if (rule != null && IsShowing(rule.transform, includeSelf: true))
                {
                    Hold($"Motion Fade Rule on '{rule.name}' is showing");
                    return HoldLevel.Solid;
                }
            }

            if (BarActive(now)) return HoldLevel.BarLesson;

            if (now < _readUntil)
            {
                Hold($"{_readReason} ({_readUntil - now:0.0} s left)");
                return HoldLevel.Solid;
            }

            return HoldLevel.None;
        }

        private bool BarActive(float now)
        {
            for (int i = 0; i < _indicators.Length; i++)
            {
                TaskProgressIndicator bar = _indicators[i];
                if (bar == null) continue;

                bool active = bar.IsInProgress ||
                              (bar.IsComplete && now - bar.CompletedAtUnscaled < holdAfterCompleteSeconds);
                // The bar's own CanvasGroup is left out of the check: it hides itself until
                // progress starts, and a bar that is fading in is exactly the one that matters.
                if (active && IsShowing(bar.transform, includeSelf: false))
                    return Hold(bar.IsComplete
                        ? $"progress bar '{bar.transform.parent?.name}/{bar.name}' just completed - bar stays solid"
                        : $"progress bar '{bar.transform.parent?.name}/{bar.name}' is filling - bar stays solid");
            }
            return false;
        }

        private bool Hold(string reason)
        {
            _holdReason = reason;
            return true;
        }

        private void UpdateStatus(HoldLevel hold, bool panelHidden)
        {
            string motion = IsMoving
                ? "MOVING"
                : "still";
            string speeds = $"rig {_rigSpeed:0.00} m/s (>{moveSpeedThreshold}), rig turn {_rigTurn:0} deg/s (>{turnSpeedThreshold}), " +
                            $"head {_headSpeed:0.00} m/s (>{physicalWalkThreshold}), head turn {_headTurn:0} deg/s (>{headTurnThreshold})";
            string state;
            if (headTransform == null) state = "no head camera found (Camera.main is null)";
            else if (panelHidden) state = "panel dismissed - nothing to fade";
            else if (hold == HoldLevel.BarLesson) state = (IsMoving ? "fading all but bar: " : "would fade all but bar: ") + _holdReason;
            else if (!string.IsNullOrEmpty(_holdReason)) state = "held solid: " + _holdReason;
            else state = IsMoving ? "fading" : "solid (not moving)";

            status = $"{motion} | {state}\n{speeds}\n{DescribeLayers()}\nrig: {(rigTransform != null ? rigTransform.name : "none")}, backing {_backingNow:0.00}, content {_contentNow:0.00}";

            if (!logChanges) return;
            string key = motion + "|" + (string.IsNullOrEmpty(_holdReason) ? state : _holdReason.Split('(')[0]);
            if (key == _lastLogged) return;
            _lastLogged = key;
            Debug.Log($"[PanelMotionFade] {status}", this);
        }

        private string DescribeLayers()
        {
            int content = 0;
            var backing = new System.Text.StringBuilder();
            foreach (Entry e in _entries.Values)
            {
                if (e.graphic == null) continue;
                if (e.isContent) { content++; continue; }
                if (backing.Length > 0) backing.Append(", ");
                backing.Append(e.graphic.name).Append(" a=").Append(e.graphic.color.a.ToString("0.00"));
                if (!e.graphic.isActiveAndEnabled) backing.Append(" (off)");
            }
            return $"backing graphics: [{backing}] | content graphics: {content}";
        }

        // ------------------------------------------------------------------ applying

        /// <summary>
        /// Writes each graphic's colour alpha. (The first version used CanvasRenderer alpha so as
        /// never to touch a colour; in play-testing on 24 Sept the backing read 0 but did not
        /// visibly change, so it now uses Graphic.color, which is what the Background's authored
        /// 0.59 alpha lives in. Nothing else in the project writes these colours - the only
        /// script that sets a Graphic colour is TaskProgressIndicator, and bars are exempt.)
        /// </summary>
        private void Apply(float backingFactor, float contentFactor)
        {
            foreach (Entry e in _entries.Values)
            {
                Graphic g = e.graphic;
                if (g == null) continue;
                Color c = g.color;

                // Something other than us changed it since our last write. That is the new
                // value to respect; we only ever scale it down.
                if (!e.tracking || !Mathf.Approximately(c.a, e.written))
                {
                    e.baseline = c.a;
                    e.tracking = true;
                }

                float w = e.baseline * (e.isContent ? contentFactor : backingFactor);
                if (!Mathf.Approximately(c.a, w))
                {
                    c.a = w;
                    g.color = c;
                }
                e.written = w;
            }
            _applied = true;
        }

        private void RestoreAll()
        {
            foreach (Entry e in _entries.Values)
            {
                Graphic g = e.graphic;
                if (g == null || !e.tracking) continue;
                // Only put back what is still ours; a value somebody else wrote since stays.
                Color c = g.color;
                if (Mathf.Approximately(c.a, e.written))
                {
                    c.a = e.baseline;
                    g.color = c;
                }
                e.tracking = false;
            }
            _applied = false;
        }

        // ------------------------------------------------------------------ discovery

        /// <summary>
        /// Rebuilds what fades and what holds. Merges rather than replaces, so a graphic that is
        /// mid-fade keeps its remembered alpha - replacing would take the dimmed value as the new
        /// normal and leave it dim for good.
        /// </summary>
        private void Refresh()
        {
            Transform root = transform;

            _scratch.Clear();
            GetComponentsInChildren(true, _scratch);
            var seen = new HashSet<Graphic>(_scratch);

            foreach (Graphic g in _scratch)
            {
                if (_entries.ContainsKey(g)) continue;
                if (IsExempt(g.transform, root)) continue;

                // Some graphics have no CanvasRenderer - the panel root's disabled placeholder
                // text is one. TMP's canvasRenderer property returns null instead of adding one,
                // and calling GetAlpha on that threw, which aborted the whole fade every frame
                // (24 Sept). Anything without its own renderer draws nothing, so skip it.
                if (!g.TryGetComponent(out CanvasRenderer cr)) continue;
                _entries.Add(g, new Entry { graphic = g, renderer = cr, isContent = IsInsideStep(g.transform, root) });
            }

            _dead.Clear();
            foreach (var kv in _entries)
                if (kv.Key == null || !seen.Contains(kv.Key)) _dead.Add(kv.Key);
            foreach (Graphic g in _dead) _entries.Remove(g);

            _indicators = GetComponentsInChildren<TaskProgressIndicator>(true);

            // Texts are only recorded when first seen, never treated as changed on discovery.
            foreach (TMP_Text t in GetComponentsInChildren<TMP_Text>(true))
                if (!_lastText.ContainsKey(t)) _lastText.Add(t, t.text);

            _holdRules.Clear();
            foreach (MotionFadeRule rule in GetComponentsInChildren<MotionFadeRule>(true))
                if (rule.RuleMode == MotionFadeRule.Mode.HoldPanelWhileShowing) _holdRules.Add(rule);
        }

        private static bool IsExempt(Transform t, Transform root)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p.GetComponent<TaskProgressIndicator>() != null) return true;
                MotionFadeRule rule = p.GetComponent<MotionFadeRule>();
                if (rule != null && rule.RuleMode == MotionFadeRule.Mode.NeverFadeThis) return true;
                if (p == root) break;
            }
            return false;
        }

        private static bool IsInsideStep(Transform t, Transform root)
        {
            for (Transform p = t; p != null && p != root; p = p.parent)
                if (p.GetComponent<TutorialStep>() != null) return true;
            return false;
        }

        /// <summary>
        /// Active, and not faded out by any CanvasGroup between it and this panel's root (the
        /// root included, so a dismissed panel counts as not showing).
        /// </summary>
        private bool IsShowing(Transform t, bool includeSelf)
        {
            if (!t.gameObject.activeInHierarchy) return false;

            float alpha = 1f;
            for (Transform p = includeSelf ? t : t.parent; p != null; p = p.parent)
            {
                CanvasGroup g = p.GetComponent<CanvasGroup>();
                if (g != null && g.enabled)
                {
                    alpha *= g.alpha;
                    if (g.ignoreParentGroups) break;
                }
                if (p == transform) break;
            }
            return alpha > 0.05f;
        }

        // ------------------------------------------------------------------ helpers

        private void ResolveSources()
        {
            if (headTransform == null && Camera.main != null)
            {
                headTransform = Camera.main.transform;
                _haveSamples = false;
            }

            if (rigTransform == null && headTransform != null)
            {
                Transform offset = headTransform.parent;
                Transform resolved = offset != null && offset.parent != null ? offset.parent : offset;
                if (resolved != null)
                {
                    rigTransform = resolved;
                    _haveSamples = false;
                }
            }
        }

        private static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
