using UnityEngine;
using UnityEngine.Events;

namespace VRTutorial
{
    /// <summary>
    /// One page of tutorial content living on the single persistent panel. Steps are siblings
    /// under the panel; TutorialFlow cross-fades between them.
    ///
    /// A step is faded by its CanvasGroup rather than SetActive, so nothing re-enables and
    /// therefore nothing re-snaps into place - that instant "appear fully formed" pop is what
    /// the old show/hide handoff produced.
    ///
    /// CanvasGroup.alpha multiplies down through children, so it composes cleanly with the
    /// Animator clips that drive Image.m_Color on the controller highlights. Fading with
    /// Image colour instead would fight those clips every frame.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public class TutorialStep : MonoBehaviour
    {
        [Tooltip("Optional label, purely to make the flow readable in the Inspector and in logs.")]
        [SerializeField] private string stepName = "";

        [Tooltip("Fires as this step starts fading in.")]
        public UnityEvent onStepEnter;

        [Tooltip("Fires once this step has finished fading out.")]
        public UnityEvent onStepExit;

        private CanvasGroup _group;
        private RectTransform _rect;
        private Vector3 _authoredLocalPos;
        private Vector3 _authoredLocalScale;
        private bool _captured;

        public string StepName => string.IsNullOrEmpty(stepName) ? name : stepName;
        public CanvasGroup Group
        {
            get
            {
                if (_group == null) _group = GetComponent<CanvasGroup>();
                return _group;
            }
        }

        private void Awake()
        {
            _rect = transform as RectTransform;
            Capture();
        }

        private void Capture()
        {
            if (_captured) return;
            _authoredLocalPos = transform.localPosition;
            _authoredLocalScale = transform.localScale;
            _captured = true;
        }

        [Tooltip("Deactivate the GameObject once fully hidden. Keeps Animators and layout " +
                 "rebuilds off for steps nobody is looking at. Untick only if something on this " +
                 "step must keep running while it is not shown.")]
        [SerializeField] private bool deactivateWhenHidden = true;

        /// <summary>
        /// Sets visibility without any transition. alpha 0 also turns off raycasts and
        /// interactability so a faded-out step can't swallow pokes or gaze.
        ///
        /// Activation is handled here as well as alpha: a step left disabled in the Inspector
        /// would otherwise never appear, because fading only drives the CanvasGroup and a
        /// disabled GameObject renders nothing however high its alpha goes.
        /// </summary>
        public void SetVisible(bool visible)
        {
            Capture();

            if (visible && !gameObject.activeSelf) gameObject.SetActive(true);

            Group.alpha = visible ? 1f : 0f;
            Group.blocksRaycasts = visible;
            Group.interactable = visible;
            transform.localPosition = _authoredLocalPos;
            transform.localScale = _authoredLocalScale;

            if (!visible && deactivateWhenHidden) gameObject.SetActive(false);
        }

        /// <summary>
        /// Applies an in-progress fade. <paramref name="t"/> is 0 (fully hidden) to 1 (fully shown).
        /// The arrive offset pushes the content slightly further from the player and scales it a
        /// touch down at t=0, so it settles forward into place rather than simply appearing.
        /// Motion is deliberately small and always away-to-neutral - content moving toward the
        /// eyes at ~1.5 m is uncomfortable, and more so for an older cohort.
        /// </summary>
        public void ApplyFade(float t, float arriveBackMetres, float arriveScale)
        {
            Capture();

            // Must be active to be seen at all - a step that was deactivated while hidden has
            // to come back on before its alpha means anything.
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            t = Mathf.Clamp01(t);
            // Smoothstep: no hard velocity change at either end of the fade.
            float eased = t * t * (3f - 2f * t);

            Group.alpha = eased;
            Group.blocksRaycasts = eased > 0.5f;
            Group.interactable = eased > 0.5f;

            if (arriveBackMetres > 0f)
            {
                // Convert metres to this canvas's local units. A world-space canvas is typically
                // scaled to ~0.001, so a raw metre value would be wildly off.
                float scaleZ = Mathf.Abs(transform.lossyScale.z);
                float localBack = scaleZ > 1e-6f ? arriveBackMetres / scaleZ : 0f;
                Vector3 p = _authoredLocalPos;
                p.z += Mathf.Lerp(localBack, 0f, eased);
                transform.localPosition = p;
            }

            if (!Mathf.Approximately(arriveScale, 1f))
            {
                transform.localScale = _authoredLocalScale * Mathf.Lerp(arriveScale, 1f, eased);
            }
        }

        internal void RaiseEnter() => onStepEnter?.Invoke();
        internal void RaiseExit() => onStepExit?.Invoke();
    }
}
