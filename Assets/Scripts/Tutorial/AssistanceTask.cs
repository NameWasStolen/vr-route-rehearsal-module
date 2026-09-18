using UnityEngine;
using UnityEngine.Events;
using TMPro;

namespace VRTutorial
{
    /// <summary>
    /// The lesson that teaches asking for help.
    ///
    /// Shaped like PauseTask so it drops into the same wiring and the same review mechanism, and
    /// it exists as a task rather than the step listening to AssistanceController directly for
    /// the same two reasons PauseTask does: steps deactivate when hidden, so the subscription
    /// only exists while this lesson is showing, and TutorialFlow's "where do I send them after a
    /// review" logic only sees steps that carry an ITutorialTask.
    ///
    /// Completes on the participant dismissing the panel, not on the request being placed.
    /// Placing it proves they found the button; dismissing it proves they can get back to the
    /// street afterwards, which for somebody anxious about being stuck is the half that matters.
    /// Same reasoning as PauseTask completing on close rather than open.
    ///
    /// Drill mode is the important part. Teaching this without it would fire a false alarm at the
    /// researcher in every single session, so the lesson holds the controller in drill mode for
    /// its whole duration: the hold, the bar and both panels all happen for real, and only the
    /// session log entry and the desktop banner are suppressed.
    /// </summary>
    public class AssistanceTask : MonoBehaviour, ITutorialTask
    {
        public enum CompleteWhen
        {
            [InspectorName("The participant dismisses the panel")] Resumed,
            [InspectorName("The request is placed")] Requested,
        }

        [Header("Requirements")]
        [Tooltip("Dismissing is the better lesson: it teaches the way back to the street, and it " +
                 "avoids the panel behind changing while this one is still in front of it.")]
        [SerializeField] private CompleteWhen completeWhen = CompleteWhen.Resumed;

        [Header("Links")]
        [Tooltip("Held in drill mode for as long as this lesson is showing, so practising does " +
                 "not summon anybody. Required - without it the lesson would place real requests.")]
        [SerializeField] private AssistanceController controller;

        [Header("Prompts")]
        [SerializeField] private TMP_Text promptLabel;

        [SerializeField]
        [TextArea]
        private string promptFirst =
            "If you ever need help, press and hold the button marked A on your right controller.";

        [SerializeField]
        [TextArea]
        private string promptHolding = "Keep holding until the bar is full.";

        [SerializeField]
        [TextArea]
        private string promptRequested = "That's how you ask for help. Press Resume to carry on.";

        [SerializeField]
        [TextArea]
        private string promptComplete = "Remember: hold that button any time you need someone.";

        [System.Serializable]
        public class PromptSet
        {
            [Tooltip("Leave any line empty to fall back to the wording above.")]
            [TextArea] public string first;
            [TextArea] public string holding;
            [TextArea] public string requested;
            [TextArea] public string complete;
        }

        [Header("Left-hand wording")]
        [Tooltip("Used when the participant has selected their left controller. Keep the same " +
                 "shape as the right-handed lines, so somebody re-reading after a settings change " +
                 "is not re-parsing a new sentence.")]
        [SerializeField] private PromptSet leftHandPrompts = new PromptSet
        {
            first = "If you ever need help, press and hold the button marked X on your left controller.",
            holding = "Keep holding until the bar is full.",
            requested = "That's how you ask for help. Press Resume to carry on.",
            complete = "Remember: hold that button any time you need someone."
        };

        [Tooltip("Hand assumed when no ControllerHandednessManager is present.")]
        [SerializeField] private ControllerHand editorFallbackHand = ControllerHand.Right;

        [Header("Events")]
        [Tooltip("Fires the first time the hold begins during this lesson. Hook the controller " +
                 "button highlight's Flash here.")]
        public UnityEvent onHoldStarted;

        [Tooltip("Fires when a hold is let go before the bar fills, while no request has been " +
                 "placed. Neutral 'try again' cue.")]
        public UnityEvent onHoldCancelled;

        [Tooltip("Fires when the practice request is placed.")]
        public UnityEvent onRequested;

