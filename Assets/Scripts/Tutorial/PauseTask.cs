using UnityEngine;
using UnityEngine.Events;
using TMPro;

namespace VRTutorial
{
    /// <summary>
    /// The lesson that teaches the pause button.
    ///
    /// Shaped like SnapTurnTask and MovementTask so it drops into the same wiring and the same
    /// review mechanism. It exists rather than the step simply listening to PauseController
    /// directly for two reasons:
    ///
    ///   - It only counts while its own step is showing. Steps deactivate when hidden, so the
    ///     subscription only exists during this lesson - a participant who opens the menu during
    ///     the walking lesson does not silently complete a lesson they have not been taught.
    ///   - TutorialFlow's "where do I send them after a review" logic looks for steps carrying an
    ///     ITutorialTask. Without one, this step would be invisible to it and treated like the
    ///     end-zone panel, which is revealed by arriving rather than by being outstanding.
    ///
    /// Completes on the menu CLOSING by default, not on it opening. Opening proves they found the
    /// button; closing proves they can get back out, which for a participant anxious about being
    /// stuck is the half that matters. It also means the panel behind does not change while the
    /// menu is sitting in front of it.
    /// </summary>
    public class PauseTask : MonoBehaviour, ITutorialTask
    {
        public enum CompleteWhen
        {
            [InspectorName("The menu is closed again")] MenuClosed,
            [InspectorName("The menu is opened")] MenuOpened,
        }

        [Header("Requirements")]
        [Tooltip("Closing is the better lesson: it teaches the way back out, and it avoids the " +
                 "panel behind the menu changing while the menu is still in front of it.")]
        [SerializeField] private CompleteWhen completeWhen = CompleteWhen.MenuClosed;

        [Header("Prompts")]
        [SerializeField] private TMP_Text promptLabel;
        [SerializeField] private string promptFirst =
            "Look at your right controller. Press the round button above the thumbstick.";
        [SerializeField] private string promptOpened = "That's the menu. Press it again to close.";
        [SerializeField] private string promptComplete = "That's how you open and close the menu.";

        [System.Serializable]
        public class PromptSet
        {
            [Tooltip("Leave any line empty to fall back to the wording above.")]
            public string first;
            public string opened;
            public string complete;
        }

        [Header("Left-hand wording")]
        [Tooltip("Used when the participant has selected their left controller. Keep these the " +
                 "same shape as the right-handed lines, so somebody re-reading after a settings " +
                 "change is not re-parsing a new sentence.")]
        [SerializeField] private PromptSet leftHandPrompts = new PromptSet
        {
            first = "Look at your left controller. Press the round button above the thumbstick.",
            opened = "That's the menu. Press it again to close.",
            complete = "That's how you open and close the menu."
        };

        [Tooltip("Hand assumed when no ControllerHandednessManager is present.")]
        [SerializeField] private ControllerHand editorFallbackHand = ControllerHand.Right;

        [Header("Events")]
        [Tooltip("Fires the first time the menu opens during this lesson. Hook the controller " +
                 "button highlight's Flash here.")]
        public UnityEvent onMenuOpened;

        [Tooltip("Fires when the lesson is satisfied. Wire to TutorialFlow.FinishAndDismiss if " +
                 "this is the last lesson, or to Begin if another lesson follows it.")]
        public UnityEvent onCompleted;

        public bool IsComplete { get; private set; }

        /// <summary>True once the participant has opened the menu at least once this attempt.</summary>
        public bool HasOpened { get; private set; }

        private ControllerHand _hand = ControllerHand.Right;

        private void OnEnable()
        {
            _hand = ControllerHandednessManager.CurrentOrDefault(editorFallbackHand);
            ControllerHandednessManager.HandChanged += OnHandChanged;
            TutorialPause.Changed += OnPauseChanged;

            ResetTask();
        }

        private void OnDisable()
        {
            ControllerHandednessManager.HandChanged -= OnHandChanged;
            TutorialPause.Changed -= OnPauseChanged;
        }

        private void OnHandChanged(ControllerHand hand)
        {
            _hand = hand;
            RefreshPrompt();
        }

        public void ResetTask()
        {
            IsComplete = false;
            HasOpened = false;
            SetPrompt(PromptSlot.First);
        }

        /// <summary>
        /// Note the guard: the menu may already be open when this lesson starts, if the
        /// participant opened it during the previous lesson and used it to get here. Waiting for
        /// a fresh open avoids completing the lesson on the close of a menu they opened before
        /// they were ever told what it was.
        /// </summary>
        private void OnPauseChanged(bool paused)
        {
            if (IsComplete) return;

            if (paused)
            {
                if (HasOpened) return;
                HasOpened = true;
                onMenuOpened?.Invoke();

                if (completeWhen == CompleteWhen.MenuOpened) Complete();
                else SetPrompt(PromptSlot.Opened);
                return;
            }

            if (completeWhen == CompleteWhen.MenuClosed && HasOpened) Complete();
        }

        private void Complete()
        {
            IsComplete = true;
            SetPrompt(PromptSlot.Complete);
            onCompleted?.Invoke();
        }

        private enum PromptSlot { First, Opened, Complete }
        private PromptSlot _currentSlot = PromptSlot.First;

        private string Resolve(PromptSlot slot)
        {
            bool left = _hand == ControllerHand.Left;

            switch (slot)
            {
                case PromptSlot.Opened:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.opened)
                        ? leftHandPrompts.opened : promptOpened;
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
