using UnityEngine;
using UnityEngine.UI;

namespace VRTutorial
{
    /// <summary>
    /// Fills an Image from a task's 0-1 progress, so the participant can see that holding the
    /// stick is doing something.
    ///
    /// This form matters more than a tick for the movement lesson. A tick says "done"; a bar
    /// says "keep going", and "keep going" is the only message that helps somebody halfway
    /// through a five-second hold. Without it a timed lesson is indistinguishable from a broken
    /// one - you push, nothing visibly happens, and the natural response is to stop and wait.
    ///
    /// Drive it from MovementTask.onProgressChanged. SetProgress is a float UnityEvent target,
    /// so no glue script is needed.
    ///
    /// The fill is smoothed rather than tracked exactly: a bar that stutters as somebody's thumb
    /// wobbles across the deadzone reads as a fault, where an easing one reads as effort being
    /// accumulated. Completion overrides the smoothing so the bar is definitely full at the
    /// moment the lesson says it is.
    ///
    /// Hide When Complete takes that a step further and fades the whole indicator away a beat
    /// after completion, so the panel holding the congratulation is not still fronted by a
    /// finished gauge. Off by default - an indicator never told to hide keeps whatever alpha it
    /// was authored with, exactly as before.
    /// </summary>
    [DisallowMultipleComponent]
    public class TaskProgressIndicator : MonoBehaviour
    {
        [Header("Fill")]
        [Tooltip("The Image whose fillAmount is driven. Set its Image Type to Filled - radial for " +
                 "a ring, horizontal for a bar. Leave empty to use the Image on this object.")]
        [SerializeField] private Image fillImage;

