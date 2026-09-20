using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

namespace VRTutorial
{
    /// <summary>
    /// The turning lesson, in whichever form the participant's turning setting calls for.
    ///
    /// Replaces <see cref="SnapTurnTask"/>, which only ever taught snap turn and - worse - could
    /// only ever be *completed* under snap turn. It detects a large single-frame yaw jump, which
    /// by design never happens under continuous rotation, so the moment continuous became
    /// selectable in settings the tutorial acquired a step a participant could sit in forever.
    /// That was the standing TODO in SettingsController.
    ///
    /// Three modes, one component, one step:
    ///
    /// | Setting    | What is taught                  | How completion is detected              |
    /// |------------|---------------------------------|-----------------------------------------|
    /// | Snap       | Flick the stick, world jumps    | One-frame rig yaw jump over a threshold |
    /// | Continuous | Hold the stick, world sweeps    | Accumulated rig yaw sweep per direction |
    /// | Raw        | No stick at all - turn yourself | Head yaw away from centre, held briefly |
    ///
    /// The mode is resolved once, when the lesson starts, from <see cref="RotationModeController"/>.
    /// Deliberately not live: a setting changed mid-lesson would otherwise redefine the task
    /// underneath somebody halfway through it. Changing the setting and then choosing "Practise
    /// turning" from the pause menu re-enters the step, which re-resolves - that is the intended
    /// route, and it is the same route a participant takes anyway.
    ///
    /// Shaped to drop into the existing wiring: same event names as SnapTurnTask, same handed
    /// prompt sets, same onCompleted meaning "this lesson is finished, flow please carry on", and
    /// it still implements ITutorialTask so TutorialFlow's review mechanism needs no changes.
    /// </summary>
    public class TurnTask : MonoBehaviour, ITutorialTask
    {
        public enum TurnStyle { Snap, Continuous, HeadOnly }

        public enum Sequence
        {
            [InspectorName("Right, then left")] RightThenLeft,
            [InspectorName("Left, then right")] LeftThenRight,
            [InspectorName("Both, either order")] EitherOrder,
            [InspectorName("Any direction, N turns")] AnyDirection,
        }

        // ------------------------------------------------------------------ references

        [Header("References")]
        [Tooltip("The rig root that snap and continuous turn rotate - your XRPlayerRig / XR Origin " +
                 "object. NOT the camera; the camera also moves when the participant physically " +
                 "turns their head, which is the whole basis of the Raw lesson. Leave empty and it " +
                 "is found by walking up from Camera.main.")]
        [SerializeField] private Transform xrOrigin;

        [Tooltip("The head. Used by the Raw lesson only, to measure physical turning. Leave empty " +
                 "and Camera.main is used.")]
        [SerializeField] private Transform headTransform;

        [Tooltip("Optional. Prompt text updated as the participant progresses.")]
        [SerializeField] private TMP_Text promptLabel;

        // ------------------------------------------------------------------ mode

        [Header("Mode")]
        [Tooltip("Turning mode assumed when no RotationModeController is present, i.e. when this " +
                 "scene is opened standalone for testing without Bootstrap.")]
        [SerializeField] private PlayerRotationMode editorFallbackMode = PlayerRotationMode.Snap;

        [Tooltip("Ignore the participant's setting and always teach the mode below. For authoring " +
                 "and testing the three variants without going through the settings menu. Leave " +
                 "unticked for the study build.")]
        [SerializeField] private bool overrideMode = false;

        [SerializeField] private PlayerRotationMode modeOverride = PlayerRotationMode.Snap;

        [Header("Mode visuals")]
        [Tooltip("Switched on when the snap lesson is the one being taught, off otherwise. Put the " +
                 "controller diagram, the thumbstick highlight and the turn chevrons in here.")]
        [SerializeField] private GameObject snapVisuals;

        [Tooltip("Switched on for the continuous lesson. Same controller diagram, but the artwork " +
                 "should read as 'push and hold' rather than 'flick' - a held stick and a curved " +
                 "sweep arrow rather than two chevrons.")]
        [SerializeField] private GameObject continuousVisuals;

