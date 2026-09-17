using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VRTutorial
{
    /// <summary>
    /// A small icon badge beside a controller, with a thin line to the physical button it
    /// explains - the pause bars pointing at B/Y, the question mark pointing at A/X.
    ///
    /// Why a badge and a line rather than a text label: an icon needs no English, the line says
    /// exactly which of two neighbouring buttons it means, and the badge sits off the controller's
    /// outer edge so it never covers the buttons or the thumbstick. It works alongside the button
    /// tint rather than replacing it - the tint marks the control, the icon says what it does.
    ///
    /// Built entirely at runtime and parented to the tracked hand on the rig, so there is nothing
    /// to add to the XR Controller prefabs or to Bootstrap. The rig lives in Bootstrap and the
    /// tutorial scene cannot hold references into it; like the highlights, this reaches the button
    /// geometry through ControllerButtonHighlight's registry.
    ///
    /// Normally driven by ControllerHighlightRelay: the lesson's onCompleted already calls
    /// HoldMarked, and that now shows the tooltip too.
    /// </summary>
    [DisallowMultipleComponent]
    public class ControllerTooltip : MonoBehaviour
    {
        private const float CanvasUnits = 100f;

        private static readonly Dictionary<int, ControllerTooltip> Active =
            new Dictionary<int, ControllerTooltip>();

        private static int Key(ControllerHand hand, ControllerButton button) =>
            ((int)hand << 4) | (int)button;

        /// <summary>
        /// Default placement relative to the button, in the tracked hand's space, before the
        /// outward component is mirrored for the left hand. X = outward, Y = up off the face,
        /// Z = forward. Pause (B/Y) sits higher and help (A/X) lower, so two 4 cm badges on the
        /// same controller never overlap and each line runs to its own button without crossing.
        /// </summary>
        public static Vector3 DefaultOffset(ControllerButton button) =>
            button == ControllerButton.Secondary
                ? new Vector3(0.065f, 0.055f, -0.005f)
                : new Vector3(0.065f, 0.012f, 0.012f);

        public struct Settings
        {
            public Texture icon;
            public Vector3 offset;
            public float diameter;
            public Color lineColour;
            public float lineWidth;
            public float fadeDuration;
        }

        /// <summary>
        /// Shows the tooltip for one button on one controller. Safe to call repeatedly - a
        /// reviewed lesson completing again does not stack a second badge.
        /// </summary>
        /// <returns>False if the rig or that button's highlight is not loaded.</returns>
        public static bool Show(ControllerHand hand, ControllerButton button, Settings settings)
        {
            int key = Key(hand, button);
            if (Active.TryGetValue(key, out ControllerTooltip existing) && existing != null)
                return true;

            ControllerButtonHighlight highlight = ControllerButtonHighlight.For(hand, button);
            if (highlight == null) return false;

            Transform buttonTransform = highlight.transform;
            Transform handTransform = FindTrackedHand(buttonTransform);

            var root = new GameObject($"Tooltip_{hand}_{button}");
            root.transform.SetParent(handTransform, false);

            var tooltip = root.AddComponent<ControllerTooltip>();
            tooltip.Build(hand, button, buttonTransform, handTransform, settings);
            Active[key] = tooltip;
            return true;
        }

        /// <summary>Removes the tooltip for one button on one controller, if it is showing.</summary>
        public static void Hide(ControllerHand hand, ControllerButton button)
        {
            int key = Key(hand, button);
            if (Active.TryGetValue(key, out ControllerTooltip tooltip))
            {
                Active.Remove(key);
                if (tooltip != null) Destroy(tooltip.gameObject);
            }
        }

        public static bool IsShowing(ControllerHand hand, ControllerButton button) =>
            Active.TryGetValue(Key(hand, button), out ControllerTooltip t) && t != null;

        /// <summary>
        /// The tracked object the controller model hangs off - the one with the Tracked Pose
        /// Driver. Its axes are the hand's (X right, Y up, Z forward) regardless of how the model
        /// inside it is rotated, which is what makes "outward" and "up" mean the same thing on
        /// every controller. Matched by type name so this does not depend on which assembly the
        /// driver comes from.
        /// </summary>
        private static Transform FindTrackedHand(Transform from)
        {
            for (Transform t = from; t != null; t = t.parent)
            {
                foreach (Component c in t.GetComponents<Component>())
                    if (c != null && c.GetType().Name == "TrackedPoseDriver") return t;
            }
            return from.parent != null ? from.parent : from;
        }

        // ------------------------------------------------------------------ instance

        private ControllerHand _hand;
        private ControllerButton _button;
        private Transform _buttonTransform;
        private Transform _handTransform;
        private Settings _settings;

        private RectTransform _badge;
        private CanvasGroup _badgeGroup;
        private LineRenderer _line;
        private Material _lineMaterial;
        private Transform _head;
        private float _shownAt;

        private void Build(ControllerHand hand, ControllerButton button, Transform buttonTransform,
                           Transform handTransform, Settings settings)
        {
            _hand = hand;
            _button = button;
            _buttonTransform = buttonTransform;
            _handTransform = handTransform;
            _settings = settings;
            _shownAt = Time.unscaledTime;

            // Badge: a tiny world-space canvas with the icon stretched across it. No raycaster -
            // it must never catch the pointer ray aimed at a panel behind it.
            var badgeGo = new GameObject("Badge", typeof(RectTransform));
            badgeGo.transform.SetParent(transform, false);
            _badge = (RectTransform)badgeGo.transform;
            _badge.sizeDelta = new Vector2(CanvasUnits, CanvasUnits);

            var canvas = badgeGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _badgeGroup = badgeGo.AddComponent<CanvasGroup>();
            _badgeGroup.blocksRaycasts = false;
            _badgeGroup.interactable = false;
            _badgeGroup.alpha = 0f;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(_badge, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            var raw = iconGo.AddComponent<RawImage>();
            raw.texture = settings.icon;
            raw.raycastTarget = false;

            // Leader line, drawn in this object's local space so it rides the tracked hand
            // without a frame of lag.
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = false;
            _line.positionCount = 2;
            _line.numCapVertices = 4;
            _line.widthMultiplier = settings.lineWidth;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            // The UI default material is always included in builds, unlike Shader.Find of a URP
            // shader. It reads its depth test from unity_GUIZTestMode, which only a Canvas sets,
            // so the copy sets it explicitly - otherwise the line would draw through everything.
            _lineMaterial = new Material(Canvas.GetDefaultCanvasMaterial())
            {
                name = "ControllerTooltip Line",
                hideFlags = HideFlags.DontSave
            };
            _lineMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            _line.sharedMaterial = _lineMaterial;

            ApplyAlpha(0f);
            UpdatePlacement();
        }

        private void LateUpdate()
        {
            if (_buttonTransform == null || _handTransform == null)
            {
                Destroy(gameObject);
                return;
            }

            float fade = _settings.fadeDuration > 0f
                ? Mathf.Clamp01((Time.unscaledTime - _shownAt) / _settings.fadeDuration)
                : 1f;
            ApplyAlpha(fade * fade * (3f - 2f * fade));

            UpdatePlacement();
        }

        private void UpdatePlacement()
        {
            float outward = _hand == ControllerHand.Left ? -1f : 1f;
            Vector3 offset = _settings.offset;

            // Button position re-read every frame: the controller model animates buttons down
            // when pressed, and the line should stay attached.
            Vector3 buttonLocal = _handTransform.InverseTransformPoint(_buttonTransform.position);
            Vector3 badgeLocal = buttonLocal + new Vector3(offset.x * outward, offset.y, offset.z);

            transform.localPosition = badgeLocal;
            transform.localRotation = Quaternion.identity;

            float scale = Mathf.Max(transform.lossyScale.x, 1e-4f);
            float radiusLocal = _settings.diameter * 0.5f / scale;

            // Line from just above the button to the edge of the badge, so it does not cross
            // the icon.
            Vector3 toButton = (buttonLocal + Vector3.up * (0.004f / scale)) - badgeLocal;
            Vector3 edge = toButton.sqrMagnitude > 1e-8f ? toButton.normalized * radiusLocal : Vector3.zero;
            _line.SetPosition(0, toButton);
            _line.SetPosition(1, edge);

            _badge.localScale = Vector3.one * (_settings.diameter / CanvasUnits / scale);

            if (_head == null && Camera.main != null) _head = Camera.main.transform;
            if (_head != null)
            {
                // Face the eye, upright relative to the head, so the icon reads the right way up
                // however the wrist is rolled. A canvas is read from its -Z side.
                Vector3 away = _badge.position - _head.position;
                if (away.sqrMagnitude > 1e-6f)
                    _badge.rotation = Quaternion.LookRotation(away, _head.up);
            }
        }

        private void ApplyAlpha(float a)
        {
            if (_badgeGroup != null) _badgeGroup.alpha = a;
            if (_line != null)
            {
                Color c = _settings.lineColour;
                c.a *= a;
                _line.startColor = c;
                _line.endColor = c;
            }
        }

        private void OnDestroy()
        {
            int key = Key(_hand, _button);
            if (Active.TryGetValue(key, out ControllerTooltip t) && t == this) Active.Remove(key);
            if (_lineMaterial != null) Destroy(_lineMaterial);
        }
    }
}