        [Tooltip("Seconds for the bar to catch up to the real value. 0 tracks exactly.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float smoothTime = 0.12f;

        [Header("Colour")]
        [Tooltip("Colour while filling.")]
        [SerializeField] private Color progressColour = new Color(0.35f, 0.75f, 1f, 1f);

        [Tooltip("Colour once complete. Keep the change modest - a bar that turns a different hue " +
                 "on completion is easy to read as a warning.")]
        [SerializeField] private Color completeColour = new Color(0.4f, 0.85f, 0.5f, 1f);

        [Tooltip("Seconds to cross-fade between the two colours.")]
        [SerializeField] private float colourFadeDuration = 0.25f;

        [Header("Visibility")]
        [Tooltip("Hide the whole indicator until there is some progress to show, so an untouched " +
                 "lesson is not fronted by an empty gauge. Needs a CanvasGroup on this object.")]
        [SerializeField] private bool hideUntilStarted = true;

        [Tooltip("Seconds to fade the indicator in once progress begins.")]
        [SerializeField] private float appearDuration = 0.2f;

        [Tooltip("Fade the indicator out shortly after the lesson completes, so the panel that " +
                 "holds the congratulation is not still fronted by a finished gauge. The bar has " +
                 "said everything it has to say by then - the prompt underneath is the thing to " +
                 "read. Needs SetComplete to be wired; progress reaching 1 on its own is not " +
                 "enough, because that is a value, not an ending.")]
        [SerializeField] private bool hideWhenComplete = false;

        [Tooltip("Seconds to wait after completion before starting to fade. Not zero on purpose: " +
                 "the bar turning its complete colour is one of the three signals that the lesson " +
                 "is done, alongside the length and the prompt changing. Whipping it away " +
                 "instantly spends that signal without anyone seeing it.")]
        [SerializeField] private float hideDelay = 0.8f;

        [Tooltip("Seconds to fade out over.")]
        [SerializeField] private float hideDuration = 0.35f;

        private CanvasGroup _group;
        private float _target;
        private float _shown;
        private float _velocity;
        private bool _complete;
        private float _colourBlend;
        private float _completeAt;
        private bool _warnedNoGroup;

        /// <summary>True when this component owns the CanvasGroup's alpha rather than leaving it alone.</summary>
        private bool DrivesAlpha => hideUntilStarted || hideWhenComplete;

        private void Awake()
        {
            if (fillImage == null) fillImage = GetComponent<Image>();
            _group = GetComponent<CanvasGroup>();

            if (fillImage != null && fillImage.type != Image.Type.Filled)
            {
                Debug.LogWarning($"[TaskProgressIndicator] '{name}' is driving an Image whose type " +
                                 "is not Filled, so fillAmount will do nothing visible.", this);
            }

            Apply(0f, instant: true);
            ApplyRestingAlpha();

            if (_group == null && DrivesAlpha && !_warnedNoGroup)
            {
                _warnedNoGroup = true;
                Debug.LogWarning($"[TaskProgressIndicator] '{name}' is set to hide itself but has " +
                                 "no CanvasGroup, so it has no way to. Add one.", this);
            }
        }

        /// <summary>
        /// The alpha this indicator rests at when nothing is in progress. Visible unless Hide
        /// Until Started says otherwise - and only touched at all when one of the hide options is
        /// on, so an indicator using neither keeps whatever alpha it was authored with.
        /// </summary>
        private void ApplyRestingAlpha()
        {
            if (_group == null || !DrivesAlpha) return;
            _group.alpha = hideUntilStarted ? 0f : 1f;
        }

        /// <summary>Float UnityEvent target. Wire MovementTask.onProgressChanged straight to this.</summary>
        public void SetProgress(float progress01)
        {
            _target = Mathf.Clamp01(progress01);
            if (_target <= 0f && !_complete) Apply(0f, instant: true);
        }

        /// <summary>Snaps to full and switches to the complete colour.</summary>
        public void SetComplete()
        {
            _complete = true;
            _completeAt = Time.unscaledTime;
            _target = 1f;
            Apply(1f, instant: true);
        }

        /// <summary>Returns to an empty, incomplete, hidden state. Wire to a step's onStepEnter.</summary>
        public void ResetIndicator()
        {
            _complete = false;
            _target = 0f;
            _colourBlend = 0f;
            _completeAt = 0f;
            Apply(0f, instant: true);

            // Snapped, not faded. A lesson replayed from the pause menu should open with the bar
            // simply there, not creeping back in from the fade-out that ended the last attempt -
            // that reads as the previous run still finishing.
            ApplyRestingAlpha();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (!_complete)
            {
                if (smoothTime > 0f)
                    _shown = Mathf.SmoothDamp(_shown, _target, ref _velocity, smoothTime, Mathf.Infinity, dt);
                else
                    _shown = _target;

                Apply(_shown, instant: false);
            }

            // Colour follows completion, not progress, so the bar does not gradually shift hue
            // on the way up and imply something is changing state when it is not.
            float colourGoal = _complete ? 1f : 0f;
            if (colourFadeDuration > 0f)
                _colourBlend = Mathf.MoveTowards(_colourBlend, colourGoal, dt / colourFadeDuration);
            else
                _colourBlend = colourGoal;

            if (fillImage != null)
                fillImage.color = Color.Lerp(progressColour, completeColour, _colourBlend);

            // One place decides the alpha, because two would fight. Left alone entirely when
            // neither hide option is on, so existing indicators behave exactly as before.
            if (_group != null && DrivesAlpha)
            {
                float alphaGoal = 1f;
                float rate = appearDuration;

                if (hideWhenComplete && _complete && Time.unscaledTime >= _completeAt + hideDelay)
                {
                    alphaGoal = 0f;
                    rate = hideDuration;
                }
                else if (hideUntilStarted && _target <= 0f && !_complete)
                {
                    alphaGoal = 0f;
                    rate = appearDuration;
                }

                _group.alpha = rate > 0f
                    ? Mathf.MoveTowards(_group.alpha, alphaGoal, dt / rate)
                    : alphaGoal;
            }
        }

        private void Apply(float value, bool instant)
        {
            if (instant)
            {
                _shown = value;
                _velocity = 0f;
            }

            if (fillImage != null) fillImage.fillAmount = value;
        }
    }
}
