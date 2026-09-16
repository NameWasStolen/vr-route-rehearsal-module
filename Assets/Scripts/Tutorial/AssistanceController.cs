using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace VRTutorial
{
    /// <summary>
    /// Lets a participant summon the researcher from inside the headset, and owns what that means.
    ///
    /// Deliberately parallel to PauseController: same input handling, same fade, same
    /// suspend-locomotion mechanism, same static state flag. A second safety mechanism that
    /// behaved differently from the first would be one more thing for somebody to learn at the
    /// worst possible moment.
    ///
    /// The gate is a press-and-HOLD, not two presses. Three reasons, and they compound for the
    /// cohort this module is for:
    ///
    ///   - A double press inverts instinct. An unexpected popup makes people press the button
    ///     again to dismiss it, and under a two-press scheme that second press confirms.
    ///   - Uncertainty and tremor both produce genuine double taps. Someone unsure whether the
    ///     first press registered presses again. That sails straight through the gate.
    ///   - The same input twice is a debounce, not a confirmation. A confirmation has to cost a
    ///     different act, so that the second one is deliberate.
    ///
    /// A hold answers all three with one gesture, and it is not a new idiom: the walking lesson
    /// already taught hold-the-control-and-watch-the-bar. Letting go is a cancel that needs no
    /// words in any language.
    ///
    /// Note the contrast with MovementTask, which is cumulative with grace. That timer measures
    /// practice, where a pause for breath should not be punished. This one is a safety gate,
    /// where releasing genuinely means no - so it resets.
    /// </summary>
    [DisallowMultipleComponent]
    public class AssistanceController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("The A/X button on the LEFT controller. Bind to {PrimaryButton} in the Menu " +
                 "action map - NOT either Locomotion map, because suspending locomotion would " +
                 "disable the button.")]
        [SerializeField] private InputActionReference leftRequestAction;

        [Tooltip("The A/X button on the RIGHT controller.")]
        [SerializeField] private InputActionReference rightRequestAction;

        [Tooltip("Also accept this key, for testing through the XR Interaction Simulator where no " +
                 "real controller button is being fed.")]
        [SerializeField] private Key editorFallbackKey = Key.H;

        [Header("Hold")]
        [Tooltip("Seconds the button must be held before the request is placed.")]
        [Range(0.5f, 5f)]
        [SerializeField] private float holdDuration = 2f;

        [Header("Availability")]
        [Tooltip("Whether the button works before the lesson has taught it. Left on deliberately, " +
                 "for the same reason the pause button is: this module is about travel anxiety, " +
                 "and somebody who needs help should never be told they have not unlocked the way " +
                 "to ask for it yet. The lesson makes them aware of a button that already works.")]
        [SerializeField] private bool availableFromStart = true;

        [Header("Panels")]
        [Tooltip("Shown while the button is held. Needs a CanvasGroup. Carries the progress bar " +
                 "and the 'keep holding' wording.")]
        [SerializeField] private GameObject confirmRoot;

        [Tooltip("Shown once the request is placed. Needs a CanvasGroup. Carries the reassurance " +
                 "line and the Resume button.")]
        [SerializeField] private GameObject requestedRoot;

        [Tooltip("Seconds to fade a panel in or out.")]
        [SerializeField] private float fadeDuration = 0.25f;

        [Header("Progress")]
        [Tooltip("Drives the bar on the confirmation panel. The bar appears the instant the hold " +
                 "starts, which is what makes the gesture self-explanatory - it is visibly " +
                 "filling under your thumb, and visibly gone if you let go.")]
        [SerializeField] private TaskProgressIndicator confirmProgress;

        [Header("Wording")]
        [SerializeField] private TMP_Text requestedLabel;

        [Tooltip("Shown on a real request. Plain and reassuring on purpose: 'External assistance " +
                 "requested' is the language of an incident report, and the person reading this " +
                 "has just asked for help because something is wrong.")]
        [TextArea]
        [SerializeField] private string requestedTextReal = "Someone is coming to help you.";

        [Tooltip("Shown during the lesson instead of the line above. Same panel, same place, same " +
                 "button, one different sentence - so the screen they meet for real is one they " +
                 "have already seen, without being told a lie they might act on.")]
        [TextArea]
        [SerializeField] private string requestedTextDrill = "That was practice. Nobody has been called.";

        [Header("Links")]
        [Tooltip("Closed when an assistance panel opens, and re-enabled on resume. Never two " +
                 "panels at once.")]
        [SerializeField] private PauseController pauseController;

        [Tooltip("Optional. Supplies the current step name for the log entry, so a request can be " +
                 "placed in the session rather than just timestamped.")]
        [SerializeField] private TutorialFlow flow;

        [Header("Logging")]
        [Tooltip("Event name written to the session log. Suppressed entirely during the lesson.")]
        [SerializeField] private string logEventName = "assistance_requested";

        [Tooltip("Optional. Shows a banner on the mirrored desktop view and nowhere else.")]
        [SerializeField] private SpectatorMarker spectatorMarker;

        [Header("Events")]
        [Tooltip("Fires when the hold begins. Hook the controller button highlight's Flash here.")]
        public UnityEvent onHoldStarted;

        [Tooltip("Fires when the button is released before the bar fills.")]
        public UnityEvent onHoldCancelled;

        [Tooltip("Fires when a request is placed, drill or real.")]
        public UnityEvent onRequested;

        [Tooltip("Fires when the participant dismisses the requested panel.")]
        public UnityEvent onResumed;

        /// <summary>
        /// When set, everything happens except the parts that reach the outside world: no log
        /// entry and no desktop banner. Set by AssistanceTask for the duration of the lesson, so
        /// teaching this does not fire a false alarm at the researcher every single session.
        /// </summary>
        public bool DrillMode { get; private set; }

        public bool IsAvailable { get; private set; }
        public AssistanceState State => AssistanceRequest.State;

        private CanvasGroup _confirmGroup;
        private CanvasGroup _requestedGroup;
        private Canvas _confirmCanvas;
        private Canvas _requestedCanvas;
        private HeadLockedUI _confirmHeadLocked;
        private HeadLockedUI _requestedHeadLocked;
        private Coroutine _confirmFade;
        private Coroutine _requestedFade;

        private float _held;
        private bool _pauseWasAvailable = true;
        private bool _warnedNoAction;

        private void Awake()
        {
            Bind(confirmRoot, ref _confirmGroup, ref _confirmCanvas, ref _confirmHeadLocked);
            Bind(requestedRoot, ref _requestedGroup, ref _requestedCanvas, ref _requestedHeadLocked);

            IsAvailable = availableFromStart;

            HideImmediate(_confirmGroup, _confirmCanvas);
            HideImmediate(_requestedGroup, _requestedCanvas);

            // The hold must not be able to finish before the panel has finished appearing, or the
            // bar the gesture is explained by is never actually seen.
            if (holdDuration < fadeDuration + 0.3f)
            {
                Debug.LogWarning($"[AssistanceController] Hold Duration ({holdDuration:0.00}s) is " +
                                 $"close to Fade Duration ({fadeDuration:0.00}s); the bar may fill " +
                                 "before the panel is readable. Raise the hold or shorten the fade.",
                                 this);
            }
        }

        private void OnEnable()
        {
            EnableAction(leftRequestAction);
            EnableAction(rightRequestAction);
        }

        private void OnDisable()
        {
            // Static state survives a scene change and would leave a fresh session convinced it
            // began mid-request, with locomotion suspended by a panel that no longer exists.
            if (AssistanceRequest.IsActive)
            {
                TutorialPause.IsPaused = false;
                ControllerHandednessManager.Instance?.ResumeLocomotion();
            }
            AssistanceRequest.ResetState();
            _held = 0f;
        }

        /// <summary>
        /// Both hands are bound, not just the preferred one. A participant who has forgotten
        /// which controller they nominated must still be able to ask for help - the cost of the
        /// idle hand also working is nil, and the cost of it not working is somebody pressing a
        /// dead button while distressed.
        /// </summary>
        private static void EnableAction(InputActionReference reference)
        {
            if (reference != null && reference.action != null && !reference.action.enabled)
                reference.action.Enable();
        }

        private static bool Held(InputActionReference reference)
        {
            return reference != null && reference.action != null && reference.action.IsPressed();
        }

        private bool ReadHeld()
        {
            bool any = leftRequestAction != null || rightRequestAction != null;
            if (!any && !_warnedNoAction)
            {
                _warnedNoAction = true;
                Debug.LogWarning("[AssistanceController] No request action assigned, so the button " +
                                 "will never do anything. Assign the Menu map's Request Assistance " +
                                 "action to both hand fields.", this);
            }

            bool held = Held(leftRequestAction) || Held(rightRequestAction);

            if (!held && editorFallbackKey != Key.None && Keyboard.current != null)
                held = Keyboard.current[editorFallbackKey].isPressed;

            return held;
        }

        private void Update()
        {
            if (!IsAvailable) return;
            if (AssistanceRequest.State == AssistanceState.Requested) return;

            bool held = ReadHeld();

            if (!held)
            {
                if (AssistanceRequest.State == AssistanceState.Confirming) CancelHold();
                return;
            }

            if (AssistanceRequest.State == AssistanceState.Idle) BeginHold();

            _held += Time.unscaledDeltaTime;
            float progress = holdDuration > 0f ? Mathf.Clamp01(_held / holdDuration) : 1f;
            if (confirmProgress != null) confirmProgress.SetProgress(progress);

            if (progress >= 1f) Confirm();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Makes the button live. Wire to a step's onStepEnter if it starts unavailable.</summary>
        public void SetAvailable(bool available)
        {
            IsAvailable = available;
            if (!available && AssistanceRequest.State == AssistanceState.Confirming) CancelHold();
        }

        /// <summary>
        /// Places a request immediately, with no hold. This is what the pause menu's Get help
        /// button calls.
        ///
        /// No second gate on purpose. The hold exists to stop a thumb brushing a face button from
        /// summoning a person; somebody who has opened a menu and pointed at a button labelled
        /// Get help has already been unambiguous, and asking them to confirm a deliberate choice
        /// is friction applied to the one participant who most needs none.
        /// </summary>
        public void Request()
        {
            if (!IsAvailable) return;
            if (AssistanceRequest.State == AssistanceState.Requested) return;
            Confirm();
        }

        /// <summary>Dismisses the requested panel and gives locomotion back. The Resume button.</summary>
        public void Resume()
        {
            if (AssistanceRequest.State != AssistanceState.Requested) return;

            AssistanceRequest.State = AssistanceState.Idle;

            TutorialPause.IsPaused = false;
            ControllerHandednessManager.Instance?.ResumeLocomotion();

            if (pauseController != null) pauseController.SetAvailable(_pauseWasAvailable);
            if (spectatorMarker != null) spectatorMarker.Hide();

            Hide(requestedRoot, _requestedGroup, _requestedCanvas, ref _requestedFade);
            if (_requestedHeadLocked != null) _requestedHeadLocked.Unfreeze();

            onResumed?.Invoke();
        }

        /// <summary>Set by AssistanceTask for the duration of the lesson.</summary>
        public void SetDrillMode(bool drill) => DrillMode = drill;

        public void EnableDrillMode() => SetDrillMode(true);
        public void DisableDrillMode() => SetDrillMode(false);

        // ------------------------------------------------------------------ internals

        private void BeginHold()
        {
            _held = 0f;
            AssistanceRequest.State = AssistanceState.Confirming;

            if (confirmProgress != null) confirmProgress.ResetIndicator();

            // Never two panels at once. A menu behind a confirmation the participant is being
            // asked to read is exactly the overlap that was taken out of the pause lesson.
            if (pauseController != null && pauseController.IsOpen) pauseController.Close();

            Show(confirmRoot, _confirmGroup, _confirmCanvas, _confirmHeadLocked, ref _confirmFade);
            onHoldStarted?.Invoke();
        }

        private void CancelHold()
        {
            _held = 0f;
            AssistanceRequest.State = AssistanceState.Idle;

            if (confirmProgress != null) confirmProgress.ResetIndicator();
            Hide(confirmRoot, _confirmGroup, _confirmCanvas, ref _confirmFade);

            onHoldCancelled?.Invoke();
        }

        private void Confirm()
        {
            _held = 0f;
            AssistanceRequest.State = AssistanceState.Requested;

            if (confirmProgress != null) confirmProgress.SetComplete();
            Hide(confirmRoot, _confirmGroup, _confirmCanvas, ref _confirmFade);

            // Requesting help suspends locomotion, the same way pausing does. The panel says
            // somebody is coming; the participant should be standing still when they arrive, not
            // walking into a road while reading it.
            TutorialPause.IsPaused = true;
            ControllerHandednessManager.Instance?.SuspendLocomotion();

            if (pauseController != null)
            {
                if (pauseController.IsOpen) pauseController.Close();
                _pauseWasAvailable = pauseController.IsAvailable;
                pauseController.SetAvailable(false);
            }

            if (requestedLabel != null)
                requestedLabel.text = DrillMode ? requestedTextDrill : requestedTextReal;

            Show(requestedRoot, _requestedGroup, _requestedCanvas, _requestedHeadLocked, ref _requestedFade);

            if (!DrillMode)
            {
                SessionLog.Record(logEventName, CurrentStepName());
                if (spectatorMarker != null) spectatorMarker.Show();
            }

            onRequested?.Invoke();
        }

        private string CurrentStepName()
        {
            if (flow == null) return string.Empty;
            TutorialStep step = flow.CurrentStep;
            return step != null ? step.name : (flow.IsDismissed ? "dismissed" : string.Empty);
        }

        // ------------------------------------------------------------------ panel plumbing

        private static void Bind(GameObject root, ref CanvasGroup group, ref Canvas canvas,
                                 ref HeadLockedUI headLocked)
        {
            if (root == null) return;
            group = root.GetComponent<CanvasGroup>();
            if (group == null) group = root.AddComponent<CanvasGroup>();
            canvas = root.GetComponent<Canvas>();
            headLocked = root.GetComponent<HeadLockedUI>();
        }

        private static void HideImmediate(CanvasGroup group, Canvas canvas)
        {
            if (group == null) return;
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            if (canvas != null) canvas.enabled = false;
        }

        private void Show(GameObject root, CanvasGroup group, Canvas canvas, HeadLockedUI headLocked,
                          ref Coroutine routine)
        {
            if (root == null || group == null) return;

            root.SetActive(true);
            if (canvas != null) canvas.enabled = true;

            // Snap rather than slide in from wherever the panel was last left, so it is already in
            // the right place on the frame it appears.
            if (headLocked != null)
            {
                headLocked.SnapToTarget();
                headLocked.Unfreeze();
            }

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(Fade(group, canvas, 1f));
        }

        private void Hide(GameObject root, CanvasGroup group, Canvas canvas, ref Coroutine routine)
        {
            if (root == null || group == null) return;
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(Fade(group, canvas, 0f));
        }

        private IEnumerator Fade(CanvasGroup group, Canvas canvas, float target)
        {
            float start = group.alpha;

            // Interactable from the first frame of appearing, so an eager participant pointing at
            // Resume during the fade is not ignored.
            if (target > 0f)
            {
                group.blocksRaycasts = true;
                group.interactable = true;
            }

            if (fadeDuration > 0f)
            {
                float t = 0f;
                while (t < fadeDuration)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / fadeDuration);
                    group.alpha = Mathf.Lerp(start, target, k * k * (3f - 2f * k));
                    yield return null;
                }
            }

            group.alpha = target;

            if (target <= 0f)
            {
                group.blocksRaycasts = false;
                group.interactable = false;
                if (canvas != null) canvas.enabled = false;
            }
        }
    }
}
