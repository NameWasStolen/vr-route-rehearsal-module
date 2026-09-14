using System.Collections;
using UnityEngine;

namespace VRTutorial
{
    /// <summary>
    /// A small label that rides on the participant's controller - used for the persistent
    /// "this button opens the menu" hint.
    ///
    /// Why the controller rather than a corner of the view: a head-locked corner badge sits in
    /// peripheral vision, where text cannot be read, so it can only ever be an icon. A label on
    /// the controller is in view exactly when somebody looks at their hands wondering which
    /// button does what, never occludes the street, and points at the real button rather than
    /// describing it.
    ///
    /// Position comes from the controller; rotation comes from the head. That split is the whole
    /// component. A label welded to the controller's own rotation is unreadable the moment the
    /// wrist rolls, which for a hint that is meant to be glanceable defeats the point.
    ///
    /// The anchor should be the actual button geometry inside the XR Controller prefab -
    /// Button_B on the right controller, its mirror on the left - so the hint stays correct if
    /// the binding changes, rather than sitting at an offset measured by eye.
    /// </summary>
    [DisallowMultipleComponent]
    public class ControllerMountedUI : MonoBehaviour
    {
        [Header("Anchors")]
        [Tooltip("Normally left EMPTY. The anchors live on the rig in Bootstrap, and Unity cannot " +
                 "serialise a reference from this scene to that one - the drag either refuses or " +
                 "resolves to None at runtime. Put a ControllerAnchor on each controller's face " +
                 "button instead and this finds them by itself. These fields are an override for " +
                 "opening a scene standalone, where there is no rig to publish anything.")]
        [SerializeField] private Transform leftAnchorOverride;

        [Tooltip("Standalone-testing override for the right controller. See above.")]
        [SerializeField] private Transform rightAnchorOverride;

        [Tooltip("Offset from the anchor, in the anchor's local space. Push it up and slightly " +
                 "back so the label clears the controller shell instead of intersecting it.")]
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 0.045f, -0.02f);

        [Tooltip("The head to face. Left empty, uses Camera.main.")]
        [SerializeField] private Transform headTransform;

        [Header("Comfort")]
        [Tooltip("Seconds of lag following the controller. A little smoothing hides tracking " +
                 "jitter; too much and the label swims behind the hand.")]
        [Range(0f, 0.2f)]
        [SerializeField] private float followSmoothTime = 0.05f;

        [Tooltip("Keep the label upright rather than rolling with the head.")]
        [SerializeField] private bool lockUpright = true;

        [Header("Scale")]
        [Tooltip("Ceiling on the accessibility font scale for this label. At roughly 12 cm from " +
                 "the eye a 1.6x label stops being a hint and becomes an obstruction, so the " +
                 "setting is honoured but capped.")]
        [Range(1f, 1.4f)]
        [SerializeField] private float maxFontScale = 1.2f;

        [Header("Reveal")]
        [Tooltip("Hidden until Reveal() is called - wire that to the pause lesson's onStepEnter. " +
                 "Once revealed it stays for the rest of the session; the participant should never " +
                 "have to wonder whether the way out is still there.")]
        [SerializeField] private bool hiddenUntilRevealed = true;

        [Tooltip("Seconds to fade in when revealed. Slow enough to be noticed as an arrival " +
                 "rather than a flicker.")]
        [SerializeField] private float revealDuration = 0.6f;

        [Tooltip("Hand assumed when no ControllerHandednessManager is present.")]
        [SerializeField] private ControllerHand editorFallbackHand = ControllerHand.Right;

        public bool IsRevealed { get; private set; }

