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

        public int CurrentIndex { get; private set; } = -1;
        public bool IsTransitioning { get; private set; }
        public TutorialStep CurrentStep =>
            CurrentIndex >= 0 && CurrentIndex < steps.Count ? steps[CurrentIndex] : null;

        private Coroutine _running;
        private Coroutine _placementRoutine;

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

        private void OnEnable()
        {
            CapturePlacement();

            for (int i = 0; i < steps.Count; i++)
                if (steps[i] != null) steps[i].SetVisible(false);

            if (showFirstStepOnEnable && steps.Count > 0) ShowImmediate(0);
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

        /// <summary>Advances one step after a delay.</summary>
        public void AdvanceAfter(float delay) => GoTo(CurrentIndex + 1, delay);

        /// <summary>Jumps to a specific step. Safe to call from a UnityEvent / zone trigger.</summary>
        public void GoTo(int index) => GoTo(index, 0f);

        public void GoTo(int index, float delay)
        {
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
        public void Restore()
        {
            if (!IsDismissed) return;
            IsDismissed = false;

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
