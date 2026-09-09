using UnityEngine;
using UnityEngine.Events;
using TMPro;

namespace VRTutorial
{
    /// <summary>
    /// Waits for the player to actually perform snap turns, rather than demonstrating one.
    ///
    /// Detection watches the XR Origin's yaw for a large single-frame jump. Snap turn rotates
    /// the rig root instantly, so one frame carries the whole rotation; continuous turn spreads
    /// it over many frames and won't cross the threshold. Physical head turning doesn't rotate
    /// the rig root at all, so it can't produce a false positive.
    /// </summary>
    public class SnapTurnTask : MonoBehaviour
    {
        public enum TurnMode
        {
            [InspectorName("Right, then left")] RightThenLeft,
            [InspectorName("Left, then right")] LeftThenRight,
            [InspectorName("Both, either order")] EitherOrder,
            [InspectorName("Any direction, N turns")] AnyDirection,
        }

        [Header("References")]
        [Tooltip("The rig root that snap turn rotates - your XRPlayerRig / XR Origin object. " +
                 "NOT the camera; the camera also moves when the player physically turns their head.")]
        [SerializeField] private Transform xrOrigin;

        [Tooltip("Optional. Prompt text updated as the player progresses.")]
        [SerializeField] private TMP_Text promptLabel;

        [Header("Detection")]
        [Tooltip("Minimum single-frame yaw change counted as a snap turn. Keep comfortably " +
                 "below your SnapTurnProvider's Turn Amount.")]
        [SerializeField] private float minSnapDegrees = 20f;

        [Header("Requirements")]
        [SerializeField] private TurnMode mode = TurnMode.RightThenLeft;

        [Tooltip("Used only in 'Any direction, N turns' mode.")]
        [SerializeField] private int requiredTurns = 2;

        [Header("Prompts")]
        [SerializeField] private string promptFirst = "Flick the right thumbstick right to turn.";
        [SerializeField] private string promptSecond = "Good. Now flick it left.";
        [SerializeField] private string promptComplete = "That's snap turning.";
        [Tooltip("Shown briefly when the player turns the wrong way in an ordered mode. " +
                 "Leave empty to say nothing and simply wait.")]
        [SerializeField] private string promptWrongDirection = "Other way - try again.";

        [System.Serializable]
        public class PromptSet
        {
            [Tooltip("Leave any line empty to fall back to the right-handed wording above.")]
            public string first;
            public string second;
            public string complete;
            public string wrongDirection;
        }

        [Header("Left-hand wording")]
        [Tooltip("Used when the player has selected their left controller. The fields above are " +
                 "the right-handed wording. Keep the two structurally identical - same length, " +
                 "same shape - so a player re-reading after a settings change is not re-parsing " +
                 "a different sentence. Any line left empty falls back to the right-handed one.")]
        [SerializeField] private PromptSet leftHandPrompts = new PromptSet
        {
            first = "Flick the left thumbstick right to turn.",
            second = "Good. Now flick it left.",
            complete = "That's snap turning.",
            wrongDirection = "Other way - try again."
        };

        [Tooltip("Hand assumed when no ControllerHandednessManager is present, i.e. when this " +
                 "scene is opened standalone for testing.")]
        [SerializeField] private ControllerHand editorFallbackHand = ControllerHand.Right;

        private ControllerHand _hand = ControllerHand.Right;

        [Header("Events")]
        [Tooltip("Fires on every accepted turn, in any mode.")]
        public UnityEvent onTurnRegistered;
        [Tooltip("Fires when a rightward turn is accepted. Hook the right chevron's tick/dim here.")]
        public UnityEvent onRightRegistered;
        [Tooltip("Fires when a leftward turn is accepted. Hook the left chevron here.")]
        public UnityEvent onLeftRegistered;
        [Tooltip("Fires when the player turns the wrong way during an ordered sequence.")]
        public UnityEvent onWrongDirection;
        public UnityEvent onCompleted;

        /// <summary>True once a rightward turn has been accepted.</summary>
        public bool RightDone { get; private set; }
        /// <summary>True once a leftward turn has been accepted.</summary>
        public bool LeftDone { get; private set; }
        /// <summary>How many turns have been accepted so far.</summary>
        public int TurnCount { get; private set; }
        public bool IsComplete { get; private set; }

        private float _lastYaw;
        private int _step;   // position in an ordered sequence

        private void OnEnable()
        {
            ResolveOrigin();

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

        public void ResetTask()
        {
            RightDone = LeftDone = IsComplete = false;
            TurnCount = 0;
            _step = 0;
            if (xrOrigin != null) _lastYaw = xrOrigin.eulerAngles.y;
            SetPrompt(PromptSlot.First);
        }

        private void Update()
        {
            if (IsComplete) return;
            if (xrOrigin == null) { ResolveOrigin(); return; }

            float yaw = xrOrigin.eulerAngles.y;
            float delta = Mathf.DeltaAngle(_lastYaw, yaw);
            _lastYaw = yaw;

            if (Mathf.Abs(delta) < minSnapDegrees) return;

            HandleTurn(delta > 0f);   // positive yaw = clockwise = rightward
        }

        private void HandleTurn(bool isRight)
        {
            // Ordered modes reject the wrong direction rather than counting it.
            if (mode == TurnMode.RightThenLeft || mode == TurnMode.LeftThenRight)
            {
                bool expectRight = (mode == TurnMode.RightThenLeft) ? _step == 0 : _step == 1;

                if (isRight != expectRight)
                {
                    onWrongDirection?.Invoke();
                    if (!string.IsNullOrEmpty(promptWrongDirection)) SetPrompt(PromptSlot.WrongDirection);
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
                SetPrompt(PromptSlot.Complete);
                onCompleted?.Invoke();
            }
            else
            {
                SetPrompt(PromptSlot.Second);
            }
        }

        private bool IsDone()
        {
            switch (mode)
            {
                case TurnMode.RightThenLeft:
                case TurnMode.LeftThenRight:
                    return _step >= 2;
                case TurnMode.EitherOrder:
                    return RightDone && LeftDone;
                default:
                    return TurnCount >= requiredTurns;
            }
        }

        private enum PromptSlot { First, Second, Complete, WrongDirection }
        private PromptSlot _currentSlot = PromptSlot.First;

        private string Resolve(PromptSlot slot)
        {
            bool left = _hand == ControllerHand.Left;

            switch (slot)
            {
                case PromptSlot.Second:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.second)
                        ? leftHandPrompts.second : promptSecond;
                case PromptSlot.Complete:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.complete)
                        ? leftHandPrompts.complete : promptComplete;
                case PromptSlot.WrongDirection:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.wrongDirection)
                        ? leftHandPrompts.wrongDirection : promptWrongDirection;
                default:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.first)
                        ? leftHandPrompts.first : promptFirst;
            }
        }

        /// <summary>Re-renders the current prompt, e.g. after the player changes hand mid-task.</summary>
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