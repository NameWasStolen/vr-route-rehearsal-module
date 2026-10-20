using UnityEngine;
using VRTutorial;

/// <summary>
/// Shows the tutorial's controller badges during a run: the pause icon with a line to B/Y, and
/// the question mark with a line to A/X, on both controllers, plus the lasting button tint.
///
/// Same look and placement as the tutorial (ControllerTooltip, and the icons in
/// Resources/ControllerTooltips), so what the participant learned carries straight into the run.
/// A badge is only shown for a button that actually does something in this scene: pause needs a
/// PauseController in RunSystem, help needs an AssistanceController. A badge on a dead button
/// would teach the wrong thing.
///
/// Each controller is handled on its own and checked for the whole run: a controller that is
/// asleep or untracked when the run loads gets its badges as soon as it wakes, and never holds up
/// the other one.
///
/// RunSystemController adds one of these automatically if the scene has none, so there is no
/// setup. Add one by hand (anywhere in RunSystem) only to change the settings below.
/// The badges come down when RunSystem unloads; the tint stays, as it does after the tutorial.
/// </summary>
[DisallowMultipleComponent]
public class RunControllerTooltips : MonoBehaviour
{
    [Tooltip("Show the pause badge on B/Y (only if RunSystem has a PauseController).")]
    [SerializeField] private bool showPause = true;
    [Tooltip("Show the help badge on A/X (only if RunSystem has an AssistanceController).")]
    [SerializeField] private bool showHelp = true;
    [Tooltip("Also tint the buttons, as the tutorial leaves them.")]
    [SerializeField] private bool tintButtons = true;

    [Header("Look (defaults match the tutorial)")]
    [SerializeField] private float diameter = 0.04f;
    [SerializeField] private Color lineColour = new Color(0.4f, 0.8f, 1f, 1f);
    [SerializeField] private float lineWidth = 0.002f;
    [SerializeField] private float fadeDuration = 0.5f;

    [Tooltip("Seconds between checks for a controller whose badge is not showing yet.")]
    [SerializeField] private float checkInterval = 0.5f;

    private static readonly ControllerHand[] Hands = { ControllerHand.Left, ControllerHand.Right };

    private bool wantPause;
    private bool wantHelp;
    private bool anyShown;
    private float nextCheck;
    private readonly bool[] loggedWaiting = new bool[4];

    private void Start()
    {
        wantPause = showPause && ExistsInScene<PauseController>();
        wantHelp = showHelp && ExistsInScene<AssistanceController>();

        if (showPause && !wantPause)
            Debug.Log("[RunControllerTooltips] No PauseController in RunSystem yet, so no pause badge. " +
                      "Run Tools > VR Full Route > Add Help and Pause Menu to RunSystem.", this);
        if (showHelp && !wantHelp)
            Debug.Log("[RunControllerTooltips] No AssistanceController in RunSystem, so no help badge. " +
                      "Run Tools > VR Full Route > Add Help and Pause Menu to RunSystem.", this);
        if (wantPause || wantHelp)
            Debug.Log($"[RunControllerTooltips] Badges wanted: pause={wantPause}, help={wantHelp}.", this);
    }

    private void Update()
    {
        if ((!wantPause && !wantHelp) || Time.unscaledTime < nextCheck)
            return;
        nextCheck = Time.unscaledTime + checkInterval;

        foreach (ControllerHand hand in Hands)
        {
            if (wantPause) Ensure(hand, ControllerButton.Secondary);
            if (wantHelp) Ensure(hand, ControllerButton.Primary);
        }
    }

    /// <summary>Shows one badge on one controller if it is not already showing.</summary>
    private void Ensure(ControllerHand hand, ControllerButton button)
    {
        if (ControllerTooltip.IsShowing(hand, button))
            return;

        ControllerButtonHighlight highlight = ControllerButtonHighlight.For(hand, button);
        if (highlight == null)
        {
            int slot = ((int)hand << 1) | (int)button;
            if (!loggedWaiting[slot])
            {
                loggedWaiting[slot] = true;
                Debug.Log($"[RunControllerTooltips] Waiting for the {hand} controller's {button} button " +
                          "highlight (controller asleep or not tracked yet?).", this);
            }
            return;
        }

        if (tintButtons)
            highlight.HoldMarked();

        var settings = new ControllerTooltip.Settings
        {
            icon = LoadIcon(button),
            offset = ControllerTooltip.DefaultOffset(button),
            diameter = diameter,
            lineColour = lineColour,
            lineWidth = lineWidth,
            fadeDuration = fadeDuration,
        };
        if (ControllerTooltip.Show(hand, button, settings))
        {
            anyShown = true;
            Debug.Log($"[RunControllerTooltips] {button} badge shown on the {hand} controller.", this);
        }
    }

    private Texture LoadIcon(ControllerButton button)
    {
        string file = button == ControllerButton.Secondary ? "TooltipIcon_Pause" : "TooltipIcon_Help";
        Texture2D icon = Resources.Load<Texture2D>("ControllerTooltips/" + file);
        if (icon == null)
            Debug.LogWarning($"[RunControllerTooltips] Resources/ControllerTooltips/{file} not found.", this);
        else
            icon.wrapMode = TextureWrapMode.Clamp;
        return icon;
    }

    private bool ExistsInScene<T>() where T : Component
    {
        foreach (T c in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.gameObject.scene == gameObject.scene)
                return true;
        return false;
    }

    private void OnDestroy()
    {
        // The badges hang off the rig in Bootstrap, which outlives the run. Take them down with
        // RunSystem so they do not follow the participant back to the main menu.
        if (!anyShown)
            return;
        foreach (ControllerHand hand in Hands)
        {
            if (wantPause) ControllerTooltip.Hide(hand, ControllerButton.Secondary);
            if (wantHelp) ControllerTooltip.Hide(hand, ControllerButton.Primary);
        }
    }
}
