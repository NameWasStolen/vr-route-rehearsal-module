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

        private void OnEnable()
        {
            for (int i = 0; i < steps.Count; i++)
                if (steps[i] != null) steps[i].SetVisible(false);

            if (showFirstStepOnEnable && steps.Count > 0) ShowImmediate(0);
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

            _running = StartCoroutine(Transition(index, delay));
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
            if (steps[index] != null) steps[index].RaiseEnter();
            onStepChanged?.Invoke(index);
        }

        // ------------------------------------------------------------------ internals

        private IEnumerator Transition(int targetIndex, float delay)
        {
            IsTransitioning = true;

            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

            yield return WaitForSnapSettle();

            TutorialStep outgoing = CurrentStep;
            TutorialStep incoming = steps[targetIndex];

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