        [Tooltip("Switched on for the Raw lesson. This one must NOT show a thumbstick: there is no " +
                 "turning control in this mode, and pointing at one would teach the opposite of " +
                 "the lesson. A figure turning on the spot is the picture wanted here.")]
        [SerializeField] private GameObject headOnlyVisuals;

        // ------------------------------------------------------------------ requirements

        [Header("Requirements")]
        [Tooltip("Applies to all three modes.")]
        [SerializeField] private Sequence sequence = Sequence.RightThenLeft;

        [Tooltip("Used only in 'Any direction, N turns' mode.")]
        [SerializeField] private int requiredTurns = 2;

        [Header("Detection - Snap")]
        [Tooltip("Minimum single-frame yaw change counted as a snap turn. Keep comfortably below " +
                 "your SnapTurnProvider's Turn Amount.")]
        [SerializeField] private float minSnapDegrees = 20f;

        [Header("Detection - Continuous")]
        [Tooltip("Degrees of smooth rotation that count as one turn. 45 is about a second and a " +
                 "half of held stick at the default turn speed - enough to feel deliberate, short " +
                 "enough not to be a chore twice over. The participant does NOT have to sweep it " +
                 "in one go; the accumulator only unwinds if they turn back the other way, which " +
                 "is the same thing the prompt asks them to do next.")]
        [SerializeField] private float continuousSweepDegrees = 45f;

        [Tooltip("A single-frame yaw change at or above this is a snap turn or a teleport, not a " +
                 "smooth sweep, and is discarded rather than accumulated. Sits between a fast " +
                 "continuous frame (a couple of degrees) and a snap step (30-45).")]
        [SerializeField] private float snapIgnoreDegrees = 15f;

        [Header("Detection - Raw (head only)")]
        [Tooltip("How far from centre the participant has to turn, in degrees, for it to count. " +
                 "60 is a comfortable shoulder turn seated or standing and is well clear of the " +
                 "20-30 degrees of idle looking-about that must NOT complete the lesson.")]
        [SerializeField] private float headYawDegrees = 60f;

        [Tooltip("How long they have to stay turned. This is what separates 'turned to look at " +
                 "something' from a glance, and it is also the part that confirms they have the " +
                 "physical room to hold the position. Keep it short - this is a check, not an " +
                 "endurance test.")]
        [SerializeField] private float headHoldSeconds = 0.5f;

        [Tooltip("How close to centre they have to come back before another turn can be scored. " +
                 "Nothing at all is counted while they are still turned further than this from " +
                 "the turn just scored - see the hysteresis note in TickHeadOnly. Keep it well " +
                 "below Head Yaw Degrees; the gap between the two is the whole point.")]
        [SerializeField] private float headReleaseDegrees = 25f;

        // 'Centre' is the head's direction, in rig space, at the moment the lesson starts, and
        // it is deliberately not re-taken mid-lesson: re-baselining would let somebody ratchet
        // round in small steps, which is not the movement being taught.

        // ------------------------------------------------------------------ prompts

        [System.Serializable]
        public class PromptSet
        {
            [TextArea(1, 3)] public string first;
            [TextArea(1, 3)] public string second;
            [TextArea(1, 3)] public string complete;
            [Tooltip("Shown when the participant turns the wrong way in an ordered sequence. " +
                     "Leave empty to say nothing and simply wait.")]
            [TextArea(1, 3)] public string wrongDirection;
        }

        [System.Serializable]
        public class ModePrompts
        {
            public PromptSet rightHand = new PromptSet();

            [Tooltip("Used when the participant has selected their left controller. Any line left " +
                     "empty falls back to the right-handed line above. Keep the two structurally " +
                     "identical - same length, same shape - so somebody re-reading after a settings " +
                     "change is not re-parsing a different sentence.")]
            public PromptSet leftHand = new PromptSet();
        }

