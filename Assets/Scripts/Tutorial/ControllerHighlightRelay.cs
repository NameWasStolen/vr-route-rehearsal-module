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

        [Tooltip("Which face button this relay drives. One relay per button: the pause lesson " +
                 "points at a Secondary relay, the assistance lesson at a Primary one. Defaults " +
                 "to Secondary so relays authored before this field existed keep driving pause.")]
        [SerializeField] private ControllerButton button = ControllerButton.Secondary;

        [Header("Tooltip")]
        [Tooltip("Also show an icon badge beside this button, with a line to it, whenever the " +
                 "button is marked - on both controllers, for the rest of the tutorial. The pause " +
                 "and help lessons already call HoldMarked on completion, so this needs no extra " +
                 "wiring. Untick to keep the tint only.")]
        [SerializeField] private bool showTooltip = true;

        [Tooltip("Icon for the badge. Leave empty to use the built-in one for this button: " +
                 "Resources/ControllerTooltips/TooltipIcon_Pause or TooltipIcon_Help.")]
        [SerializeField] private Texture tooltipIcon;

        [Tooltip("Badge position relative to the button, in metres, in the hand's space: " +
                 "X outward (mirrored automatically for the left hand), Y up off the controller " +
                 "face, Z forward. Leave at zero for the default for this button.")]
        [SerializeField] private Vector3 tooltipOffset = Vector3.zero;

        [Tooltip("Badge diameter in metres. About 4 cm reads comfortably at arm's length without " +
                 "hiding the hand.")]
        [SerializeField] private float tooltipDiameter = 0.04f;

        [Tooltip("Colour of the line from badge to button. Matches the button tint by default so " +
                 "the badge, the line and the lit button read as one thing.")]
        [SerializeField] private Color tooltipLineColour = new Color(0.4f, 0.8f, 1f, 1f);

        [Tooltip("Line thickness in metres.")]
        [SerializeField] private float tooltipLineWidth = 0.002f;

        [Tooltip("Seconds for the badge to fade in.")]
        [SerializeField] private float tooltipFadeDuration = 0.5f;

        public enum Target { ActiveHand, Left, Right, Both }

        public void StartPulsing() => ForEach(h => h.StartPulsing());
        public void StopPulsing() => ForEach(h => h.StopPulsing());
        public void Flash() => ForEach(h => h.Flash());

        /// <summary>
        /// Marks this relay's button on BOTH controllers, whatever its Target is set to.
        ///
        /// Not an oversight, and it holds for both buttons this drives. Pause and Request
        /// Assistance are each bound to their button on both hands, and both controllers are
        /// listened to, so both buttons genuinely work - marking only the preferred one would be
        /// a lie. It also has to survive a participant switching hands in the settings an hour
        /// later, which a single-hand mark would not.
        /// </summary>
        public void HoldMarked()
        {
            Apply(ControllerHand.Left, h => h.HoldMarked());
            Apply(ControllerHand.Right, h => h.HoldMarked());
            if (showTooltip) ShowTooltip();
        }

        /// <summary>Removes the lasting mark from both controllers.</summary>
        public void ClearMark()
        {
            Apply(ControllerHand.Left, h => h.ClearMark());
            Apply(ControllerHand.Right, h => h.ClearMark());
            HideTooltip();
        }

        /// <summary>
        /// Shows this button's tooltip on both controllers. Both, for the same reason the mark is
        /// on both: the button works on either hand, and the label must survive a hand switch.
        /// </summary>
        public void ShowTooltip()
        {
            ControllerTooltip.Settings settings = new ControllerTooltip.Settings
            {
                icon = ResolveIcon(),
                offset = tooltipOffset == Vector3.zero ? ControllerTooltip.DefaultOffset(button) : tooltipOffset,
                diameter = tooltipDiameter,
                lineColour = tooltipLineColour,
                lineWidth = tooltipLineWidth,
                fadeDuration = tooltipFadeDuration
            };

            bool left = ControllerTooltip.Show(ControllerHand.Left, button, settings);
            bool right = ControllerTooltip.Show(ControllerHand.Right, button, settings);

            if (!left || !right)
                Debug.LogWarning($"[ControllerHighlightRelay] Could not show the {button} tooltip on " +
                                 $"{(!left && !right ? "either controller" : !left ? "the left controller" : "the right controller")}. " +
                                 "Each controller needs a ControllerButtonHighlight for this button " +
                                 "(they are on the rig in Bootstrap). Is Bootstrap loaded?", this);
        }

        /// <summary>Removes this button's tooltip from both controllers.</summary>
        public void HideTooltip()
        {
            ControllerTooltip.Hide(ControllerHand.Left, button);
            ControllerTooltip.Hide(ControllerHand.Right, button);
        }

        /// <summary>
        /// The tooltips hang off the rig in Bootstrap, which outlives this scene. Taking them down
        /// with the relay is what limits them to the rest of the tutorial rather than the whole
        /// session. The button tint is deliberately left as it was.
        /// </summary>
        private void OnDestroy()
        {
            HideTooltip();
        }

        private Texture ResolveIcon()
        {
            if (tooltipIcon != null) return tooltipIcon;

            string name = button == ControllerButton.Secondary ? "TooltipIcon_Pause" : "TooltipIcon_Help";
            Texture2D loaded = Resources.Load<Texture2D>("ControllerTooltips/" + name);
            if (loaded == null)
                Debug.LogWarning($"[ControllerHighlightRelay] No tooltip icon assigned and " +
                                 $"Resources/ControllerTooltips/{name} was not found.", this);
            else
                loaded.wrapMode = TextureWrapMode.Clamp;
            return loaded;
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

        private void Apply(ControllerHand hand, System.Action<ControllerButtonHighlight> action)
        {
            ControllerButtonHighlight highlight = ControllerButtonHighlight.For(hand, button);
            if (highlight != null) action(highlight);
        }
    }
}
