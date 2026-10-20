using System.Collections.Generic;
using UnityEngine;
using VRTutorial;

/// <summary>
/// What makes a Guided run different from an Unguided one. Added and driven by
/// RunSystemController; no scene setup beyond the guide line itself
/// (Tools > VR Full Route > Add Guide Line to RunSystem).
///
/// GUIDED (decided with Kade, 30 Sep 2026):
///   - The blue route line from the tutorial, in its on-request form, following the full route's
///     ideal walking line (RouteDefinition). Off that line it takes the shortest walk over the
///     footpath network (FootpathNetwork): along the footpath they are on, crossing the main road
///     only at a zebra.
///   - A quick TAP of A/X draws it for 8 s (holding still calls the researcher, unchanged).
///   - A WRONG TURN - the participant crossing a side street's trigger, the same point where it
///     is counted in both modes - buzzes both controllers, shows the "Turn around" signal in
///     front of them, and draws the line back. The line stays until they are back on the route
///     street, then lingers and retracts. The signal goes after 6 s or once they are back.
///
/// UNGUIDED: nothing is ever shown. A tap is still recorded (guide_request_unanswered in the
/// session log, and in the guide_requests CSV columns), as a marker of uncertainty.
///
/// Wrong turns are detected by RouteProgressTracker in both modes, so the counts are comparable;
/// Guided only adds the response.
///
/// STATISTICS: taps -> PlayerPositionTracker.RecordGuideRequest; the line being on screen ->
/// PlayerPositionTracker.GuideVisible; session log guide_shown (with the reason: request or
/// wrong_turn) and guide_hidden.
/// </summary>
[DisallowMultipleComponent]
public class RunGuidance : MonoBehaviour
{
    [Tooltip("Metres of line drawn ahead of the participant. The full route is about 305 m; " +
             "drawing all of it every frame is needlessly expensive on Quest.")]
    [SerializeField, Min(10f)] private float drawAhead = 60f;

    private RouteGuideLine guide;
    private TurnAroundSignal signal;
    private AssistanceController assistance;
    private RouteProgressTracker tracker;
    private PlayerPositionTracker positions;
    private bool guided;
    private bool active;

    public bool IsGuided => guided;

    /// <summary>
    /// Times the guide appeared this run (the line drawn on a tap or after a wrong turn). The
    /// post-run survey asks how helpful the blue line was only if this is above 0.
    /// </summary>
    public int TimesShown { get; private set; }

    /// <summary>Called by RunSystemController as the run loads.</summary>
    public void Configure(string runType, AssistanceController help, RouteProgressTracker routeTracker,
                          PlayerPositionTracker positionTracker)
    {
        End();
        TimesShown = 0;
        guided = string.Equals(runType, "guided", System.StringComparison.OrdinalIgnoreCase);
        assistance = help;
        tracker = routeTracker;
        positions = positionTracker;
        guide = FindInScene<RouteGuideLine>();

        if (assistance != null) assistance.onTapped.AddListener(OnTap);
        else Debug.LogWarning("[RunGuidance] No help button in this run, so taps cannot be read.", this);

        if (!guided)
        {
            // Unguided: the line must never appear, whatever else happens.
            if (guide != null) guide.Hide();
            active = true;
            return;
        }

        if (guide == null)
        {
            Debug.LogWarning("[RunGuidance] Guided run, but RunSystem has no RouteGuideLine, so there is no line " +
                             "to show. Run Tools > VR Full Route > Add Guide Line to RunSystem.", this);
        }
        else
        {
            RouteDefinition route = RouteDefinition.Find(gameObject);
            if (route == null || route.PointCount < 2)
            {
                Debug.LogWarning("[RunGuidance] No RouteDefinition to draw the line along. Run Tools > VR Full " +
                                 "Route > Update Route Definition in RunSystem.", this);
                guide = null;
            }
            else
            {
                var pts = new Vector2[route.PointCount];
                for (int i = 0; i < pts.Length; i++)
                {
                    Vector3 w = route.WalkingPoint(i);
                    pts[i] = new Vector2(w.x, w.z);
                }
                guide.SetRoute(pts);
                if (route.HasFootpathNetwork)
                    guide.SetPathSource(new FootpathNetwork(route));
                else
                {
                    guide.SetPathSource(null);
                    Debug.LogWarning("[RunGuidance] The route has no footpath network yet, so off the ideal line the guide " +
                                     "may draw straight across a road. Run Tools > VR Full Route > Update Route Definition " +
                                     "in RunSystem.", this);
                }
                guide.MaxDrawLength = drawAhead;
                guide.RequestLogEvent = "guide_requested";
                guide.Shown += OnGuideShown;
                guide.Hidden += OnGuideHidden;
                guide.Arm();
            }
        }

        if (signal == null)
            signal = TurnAroundSignal.Create(gameObject, FindTemplatePanel());

        if (tracker != null)
        {
            tracker.ExcursionStarted += OnDetourStarted;
            tracker.ExcursionEnded += OnDetourEnded;
        }
        active = true;
        Debug.Log($"[RunGuidance] Guided run: line {(guide != null ? "armed" : "missing")}, " +
                  "tap A/X for the way, wrong turns show 'Turn around'.", this);
    }

