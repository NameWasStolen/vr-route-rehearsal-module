using System.Collections.Generic;
using UnityEngine;

namespace VRTutorial
{
    /// <summary>
    /// Marks a point on a controller that scene-level UI can attach itself to - the face button
    /// the pause hint sits beside, most obviously.
    ///
    /// This exists because of where the rig lives. XRPlayerRig is in Bootstrap; the tutorial UI
    /// is in an additively-loaded content scene, and Unity cannot serialise a reference from one
    /// scene to another. Dragging Button_B into a tutorial-scene field either refuses outright or
    /// resolves to None at runtime - the same trap as dragging Bootstrap's UiCuePlayer into a
    /// tutorial UnityEvent, and the same solution: the thing in Bootstrap publishes itself, and
    /// the thing in the content scene looks it up.
    ///
    /// Put one of these on the button geometry inside the XR Controller prefab, set its hand, and
    /// anything in any scene can find it.
    /// </summary>
    [DisallowMultipleComponent]
    public class ControllerAnchor : MonoBehaviour
    {
        [Tooltip("Which controller this anchor is on. Getting this wrong puts the pause hint on " +
                 "the other hand, which is a confusing failure rather than an obvious one.")]
        [SerializeField] private ControllerHand hand = ControllerHand.Right;

        private static readonly Dictionary<ControllerHand, ControllerAnchor> Registered =
            new Dictionary<ControllerHand, ControllerAnchor>();

        public ControllerHand Hand => hand;

        /// <summary>The anchor registered for a hand, or null if the rig is not loaded yet.</summary>
        public static Transform For(ControllerHand hand)
        {
            return Registered.TryGetValue(hand, out ControllerAnchor anchor) && anchor != null
                ? anchor.transform
                : null;
        }

        private void OnEnable()
        {
            if (Registered.TryGetValue(hand, out ControllerAnchor existing) &&
                existing != null && existing != this)
            {
                Debug.LogWarning($"[ControllerAnchor] Two anchors claim the {hand} controller - " +
                                 $"'{existing.name}' and '{name}'. The newer one wins.", this);
            }

            Registered[hand] = this;
        }

        private void OnDisable()
        {
            if (Registered.TryGetValue(hand, out ControllerAnchor current) && current == this)
                Registered.Remove(hand);
        }
    }
}
