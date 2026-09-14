using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VRTutorial
{
    /// <summary>
    /// Tints a real piece of controller geometry - Button_B and friends inside the XR Controller
    /// prefab - to draw the eye to it, and to confirm a press.
    ///
    /// Worth doing on the model rather than only on the panel diagram: at the moment somebody
    /// presses a button they are looking at their hand, not at a panel a metre and a half away.
    /// A confirmation they are already looking at is a confirmation they will actually register,
    /// which is the specific failure mode this cohort has - not that a cue is too harsh, but that
    /// it is missed entirely.
    ///
    /// Pulse while waiting, flash on press. Both go through the renderer's instanced material
    /// rather than a property block, so the shared Controller_White asset is never written to -
    /// editing that would tint every controller in the project, and would show up as a change on
    /// disk after a play session.
    /// </summary>
    [DisallowMultipleComponent]
    public class ControllerButtonHighlight : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Which controller this highlight is on. Published under this hand so a tutorial " +
                 "scene can reach it - the rig lives in Bootstrap, so nothing in a content scene " +
                 "can hold a direct reference. Drive it through a ControllerHighlightRelay.")]
        [SerializeField] private ControllerHand hand = ControllerHand.Right;

        [Tooltip("Renderer of the button geometry. Leave empty to use the Renderer on this object.")]
        [SerializeField] private Renderer targetRenderer;

        [Tooltip("Colour property to drive. URP Lit uses _BaseColor; older shaders use _Color. " +
                 "Both are tried in that order if this is left at the default.")]
        [SerializeField] private string colourProperty = "_BaseColor";

        [Header("Colours")]
        [Tooltip("Colour at the top of a pulse, and on a press.")]
        [SerializeField] private Color highlightColour = new Color(0.4f, 0.8f, 1f, 1f);

        [Header("Waiting pulse")]
        [Tooltip("Seconds for one full pulse cycle while waiting for the press. Slow - a fast " +
                 "pulse on a physical control reads as a warning light.")]
        [SerializeField] private float pulsePeriod = 1.8f;

        [Tooltip("How far toward the highlight colour a pulse travels, 0-1. Deliberately partial: " +
                 "the button should look lit, not replaced.")]
        [Range(0f, 1f)]
        [SerializeField] private float pulseDepth = 0.55f;

        [Header("Press flash")]
        [Tooltip("Seconds for the confirmation flash to rise and fall.")]
        [SerializeField] private float flashDuration = 0.35f;

        private Material _material;
        private int _propertyId = -1;
        private Color _authoredColour;
        private bool _ready;
        private Coroutine _pulseRoutine;
        private Coroutine _flashRoutine;

        private static readonly Dictionary<ControllerHand, ControllerButtonHighlight> Registered =
            new Dictionary<ControllerHand, ControllerButtonHighlight>();

        /// <summary>The highlight registered for a hand, or null if the rig is not loaded.</summary>
        public static ControllerButtonHighlight For(ControllerHand hand)
        {
            return Registered.TryGetValue(hand, out ControllerButtonHighlight h) ? h : null;
        }

        private void Awake()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            Prepare();
        }

        private void OnEnable()
        {
            Registered[hand] = this;
        }

        private void Prepare()
        {
            if (_ready || targetRenderer == null) return;

            // Touching .material instantiates the shared asset for this renderer only, which is
            // exactly what is wanted here.
            _material = targetRenderer.material;
            if (_material == null) return;

            if (!string.IsNullOrEmpty(colourProperty) && _material.HasProperty(colourProperty))
                _propertyId = Shader.PropertyToID(colourProperty);
            else if (_material.HasProperty("_BaseColor"))
                _propertyId = Shader.PropertyToID("_BaseColor");
            else if (_material.HasProperty("_Color"))
                _propertyId = Shader.PropertyToID("_Color");

            if (_propertyId == -1)
            {
                Debug.LogWarning($"[ControllerButtonHighlight] '{name}' found no colour property to " +
                                 "drive on its material, so the highlight will do nothing.", this);
                return;
            }

            _authoredColour = _material.GetColor(_propertyId);
            _ready = true;
        }

        private void OnDisable()
        {
            if (Registered.TryGetValue(hand, out ControllerButtonHighlight current) && current == this)
                Registered.Remove(hand);

            StopPulsing();
            Restore();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Starts the slow waiting pulse. Wire to the pause lesson's onStepEnter.</summary>
        public void StartPulsing()
        {
            Prepare();
            if (!_ready) return;

            if (_pulseRoutine != null) StopCoroutine(_pulseRoutine);
            _pulseRoutine = StartCoroutine(Pulse());
        }

        /// <summary>Stops the waiting pulse and returns the button to its authored colour.</summary>
        public void StopPulsing()
        {
            if (_pulseRoutine != null)
            {
                StopCoroutine(_pulseRoutine);
                _pulseRoutine = null;
            }
            Restore();
        }

        /// <summary>
        /// One confirmation flash. Wire to whatever fires on a successful press. Stops the waiting
        /// pulse, since the thing it was waiting for has now happened.
        /// </summary>
        public void Flash()
        {
            Prepare();
            if (!_ready) return;

            if (_pulseRoutine != null)
            {
                StopCoroutine(_pulseRoutine);
                _pulseRoutine = null;
            }

            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashOnce());
        }

        // ------------------------------------------------------------------ internals

        private IEnumerator Pulse()
        {
            while (true)
            {
                float t = 0f;
                float period = Mathf.Max(0.1f, pulsePeriod);

                while (t < period)
                {
                    t += Time.unscaledDeltaTime;

                    // Sine rather than a triangle wave: no visible corner at the top or bottom of
                    // the cycle, so it breathes instead of blinking.
                    float k = (1f - Mathf.Cos(t / period * Mathf.PI * 2f)) * 0.5f;
                    SetBlend(k * pulseDepth);
                    yield return null;
                }
            }
        }

        private IEnumerator FlashOnce()
        {
            float half = Mathf.Max(0.01f, flashDuration * 0.5f);

            float t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                SetBlend(Mathf.Clamp01(t / half));
                yield return null;
            }

            t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                SetBlend(1f - Mathf.Clamp01(t / half));
                yield return null;
            }

            Restore();
            _flashRoutine = null;
        }

        private void SetBlend(float k)
        {
            if (!_ready) return;
            _material.SetColor(_propertyId, Color.Lerp(_authoredColour, highlightColour, Mathf.Clamp01(k)));
        }

        private void Restore()
        {
            if (!_ready) return;
            _material.SetColor(_propertyId, _authoredColour);
        }
    }
}
