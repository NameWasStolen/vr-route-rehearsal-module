using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using TMPro;

namespace VRTutorial
{
    /// <summary>
    /// Waits for the participant to hold the movement input for a few seconds, rather than for
    /// them to walk out of a trigger volume.
    ///
    /// Why the change: a zone exit measures where somebody ended up, not whether they can work
    /// the control. Someone can drift out of a volume by leaning, or by being nudged, and the
    /// tutorial would call that a pass. Holding the grip for five seconds is the thing we
    /// actually want to know they can do.
    ///
    /// Movement in this project is the grip button, through the 'Move Forward' action - a Vector2
    /// composite with only its 'up' part wired, so the rig walks straight ahead while the grip is
    /// squeezed. The thumbstick is Turn, which belongs to the camera lesson. Getting those the
    /// wrong way round in the prompt wording would teach the wrong control.
    ///
    /// Shaped deliberately like SnapTurnTask - same handed prompt sets, same onCompleted meaning
    /// "this lesson is finished, flow please carry on" - so it drops into the existing wiring and
    /// into TutorialFlow's review mechanism without either needing to know about it.
    /// </summary>
    public class MovementTask : MonoBehaviour, ITutorialTask
    {
        [Header("Input")]
        [Tooltip("The 'Move Forward' action from the LEFT Locomotion map. Read directly rather " +
                 "than measuring how far the rig travelled, so a participant pressed against a " +
                 "fence still makes progress instead of being silently stuck.")]
        [SerializeField] private InputActionReference leftMoveAction;

        [Tooltip("The 'Move Forward' action from the RIGHT Locomotion map.")]
        [SerializeField] private InputActionReference rightMoveAction;

        [Tooltip("Magnitude that counts as 'moving'. Move Forward is a Vector2 composite whose " +
                 "only wired part is the grip button, so in practice this reads 0 or 1 and any " +
                 "threshold in this range behaves the same. It stays adjustable for the day a " +
                 "stick axis is added alongside the grip, when partial deflection starts to mean " +
                 "something.")]
        [Range(0.05f, 0.9f)]
        [SerializeField] private float moveThreshold = 0.25f;

        [Header("Requirements")]
        [Tooltip("Seconds of movement input needed to finish the lesson.")]
        [SerializeField] private float requiredSeconds = 5f;

        [Tooltip("Seconds after releasing the stick before progress starts to drain. A pause for " +
                 "breath, or to look at something, must not be punished.")]
        [SerializeField] private float graceSeconds = 0.75f;

        [Tooltip("How fast progress drains once the grace window has passed, as a multiple of " +
                 "the rate it filled at. Below 1 means letting go costs less than pushing gained, " +
                 "which is the intent - this is cumulative with grace, not an unbroken hold.")]
        [Range(0f, 2f)]
        [SerializeField] private float decayRate = 0.5f;

        [Header("Sanity check (optional)")]
        [Tooltip("Also require the rig to actually be translating, not just the grip to be held. " +
                 "Off by default, and worth leaving off here: movement is forward-only, so a " +
                 "participant who ends up facing a fence would hold the grip, go nowhere, and see " +
                 "a frozen bar with no explanation of why. That is worse than passing them.")]
        [SerializeField] private bool requireActualMovement = false;

        [Tooltip("The rig root that locomotion moves. Left empty, it is found from Camera.main.")]
        [SerializeField] private Transform xrOrigin;

        [Tooltip("Metres per second that counts as actually moving. Used when the check above is " +
                 "ticked, and as the fallback when no action has been assigned.")]
        [SerializeField] private float minSpeed = 0.15f;

        [Header("Prompts")]
        [SerializeField] private TMP_Text promptLabel;
        [SerializeField] private string promptFirst = "Squeeze and hold the grip to walk forward.";
        [SerializeField] private string promptHolding = "That's it - keep holding.";
        [SerializeField] private string promptComplete = "That's how you walk.";

        [System.Serializable]
        public class PromptSet
        {
            [Tooltip("Leave any line empty to fall back to the wording above.")]
            public string first;
            public string holding;
            public string complete;
        }

        [Header("Left-hand wording")]
        [Tooltip("Used when the participant has selected their left controller. Keep these " +
                 "structurally identical to the right-handed lines - same length, same shape - " +
                 "so somebody re-reading after a settings change is not re-parsing a new sentence.")]
        [SerializeField] private PromptSet leftHandPrompts = new PromptSet
        {
            first = "Squeeze and hold the left grip to walk forward.",
            holding = "That's it - keep holding.",
            complete = "That's how you walk."
        };

        [Tooltip("Hand assumed when no ControllerHandednessManager is present, i.e. when this " +
                 "scene is opened standalone for testing.")]
        [SerializeField] private ControllerHand editorFallbackHand = ControllerHand.Right;

        [Header("Events")]
        [Tooltip("Fires every frame the progress changes, 0 to 1. Wire the progress ring here.")]
        public UnityEvent<float> onProgressChanged;

        [Tooltip("Fires the first time the participant produces movement input.")]
        public UnityEvent onHoldStarted;

        [Tooltip("Fires when progress starts draining after a release. Use for a gentle visual " +
                 "hint, never for a failure sound.")]
        public UnityEvent onHoldBroken;

        [Tooltip("Fires once the hold is satisfied. Wire to TutorialFlow.Begin, exactly like " +
                 "SnapTurnTask - the flow decides whether that means advance or end a review.")]
        public UnityEvent onCompleted;

        /// <summary>0 to 1 progress toward the required hold.</summary>
        public float Progress01 => requiredSeconds > 0f
            ? Mathf.Clamp01(_held / requiredSeconds)
            : 1f;