        [Header("Wording - Snap")]
        [SerializeField] private ModePrompts snapPrompts = new ModePrompts
        {
            rightHand = new PromptSet
            {
                first = "Flick the right thumbstick right to turn.",
                second = "Good. Now flick it left.",
                complete = "That's snap turning.",
                wrongDirection = "Other way - try again."
            },
            leftHand = new PromptSet
            {
                first = "Flick the left thumbstick right to turn.",
                second = "Good. Now flick it left.",
                complete = "That's snap turning.",
                wrongDirection = "Other way - try again."
            }
        };

        [Header("Wording - Continuous")]
        [SerializeField] private ModePrompts continuousPrompts = new ModePrompts
        {
            rightHand = new PromptSet
            {
                first = "Push the right thumbstick right and hold. You will turn slowly.",
                second = "Good. Now push it left and hold.",
                complete = "That's how you turn.",
                wrongDirection = "Other way - try again."
            },
            leftHand = new PromptSet
            {
                first = "Push the left thumbstick right and hold. You will turn slowly.",
                second = "Good. Now push it left and hold.",
                complete = "That's how you turn.",
                wrongDirection = "Other way - try again."
            }
        };

        [Header("Wording - Raw (head only)")]
        [Tooltip("Turning your body is not a handed action, so the left-hand set here can be left " +
                 "entirely empty - every line falls back to the wording above.")]
        [SerializeField] private ModePrompts headOnlyPrompts = new ModePrompts
        {
            rightHand = new PromptSet
            {
                first = "There is no turning button. Turn your body to look right.",
                second = "Good. Now turn to look left.",
                complete = "That's how you turn. Turn your body any time.",
                wrongDirection = "Other way - try again."
            },
            leftHand = new PromptSet()
        };

        [Tooltip("Hand assumed when no ControllerHandednessManager is present, i.e. when this " +
                 "scene is opened standalone for testing.")]
        [SerializeField] private ControllerHand editorFallbackHand = ControllerHand.Right;

        [Tooltip("How long the wrong-direction line stays up before the instruction comes back. " +
                 "SnapTurnTask left it up until the next accepted turn, which meant a participant " +
                 "who went the wrong way lost the instruction entirely and was left with a " +
                 "correction they could not act on - the worst state to be in for somebody reading " +
                 "in a second language. Set to 0 to keep the old behaviour.")]
        [SerializeField] private float wrongDirectionSeconds = 2.5f;

        // ------------------------------------------------------------------ events

        [Header("Events")]
        [Tooltip("Fires on every accepted turn, in any mode.")]
        public UnityEvent onTurnRegistered;

        [Tooltip("Fires when a rightward turn is accepted. Hook the right chevron's tick/dim here.")]
        public UnityEvent onRightRegistered;

        [Tooltip("Fires when a leftward turn is accepted. Hook the left chevron here.")]
        public UnityEvent onLeftRegistered;

        [Tooltip("Fires when the participant turns the wrong way during an ordered sequence.")]
        public UnityEvent onWrongDirection;

        [Tooltip("Fires as progress toward the CURRENT direction changes, 0 to 1. Wire a progress " +
                 "bar here exactly as the movement lesson does. It resets to 0 after each accepted " +
                 "turn, because each direction is its own attempt. Snap turning has no meaningful " +
                 "partial progress, so under snap this reports whole turns completed instead.")]
        public UnityEvent<float> onProgressChanged;

        [Header("Events - mode resolved")]
        [Tooltip("Fires once the lesson has decided which mode it is teaching, after the mode " +
                 "visuals have been switched. Fires again only if the resolved mode CHANGES on a " +
                 "later re-entry - so wire idempotent things here (audio cue for this variant, a " +
                 "highlight relay). A cue that should play every time the step opens belongs on " +
                 "TutorialStep's On Step Enter, not here.")]
        public UnityEvent onSnapMode;
        public UnityEvent onContinuousMode;
        public UnityEvent onHeadOnlyMode;