        [Tooltip("Fires when the lesson is satisfied. Wire to TutorialFlow.FinishAndDismiss if " +
                 "this is the last lesson, or to Begin if another follows it.")]
        public UnityEvent onCompleted;

        public bool IsComplete { get; private set; }

        /// <summary>True once the participant has placed a practice request this attempt.</summary>
        public bool HasRequested { get; private set; }

        private ControllerHand _hand = ControllerHand.Right;
        private bool _hasHeld;
        private bool _confirming;

        private void OnEnable()
        {
            _hand = ControllerHandednessManager.CurrentOrDefault(editorFallbackHand);
            ControllerHandednessManager.HandChanged += OnHandChanged;
            AssistanceRequest.Changed += OnAssistanceChanged;

            if (controller != null) controller.SetDrillMode(true);
            else
                Debug.LogWarning("[AssistanceTask] No AssistanceController assigned, so this lesson " +
                                 "cannot switch on drill mode - practising it would place a real " +
                                 "request and alert the researcher.", this);

            ResetTask();
        }

        private void OnDisable()
        {
            ControllerHandednessManager.HandChanged -= OnHandChanged;
            AssistanceRequest.Changed -= OnAssistanceChanged;

            // Cleared here rather than on completion, so a participant who places a second
            // practice request while still reading the lesson does not silently place a real one.
            if (controller != null) controller.SetDrillMode(false);
        }

        private void OnHandChanged(ControllerHand hand)
        {
            _hand = hand;
            RefreshPrompt();
        }

        public void ResetTask()
        {
            IsComplete = false;
            HasRequested = false;
            _hasHeld = false;
            _confirming = false;
            SetPrompt(PromptSlot.First);
        }

        /// <summary>
        /// Note the guard on Idle: the state returns to Idle both when a hold is cancelled and
        /// when a request is dismissed. Only the second should complete the lesson, which is what
        /// HasRequested distinguishes - otherwise starting a hold and thinking better of it would
        /// count as having learnt this.
        /// </summary>
        private void OnAssistanceChanged(AssistanceState state)
        {
            if (IsComplete) return;

            bool wasConfirming = _confirming;
            _confirming = state == AssistanceState.Confirming;

            switch (state)
            {
                case AssistanceState.Confirming:
                    if (!_hasHeld)
                    {
                        _hasHeld = true;
                        onHoldStarted?.Invoke();
                    }
                    SetPrompt(PromptSlot.Holding);
                    break;

                case AssistanceState.Requested:
                    HasRequested = true;
                    onRequested?.Invoke();
                    if (completeWhen == CompleteWhen.Requested) Complete();
                    else SetPrompt(PromptSlot.Requested);
                    break;

                case AssistanceState.Idle:
                    if (completeWhen == CompleteWhen.Resumed && HasRequested) Complete();
                    else if (!HasRequested)
                    {
                        if (wasConfirming) onHoldCancelled?.Invoke();
                        SetPrompt(PromptSlot.First);
                    }
                    break;
            }
        }

        private void Complete()
        {
            IsComplete = true;
            SetPrompt(PromptSlot.Complete);
            onCompleted?.Invoke();
        }

        private enum PromptSlot { First, Holding, Requested, Complete }
        private PromptSlot _currentSlot = PromptSlot.First;

        private string Resolve(PromptSlot slot)
        {
            bool left = _hand == ControllerHand.Left;

            switch (slot)
            {
                case PromptSlot.Holding:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.holding)
                        ? leftHandPrompts.holding : promptHolding;
                case PromptSlot.Requested:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.requested)
                        ? leftHandPrompts.requested : promptRequested;
                case PromptSlot.Complete:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.complete)
                        ? leftHandPrompts.complete : promptComplete;
                default:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.first)
                        ? leftHandPrompts.first : promptFirst;
            }
        }

        private void RefreshPrompt()
        {
            if (promptLabel != null) promptLabel.text = Resolve(_currentSlot);
        }

        private void SetPrompt(PromptSlot slot)
        {
            _currentSlot = slot;
            RefreshPrompt();
        }
    }
}
