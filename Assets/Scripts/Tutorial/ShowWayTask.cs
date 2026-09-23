using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace VRTutorial
{
    /// <summary>
    /// The lesson that teaches "tap A/X to see the way". Comes straight after the assistance
    /// lesson, which taught HOLDING the same button - so this one is mostly about the difference.
    ///
    /// Shaped like AssistanceTask so it drops into the same wiring and review mechanism: a task
    /// rather than the step listening directly, because steps deactivate when hidden (so the
    /// subscriptions only exist while the lesson is showing) and TutorialFlow's review-return
    /// logic only sees steps that carry an ITutorialTask.
    ///
    /// Completes on the first tap. The tap itself draws the real route line - the guide is
    /// armed for the lesson so RequestShow works - which is the demonstration.
    ///
    /// Drill mode stays ON for this lesson. The button was held for help one lesson ago, so
    /// holding it again here is the likeliest mistake; in drill mode that shows "That was
    /// practice. Nobody has been called." instead of alerting the researcher, and the prompt then
    /// explains the difference. The accepted cost: someone who genuinely needs help during these
    /// few seconds gets the practice message - the pause menu's Get help still places a real one.
    /// </summary>
    public class ShowWayTask : MonoBehaviour, ITutorialTask
    {
        [Header("Links")]
        [Tooltip("Source of the tap (onTapped), and held in drill mode for this lesson.")]
        [SerializeField] private AssistanceController controller;

        [Tooltip("Armed for the lesson so the tap draws the line. Its tap logging is suppressed " +
                 "while the lesson runs, so practice taps do not look like real requests.")]
        [SerializeField] private RouteGuideLine guide;

        [Header("Prompts")]
        [SerializeField] private TMP_Text promptLabel;

        [SerializeField, TextArea]
        private string promptFirst =
            "Not sure of the way? Tap the A button. A blue line will show you where to go.";

        [Tooltip("Shown if they hold instead of tapping. Names both uses of the button, because " +
                 "that is exactly the confusion being corrected.")]
        [SerializeField, TextArea]
        private string promptHeld =
            "Holding A asks for help. To see the way, just tap A.";

        [SerializeField, TextArea]
        private string promptComplete =
            "That's how you see the way. Tap A any time you are not sure.";

        [System.Serializable]
        public class PromptSet
        {
            [Tooltip("Leave any line empty to fall back to the wording above.")]
            [TextArea] public string first;
            [TextArea] public string held;
            [TextArea] public string complete;
        }

        [Header("Left-hand wording")]
        [SerializeField] private PromptSet leftHandPrompts = new PromptSet
        {
            first = "Not sure of the way? Tap the X button. A blue line will show you where to go.",
            held = "Holding X asks for help. To see the way, just tap X.",
            complete = "That's how you see the way. Tap X any time you are not sure.",
        };

        [Tooltip("Hand assumed when no ControllerHandednessManager is present.")]
        [SerializeField] private ControllerHand editorFallbackHand = ControllerHand.Right;

        [Header("Events")]
        [Tooltip("Fires on the tap that completes the lesson. Hook the button highlight's Flash here.")]
        public UnityEvent onTapped;

        [Tooltip("Fires when they start holding instead of tapping.")]
        public UnityEvent onHeldInstead;

        [Tooltip("Fires when the lesson is satisfied. Wire to TutorialFlow.Begin (the \"All done\" " +
                 "step follows).")]
        public UnityEvent onCompleted;

        public bool IsComplete { get; private set; }

        private ControllerHand _hand = ControllerHand.Right;

        private void OnEnable()
        {
            _hand = ControllerHandednessManager.CurrentOrDefault(editorFallbackHand);
            ControllerHandednessManager.HandChanged += OnHandChanged;
            AssistanceRequest.Changed += OnAssistanceChanged;

            if (controller != null)
            {
                controller.onTapped.AddListener(OnTapped);
                controller.SetDrillMode(true);
            }
            else
            {
                Debug.LogWarning("[ShowWayTask] No AssistanceController assigned - the tap cannot be " +
                                 "detected, and a hold would place a REAL request.", this);
            }

            if (guide != null)
            {
                guide.Arm();
                guide.SuppressLogging = true;
            }

            ResetTask();
        }

        private void OnDisable()
        {
            ControllerHandednessManager.HandChanged -= OnHandChanged;
            AssistanceRequest.Changed -= OnAssistanceChanged;

            if (controller != null)
            {
                controller.onTapped.RemoveListener(OnTapped);
                controller.SetDrillMode(false);
            }
            if (guide != null) guide.SuppressLogging = false;
        }

        private void Update()
        {
            // The assistance lesson switches drill mode OFF in its OnDisable. During the
            // cross-fade that can land after this OnEnable, so re-assert it while this lesson runs.
            if (controller != null && !controller.DrillMode) controller.SetDrillMode(true);
        }

        public void ResetTask()
        {
            IsComplete = false;
            SetPrompt(Slot.First);
        }

        private void OnTapped()
        {
            if (IsComplete) return;
            IsComplete = true;
            onTapped?.Invoke();
            SetPrompt(Slot.Complete);
            onCompleted?.Invoke();
        }

        private void OnAssistanceChanged(AssistanceState state)
        {
            if (IsComplete) return;
            if (state == AssistanceState.Confirming)
            {
                SetPrompt(Slot.Held);
                onHeldInstead?.Invoke();
            }
        }

        private void OnHandChanged(ControllerHand hand)
        {
            _hand = hand;
            RefreshPrompt();
        }

        // ------------------------------------------------------------------ prompts
        private enum Slot { First, Held, Complete }
        private Slot _slot = Slot.First;

        private string Resolve(Slot slot)
        {
            bool left = _hand == ControllerHand.Left;
            switch (slot)
            {
                case Slot.Held:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.held) ? leftHandPrompts.held : promptHeld;
                case Slot.Complete:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.complete) ? leftHandPrompts.complete : promptComplete;
                default:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.first) ? leftHandPrompts.first : promptFirst;
            }
        }

        private void SetPrompt(Slot slot)
        {
            _slot = slot;
            RefreshPrompt();
        }

        private void RefreshPrompt()
        {
            if (promptLabel != null) promptLabel.text = Resolve(_slot);
        }
    }
}