    /// <summary>Called by RunSystemController when the run ends or is left.</summary>
    public void End()
    {
        if (!active) return;
        active = false;
        if (assistance != null) assistance.onTapped.RemoveListener(OnTap);
        if (tracker != null)
        {
            tracker.ExcursionStarted -= OnDetourStarted;
            tracker.ExcursionEnded -= OnDetourEnded;
        }
        if (guide != null)
        {
            guide.Shown -= OnGuideShown;
            guide.Hidden -= OnGuideHidden;
            guide.Hide();
        }
        if (signal != null) signal.Hide();
        if (positions != null) positions.GuideVisible = false;
    }

    private void OnDestroy() => End();

    // ------------------------------------------------------------------ events
    private void OnTap()
    {
        positions?.RecordGuideRequest();
        if (guided)
        {
            if (guide != null) guide.RequestShow();         // logs guide_requested
        }
        else
        {
            SessionLog.Record("guide_request_unanswered", Place());
        }
    }

    private void OnDetourStarted(RouteProgressTracker.Excursion e)
    {
        if (!guided) return;
        if (guide != null) guide.BeginCorrection(e.Branch);
        else TimesShown++;      // no line in the scene: the Turn around sign is the guide
        if (signal != null) signal.Show();
    }

    private void OnDetourEnded(RouteProgressTracker.Excursion e)
    {
        if (!guided) return;
        if (e.EndedBy == "into_another_street") return;     // a new detour starts straight away
        if (guide != null) guide.EndCorrection();
        if (signal != null) signal.Hide();
    }

    private void OnGuideShown(string reason)
    {
        TimesShown++;
        if (positions != null) positions.GuideVisible = true;
        SessionLog.Record("guide_shown", reason);
    }

    private void OnGuideHidden()
    {
        if (positions != null) positions.GuideVisible = false;
        SessionLog.Record("guide_hidden");
    }

    // ------------------------------------------------------------------ helpers
    private string Place()
    {
        if (tracker == null || !tracker.IsTracking) return "before start";
        if (!string.IsNullOrEmpty(tracker.Zone)) return tracker.Zone;
        if (tracker.CurrentExcursion != null) return "in " + tracker.CurrentExcursion.Branch;
        return (tracker.OnRoute ? "near " : "off route near ") + tracker.NearestNode;
    }

    /// <summary>The run's help panel (RunHelp), to copy head-locked placement and look from.</summary>
    private HeadLockedUI FindTemplatePanel()
    {
        HeadLockedUI any = null;
        foreach (HeadLockedUI h in FindObjectsByType<HeadLockedUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (h.gameObject.scene != gameObject.scene) continue;
            if (h.gameObject.name.Contains("Requested")) return h;
            if (any == null) any = h;
        }
        return any;
    }

    private T FindInScene<T>() where T : Component
    {
        foreach (T c in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.gameObject.scene == gameObject.scene) return c;
        return null;
    }
}
