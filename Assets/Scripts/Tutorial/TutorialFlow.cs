using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace VRTutorial
{
    /// <summary>
    /// Owns the single persistent tutorial panel and cross-fades between its steps.
    /// Drop-in replacement for DelayedHandoff: Begin() still means "wait, then move on", so
    /// existing SnapTurnTask.onCompleted wiring keeps working.
    ///
    /// Why one panel instead of two:
    ///   - Nothing is enabled or disabled, so HeadLockedUI never re-runs OnEnable and never
    ///     snaps a fresh panel into existence in front of the player.
    ///   - There is no frame where two panels are both lit. The old handoff showed the incoming
    ///     panel before hiding the outgoing one, and both sat at nearly the same head offset.
    ///   - Spatially it reads as "the sign updated", not "a thing vanished and another appeared".
    ///
    /// Fade timing is deliberately asymmetric - out faster than in. Leaving feels sluggish if it
    /// lingers; arriving feels aggressive if it is quick.
    /// </summary>
    [DisallowMultipleComponent]
    public class TutorialFlow : MonoBehaviour
    {
        [Header("Steps")]
        [Tooltip("Content groups on the panel, in the order the player should see them. " +
                 "All but the first are hidden at start.")]
        [SerializeField] private List<TutorialStep> steps = new List<TutorialStep>();

        [Tooltip("Show the first step automatically when this object enables.")]
        [SerializeField] private bool showFirstStepOnEnable = true;

        [Header("Timing")]
        [Tooltip("Seconds to fade the outgoing step out.")]
        [SerializeField] private float fadeOutDuration = 0.25f;

        [Tooltip("Seconds of empty panel between the two fades. A short beat reads as " +
                 "deliberate; without it the two fades blur into one muddy dissolve.")]
        [SerializeField] private float gapDuration = 0.10f;

        [Tooltip("Seconds to fade the incoming step in. Longer than the fade out on purpose.")]
        [SerializeField] private float fadeInDuration = 0.35f;

        [Tooltip("Default wait used by Begin() before the transition starts - time to read a " +
                 "completion message. Older users generally need longer than feels right to you.")]
        [SerializeField] private float defaultAdvanceDelay = 2.5f;

        [Header("Arrival motion")]
        [Tooltip("Metres the incoming content starts behind its resting place. Keep small - " +
                 "6-8 cm reads as depth without being a lunge. 0 disables.")]
        [SerializeField] private float arriveBackMetres = 0.07f;

        [Tooltip("Scale the incoming content starts at, easing to 1. 0.96 is plenty. 1 disables.")]
        [SerializeField] private float arriveScale = 0.96f;

        [Header("Snap-turn gate")]
        [Tooltip("The panel's HeadLockedUI. During the snap-turn lesson its auto-snap fires " +
                 "constantly by design; starting a fade on the same frame as a teleport looks " +
                 "like two things going wrong at once. Leave empty to skip the gate.")]
        [SerializeField] private HeadLockedUI headLockedUI;

        [Tooltip("Seconds of no snap required before a transition may start.")]
        [SerializeField] private float snapSettleDelay = 0.3f;

        [Tooltip("Give up waiting for a settle after this long, so a player who keeps turning " +
                 "can never wedge the tutorial.")]
        [SerializeField] private float maxSettleWait = 2f;

        [Header("Panel placement")]
        [Tooltip("Seconds to move the panel between two steps' placements. -1 uses the fade-out " +
                 "plus gap, so the panel has finished moving by the time the new content appears. " +
                 "The panel background stays visible throughout, so this must never be a jump.")]
        [SerializeField] private float placementMoveDuration = -1f;

        [Header("Pulling forward")]
        [Tooltip("Distance from the head, in metres, that the panel moves to while PullPanelForward " +
                 "is active. This is an ABSOLUTE distance, not a nudge, so the result does not " +
                 "depend on which step happens to be showing.\n\n" +
                 "It must be smaller than the pause menu's own Local Offset z, or the instruction " +
                 "will still be behind the menu it is telling them to close.")]
        [SerializeField] private float pulledDistance = 0.95f;

        [Tooltip("Seconds to ease in and out of the pulled position.")]
        [SerializeField] private float pullDuration = 0.3f;

        [Header("Dismissing")]
        [Tooltip("Seconds to fade the whole panel out when the player is trusted to carry on " +
                 "unaided. Slower than a step change - this is the guide withdrawing, not " +
                 "swapping pages, and it should not read as the UI breaking.")]
        [SerializeField] private float dismissFadeDuration = 0.5f;

        [Tooltip("Disable the Canvas once fully faded. Stops it rendering and raycasting at all; " +
                 "re-enabled automatically if the panel is ever restored.")]
        [SerializeField] private bool disableCanvasWhenDismissed = true;

        [Tooltip("Fires when the panel is dismissed.")]
        public UnityEvent onDismissed;

        [Header("Events")]
        [Tooltip("Fires as each step begins, with its index.")]
        public UnityEvent<int> onStepChanged;

        [Tooltip("Fires when the last step has finished appearing.")]
        public UnityEvent onFlowCompleted;

        [Header("Reviewing")]
        [Tooltip("Fires with the step index when a lesson is reopened for practice, e.g. from " +
                 "the pause menu. Wire the pause menu's own close here - the participant cannot " +
                 "practise moving while the menu is holding locomotion suspended.")]
        public UnityEvent<int> onReviewStarted;

        [Tooltip("Fires once a reviewed lesson has been completed and the flow has been put back " +
                 "where it was. Deliberately does NOT reopen the pause menu; the controller hint " +
                 "is there if they want it again.")]
        public UnityEvent onReviewFinished;

        public int CurrentIndex { get; private set; } = -1;
        public bool IsTransitioning { get; private set; }
        public TutorialStep CurrentStep =>
            CurrentIndex >= 0 && CurrentIndex < steps.Count ? steps[CurrentIndex] : null;

        /// <summary>Number of steps, end-zone panel included.</summary>
        public int StepCount => steps.Count;

        private Coroutine _running;
        private Coroutine _placementRoutine;
        private Coroutine _reviewRoutine;
        private Coroutine _reviewFinishRoutine;
        private Coroutine _finishRoutine;

        // Where the participant was before a review started, so they can be put back there.
        private int _reviewReturnIndex = -1;
        private bool _reviewReturnDismissed;

        // The panel as authored, used by any step that does not override placement.
        private Vector3 _basePanelOffset;
        private Vector3 _basePanelRotation;
        private Vector3 _basePanelScale;
        private Vector2 _basePanelSize;
        private CanvasGroup _panelGroup;
        private Canvas _panelCanvas;
        private Coroutine _panelFadeRoutine;
        private RectTransform _panelRect;
        private bool _placementCaptured;
        private bool _pulledForward;

        /// <summary>
        /// Accessibility text scale. The whole panel is magnified by it - frame, text, controller
        /// diagram and highlights together - the same way ScalableUIRoot magnifies the pause and
        /// help panels.
        ///
        /// Previously only the frame's sizeDelta was multiplied, and nothing in the steps scaled
        /// its text, so a larger setting gave a bigger border around the same small words.
        /// Magnifying the transform keeps every hand-placed element in proportion, so line breaks
        /// and diagram positions never change and nothing can overlap. It is folded in here
        /// rather than by a ScalableUIRoot on the panel because this component already owns the
        /// panel's localScale - a second component writing it would be overwritten on the next
        /// step change.
        /// </summary>
        private float _fontScale = AccessibilitySettings.DefaultFontScale;

        [Header("Text size")]
        [Tooltip("Ceiling on how far the text-size setting magnifies the tutorial panel. Matches " +
                 "ScalableUIRoot on the pause menu, so all panels grow alike. Past this the panel " +
                 "spreads beyond a comfortable head-turn and gets harder to read, not easier.")]
        [Range(1f, 2f)]
        [SerializeField] private float maxFontMagnification = 1.6f;

        [Tooltip("Let a text size below 100% shrink the panel. Off by default, like the pause " +
                 "menu: a smaller panel also means smaller things to point at.")]
        [SerializeField] private bool allowFontShrink = false;

        private float PanelMagnification
        {
            get
            {
                float m = Mathf.Min(_fontScale, maxFontMagnification);
                return allowFontShrink ? m : Mathf.Max(m, 1f);
            }
        }

        private void OnEnable()
        {
            CapturePlacement();

            // Read before subscribing. This scene loads additively, long after Bootstrap has
            // already fired the change event, so subscription alone would leave the panel at
            // 100% until the participant happened to move the slider again.
            _fontScale = AccessibilitySettings.CurrentOrDefault(AccessibilitySettings.DefaultFontScale);
            AccessibilitySettings.FontScaleChanged += OnFontScaleChanged;

            for (int i = 0; i < steps.Count; i++)
                if (steps[i] != null) steps[i].SetVisible(false);

            if (showFirstStepOnEnable && steps.Count > 0) ShowImmediate(0);
        }

        private void OnDisable()
        {
            AccessibilitySettings.FontScaleChanged -= OnFontScaleChanged;
        }

        private void OnFontScaleChanged(float scale)
        {
            _fontScale = scale;

            // Re-run placement for whatever is on screen so the change is visible immediately.
            // Going through MovePanel rather than snapping means the frame eases to its new
            // size while the participant is looking at it, which reads as a response to the
            // slider rather than a glitch.
            if (headLockedUI == null || !_placementCaptured) return;
            if (_placementRoutine != null) StopCoroutine(_placementRoutine);
            _placementRoutine = StartCoroutine(MovePanel(CurrentStep, 0.2f));
        }

        private void CapturePlacement()
        {
            if (_placementCaptured || headLockedUI == null) return;
            _basePanelOffset = headLockedUI.LocalOffset;
            _basePanelRotation = headLockedUI.RotationOffset;
            _basePanelScale = headLockedUI.transform.localScale;
            _panelRect = headLockedUI.transform as RectTransform;
            if (_panelRect != null) _basePanelSize = _panelRect.sizeDelta;
            _placementCaptured = true;
        }

        private void PlacementFor(TutorialStep step, out Vector3 offset, out Vector3 rot,
                                  out Vector3 scale, out Vector2 size)
        {
            if (step != null && step.OverridePlacement)
            {
                offset = step.LocalOffset;
                rot = step.RotationOffset;
                scale = _basePanelScale * step.PanelScale;
            }
            else
            {
                offset = _basePanelOffset;
                rot = _basePanelRotation;
                scale = _basePanelScale;
            }

            // Frame size is independent of placement - a step can resize the border without
            // moving, or move without resizing.
            size = (step != null && step.OverrideSize) ? step.PanelSize : _basePanelSize;

            // Pulling forward overrides the step's own distance rather than offsetting it, for
            // the same reason it is applied here: every path that positions the panel goes
            // through this method, so none of them can disagree about whether it is in effect.
            if (_pulledForward) offset.z = pulledDistance;

            // Accessibility scale magnifies whatever the step asked for. Applied here rather
            // than at the call sites so that ApplyPlacementImmediate and MovePanel cannot
            // disagree about whether it has been applied yet.
            scale *= PanelMagnification;
        }

        private void ApplyPlacementImmediate(TutorialStep step)
        {
            if (headLockedUI == null || !_placementCaptured) return;

            PlacementFor(step, out Vector3 o, out Vector3 r, out Vector3 s, out Vector2 sz);
            headLockedUI.LocalOffset = o;
            headLockedUI.RotationOffset = r;
            headLockedUI.transform.localScale = s;
            if (_panelRect != null) _panelRect.sizeDelta = sz;
            headLockedUI.SnapToTarget();
        }

        /// <summary>
        /// Eases the panel to a step's placement. Runs alongside the cross-fade rather than
        /// inside it: the panel background never fades, so any placement change is visible and
        /// has to be a movement, not a cut.
        /// </summary>
        private IEnumerator MovePanel(TutorialStep step, float duration)
        {
            if (headLockedUI == null || !_placementCaptured) yield break;

            PlacementFor(step, out Vector3 toOffset, out Vector3 toRot, out Vector3 toScale, out Vector2 toSize);

            Vector3 fromOffset = headLockedUI.LocalOffset;
            Vector3 fromRot = headLockedUI.RotationOffset;
            Vector3 fromScale = headLockedUI.transform.localScale;
            Vector2 fromSize = _panelRect != null ? _panelRect.sizeDelta : Vector2.zero;

            if (duration <= 0f)
            {
                headLockedUI.LocalOffset = toOffset;
                headLockedUI.RotationOffset = toRot;
                headLockedUI.transform.localScale = toScale;
                if (_panelRect != null) _panelRect.sizeDelta = toSize;
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                k = k * k * (3f - 2f * k);   // smoothstep, no velocity jump at either end

                headLockedUI.LocalOffset = Vector3.Lerp(fromOffset, toOffset, k);
                headLockedUI.RotationOffset = Vector3.Lerp(fromRot, toRot, k);
                headLockedUI.transform.localScale = Vector3.Lerp(fromScale, toScale, k);
                if (_panelRect != null) _panelRect.sizeDelta = Vector2.Lerp(fromSize, toSize, k);
                yield return null;
            }

            headLockedUI.LocalOffset = toOffset;
            headLockedUI.RotationOffset = toRot;
            headLockedUI.transform.localScale = toScale;
            if (_panelRect != null) _panelRect.sizeDelta = toSize;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Drop-in for DelayedHandoff.Begin(): waits the default delay, then advances one step.
        /// Repeat calls while a transition is already queued or running are ignored.
        /// </summary>
        public void Begin() => AdvanceAfter(defaultAdvanceDelay);

        /// <summary>Advances one step immediately (still cross-faded).</summary>
        public void Advance() => AdvanceAfter(0f);

        /// <summary>
        /// Advances one step after a delay.
        ///
        /// This is the single choke point for "the lesson just showing has been completed" -
        /// Begin() and Advance() both come through here, and every task's onCompleted is wired
        /// to one of them. During a review that must NOT mean "move on": completing a reopened
        /// movement lesson would otherwise chain into the pause lesson and walk the participant
        /// through the rest of the tutorial again. So a review intercepts it here, which is why
        /// no task script needs to know reviewing exists.
        /// </summary>
        public void AdvanceAfter(float delay)
        {
            if (IsReviewing) { FinishReviewAfter(delay); return; }
            GoTo(CurrentIndex + 1, delay);
        }

        /// <summary>
        /// "This lesson is finished and there is nothing to advance to" - hold the congratulatory
        /// wording for a beat, then fade the panel away and leave the participant to it.
        ///
        /// The LAST taught lesson wires its onCompleted here rather than to Begin(). Begin means
        /// "move to the next step", and the step after the last lesson is the end-zone panel,
        /// which must not appear until the participant actually reaches the end zone. Without
        /// this, finishing the final lesson teleports the ending in front of them.
        ///
        /// Review-aware in exactly the same way Begin is, so a reviewed lesson still ends as a
        /// review rather than as a dismissal.
        /// </summary>
        public void FinishAndDismiss() => FinishAndDismissAfter(defaultAdvanceDelay);

        /// <summary>Finishes the lesson and dismisses after a specific delay.</summary>
        public void FinishAndDismissAfter(float delay)
        {
            if (IsReviewing) { FinishReviewAfter(delay); return; }
            if (_finishRoutine != null) return;
            _finishRoutine = StartCoroutine(FinishThenDismiss(delay));
        }

        private IEnumerator FinishThenDismiss(float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            Dismiss();
            _finishRoutine = null;
        }

        /// <summary>Jumps to a specific step. Safe to call from a UnityEvent / zone trigger.</summary>
        public void GoTo(int index) => GoTo(index, 0f);

        public void GoTo(int index, float delay)
        {
            // An explicit jump overrides a review rather than being swallowed by it, but it must
            // not then "restore" the participant to a position that is no longer meaningful.
            CancelReview();

            if (IsTransitioning) return;
            if (index < 0 || index >= steps.Count) return;
            if (index == CurrentIndex) return;

            // A step change implies the guide is speaking again - bring the panel back if a
            // trigger had dismissed it.
            Restore();
            _running = StartCoroutine(Transition(index, delay));
        }

        /// <summary>
        /// Shows a step as the panel ARRIVING rather than as a page turn.
        ///
        /// Wire this to a zone trigger that fires after the guide has already withdrawn - the
        /// end zone being the obvious one. GoTo() would cross-fade out of whatever step was
        /// last showing, and a step the player cannot currently see has no business ghosting
        /// over the panel on its way back; it reads as the old instruction flickering up again
        /// for no reason. Swapping the content while the panel is still at zero alpha and then
        /// fading the whole thing in gives a clean "here is the last thing to do" instead.
        ///
        /// Falls through to the normal cross-fade when the panel is still on screen, so a
        /// player who reaches the trigger without ever leaving the previous zone gets a proper
        /// transition rather than a hard cut.
        /// </summary>
        public void RevealStep(int index)
        {
            if (index < 0 || index >= steps.Count) return;

            if (IsDismissed)
            {
                ShowImmediate(index);
                Restore();
                return;
            }

            GoTo(index);
        }

        /// <summary>Cancels a pending or running transition and settles on the current step.</summary>
        public void Cancel()
        {
            ReleasePanelForward();
            CancelReview();
            if (_finishRoutine != null) { StopCoroutine(_finishRoutine); _finishRoutine = null; }
            if (_running != null) StopCoroutine(_running);
            _running = null;
            IsTransitioning = false;
            if (CurrentStep != null) CurrentStep.SetVisible(true);
        }

        /// <summary>Shows a step with no transition - used for the initial state.</summary>
        public void ShowImmediate(int index)
        {
            if (index < 0 || index >= steps.Count) return;

            for (int i = 0; i < steps.Count; i++)
                if (steps[i] != null) steps[i].SetVisible(i == index);

            CurrentIndex = index;
            ApplyPlacementImmediate(steps[index]);
            if (steps[index] != null) steps[index].RaiseEnter();
            onStepChanged?.Invoke(index);
        }

        // ------------------------------------------------------------------ dismissing

        /// <summary>True once the panel has been faded away and the player left to it.</summary>
        public bool IsDismissed { get; private set; }

        /// <summary>
        /// The panel root has no CanvasGroup of its own, so one is added on demand. Fading the
        /// step alone is not enough: the Background sits on the panel root, outside every step,
        /// and would be left hanging in front of the player as an empty frame.
        /// </summary>
        private CanvasGroup PanelGroup
        {
            get
            {
                if (_panelGroup == null && headLockedUI != null)
                {
                    _panelGroup = headLockedUI.GetComponent<CanvasGroup>();
                    if (_panelGroup == null)
                        _panelGroup = headLockedUI.gameObject.AddComponent<CanvasGroup>();
                }
                return _panelGroup;
            }
        }

        private Canvas PanelCanvas
        {
            get
            {
                if (_panelCanvas == null && headLockedUI != null)
                    _panelCanvas = headLockedUI.GetComponent<Canvas>();
                return _panelCanvas;
            }
        }

        /// <summary>Fades the whole panel away. Wire a zone trigger's onPlayerEntered here.</summary>
        public void Dismiss()
        {
            if (IsDismissed) return;
            IsDismissed = true;

            if (_panelFadeRoutine != null) StopCoroutine(_panelFadeRoutine);
            _panelFadeRoutine = StartCoroutine(FadePanel(0f, dismissFadeDuration));

            onDismissed?.Invoke();
        }

        /// <summary>
        /// Dismisses only while a particular step is showing. Use this when the player could
        /// cross the trigger during an earlier step - walking forward during the camera lesson,
        /// say - and you do not want the guide to vanish early.
        /// </summary>
        public void DismissIfCurrentStepIs(int index)
        {
            if (CurrentIndex == index) Dismiss();
        }

        /// <summary>Brings the panel back after a dismissal.</summary>
        /// <summary>
        /// Dismisses the panel to get it out of the way of something else, remembering whether
        /// it actually had to do anything.
        ///
        /// Pair with RestoreIfTemporary. The naive pairing of Dismiss and Restore has a bug that
        /// only shows up away from the tutorial: a participant who asks for help halfway down the
        /// street has a panel that is already dismissed, so Dismiss does nothing - and then the
        /// matching Restore faithfully fades the tutorial panel back in, resurrecting a lesson
        /// they finished ten minutes ago. Remembering who dismissed it is what stops that.
        /// </summary>
        public void DismissTemporarily()
        {
            if (IsDismissed)
            {
                _temporarilyDismissed = false;   // somebody else's; not ours to put back
                return;
            }

            _temporarilyDismissed = true;
            Dismiss();
        }

        /// <summary>Undoes DismissTemporarily, and does nothing if that is not what dismissed it.</summary>
        public void RestoreIfTemporary()
        {
            if (!_temporarilyDismissed) return;
            _temporarilyDismissed = false;
            Restore();
        }

        private bool _temporarilyDismissed;

        public void Restore()
        {
            if (!IsDismissed) return;
            IsDismissed = false;
            _temporarilyDismissed = false;

            if (_panelFadeRoutine != null) StopCoroutine(_panelFadeRoutine);
            _panelFadeRoutine = StartCoroutine(FadePanel(1f, dismissFadeDuration));
        }

        private IEnumerator FadePanel(float target, float duration)
        {
            CanvasGroup group = PanelGroup;
            if (group == null) yield break;

            // Must be rendering before it can be seen to fade back in.
            if (target > 0f && PanelCanvas != null) PanelCanvas.enabled = true;

            float start = group.alpha;

            if (duration > 0f)
            {
                float t = 0f;
                while (t < duration)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / duration);
                    group.alpha = Mathf.Lerp(start, target, k * k * (3f - 2f * k));
                    yield return null;
                }
            }

            group.alpha = target;
            group.blocksRaycasts = target > 0.5f;
            group.interactable = target > 0.5f;

            if (target <= 0f && disableCanvasWhenDismissed && PanelCanvas != null)
                PanelCanvas.enabled = false;

            _panelFadeRoutine = null;
        }

        // ------------------------------------------------------------------ pulling forward

        /// <summary>
        /// Brings the panel in front of the pause menu.
        ///
        /// Needed for the half of the pause lesson that says "press it again to close": the menu
        /// is deliberately nearer than the tutorial panel, so without this the instruction for
        /// getting out of the menu is hidden behind the menu itself.
        ///
        /// Wire to PauseTask.onMenuOpened, and ReleasePanelForward to its onCompleted.
        /// </summary>
        public void PullPanelForward()
        {
            if (_pulledForward) return;
            _pulledForward = true;
            EasePlacement();
        }

        /// <summary>Returns the panel to its step's authored distance.</summary>
        public void ReleasePanelForward()
        {
            if (!_pulledForward) return;
            _pulledForward = false;
            EasePlacement();
        }

        private void EasePlacement()
        {
            if (headLockedUI == null || !_placementCaptured) return;
            if (_placementRoutine != null) StopCoroutine(_placementRoutine);
            _placementRoutine = StartCoroutine(MovePanel(CurrentStep, pullDuration));
        }

        // ------------------------------------------------------------------ named steps

        /// <summary>
        /// Index of a step by its Step Name (falling back to its GameObject name), or -1.
        /// Case- and whitespace-insensitive, because these names get typed into the Inspector
        /// by hand and "Movement " should not be a silent failure.
        /// </summary>
        public int IndexOf(string stepName)
        {
            if (string.IsNullOrWhiteSpace(stepName)) return -1;
            string wanted = stepName.Trim();

            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] == null) continue;
                if (string.Equals(steps[i].StepName.Trim(), wanted,
                                  System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Jumps to a step by name. Prefer this over GoTo(int) for anything wired in the
        /// Inspector: the indices are serialised into the scene, so inserting a lesson silently
        /// re-points every existing call at the wrong step, with no error to notice.
        /// </summary>
        public void GoToStep(string stepName)
        {
            int index = IndexOf(stepName);
            if (index < 0)
            {
                Debug.LogWarning($"[TutorialFlow] No step named '{stepName}'.", this);
                return;
            }
            GoTo(index);
        }

        // ------------------------------------------------------------------ reviewing

        /// <summary>True while a lesson has been reopened for practice.</summary>
        public bool IsReviewing { get; private set; }

        /// <summary>The step being reviewed, or -1.</summary>
        public int ReviewIndex { get; private set; } = -1;

        /// <summary>
        /// Name-based RevealStep. Use this for the end-zone trigger rather than the int overload:
        /// inserting the pause lesson shifts the end step's index, and a serialised RevealStep(2)
        /// would quietly start revealing the pause lesson at the end zone instead.
        /// </summary>
        public void RevealStep(string stepName)
        {
            int index = IndexOf(stepName);
            if (index < 0)
            {
                Debug.LogWarning($"[TutorialFlow] No step named '{stepName}' to reveal.", this);
                return;
            }
            RevealStep(index);
        }

        /// <summary>Name-based DismissIfCurrentStepIs, for the same reason.</summary>
        public void DismissIfCurrentStepIs(string stepName)
        {
            int index = IndexOf(stepName);
            if (index >= 0) DismissIfCurrentStepIs(index);
        }

        /// <summary>Reopens a lesson by name for practice. Wire the pause menu's buttons here.</summary>
        public void ReviewStep(string stepName)
        {
            int index = IndexOf(stepName);
            if (index < 0)
            {
                Debug.LogWarning($"[TutorialFlow] No step named '{stepName}' to review.", this);
                return;
            }
            Review(index);
        }

        /// <summary>
        /// Reopens a lesson as a fresh attempt: the step is re-entered, its task is reset, and
        /// the participant has to actually perform it again. On completion they are put back
        /// where they were rather than advanced.
        ///
        /// Deliberately not built on GoTo, which early-returns when the target is already the
        /// current step. Reviewing the lesson you are currently stuck on is the commonest case -
        /// someone who cannot manage the movement hold opens the menu and asks for that same
        /// lesson again - and GoTo would silently do nothing.
        /// </summary>
        public void Review(int index)
        {
            if (index < 0 || index >= steps.Count) return;
            if (IsReviewing) return;

            // Stopping a transition mid-flight leaves IsTransitioning stuck true, which would
            // block every later GoTo for the rest of the session.
            if (_running != null) { StopCoroutine(_running); _running = null; }
            IsTransitioning = false;
            if (_reviewRoutine != null) StopCoroutine(_reviewRoutine);

            IsReviewing = true;
            ReviewIndex = index;
            _reviewReturnIndex = CurrentIndex;
            _reviewReturnDismissed = IsDismissed;

            _reviewRoutine = StartCoroutine(RunReview(index));
        }

        private IEnumerator RunReview(int index)
        {
            onReviewStarted?.Invoke(index);

            if (IsDismissed)
            {
                // The panel is invisible, so there is nothing to cross-fade out of. Swap the
                // content while it is still at zero alpha and then fade the whole panel in -
                // the same reasoning as RevealStep. Cross-fading here would ghost the previous
                // instruction over the panel on its way back for no reason.
                ShowImmediate(index);
                ResetTasksOn(steps[index]);
                Restore();
            }
            else
            {
                yield return ReEnterStep(index);
            }

            _reviewRoutine = null;
        }

        private void FinishReviewAfter(float delay)
        {
            if (_reviewFinishRoutine != null) return;
            _reviewFinishRoutine = StartCoroutine(FinishReview(delay));
        }

        /// <summary>
        /// Holds the congratulatory wording for a beat, then restores the pre-review state.
        /// </summary>
        private IEnumerator FinishReview(float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

            IsReviewing = false;
            ReviewIndex = -1;

            // Where to put them next is decided by what they still have left to learn, not by
            // where they happened to be standing when they opened the menu. Someone who has
            // finished every lesson and is walking the route should be handed back the route,
            // not shown an instruction they already completed.
            int next = FirstUnfinishedLesson();

            if (next < 0)
            {
                // Nothing left to teach. Fade away and let a trigger bring up whatever comes
                // next - the ending belongs to the end zone, not to the flow.
                Dismiss();
                if (dismissFadeDuration > 0f)
                    yield return new WaitForSecondsRealtime(dismissFadeDuration);
            }
            else if (next != CurrentIndex)
            {
                yield return ReEnterStep(next);
            }
            else if (IsDismissed)
            {
                // Already on the right lesson, just not visible.
                Restore();
            }

            _reviewReturnIndex = -1;
            _reviewFinishRoutine = null;
            onReviewFinished?.Invoke();
        }

        /// <summary>
        /// The earliest step carrying a task the participant has not yet satisfied, or -1 when
        /// every lesson is done.
        ///
        /// Steps with no task are skipped deliberately. The end-zone panel is one: it is revealed
        /// by arriving at the end zone, and the flow must never decide to show it on its own -
        /// that is how the ending ends up hovering in front of somebody still halfway down the
        /// street.
        ///
        /// A task's IsComplete survives its step being deactivated, so a lesson finished earlier
        /// in the session still reads as done here.
        /// </summary>
        private int FirstUnfinishedLesson()
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] == null) continue;

                ITutorialTask[] tasks = steps[i].GetComponentsInChildren<ITutorialTask>(true);
                if (tasks.Length == 0) continue;

                for (int t = 0; t < tasks.Length; t++)
                    if (tasks[t] != null && !tasks[t].IsComplete) return i;
            }
            return -1;
        }

        /// <summary>
        /// Abandons a review without restoring anything. Used when something explicitly jumps
        /// the flow elsewhere, which makes the stashed position meaningless.
        /// </summary>
        public void CancelReview()
        {
            if (!IsReviewing && _reviewFinishRoutine == null) return;

            if (_reviewRoutine != null) { StopCoroutine(_reviewRoutine); _reviewRoutine = null; }
            if (_reviewFinishRoutine != null) { StopCoroutine(_reviewFinishRoutine); _reviewFinishRoutine = null; }

            IsReviewing = false;
            ReviewIndex = -1;
            _reviewReturnIndex = -1;
        }

        /// <summary>
        /// Swaps which step's content is loaded without firing enter events or progress cues.
        /// Used to restore the panel's contents while it is faded out, where RaiseEnter would
        /// announce an instruction nobody can see.
        /// </summary>
        private void SetContentSilently(int index)
        {
            if (index < 0 || index >= steps.Count) return;

            for (int i = 0; i < steps.Count; i++)
                if (steps[i] != null) steps[i].SetVisible(i == index);

            CurrentIndex = index;
            ApplyPlacementImmediate(steps[index]);
        }

        /// <summary>
        /// Cross-fades to a step, tolerating the case where it is already the current step.
        ///
        /// Transition() cannot be used for that: it treats outgoing and incoming as different
        /// objects, and the flow's public entry points refuse a move to the current index. Here
        /// hiding the step deactivates it and showing it again enables it, which is what makes
        /// its task reset - the same mechanism that makes a normally-ordered step start clean.
        /// </summary>
        private IEnumerator ReEnterStep(int index)
        {
            if (index < 0 || index >= steps.Count) yield break;

            IsTransitioning = true;

            yield return WaitForSnapSettle();

            TutorialStep outgoing = CurrentStep;
            TutorialStep incoming = steps[index];

            float moveTime = placementMoveDuration >= 0f
                ? placementMoveDuration
                : fadeOutDuration + gapDuration;
            if (_placementRoutine != null) StopCoroutine(_placementRoutine);
            _placementRoutine = StartCoroutine(MovePanel(incoming, moveTime));

            if (outgoing != null && fadeOutDuration > 0f)
            {
                float t = 0f;
                while (t < fadeOutDuration)
                {
                    t += Time.unscaledDeltaTime;
                    outgoing.ApplyFade(1f - (t / fadeOutDuration), arriveBackMetres, arriveScale);
                    yield return null;
                }
            }
            if (outgoing != null)
            {
                outgoing.SetVisible(false);
                outgoing.RaiseExit();
            }

            if (gapDuration > 0f) yield return new WaitForSecondsRealtime(gapDuration);

            CurrentIndex = index;
            if (incoming != null)
            {
                incoming.ApplyFade(0f, arriveBackMetres, arriveScale);   // re-activates the object

                // Belt and braces. Re-activation already resets any task through its own
                // OnEnable; this covers a step with Deactivate When Hidden unticked, where no
                // enable happens and the task would otherwise still be sitting at complete.
                ResetTasksOn(incoming);

                incoming.RaiseEnter();
                onStepChanged?.Invoke(index);

                if (fadeInDuration > 0f)
                {
                    float t = 0f;
                    while (t < fadeInDuration)
                    {
                        t += Time.unscaledDeltaTime;
                        incoming.ApplyFade(t / fadeInDuration, arriveBackMetres, arriveScale);
                        yield return null;
                    }
                }
                incoming.SetVisible(true);
            }

            IsTransitioning = false;
        }

        private static void ResetTasksOn(TutorialStep step)
        {
            if (step == null) return;

            ITutorialTask[] tasks = step.GetComponentsInChildren<ITutorialTask>(true);
            for (int i = 0; i < tasks.Length; i++) tasks[i]?.ResetTask();
        }

        // ------------------------------------------------------------------ internals

        private IEnumerator Transition(int targetIndex, float delay)
        {
            IsTransitioning = true;

            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

            yield return WaitForSnapSettle();

            TutorialStep outgoing = CurrentStep;
            TutorialStep incoming = steps[targetIndex];

            // Start the panel moving now, so it has settled by the time the new content appears.
            float moveTime = placementMoveDuration >= 0f
                ? placementMoveDuration
                : fadeOutDuration + gapDuration;
            if (_placementRoutine != null) StopCoroutine(_placementRoutine);
            _placementRoutine = StartCoroutine(MovePanel(incoming, moveTime));

            // Fade out
            if (outgoing != null && fadeOutDuration > 0f)
            {
                float t = 0f;
                while (t < fadeOutDuration)
                {
                    t += Time.unscaledDeltaTime;
                    outgoing.ApplyFade(1f - (t / fadeOutDuration), arriveBackMetres, arriveScale);
                    yield return null;
                }
            }
            if (outgoing != null)
            {
                outgoing.SetVisible(false);
                outgoing.RaiseExit();
            }

            if (gapDuration > 0f) yield return new WaitForSecondsRealtime(gapDuration);

            // Fade in
            CurrentIndex = targetIndex;
            if (incoming != null)
            {
                incoming.ApplyFade(0f, arriveBackMetres, arriveScale);
                incoming.RaiseEnter();
                onStepChanged?.Invoke(targetIndex);

                if (fadeInDuration > 0f)
                {
                    float t = 0f;
                    while (t < fadeInDuration)
                    {
                        t += Time.unscaledDeltaTime;
                        incoming.ApplyFade(t / fadeInDuration, arriveBackMetres, arriveScale);
                        yield return null;
                    }
                }
                incoming.SetVisible(true);
            }

            IsTransitioning = false;
            _running = null;

            if (targetIndex == steps.Count - 1) onFlowCompleted?.Invoke();
        }

        /// <summary>
        /// Holds off a transition until the panel has stopped teleporting. Bounded by
        /// maxSettleWait so a player spinning on the spot cannot stall the tutorial forever.
        /// </summary>
        private IEnumerator WaitForSnapSettle()
        {
            if (headLockedUI == null || snapSettleDelay <= 0f) yield break;

            float waitedSince = Time.unscaledTime;
            while (Time.unscaledTime - headLockedUI.LastSnapTimeUnscaled < snapSettleDelay)
            {
                if (Time.unscaledTime - waitedSince > maxSettleWait) yield break;
                yield return null;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
            fadeInDuration = Mathf.Max(0f, fadeInDuration);
            gapDuration = Mathf.Max(0f, gapDuration);
            arriveScale = Mathf.Clamp(arriveScale, 0.5f, 1f);
        }
#endif
    }
}