        private CanvasGroup _group;
        private Transform _anchor;
        private ControllerHand _hand = ControllerHand.Right;
        private float _searchingSince = -1f;
        private bool _warnedNoAnchor;
        private Vector3 _velocity;
        private Vector3 _authoredScale;
        private Coroutine _revealRoutine;
        private bool _captured;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            Capture();
        }

        private void Capture()
        {
            if (_captured) return;
            _authoredScale = transform.localScale;
            _captured = true;
        }

        private void OnEnable()
        {
            Capture();
            TryResolveHead();

            // Read before subscribing, twice over: this scene loads additively, so both the hand
            // choice and the font scale were applied long before this object existed.
            ApplyHand(ControllerHandednessManager.CurrentOrDefault(editorFallbackHand));
            ControllerHandednessManager.HandChanged += ApplyHand;

            ApplyFontScale(AccessibilitySettings.CurrentOrDefault(AccessibilitySettings.DefaultFontScale));
            AccessibilitySettings.FontScaleChanged += ApplyFontScale;

            if (hiddenUntilRevealed && !IsRevealed) _group.alpha = 0f;

            SnapToAnchor();
        }

        private void OnDisable()
        {
            ControllerHandednessManager.HandChanged -= ApplyHand;
            AccessibilitySettings.FontScaleChanged -= ApplyFontScale;
        }

        private void TryResolveHead()
        {
            if (headTransform == null && Camera.main != null)
                headTransform = Camera.main.transform;

            Canvas canvas = GetComponent<Canvas>();
            if (canvas != null && canvas.worldCamera == null && headTransform != null)
                canvas.worldCamera = headTransform.GetComponent<Camera>();
        }

        /// <summary>
        /// Points the hint at one controller. One hint, moved - not one per hand toggled on and
        /// off: a label on the idle controller would advertise a button whose locomotion map is
        /// disabled, and HandedVisual already establishes the move-it pattern for handed UI.
        /// </summary>
        public void ApplyHand(ControllerHand hand)
        {
            _hand = hand;
            ResolveAnchor();
        }

        /// <summary>
        /// Finds the anchor for the current hand, preferring an explicit override.
        ///
        /// Called again from LateUpdate while it comes up empty rather than warning and giving
        /// up: this scene loads additively and there is no guarantee the rig's controllers have
        /// enabled by the time this object does, so a single failed attempt must not be
        /// permanent. Same reasoning as HeadLockedUI re-resolving its head.
        /// </summary>
        private void ResolveAnchor()
        {
            Transform over = _hand == ControllerHand.Left ? leftAnchorOverride : rightAnchorOverride;
            Transform found = over != null ? over : ControllerAnchor.For(_hand);

            if (found == _anchor) return;

            _anchor = found;
            if (_anchor != null)
            {
                _warnedNoAnchor = false;
                SnapToAnchor();
            }
        }

        private void ApplyFontScale(float scale)
        {
            Capture();
            transform.localScale = _authoredScale * Mathf.Min(scale, maxFontScale);
        }

        /// <summary>Fades the hint in for good. Wire to the pause lesson's onStepEnter.</summary>
        public void Reveal()
        {
            if (IsRevealed) return;
            IsRevealed = true;

            if (_revealRoutine != null) StopCoroutine(_revealRoutine);
            _revealRoutine = StartCoroutine(FadeIn());
        }

        private IEnumerator FadeIn()
        {
            float start = _group.alpha;

            if (revealDuration > 0f)
            {
                float t = 0f;
                while (t < revealDuration)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / revealDuration);
                    _group.alpha = Mathf.Lerp(start, 1f, k * k * (3f - 2f * k));
                    yield return null;
                }
            }

            _group.alpha = 1f;
            _revealRoutine = null;
        }

        private void LateUpdate()
        {
            if (headTransform == null) TryResolveHead();

            if (_anchor == null)
            {
                ResolveAnchor();
                WarnIfAnchorMissingForTooLong();
                return;
            }

            if (headTransform == null) return;

            Vector3 target = _anchor.TransformPoint(localOffset);

            transform.position = followSmoothTime > 0f
                ? Vector3.SmoothDamp(transform.position, target, ref _velocity, followSmoothTime)
                : target;

            transform.rotation = FacingRotation();
        }

        /// <summary>
        /// Complains once, and only after a grace period, so a rig that simply has not finished
        /// loading does not produce a warning per frame.
        /// </summary>
        private void WarnIfAnchorMissingForTooLong()
        {
            if (_warnedNoAnchor) return;

            if (_searchingSince < 0f) { _searchingSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _searchingSince < 3f) return;

            _warnedNoAnchor = true;
            Debug.LogWarning($"[ControllerMountedUI] '{name}' found no ControllerAnchor for the " +
                             $"{_hand} controller after 3 seconds, so the hint has nowhere to sit. " +
                             "Check the rig's face buttons each have a ControllerAnchor with the " +
                             "right hand set.", this);
        }

        private Quaternion FacingRotation()
        {
            Vector3 toPlayer = transform.position - headTransform.position;
            if (lockUpright) toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 1e-6f) return transform.rotation;

            // The canvas's +Z is its readable face, so it must point away from the head.
            return Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        }

        private void SnapToAnchor()
        {
            if (_anchor == null) return;

            transform.position = _anchor.TransformPoint(localOffset);
            _velocity = Vector3.zero;

            if (headTransform != null) transform.rotation = FacingRotation();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Transform a = _anchor != null ? _anchor : rightAnchorOverride;
            if (a == null) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(a.TransformPoint(localOffset), 0.01f);
            Gizmos.DrawLine(a.position, a.TransformPoint(localOffset));
        }
#endif
    }
}