        public bool IsComplete { get; private set; }

        private ControllerHand _hand = ControllerHand.Right;
        private float _held;
        private float _sinceInput;
        private float _lastReported = -1f;
        private bool _started;
        private bool _draining;
        private Vector3 _lastOriginPos;
        private bool _warnedNoAction;

        private void OnEnable()
        {
            ResolveOrigin();

            // Pull before subscribing. This scene loads additively and the manager in Bootstrap
            // has usually already fired its initial event, so subscribing alone would leave the
            // wording stuck on whatever was authored.
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
            RefreshPrompt();
        }

        private void ResolveOrigin()
        {
            if (xrOrigin != null) return;

            if (Camera.main != null)
            {
                Transform t = Camera.main.transform;
                xrOrigin = t.parent != null && t.parent.parent != null ? t.parent.parent : t.root;
            }
        }

        public void ResetTask()
        {
            IsComplete = false;
            _held = 0f;
            _sinceInput = 0f;
            _started = false;
            _draining = false;
            _lastReported = -1f;
            if (xrOrigin != null) _lastOriginPos = xrOrigin.position;

            SetPrompt(PromptSlot.First);
            Report();
        }

        private void Update()
        {
            if (IsComplete) return;

            if (xrOrigin == null) ResolveOrigin();

            // Progress holds while the pause menu is up. Draining it would punish somebody for
            // stopping to change a setting, which is the opposite of what the menu is for.
            //
            // The displacement reference still has to be kept current while paused: it is a
            // per-frame difference, so a stale one would measure the whole paused interval as a
            // single frame of movement and read as an impossible speed on the first frame back.
            if (TutorialPause.IsPaused)
            {
                if (xrOrigin != null) _lastOriginPos = xrOrigin.position;
                return;
            }

            float dt = Time.unscaledDeltaTime;
            bool moving = ReadIsMoving(dt);

            if (moving)
            {
                _sinceInput = 0f;

                if (_draining)
                {
                    _draining = false;
                }

                if (!_started)
                {
                    _started = true;
                    onHoldStarted?.Invoke();
                    SetPrompt(PromptSlot.Holding);
                }

                _held += dt;

                if (_held >= requiredSeconds)
                {
                    _held = requiredSeconds;
                    Complete();
                    Report();
                    return;
                }
            }
            else
            {
                _sinceInput += dt;

                if (_sinceInput > graceSeconds && _held > 0f && decayRate > 0f)
                {
                    if (!_draining)
                    {
                        _draining = true;
                        onHoldBroken?.Invoke();
                    }
                    _held = Mathf.Max(0f, _held - dt * decayRate);
                }
            }

            Report();
        }

        /// <summary>
        /// True while the participant is producing movement input.
        ///
        /// Falls back to measuring the rig's own speed when no action has been assigned, so the
        /// lesson still works in a scene opened standalone or driven by the XR Interaction
        /// Simulator, where the locomotion maps may not be the ones being fed.
        /// </summary>
        private bool ReadIsMoving(float dt)
        {
            // Sampled unconditionally, exactly once per frame. MeasuredSpeed consumes the stored
            // position as it goes, so calling it only on some frames would leave the reference
            // stale and overstate the speed the next time it was asked for.
            float speed = MeasuredSpeed(dt);

            InputActionReference reference = _hand == ControllerHand.Left
                ? leftMoveAction
                : rightMoveAction;

            InputAction action = reference != null ? reference.action : null;

            if (action == null)
            {
                if (!_warnedNoAction)
                {
                    _warnedNoAction = true;
                    Debug.LogWarning("[MovementTask] No move action assigned for the active hand - " +
                                     "falling back to measuring rig speed.", this);
                }
                return speed >= minSpeed;
            }

            // A Vector2 read even though the grip is a button: Move Forward is a composite, so
            // the press arrives as (0, 1) on its 'up' part. Reading a float here would throw.
            bool inputHeld = action.ReadValue<Vector2>().magnitude >= moveThreshold;
            return requireActualMovement ? inputHeld && speed >= minSpeed : inputHeld;
        }

        private float MeasuredSpeed(float dt)
        {
            if (xrOrigin == null) { ResolveOrigin(); return 0f; }
            if (dt <= 0f) return 0f;

            Vector3 now = xrOrigin.position;
            Vector3 delta = now - _lastOriginPos;
            _lastOriginPos = now;

            // Horizontal only - a rig settling vertically onto the ground is not walking.
            delta.y = 0f;
            return delta.magnitude / dt;
        }

        private void Complete()
        {
            IsComplete = true;
            SetPrompt(PromptSlot.Complete);
            onCompleted?.Invoke();
        }

        private void Report()
        {
            float p = Progress01;
            if (Mathf.Approximately(p, _lastReported)) return;
            _lastReported = p;
            onProgressChanged?.Invoke(p);
        }

        private enum PromptSlot { First, Holding, Complete }
        private PromptSlot _currentSlot = PromptSlot.First;

        private string Resolve(PromptSlot slot)
        {
            bool left = _hand == ControllerHand.Left;

            switch (slot)
            {
                case PromptSlot.Holding:
                    return left && !string.IsNullOrEmpty(leftHandPrompts.holding)
                        ? leftHandPrompts.holding : promptHolding;
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            requiredSeconds = Mathf.Max(0.5f, requiredSeconds);
            graceSeconds = Mathf.Max(0f, graceSeconds);
            minSpeed = Mathf.Max(0f, minSpeed);
        }
#endif
    }
}
