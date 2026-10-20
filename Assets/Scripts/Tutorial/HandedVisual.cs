using UnityEngine;
using UnityEngine.UI;

namespace VRTutorial
{
    /// <summary>
    /// Drives one UI Image (and optionally its position) from the player's chosen controller hand.
    ///
    /// This replaces the "duplicate the object for each hand and toggle one off" approach. One
    /// object, one Image, sprite assigned at runtime - so a hand change part-way through the
    /// tutorial is picked up immediately instead of leaving a stale diagram on screen.
    ///
    /// IMPORTANT - do not animate this Image's sprite from an Animator. The clips in
    /// Art/Sprites/Controller animate m_Color only (no PPtr curves), which is exactly right:
    /// the Animator owns colour/alpha, this component owns which sprite is shown, and the two
    /// never fight. If you later add a clip that swaps sprites, it will overwrite this every
    /// frame it plays.
    /// </summary>
    [DisallowMultipleComponent]
    public class HandedVisual : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Image to drive. Leave empty to use the Image on this GameObject.")]
        [SerializeField] private Image targetImage;

        [Header("Sprites")]
        [Tooltip("Shown when the player is using their LEFT controller.")]
        [SerializeField] private Sprite leftSprite;

        [Tooltip("Shown when the player is using their RIGHT controller.")]
        [SerializeField] private Sprite rightSprite;

        [Header("Mirroring")]
        [Tooltip("Mirror this element's anchored X position when the left hand is active. " +
                 "Use for things that sit beside the stick - highlights, chevrons - so they stay " +
                 "attached to the correct side of the controller. Author the layout for the RIGHT " +
                 "hand; left is derived by negating X.")]
        [SerializeField] private bool mirrorXOnLeftHand = false;

        [Tooltip("Also mirror the anchor/pivot, not just the position. Needed when the element " +
                 "is anchored to one edge rather than centred.")]
        [SerializeField] private bool mirrorAnchorsAndPivot = false;

        [Tooltip("Also mirror this element's rotation when the left hand is active. Needed for " +
                 "anything tilted to lie along the controller's face - the turn chevrons - because " +
                 "the left controller's face tilts the opposite way.")]
        [SerializeField] private bool mirrorRotationOnLeftHand = false;

        [Tooltip("For a pair of direction markers, like the right and left turn chevrons. When the " +
                 "left hand is active, take the MIRRORED position and rotation of this other " +
                 "element instead of mirroring this one's own.\n\n" +
                 "Mirroring the whole diagram would put the right chevron on the left of the stick. " +
                 "What is wanted is the right chevron staying on the right, on the line the left " +
                 "controller's face actually runs along - which is where the mirrored LEFT chevron " +
                 "lands. Set RightArrow to LeftArrow and LeftArrow to RightArrow. Rotation is " +
                 "mirrored automatically when this is set.")]
        [SerializeField] private RectTransform leftHandPoseFrom;

        [Header("Fallback")]
        [Tooltip("Hand assumed when no ControllerHandednessManager exists - i.e. when this scene " +
                 "is opened on its own for testing, rather than entered through Bootstrap.")]
        [SerializeField] private ControllerHand editorFallbackHand = ControllerHand.Right;

        // Authored (right-hand) layout, captured once so mirroring is idempotent - applying
        // left twice must not drift the element further each time.
        private Vector2 _authoredAnchoredPos;
        private Vector2 _authoredAnchorMin;
        private Vector2 _authoredAnchorMax;
        private Vector2 _authoredPivot;
        private Quaternion _authoredRotation;
        private RectTransform _rect;
        private bool _captured;

        private void Awake()
        {
            if (targetImage == null) targetImage = GetComponent<Image>();
            CaptureAuthoredLayout();
        }

