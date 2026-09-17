using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRTutorial
{
    /// <summary>
    /// Which controller, if any, is currently hidden. Read by anything drawn on a controller that
    /// is not simply a renderer under it - the tooltip badges - so they hide with it.
    /// </summary>
    public static class ControllerVisibility
    {
        private static ControllerHand? _hidden;

        public static event Action Changed;

        public static bool IsHidden(ControllerHand hand) => _hidden.HasValue && _hidden.Value == hand;

        internal static void SetHidden(ControllerHand? hand)
        {
            if (_hidden == hand) return;
            _hidden = hand;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Shows only the participant's selected controller while this scene is loaded. The other
    /// one's model, pointer ray and tooltip badges are hidden.
    ///
    /// Why: two identical controllers on screen, when only one of them is the one being taught,
    /// invites the participant to follow the wrong hand. With only the selected controller
    /// visible, every diagram, highlight and badge has one obvious real counterpart.
    ///
    /// What is deliberately NOT turned off: the idle controller's pause (B/Y) and help (A/X)
    /// buttons. Those live in the Menu action map and keep working on both hands - somebody who
    /// picks up the wrong controller in a moment of stress must still be able to pause or ask for
    /// help. Locomotion was already limited to the selected hand by ControllerHandednessManager.
    ///
    /// The model is hidden by switching its renderers off rather than deactivating it, so the
    /// button highlights, anchors and their registrations on it keep running - the lasting tint
    /// is still there the moment the participant switches hands. The pointer ray is deactivated
    /// outright, so an invisible ray can never click a menu.
    ///
    /// Scene-scoped on purpose: put one in Tutorial.unity. When the scene unloads, everything is
    /// put back, so both controllers show again at the main menu.
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectedControllerOnly : MonoBehaviour
    {
        [Tooltip("Hand assumed when no ControllerHandednessManager is present (scene opened " +
                 "standalone). Nothing is hidden in that case unless a rig is found.")]
        [SerializeField] private ControllerHand editorFallbackHand = ControllerHand.Right;

        [Tooltip("Seconds between re-checks. Catches renderers or interactors that something " +
                 "else switched back on, and a rig that finished loading after this scene.")]
        [SerializeField] private float recheckInterval = 1f;

        private readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
        private readonly List<GameObject> _hiddenInteractors = new List<GameObject>();
        private ControllerHand? _appliedHiddenHand;
        private float _nextCheck;

        private void OnEnable()
        {
            ControllerHandednessManager.HandChanged += OnHandChanged;
            Apply();
        }

        private void OnDisable()
        {
            ControllerHandednessManager.HandChanged -= OnHandChanged;
            RestoreAll();
            ControllerVisibility.SetHidden(null);
        }

        private void OnHandChanged(ControllerHand _) => Apply();

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + recheckInterval;
            Apply();
        }

        private void Apply()
        {
            ControllerHand selected = ControllerHandednessManager.CurrentOrDefault(editorFallbackHand);
            ControllerHand idle = selected == ControllerHand.Left ? ControllerHand.Right : ControllerHand.Left;

            Transform idleHand = FindHand(idle);
            if (idleHand == null) return;   // rig not loaded yet; the re-check will retry

            // Switched hands: bring the previously hidden controller back first.
            if (_appliedHiddenHand.HasValue && _appliedHiddenHand.Value != idle) RestoreAll();

            _appliedHiddenHand = idle;
            ControllerVisibility.SetHidden(idle);
            HideUnder(idleHand);
        }

        private void HideUnder(Transform hand)
        {
            // Pointer interactors first: deactivating them also removes their line visuals.
            foreach (Component c in hand.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c.transform == hand) continue;
                if (!c.GetType().Name.EndsWith("Interactor")) continue;

                GameObject go = c.gameObject;
                if (go.activeSelf)
                {
                    go.SetActive(false);
                    if (!_hiddenInteractors.Contains(go)) _hiddenInteractors.Add(go);
                }
            }

            foreach (Renderer r in hand.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (r.GetComponentInParent<ControllerTooltip>(true) != null) continue;   // hides itself

                r.enabled = false;
                if (!_hiddenRenderers.Contains(r)) _hiddenRenderers.Add(r);
            }
        }

        private void RestoreAll()
        {
            foreach (Renderer r in _hiddenRenderers)
                if (r != null) r.enabled = true;
            _hiddenRenderers.Clear();

            foreach (GameObject go in _hiddenInteractors)
                if (go != null) go.SetActive(true);
            _hiddenInteractors.Clear();

            _appliedHiddenHand = null;
        }

        /// <summary>
        /// The tracked hand object for a controller, found through that controller's button
        /// highlight - the same route the tooltips use, so no reference into Bootstrap is needed.
        /// </summary>
        private static Transform FindHand(ControllerHand hand)
        {
            ControllerButtonHighlight h = ControllerButtonHighlight.For(hand, ControllerButton.Secondary)
                                          ?? ControllerButtonHighlight.For(hand, ControllerButton.Primary);
            return h != null ? ControllerTooltip.FindTrackedHand(h.transform) : null;
        }
    }
}
