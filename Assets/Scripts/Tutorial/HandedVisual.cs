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
        private RectTransform _rect;
        private bool _captured;

        private void Awake()
        {
            if (targetImage == null) targetImage = GetComponent<Image>();
            _rect = transform as RectTransform;
            CaptureAuthoredLayout();
        }

        private void CaptureAuthoredLayout()
        {
            if (_captured || _rect == null) return;
            _authoredAnchoredPos = _rect.anchoredPosition;
            _authoredAnchorMin = _rect.anchorMin;
            _authoredAnchorMax = _rect.anchorMax;
            _authoredPivot = _rect.pivot;
            _captured = true;
        }

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

            if (_rect == null || !_captured) return;

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