        private void CaptureAuthoredLayout()
        {
            if (_rect == null) _rect = transform as RectTransform;
            if (_captured || _rect == null) return;
            _authoredAnchoredPos = _rect.anchoredPosition;
            _authoredAnchorMin = _rect.anchorMin;
            _authoredAnchorMax = _rect.anchorMax;
            _authoredPivot = _rect.pivot;
            _authoredRotation = _rect.localRotation;
            _captured = true;
        }

        /// <summary>
        /// This element's right-hand (authored) position and rotation, captured before any
        /// mirroring. Safe to call on an inactive object whose Awake has not run - a partner
        /// chevron that starts hidden still reports where it was authored, not where a hand
        /// change has since moved it.
        /// </summary>
        public void GetAuthoredPose(out Vector2 anchoredPosition, out Quaternion rotation)
        {
            CaptureAuthoredLayout();
            anchoredPosition = _authoredAnchoredPos;
            rotation = _authoredRotation;
        }

        private static Quaternion MirrorX(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

        private void OnEnable()
        {
            CaptureAuthoredLayout();

            // Pull first: the manager lives in Bootstrap and calls SelectHand in Start(), which
            // may already have happened before this additively-loaded scene enabled. Relying on
            // the event alone would leave this showing whatever was authored in the prefab.
            Apply(ControllerHandednessManager.CurrentOrDefault(editorFallbackHand));

            // Then subscribe, so a mid-tutorial change from the settings page is picked up live.
            ControllerHandednessManager.HandChanged += Apply;
        }

        private void OnDisable()
        {
            ControllerHandednessManager.HandChanged -= Apply;
        }

        /// <summary>Applies a hand immediately. Public so it can be driven from a UnityEvent too.</summary>
        public void Apply(ControllerHand hand)
        {
            bool isLeft = hand == ControllerHand.Left;

            if (targetImage != null)
            {
                Sprite next = isLeft ? leftSprite : rightSprite;
                // Only assign when it actually differs - assigning a sprite dirties the canvas
                // and forces a rebuild, and this can be called on every settings change.
                if (next != null && targetImage.sprite != next) targetImage.sprite = next;
            }

            CaptureAuthoredLayout();
            if (_rect == null || !_captured) return;

            if (leftHandPoseFrom != null)
            {
                if (!isLeft)
                {
                    _rect.anchoredPosition = _authoredAnchoredPos;
                    _rect.localRotation = _authoredRotation;
                    return;
                }

                // The partner's authored pose, not its live one: it may already have moved itself
                // for the left hand, and mirroring an already-mirrored pose puts it back.
                Vector2 pos;
                Quaternion rot;
                var partner = leftHandPoseFrom.GetComponent<HandedVisual>();
                if (partner != null) partner.GetAuthoredPose(out pos, out rot);
                else { pos = leftHandPoseFrom.anchoredPosition; rot = leftHandPoseFrom.localRotation; }

                _rect.anchoredPosition = new Vector2(-pos.x, pos.y);
                _rect.localRotation = MirrorX(rot);
                return;
            }

            if (mirrorRotationOnLeftHand)
                _rect.localRotation = isLeft ? MirrorX(_authoredRotation) : _authoredRotation;

            if (mirrorXOnLeftHand)
            {
                _rect.anchoredPosition = isLeft
                    ? new Vector2(-_authoredAnchoredPos.x, _authoredAnchoredPos.y)
                    : _authoredAnchoredPos;

                if (mirrorAnchorsAndPivot)
                {
                    _rect.anchorMin = isLeft
                        ? new Vector2(1f - _authoredAnchorMax.x, _authoredAnchorMin.y)
                        : _authoredAnchorMin;
                    _rect.anchorMax = isLeft
                        ? new Vector2(1f - _authoredAnchorMin.x, _authoredAnchorMax.y)
                        : _authoredAnchorMax;
                    _rect.pivot = isLeft
                        ? new Vector2(1f - _authoredPivot.x, _authoredPivot.y)
                        : _authoredPivot;
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (targetImage == null) targetImage = GetComponent<Image>();
        }
#endif
    }
}
