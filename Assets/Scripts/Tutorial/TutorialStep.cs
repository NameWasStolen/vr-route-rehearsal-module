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

        [Header("Panel placement (optional)")]
        [Tooltip("Move/resize the shared panel while this step is showing. Leave unticked to " +
                 "keep whatever the panel was authored with. Both steps live on one panel now, " +
                 "so placement has to belong to the step rather than the panel.")]
        [SerializeField] private bool overridePlacement = false;

        [Tooltip("Offset from the head in head-local space. Z is forward, Y up, X right. " +
                 "Lower the Y to drop the panel below eye line.")]
        [SerializeField] private Vector3 localOffset = new Vector3(0f, -0.35f, 1.4f);

        [Tooltip("Extra rotation on top of the billboard, degrees. Positive X leans the top " +
                 "away from you, which is what makes a low panel face upward into your view.")]
        [SerializeField] private Vector3 rotationOffset = new Vector3(30f, 0f, 0f);

        [Tooltip("Multiplier on the panel's authored scale. Below 1 makes it smaller.")]
        [Range(0.2f, 2f)]
        [SerializeField] private float panelScale = 1f;

        [Tooltip("Resize the panel FRAME for this step - the Background stretches to the canvas, " +
                 "so this changes the border without shrinking the text inside it. Use this " +
                 "rather than Panel Scale when you want a tighter frame but the same legible " +
                 "type; Panel Scale shrinks the wording too.")]
        [SerializeField] private bool overrideSize = false;

        [Tooltip("Canvas width and height in UI units. The panel is authored at 1200 x 1200.")]
        [SerializeField] private Vector2 panelSize = new Vector2(1000f, 700f);

        public bool OverridePlacement => overridePlacement;
        public bool OverrideSize => overrideSize;
        public Vector2 PanelSize => panelSize;
        public Vector3 LocalOffset => localOffset;
        public Vector3 RotationOffset => rotationOffset;
        public float PanelScale => panelScale;

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
