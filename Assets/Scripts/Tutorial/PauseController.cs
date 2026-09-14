using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace VRTutorial
{
    /// <summary>
    /// Opens and closes the pause menu, and owns what pausing means.
    ///
    /// Pausing suspends locomotion through ControllerHandednessManager - the same action-map
    /// mechanism that already switches hands - and raises TutorialPause so tasks hold their
    /// timers. It does not touch Time.timeScale; see TutorialPause for why.
    ///
    /// The pause action must NOT live in the Left/Right Locomotion maps, because suspending
    /// locomotion would disable the button that un-suspends it. Put it in its own map, or leave
    /// it in a map nothing else switches.
    /// </summary>
    [DisallowMultipleComponent]
    public class PauseController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Face button on the LEFT controller, used when the participant has chosen their " +
                 "left hand. Bound as an action reference rather than a raw button so the actual " +
                 "binding can be settled in the headset without touching this script.")]
        [SerializeField] private InputActionReference leftPauseAction;

        [Tooltip("Face button on the RIGHT controller.")]
        [SerializeField] private InputActionReference rightPauseAction;

        [Tooltip("Also accept this key, for testing through the XR Interaction Simulator where " +
                 "no real controller button is being fed.")]
        [SerializeField] private Key editorFallbackKey = Key.P;

        [Header("Availability")]
        [Tooltip("Whether the button works before the pause lesson has taught it. Left on " +
                 "deliberately: this module is about travel anxiety, and someone who wants out of " +
                 "the experience should never be told they have not unlocked the way out yet. " +
                 "Untick only if an untaught menu is actively causing confusion in testing.")]
        [SerializeField] private bool availableFromStart = true;

        [Header("Menu")]
        [Tooltip("Root of the pause menu panel. Needs a CanvasGroup.")]
        [SerializeField] private GameObject menuRoot;

        [Tooltip("Hold the menu at a fixed world pose while it is open, instead of letting it " +
                 "follow the head.\n\n" +
                 "Off by default. Freezing makes the buttons a stationary target, which matters " +
                 "for poking; with a ray pointer it matters much less, and a menu that stays put " +
                 "while the participant looks around reads as the menu having been left behind. " +
                 "Tick it only if aiming at the buttons turns out to be a problem in testing.")]
        [SerializeField] private bool freezeWhenOpen = false;

        [Tooltip("Seconds to fade the menu in and out.")]
        [SerializeField] private float fadeDuration = 0.25f;

        [Header("Events")]
        public UnityEvent onOpened;
        public UnityEvent onClosed;

        public bool IsOpen { get; private set; }

        /// <summary>
        /// Whether the pause button currently does anything. The pause lesson can turn this on
        /// through SetAvailable if Available From Start is unticked.
        /// </summary>
        public bool IsAvailable { get; private set; }

        private CanvasGroup _group;
        private Canvas _canvas;
        private HeadLockedUI _headLocked;
        private Coroutine _fadeRoutine;

        private void Awake()
        {
            if (menuRoot != null)
            {
                _group = menuRoot.GetComponent<CanvasGroup>();
                if (_group == null) _group = menuRoot.AddComponent<CanvasGroup>();
                _canvas = menuRoot.GetComponent<Canvas>();
                _headLocked = menuRoot.GetComponent<HeadLockedUI>();
            }

            IsAvailable = availableFromStart;
            ApplyClosedStateImmediate();
        }

        private void OnEnable()
        {
            EnableAction(leftPauseAction);
            EnableAction(rightPauseAction);
        }

        private void OnDisable()
        {
            // The flag is static and would otherwise survive into the next scene, leaving a fresh
            // session convinced it began paused.
            if (IsOpen)
            {
                TutorialPause.IsPaused = false;
                ControllerHandednessManager.Instance?.ResumeLocomotion();
            }
            TutorialPause.ResetState();
            IsOpen = false;
        }

        /// <summary>
        /// Both actions are enabled, not just the active hand's. The idle controller's button
        /// doing nothing is a worse failure than either hand working - a participant who has
        /// forgotten which controller they nominated should still be able to get out.
        /// </summary>
        private static void EnableAction(InputActionReference reference)
        {
            if (reference != null && reference.action != null && !reference.action.enabled)
                reference.action.Enable();
        }

        private static bool Pressed(InputActionReference reference)
        {
            return reference != null && reference.action != null && reference.action.WasPressedThisFrame();
        }

        private void Update()
        {
            if (!IsAvailable) return;

            bool pressed = Pressed(leftPauseAction) || Pressed(rightPauseAction);

            if (!pressed && editorFallbackKey != Key.None && Keyboard.current != null)
                pressed = Keyboard.current[editorFallbackKey].wasPressedThisFrame;

            if (pressed) Toggle();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Makes the pause button live. Wire to the pause lesson's onStepEnter.</summary>
        public void SetAvailable(bool available) => IsAvailable = available;

        public void EnablePausing() => SetAvailable(true);

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        public void Open()
        {
            if (IsOpen || menuRoot == null) return;
            IsOpen = true;

            TutorialPause.IsPaused = true;
            ControllerHandednessManager.Instance?.SuspendLocomotion();

            menuRoot.SetActive(true);
            if (_canvas != null) _canvas.enabled = true;

            if (_headLocked != null)
            {
                // Snap first either way, so the menu is already in the right place on the frame
                // it appears rather than sliding in from wherever the panel was last left.
                _headLocked.SnapToTarget();
                if (freezeWhenOpen) _headLocked.FreezeAtCurrent();
                else _headLocked.Unfreeze();
            }

            StartFade(1f);
            onOpened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;

            TutorialPause.IsPaused = false;
            ControllerHandednessManager.Instance?.ResumeLocomotion();

            if (_headLocked != null) _headLocked.Unfreeze();

            StartFade(0f);
            onClosed?.Invoke();
        }

        /// <summary>
        /// Matches UnityEvent&lt;int&gt; so it can be wired straight to TutorialFlow.onReviewStarted.
        /// Reopening a lesson has to close the menu: practising the movement hold is impossible
        /// while the menu is holding locomotion suspended.
        /// </summary>
        public void Close(int _) => Close();

        // ------------------------------------------------------------------ internals

        private void ApplyClosedStateImmediate()
        {
            if (_group == null) return;

            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            if (_canvas != null) _canvas.enabled = false;
        }

        private void StartFade(float target)
        {
            if (_group == null) return;
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(Fade(target));
        }

        private IEnumerator Fade(float target)
        {
            float start = _group.alpha;

            // Interactable the moment it starts appearing when opening, so an eager participant
            // pointing at a button during the fade is not ignored.
            if (target > 0f)
            {
                _group.blocksRaycasts = true;
                _group.interactable = true;
            }

            if (fadeDuration > 0f)
            {
                float t = 0f;
                while (t < fadeDuration)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / fadeDuration);
                    _group.alpha = Mathf.Lerp(start, target, k * k * (3f - 2f * k));
                    yield return null;
                }
            }

            _group.alpha = target;

            if (target <= 0f)
            {
                _group.blocksRaycasts = false;
                _group.interactable = false;
                if (_canvas != null) _canvas.enabled = false;
            }

            _fadeRoutine = null;
        }
    }
}
