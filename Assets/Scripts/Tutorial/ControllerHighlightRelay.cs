using UnityEngine;

namespace VRTutorial
{
    /// <summary>
    /// Local stand-in for the button highlights that live on the rig in Bootstrap, so a tutorial
    /// step's UnityEvent has something in its own scene to point at.
    ///
    /// Exactly the CueRelay pattern, for exactly the same reason: a cross-scene reference cannot
    /// be serialised, so the step wires to this, and this forwards through a static lookup. Drop
    /// one anywhere in the tutorial scene and aim the step's events at it.
    /// </summary>
    [DisallowMultipleComponent]
    public class ControllerHighlightRelay : MonoBehaviour
    {
        [Tooltip("Which controller to drive. Active Hand follows the participant's choice, which " +
                 "is almost always what you want - the pause button is taught on the hand they " +
                 "are actually using.")]
        [SerializeField] private Target target = Target.ActiveHand;

        [Tooltip("Hand assumed when no ControllerHandednessManager is present.")]
        [SerializeField] private ControllerHand editorFallbackHand = ControllerHand.Right;

        public enum Target { ActiveHand, Left, Right, Both }

        public void StartPulsing() => ForEach(h => h.StartPulsing());
        public void StopPulsing() => ForEach(h => h.StopPulsing());
        public void Flash() => ForEach(h => h.Flash());

        /// <summary>
        /// Marks the pause button on BOTH controllers, whatever this relay's Target is set to.
        ///
        /// Not an oversight. The Pause action is bound to the secondary button on each hand and
        /// PauseController listens to both, so both buttons genuinely open the menu - marking only
        /// the preferred one would be a lie. It also has to survive a participant switching hands
        /// in the settings an hour later, which a single-hand mark would not.
        /// </summary>
        public void HoldMarked()
        {
            Apply(ControllerHand.Left, h => h.HoldMarked());
            Apply(ControllerHand.Right, h => h.HoldMarked());
        }

        /// <summary>Removes the lasting mark from both controllers.</summary>
        public void ClearMark()
        {
            Apply(ControllerHand.Left, h => h.ClearMark());
            Apply(ControllerHand.Right, h => h.ClearMark());
        }

        private void ForEach(System.Action<ControllerButtonHighlight> action)
        {
            switch (target)
            {
                case Target.Left:
                    Apply(ControllerHand.Left, action);
                    break;
                case Target.Right:
                    Apply(ControllerHand.Right, action);
                    break;
                case Target.Both:
                    Apply(ControllerHand.Left, action);
                    Apply(ControllerHand.Right, action);
                    break;
                default:
                    Apply(ControllerHandednessManager.CurrentOrDefault(editorFallbackHand), action);
                    break;
            }
        }

        private static void Apply(ControllerHand hand, System.Action<ControllerButtonHighlight> action)
        {
            ControllerButtonHighlight highlight = ControllerButtonHighlight.For(hand);
            if (highlight != null) action(highlight);
        }
    }
}
