using UnityEngine;

namespace VRTutorial
{
    /// <summary>
    /// Per-element override for PanelMotionFade. Put it on something inside a panel that has a
    /// PanelMotionFade on its root.
    ///
    /// Progress bars (TaskProgressIndicator) never need this. They are exempt from fading
    /// automatically, and one that is filling holds the whole panel solid by itself.
    /// </summary>
    [DisallowMultipleComponent]
    public class MotionFadeRule : MonoBehaviour
    {
        public enum Mode
        {
            [Tooltip("This element and its children never fade. Use it on a progress bar's track " +
                     "when the track is a sibling of the fill rather than its parent, or on a " +
                     "step counter that should stay readable.")]
            NeverFadeThis,

            [Tooltip("While this object is showing, the whole panel stays solid during movement. " +
                     "Put it on a TutorialStep whose instruction has to be read WHILE moving - the " +
                     "turning lesson (StepCamera), in all three modes. Its bar returns to zero " +
                     "between directions, so the bar alone cannot hold the panel there.")]
            HoldPanelWhileShowing,
        }

        [SerializeField] private Mode mode = Mode.HoldPanelWhileShowing;

        public Mode RuleMode => mode;
    }
}