        [Tooltip("Fires once the lesson is satisfied. Wire to TutorialFlow.Begin, exactly as " +
                 "SnapTurnTask was - the flow decides whether that means advance or end a review.")]
        public UnityEvent onCompleted;

        // ------------------------------------------------------------------ state

        /// <summary>True once a rightward turn has been accepted.</summary>
        public bool RightDone { get; private set; }

        /// <summary>True once a leftward turn has been accepted.</summary>
        public bool LeftDone { get; private set; }

        /// <summary>How many turns have been accepted so far.</summary>
        public int TurnCount { get; private set; }

        public bool IsComplete { get; private set; }

        /// <summary>Which lesson is live. Resolved when the lesson starts.</summary>
        public TurnStyle Style => _style;

        private TurnStyle _style = TurnStyle.Snap;
        private bool _styleAnnounced;
        private TurnStyle _announcedStyle;

        private ControllerHand _hand = ControllerHand.Right;

        private float _lastYaw;
        private int _step;          // position in an ordered sequence

        private float _sweep;       // continuous: signed degrees accumulated this gesture

        private float _headBaseline;   // raw: head yaw in rig space when the lesson started
        private float _hold;
        private bool _holdActive;
        private bool _holdDirRight;
        private bool _headLatched;

        private float _lastReported = -1f;

        private PromptSlot _instructionSlot = PromptSlot.First;   // what to fall back to
        private float _wrongUntil = -1f;

        // ------------------------------------------------------------------ replaying

        [Header("Replaying")]
        [Tooltip("Put this step's child objects back to how they were authored whenever the " +
                 "lesson resets. The turn chevrons are switched on and off by this task's own " +
                 "events (right turn hides RightArrow and shows LeftArrow), and nothing switched " +
                 "them back - so replaying the lesson from the pause menu started with the right " +
                 "chevron already hidden. Runs BEFORE the mode visuals are applied, so it can " +
                 "never undo them.")]
        [SerializeField] private bool restoreChildVisibilityOnReset = true;

        private readonly List<KeyValuePair<GameObject, bool>> _authoredVisibility =
            new List<KeyValuePair<GameObject, bool>>();
        private bool _visibilityCaptured;

        private void Awake()
        {
            CaptureChildVisibility();
        }

        /// <summary>
        /// Records every descendant's active state as authored. Runs on the first enable, before
        /// any turn has been made, so it captures the scene as built - RightArrow on, LeftArrow off.
        /// </summary>
        private void CaptureChildVisibility()
        {
            if (_visibilityCaptured) return;
            _visibilityCaptured = true;

            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform) continue;
                _authoredVisibility.Add(new KeyValuePair<GameObject, bool>(t.gameObject, t.gameObject.activeSelf));
            }
        }

        private void RestoreChildVisibility()
        {
            if (!restoreChildVisibilityOnReset) return;
            CaptureChildVisibility();

            foreach (KeyValuePair<GameObject, bool> entry in _authoredVisibility)
                if (entry.Key != null && entry.Key.activeSelf != entry.Value)
                    entry.Key.SetActive(entry.Value);
        }

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            ResolveOrigin();
            ResolveHead();

            // Pull the current hand first - the manager lives in Bootstrap and has usually
            // already fired its initial event before this additively-loaded scene enables.
            _hand = ControllerHandednessManager.CurrentOrDefault(editorFallbackHand);
            ControllerHandednessManager.HandChanged += OnHandChanged;

            ResetTask();
        }

        private void OnDisable()
        {
            ControllerHandednessManager.HandChanged -= OnHandChanged;
        }

        private void OnHandChanged(ControllerHand hand)
        {
            _hand = hand;
            RefreshPrompt();   // re-render whatever prompt is on screen, in the new wording
        }

        private void ResolveOrigin()
        {
            if (xrOrigin != null) return;

            // Fall back to walking up from the main camera - the rig root is typically two
            // levels above it (Camera -> Camera Offset -> XR Origin).
            if (Camera.main != null)
            {
                Transform t = Camera.main.transform;
                xrOrigin = t.parent != null && t.parent.parent != null ? t.parent.parent : t.root;
            }
        }

        private void ResolveHead()
        {
            if (headTransform == null && Camera.main != null) headTransform = Camera.main.transform;
        }

        // ------------------------------------------------------------------ mode

        /// <summary>
        /// Decides which of the three lessons this is. Called from ResetTask only, which is what
        /// makes the mode fixed for the duration of an attempt.
        /// </summary>
        private void ResolveStyle()
        {
            PlayerRotationMode mode = overrideMode
                ? modeOverride
                : RotationModeController.CurrentOrDefault(editorFallbackMode);

            switch (mode)
            {
                case PlayerRotationMode.Snap:     _style = TurnStyle.Snap; break;
                case PlayerRotationMode.HeadOnly: _style = TurnStyle.HeadOnly; break;
                default:                          _style = TurnStyle.Continuous; break;
            }
        }

        private void ApplyModeVisuals()
        {
            if (snapVisuals != null) snapVisuals.SetActive(_style == TurnStyle.Snap);
            if (continuousVisuals != null) continuousVisuals.SetActive(_style == TurnStyle.Continuous);
            if (headOnlyVisuals != null) headOnlyVisuals.SetActive(_style == TurnStyle.HeadOnly);
        }

        private void AnnounceStyle()
        {
            if (_styleAnnounced && _announcedStyle == _style) return;

            _styleAnnounced = true;
            _announcedStyle = _style;

            switch (_style)
            {
                case TurnStyle.Snap:       onSnapMode?.Invoke(); break;
                case TurnStyle.Continuous: onContinuousMode?.Invoke(); break;
                default:                   onHeadOnlyMode?.Invoke(); break;
            }
        }

        // ------------------------------------------------------------------ ITutorialTask

        public void ResetTask()
        {
            RightDone = LeftDone = IsComplete = false;
            TurnCount = 0;
            _step = 0;
            _sweep = 0f;
            _hold = 0f;
            _holdActive = false;
            _headLatched = false;
            _lastReported = -1f;
            _wrongUntil = -1f;

            if (xrOrigin != null) _lastYaw = xrOrigin.eulerAngles.y;
            _headBaseline = MeasureHeadYaw();

            // Order matters: authored state first, then the mode's own visuals on top of it.
            // The other way round and a restore would switch the wrong diagram back on.
            RestoreChildVisibility();
            ResolveStyle();
            ApplyModeVisuals();

            SetPrompt(PromptSlot.First);
            ReportProgress(0f);
            AnnounceStyle();
        }

        // ------------------------------------------------------------------ detection

        private void Update()
        {
            if (IsComplete) return;

            if (xrOrigin == null) { ResolveOrigin(); if (xrOrigin == null) return; }

            // Progress holds while the pause menu is up - somebody who stopped to change a
            // setting must not be punished for it. The yaw reference still has to be kept
            // current, or the whole paused interval reads as one enormous frame and registers
            // as a snap turn on the first frame back.
            if (TutorialPause.IsPaused)
            {
                _lastYaw = xrOrigin.eulerAngles.y;

                // Hold the correction's deadline open across the pause. Time.unscaledTime keeps
                // running behind the menu, so without this a participant who opened the menu
                // would come back to an instruction that had already replaced the correction
                // they never finished reading.
                if (_wrongUntil > 0f) _wrongUntil += Time.unscaledDeltaTime;
                return;
            }

            // Put the instruction back once the correction has had time to be read.
            if (_currentSlot == PromptSlot.WrongDirection && _wrongUntil > 0f &&
                Time.unscaledTime >= _wrongUntil)
            {
                _wrongUntil = -1f;
                SetPrompt(_instructionSlot);
            }

            switch (_style)
            {
                case TurnStyle.Snap:       TickSnap(); break;
                case TurnStyle.Continuous: TickContinuous(); break;
                default:                   TickHeadOnly(); break;
            }
        }

        /// <summary>
        /// Snap turn rotates the rig root instantly, so one frame carries the whole rotation.
        /// Continuous turn spreads it over many frames and won't cross the threshold; physical
        /// head turning doesn't rotate the rig root at all, so neither can produce a false pass.
        /// </summary>
        private void TickSnap()
        {
            float yaw = xrOrigin.eulerAngles.y;
            float delta = Mathf.DeltaAngle(_lastYaw, yaw);
            _lastYaw = yaw;

            if (Mathf.Abs(delta) < minSnapDegrees) return;

            HandleTurn(delta > 0f);   // positive yaw = clockwise = rightward
        }

        /// <summary>
        /// Continuous turn arrives as a stream of small per-frame rotations, so the measure is a
        /// running total rather than any single frame.
        ///
        /// The accumulator is signed and is never reset on a direction change - opposite frames
        /// simply subtract. That is deliberate: it means turning back the other way unwinds the
        /// total through zero and rebuilds it the other side, which is exactly the gesture the
        /// prompt asks for next, and it makes the progress bar honest about a participant who is
        /// pushing the stick the wrong way.
        ///
        /// Nothing here needs a deadzone. The rig root only rotates when a turn provider moves
        /// it - head tracking moves the camera, not the root - so an idle frame is exactly zero.
        /// </summary>
        private void TickContinuous()
        {
            float yaw = xrOrigin.eulerAngles.y;
            float delta = Mathf.DeltaAngle(_lastYaw, yaw);
            _lastYaw = yaw;

            // A jump this big in one frame is a snap or a teleport, not a sweep. Discarded
            // rather than accumulated so a stray provider or a scene transition can't hand the
            // participant a turn they never made.
            if (Mathf.Abs(delta) >= snapIgnoreDegrees) return;

            _sweep += delta;
            ReportProgress(SweepFraction());

            if (Mathf.Abs(_sweep) < continuousSweepDegrees) return;

            bool isRight = _sweep > 0f;
            _sweep = 0f;
            HandleTurn(isRight);
        }

        /// <summary>
        /// Raw mode has no turning control at all, so the lesson is the participant turning
        /// themselves. Measured as head yaw in RIG space, against a baseline taken when the
        /// lesson started - not against the world, so it still reads correctly if the rig is ever
        /// rotated by something else.
        ///
        /// The hold is what makes it a lesson rather than an accident. Somebody looking around
        /// the scene crosses 60 degrees constantly; somebody who has been asked to turn right and
        /// has done so stays there. Half a second is enough to tell those apart and short enough
        /// that it never feels like being made to wait.
        /// </summary>
        private void TickHeadOnly()
        {
            if (headTransform == null) { ResolveHead(); if (headTransform == null) return; }

            float rel = Mathf.DeltaAngle(_headBaseline, MeasureHeadYaw());
            bool isRight = rel > 0f;

            // Hysteresis, and it is not optional. Once a turn has been scored - accepted OR
            // rejected - the participant is by definition still turned that way, and continuing
            // to stand there is not new information. Without this the lesson re-scores them every
            // half second from the moment the first turn lands: the right turn is accepted, they
            // are still facing right, the sequence now wants left, so every subsequent hold is
            // read as a wrong-direction turn. That is the repeating "Other way" line, and the
            // repeating retry cue with it, lasting until they happen to face forward again.
            //
            // So nothing counts until they have come back within Head Release Degrees of centre.
            // The prompt already tells them to turn the other way, and doing that carries them
            // through centre on the way, so this asks nothing extra of them.
            if (_headLatched)
            {
                if (Mathf.Abs(rel) > headReleaseDegrees) { ReportProgress(0f); return; }
                _headLatched = false;
            }

            bool past = Mathf.Abs(rel) >= headYawDegrees;
            float approach = ApproachFraction(rel);

            if (!past)
            {
                _hold = 0f;
                _holdActive = false;
                ReportProgress(0.6f * approach);
                return;
            }

            if (!_holdActive || _holdDirRight != isRight)
            {
                _holdActive = true;
                _holdDirRight = isRight;
                _hold = 0f;
            }

            _hold += Time.unscaledDeltaTime;

            float holdFraction = headHoldSeconds > 0f ? Mathf.Clamp01(_hold / headHoldSeconds) : 1f;

            // Multiplied, not added: turning the way the sequence did NOT ask for leaves approach
            // at zero, so the bar stays empty however long they hold it. A bar that fills on the
            // wrong movement tells them the wrong movement is working.
            ReportProgress(approach * (0.6f + 0.4f * holdFraction));

            if (_hold < headHoldSeconds) return;

            _hold = 0f;
            _holdActive = false;
            _headLatched = true;
            HandleTurn(isRight);
        }

        /// <summary>
        /// The direction the sequence is asking for right now, or null when it does not care.
        ///
        /// Used for two things that have to agree: whether a turn is accepted, and whether
        /// movement counts as progress at all. Before this was shared, the bar measured raw
        /// magnitude - so a participant pushing the stick the wrong way, or turning the wrong
        /// way, watched it fill and then got told they were wrong. The bar has to mean the same
        /// thing the lesson means.
        /// </summary>
        private bool? ExpectedRight()
        {
            switch (sequence)
            {
                case Sequence.RightThenLeft: return _step == 0;
                case Sequence.LeftThenRight: return _step == 1;
                default: return null;   // either order, or any direction - nothing is wrong
            }
        }

        /// <summary>How far the continuous sweep has got, along the direction being asked for.</summary>
        private float SweepFraction()
        {
            bool? expect = ExpectedRight();
            float toward = expect.HasValue ? (expect.Value ? _sweep : -_sweep) : Mathf.Abs(_sweep);
            return Mathf.Clamp01(toward / Mathf.Max(1f, continuousSweepDegrees));
        }

        /// <summary>How far the head has turned toward the direction being asked for, 0 to 1.</summary>
        private float ApproachFraction(float rel)
        {
            bool? expect = ExpectedRight();
            float toward = expect.HasValue ? (expect.Value ? rel : -rel) : Mathf.Abs(rel);
            return Mathf.Clamp01(toward / Mathf.Max(1f, headYawDegrees));
        }

        /// <summary>Head yaw relative to the rig, in degrees. Right is positive.</summary>
        private float MeasureHeadYaw()
        {
            if (headTransform == null || xrOrigin == null) return 0f;
            return Mathf.DeltaAngle(xrOrigin.eulerAngles.y, headTransform.eulerAngles.y);
        }

        // ------------------------------------------------------------------ scoring

        private void HandleTurn(bool isRight)
        {
            // Ordered sequences reject the wrong direction rather than counting it.
            bool? expect = ExpectedRight();
            if (expect.HasValue)
            {
                bool expectRight = expect.Value;

                if (isRight != expectRight)
                {
                    onWrongDirection?.Invoke();
                    if (!string.IsNullOrEmpty(Resolve(PromptSlot.WrongDirection)))
                    {
                        SetPrompt(PromptSlot.WrongDirection);
                        _wrongUntil = wrongDirectionSeconds > 0f
                            ? Time.unscaledTime + wrongDirectionSeconds
                            : -1f;
                    }
                    return;
                }
                _step++;
            }

            Accept(isRight);
        }

        private void Accept(bool isRight)
        {
            TurnCount++;
            if (isRight) RightDone = true; else LeftDone = true;

            onTurnRegistered?.Invoke();
            if (isRight) onRightRegistered?.Invoke(); else onLeftRegistered?.Invoke();

            if (IsDone())
            {
                IsComplete = true;
                ReportProgress(1f);
                SetPrompt(PromptSlot.Complete);
                onCompleted?.Invoke();
            }
            else
            {
                // Each direction is its own attempt, so the bar starts again rather than
                // carrying a half-full look into a fresh instruction.
                ReportProgress(_style == TurnStyle.Snap ? SnapProgress() : 0f);
                SetPrompt(PromptSlot.Second);
            }
        }

        /// <summary>
        /// Snap turning is instantaneous, so there is no partial progress to report. Whole turns
        /// completed is the only honest number, and it keeps a wired progress bar meaningful
        /// across all three modes instead of sitting dead under one of them.
        /// </summary>
        private float SnapProgress()
        {
            switch (sequence)
            {
                case Sequence.RightThenLeft:
                case Sequence.LeftThenRight:
                    return Mathf.Clamp01(_step / 2f);
                case Sequence.EitherOrder:
                    return ((RightDone ? 1f : 0f) + (LeftDone ? 1f : 0f)) / 2f;
                default:
                    return requiredTurns > 0 ? Mathf.Clamp01((float)TurnCount / requiredTurns) : 1f;
            }
        }

        private bool IsDone()
        {
            switch (sequence)
            {
                case Sequence.RightThenLeft:
                case Sequence.LeftThenRight:
                    return _step >= 2;
                case Sequence.EitherOrder:
                    return RightDone && LeftDone;
                default:
                    return TurnCount >= requiredTurns;
            }
        }

        private void ReportProgress(float p)
        {
            p = Mathf.Clamp01(p);
            if (Mathf.Approximately(p, _lastReported)) return;
            _lastReported = p;
            onProgressChanged?.Invoke(p);
        }

        // ------------------------------------------------------------------ wording

        private enum PromptSlot { First, Second, Complete, WrongDirection }
        private PromptSlot _currentSlot = PromptSlot.First;

        private ModePrompts ActivePrompts
        {
            get
            {
                switch (_style)
                {
                    case TurnStyle.Continuous: return continuousPrompts;
                    case TurnStyle.HeadOnly:   return headOnlyPrompts;
                    default:                   return snapPrompts;
                }
            }
        }

        /// <summary>
        /// The line for a slot, in the active mode's wording, in the participant's hand. Falls
        /// back a step at a time rather than all at once: an empty left-handed line falls back to
        /// the right-handed line of the same mode, never to a different mode's wording.
        /// </summary>
        private string Resolve(PromptSlot slot)
        {
            ModePrompts prompts = ActivePrompts;
            if (prompts == null) return string.Empty;

            bool left = _hand == ControllerHand.Left;
            string preferred = Line(left ? prompts.leftHand : prompts.rightHand, slot);

            if (!string.IsNullOrEmpty(preferred)) return preferred;
            return left ? Line(prompts.rightHand, slot) : string.Empty;
        }

        private static string Line(PromptSet set, PromptSlot slot)
        {
            if (set == null) return string.Empty;

            switch (slot)
            {
                case PromptSlot.Second:         return set.second;
                case PromptSlot.Complete:       return set.complete;
                case PromptSlot.WrongDirection: return set.wrongDirection;
                default:                        return set.first;
            }
        }

        /// <summary>Re-renders the current prompt, e.g. after the participant changes hand mid-task.</summary>
        private void RefreshPrompt()
        {
            if (promptLabel != null) promptLabel.text = Resolve(_currentSlot);
        }

        private void SetPrompt(PromptSlot slot)
        {
            _currentSlot = slot;
            if (slot != PromptSlot.WrongDirection) _instructionSlot = slot;
            RefreshPrompt();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            minSnapDegrees = Mathf.Max(1f, minSnapDegrees);
            continuousSweepDegrees = Mathf.Max(5f, continuousSweepDegrees);
            snapIgnoreDegrees = Mathf.Max(2f, snapIgnoreDegrees);
            headYawDegrees = Mathf.Clamp(headYawDegrees, 15f, 170f);
            headHoldSeconds = Mathf.Max(0f, headHoldSeconds);
            headReleaseDegrees = Mathf.Clamp(headReleaseDegrees, 5f, headYawDegrees - 5f);
            requiredTurns = Mathf.Max(1, requiredTurns);
            wrongDirectionSeconds = Mathf.Max(0f, wrongDirectionSeconds);
        }
#endif
    }
}
