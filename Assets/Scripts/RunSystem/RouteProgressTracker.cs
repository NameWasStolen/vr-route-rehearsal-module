using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Follows the participant along the full route during a run and works out what they did:
/// how far along the route they are, whether they are on it, each detour into a side street,
/// time and head-scanning at each decision point, road crossings, distance walked.
///
/// Reads the route layout from RouteDefinition (baked by the editor), so nothing here is tied to
/// coordinates. Guided and Unguided runs are measured identically; Guided only adds the line.
///
/// Rules agreed with Kade (30 Sep 2026):
///   - WRONG TURNS are counted by the WrongTurn_ triggers, which sit about 3 m past the footpath
///     into each side street - one per crossing into the street (TriggerController). This
///     tracker listens to them and measures the detour: how deep, how long, how far walked.
///   - ON THE ROUTE means anywhere within the route's street (either footpath, the road, the
///     nature strips), from the bus stop to the end zone. Crossing to the other footpath is not
///     leaving the route; road crossings are recorded separately.
///   - A DETOUR ENDS when the participant is back on the route street - not merely back across
///     the trigger, so hovering at the mouth of the side street still counts as off route.
///
/// Everything is written to the session log as it happens, and summarised at the end of the run
/// (Console + a route_summary event). The run CSVs pick these values up in step 5.
/// </summary>
[DisallowMultipleComponent]
public class RouteProgressTracker : MonoBehaviour
{
    [Tooltip("Checks per second. Position, surface and zones are sampled at this rate.")]
    [SerializeField, Min(1f)] private float checksPerSecond = 10f;

    [Tooltip("Movement under this per check is treated as head jitter, not walking (metres).")]
    [SerializeField, Min(0f)] private float jitterMetres = 0.02f;

    [Tooltip("A stretch on the road counts as a crossing once this much of it has been walked (metres).")]
    [SerializeField, Min(0f)] private float minCrossingMetres = 1.5f;

    [Tooltip("A road crossing within this distance of a route node counts as at a junction, not mid-block (metres).")]
    [SerializeField, Min(0f)] private float junctionRadius = 10f;

    [Tooltip("Surfaces are probed downward from the head, up to this far (metres).")]
    [SerializeField, Min(0.5f)] private float probeDistance = 3f;

    // ------------------------------------------------------------------ records
    /// <summary>One detour into a side street.</summary>
    public class Excursion
    {
        public string Branch;          // trigger name, e.g. WrongTurn_Side8_E
        public string Node;            // e.g. N8
        public bool AtDecision;        // the node is one of the six decision points
        public float StartTime;        // seconds since the run started
        public float EndTime = -1f;
        public float Depth;            // furthest distance down the side street from its mouth (m)
        public float Walked;           // distance walked during the detour (m)
        public int HelpRequests;       // help requests placed during it
        public string EndedBy = "";    // returned / returned_after_help / run_ended
        public float Duration => (EndTime >= 0f ? EndTime : StartTime) - StartTime;
    }

    /// <summary>Time and head-scanning in one decision zone, over the whole run.</summary>
    public class DecisionStats
    {
        public string Zone;
        public int Visits;
        public float FirstEntry = -1f;
        public float TotalTime;
        public float MaxScanDegrees;   // widest left-right head sweep on any one visit
    }

    /// <summary>One stretch walked on the road.</summary>
    public class Crossing
    {
        public float StartTime;
        public float Duration;
        public float Walked;
        public string Kind;            // zebra / junction / midblock
        public string Where;           // zebra name or nearest node
    }

    public event Action<Excursion> ExcursionStarted;
    public event Action<Excursion> ExcursionEnded;
    public event Action<string> DecisionZoneEntered;
    public event Action<DecisionStats, float, float> DecisionZoneExited;   // stats, visit seconds, visit scan degrees
    public event Action<Crossing> RoadCrossed;

    // ------------------------------------------------------------------ live state
    public bool IsTracking { get; private set; }
    public RouteDefinition Route { get; private set; }

    /// <summary>Distance along the ideal walking line (m).</summary>
    public float Progress { get; private set; }
    /// <summary>Horizontal distance from the walking line (m).</summary>
    public float Lateral { get; private set; }
    public bool OnRoute { get; private set; } = true;
    /// <summary>Decision zone or zebra the participant is in, or null.</summary>
    public string Zone { get; private set; }
    public string NearestNode { get; private set; }
    public SurfaceKind? Surface { get; private set; }
    public Excursion CurrentExcursion { get; private set; }

    // ------------------------------------------------------------------ run totals
    public float DistanceWalked { get; private set; }
    public float MaxProgress { get; private set; }
    public float Backtracked { get; private set; }
    public float OffRouteTime { get; private set; }
    public float OffRouteDistance { get; private set; }
    public float RoadTime { get; private set; }
    public IReadOnlyList<Excursion> Excursions => excursions;
    public IReadOnlyList<Crossing> Crossings => crossings;
    public IReadOnlyDictionary<string, DecisionStats> Decisions => decisions;

    private readonly List<Excursion> excursions = new List<Excursion>();
    private readonly List<Crossing> crossings = new List<Crossing>();
    private readonly Dictionary<string, DecisionStats> decisions = new Dictionary<string, DecisionStats>();
    private readonly List<WrongTurnController> wrongTurnSources = new List<WrongTurnController>();

    private Transform head;
    private float runStart;
    private float nextCheck;
    private Vector2 lastPos;
    private float lastProgress;

    // decision zone visit
    private string zoneVisit;
    private float zoneVisitStart;
    private float yawAccum, yawMin, yawMax, lastYaw;

    // road crossing
    private Crossing crossingNow;
    private bool crossingTouchedZebra;
    private string crossingZebra;

    // ------------------------------------------------------------------ control
    /// <summary>Starts tracking. Called by RunSystemController when the run timer starts.</summary>
    public void Begin(Transform playerHead)
    {
        Route = RouteDefinition.Find(gameObject);
        if (Route == null)
        {
            Debug.LogWarning("[RouteProgressTracker] No RouteDefinition in the scene, so there is no route to " +
                             "measure against. Run Tools > VR Full Route > Update Route Definition in RunSystem.", this);
            return;
        }
        if (playerHead == null) return;

        head = playerHead;
        runStart = Time.time;
        nextCheck = 0f;
        excursions.Clear();
        crossings.Clear();
        decisions.Clear();
        DistanceWalked = MaxProgress = Backtracked = OffRouteTime = OffRouteDistance = RoadTime = 0f;
        CurrentExcursion = null;
        crossingNow = null;
        zoneVisit = null;

        lastPos = Horizontal(head.position);
        Progress = lastProgress = Route.Project(head.position, out float lateral);
        Lateral = lateral;
        MaxProgress = Progress;
        OnRoute = Route.OnRouteStreet(head.position);

        wrongTurnSources.Clear();
        foreach (WrongTurnController w in FindObjectsByType<WrongTurnController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (w.gameObject.scene != gameObject.scene) continue;
            w.WrongTurnRecorded += OnWrongTurn;
            wrongTurnSources.Add(w);
        }

        IsTracking = true;
        Debug.Log($"[RouteProgressTracker] Tracking the route: ideal walk {Route.OptimalLength:F0} m, " +
                  $"{Route.Branches.Count} side streets, {Route.DecisionZones.Count} decision zones.", this);
    }

    /// <summary>Stops tracking, closes anything still open and writes the summary.</summary>
    public void End(bool completed, float runSeconds)
    {
        if (!IsTracking) return;
        Check(force: true);

        float now = Elapsed;
        if (CurrentExcursion != null) FinishExcursion(now, "run_ended");
        if (zoneVisit != null) FinishZoneVisit(now);
        if (crossingNow != null) FinishCrossing(now);

        foreach (WrongTurnController w in wrongTurnSources)
            if (w != null) w.WrongTurnRecorded -= OnWrongTurn;
        wrongTurnSources.Clear();

        IsTracking = false;
        string summary = BuildSummary(completed, runSeconds);
        SessionLog.Record("route_summary", summary);
        Debug.Log("[RouteProgressTracker] Run summary: " + summary, this);
    }

    /// <summary>A help request was placed; counted against any detour in progress.</summary>
    public void NoteHelpRequest()
    {
        if (CurrentExcursion != null) CurrentExcursion.HelpRequests++;
    }

    private void OnDisable()
    {
        foreach (WrongTurnController w in wrongTurnSources)
            if (w != null) w.WrongTurnRecorded -= OnWrongTurn;
        wrongTurnSources.Clear();
        IsTracking = false;
    }

    private float Elapsed => Time.time - runStart;

    // ------------------------------------------------------------------ per check
    private void Update()
    {
        if (!IsTracking || head == null) return;
        if (Time.time < nextCheck) return;
        Check(force: false);
    }

    private void Check(bool force)
    {
        if (head == null || Route == null) return;
        float interval = 1f / Mathf.Max(1f, checksPerSecond);
        nextCheck = Time.time + interval;
        float now = Elapsed;

        // Movement.
        Vector2 pos = Horizontal(head.position);
        float step = Vector2.Distance(pos, lastPos);
        if (step >= jitterMetres || force)
        {
            DistanceWalked += step;
            if (!OnRoute) OffRouteDistance += step;
            if (CurrentExcursion != null) CurrentExcursion.Walked += step;
            if (crossingNow != null) crossingNow.Walked += step;
            lastPos = pos;
        }

        // Where on the route.
        Progress = Route.Project(head.position, out float lateral);
        Lateral = lateral;
        bool onRoute = Route.OnRouteStreet(head.position);
        if (onRoute)
        {
            if (Progress > MaxProgress) MaxProgress = Progress;
            float dp = Progress - lastProgress;
            if (dp < -jitterMetres) Backtracked += -dp;
            lastProgress = Progress;
        }
        else
        {
            OffRouteTime += interval;
            lastProgress = Progress;
        }
        OnRoute = onRoute;
        NearestNode = Route.NearestNode(head.position, out _)?.name;

        // Detour.
        if (CurrentExcursion != null)
        {
            RouteDefinition.Branch b = FindBranch(CurrentExcursion.Branch);
            if (b != null)
            {
                Vector3 d = head.position - Route.BranchMouth(b);
                Vector3 into = Route.BranchInto(b);
                float depth = d.x * into.x + d.z * into.z;
                if (depth > CurrentExcursion.Depth) CurrentExcursion.Depth = depth;
            }
            if (onRoute)
                FinishExcursion(now, CurrentExcursion.HelpRequests > 0 ? "returned_after_help" : "returned");
        }

        // Decision zones and zebras.
        string zone = Route.ZoneAt(head.position);
        Zone = zone;
        string decisionZone = zone != null && zone.StartsWith("CP_Decision_") ? zone : null;
        if (decisionZone != zoneVisit)
        {
            if (zoneVisit != null) FinishZoneVisit(now);
            if (decisionZone != null) StartZoneVisit(decisionZone, now);
        }
        else if (zoneVisit != null)
        {
            float yaw = HeadYaw();
            yawAccum += Mathf.DeltaAngle(lastYaw, yaw);
            lastYaw = yaw;
            if (yawAccum < yawMin) yawMin = yawAccum;
            if (yawAccum > yawMax) yawMax = yawAccum;
        }

        // Surface and road crossings.
        Surface = ProbeSurface();
        bool onRoad = Surface == SurfaceKind.Asphalt;
        if (onRoad)
        {
            RoadTime += interval;
            if (crossingNow == null)
            {
                crossingNow = new Crossing { StartTime = now };
                crossingTouchedZebra = false;
                crossingZebra = null;
            }
            if (zone != null && zone.StartsWith("Zebra_"))
            {
                crossingTouchedZebra = true;
                crossingZebra = zone;
            }
            else if (zone == null && !crossingTouchedZebra)
            {
                string zebra = Route.ZebraAt(head.position, 0.5f);
                if (zebra != null) { crossingTouchedZebra = true; crossingZebra = zebra; }
            }
        }
        else if (crossingNow != null)
        {
            FinishCrossing(now);
        }
    }

    // ------------------------------------------------------------------ detours
    private void OnWrongTurn(string triggerName)
    {
        if (!IsTracking) return;
        if (CurrentExcursion != null)
        {
            // Straight from one side street into another without coming back to the route
            // (possible where two leave the same junction): close the first, start the second.
            FinishExcursion(Elapsed, "into_another_street");
        }

        RouteDefinition.Branch b = FindBranch(triggerName);
        var e = new Excursion
        {
            Branch = triggerName,
            Node = b != null ? b.node : "",
            AtDecision = b != null && b.atDecision,
            StartTime = Elapsed,
        };
        CurrentExcursion = e;
        excursions.Add(e);

        SessionLog.Record("wrong_turn_start",
            $"{triggerName} at {e.Node} ({(e.AtDecision ? "decision point" : "straight-on junction")})");
        ExcursionStarted?.Invoke(e);
    }

    private void FinishExcursion(float now, string endedBy)
    {
        Excursion e = CurrentExcursion;
        if (e == null) return;
        e.EndTime = now;
        e.EndedBy = endedBy;
        CurrentExcursion = null;
        SessionLog.Record("wrong_turn_end", string.Format(CultureInfo.InvariantCulture,
            "{0}: {1:F1} s, {2:F1} m walked, {3:F1} m deep, {4}", e.Branch, e.Duration, e.Walked, e.Depth, e.EndedBy));
        ExcursionEnded?.Invoke(e);
    }

    private RouteDefinition.Branch FindBranch(string name)
    {
        if (Route == null || string.IsNullOrEmpty(name)) return null;
        foreach (RouteDefinition.Branch b in Route.Branches)
            if (b.name == name) return b;
        return null;
    }

    // ------------------------------------------------------------------ decision zones
    private void StartZoneVisit(string zone, float now)
    {
        zoneVisit = zone;
        zoneVisitStart = now;
        yawAccum = yawMin = yawMax = 0f;
        lastYaw = HeadYaw();

        if (!decisions.TryGetValue(zone, out DecisionStats s))
        {
            s = new DecisionStats { Zone = zone };
            decisions[zone] = s;
        }
        s.Visits++;
        if (s.FirstEntry < 0f) s.FirstEntry = now;
        SessionLog.Record("decision_enter", zone);
        DecisionZoneEntered?.Invoke(zone);
    }

    private void FinishZoneVisit(float now)
    {
        string zone = zoneVisit;
        zoneVisit = null;
        if (zone == null || !decisions.TryGetValue(zone, out DecisionStats s)) return;
        float seconds = now - zoneVisitStart;
        float scan = yawMax - yawMin;
        s.TotalTime += seconds;
        if (scan > s.MaxScanDegrees) s.MaxScanDegrees = scan;
        SessionLog.Record("decision_exit", string.Format(CultureInfo.InvariantCulture,
            "{0}: {1:F1} s, head scan {2:F0} deg", zone, seconds, scan));
        DecisionZoneExited?.Invoke(s, seconds, scan);
    }

    private float HeadYaw()
    {
        Vector3 f = head.forward;
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    // ------------------------------------------------------------------ road crossings
    private void FinishCrossing(float now)
    {
        Crossing c = crossingNow;
        crossingNow = null;
        if (c == null) return;
        c.Duration = now - c.StartTime;
        if (c.Walked < minCrossingMetres) return;   // a step off the kerb and back

        if (crossingTouchedZebra)
        {
            c.Kind = "zebra";
            c.Where = crossingZebra;
        }
        else
        {
            RouteDefinition.Node n = Route.NearestNode(head.position, out float d);
            c.Kind = d <= junctionRadius ? "junction" : "midblock";
            c.Where = n != null ? n.name : "";
        }
        crossings.Add(c);
        SessionLog.Record("road_crossing", string.Format(CultureInfo.InvariantCulture,
            "{0} {1}: {2:F1} m, {3:F1} s", c.Kind, c.Where, c.Walked, c.Duration));
        RoadCrossed?.Invoke(c);
    }

    private SurfaceKind? ProbeSurface()
    {
        Vector3 origin = head.position;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, probeDistance,
                                               Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        SurfaceKind? kind = null;
        foreach (RaycastHit h in hits)
        {
            if (h.distance >= best) continue;
            if (h.collider.CompareTag("Player") || h.collider.CompareTag("MainCamera")) continue;
            var marker = h.collider.GetComponentInParent<SurfaceMarker>();
            if (marker == null) continue;
            best = h.distance;
            kind = marker.Kind;
        }
        return kind;
    }

    // ------------------------------------------------------------------ summary
    /// <summary>The run's route measures as "key=value; ..." - logged at the end of the run.</summary>
    public string BuildSummary(bool completed, float runSeconds)
    {
        var inv = CultureInfo.InvariantCulture;
        int atDecision = 0, straightOn = 0, selfCorrected = 0;
        foreach (Excursion e in excursions)
        {
            if (e.AtDecision) atDecision++; else straightOn++;
            if (e.EndedBy == "returned") selfCorrected++;
        }
        int zebra = 0, junction = 0, midblock = 0;
        bool crossedAtRouteZebra = false;
        foreach (Crossing c in crossings)
        {
            if (c.Kind == "zebra") { zebra++; if (c.Where == "Zebra_RouteCrossing") crossedAtRouteZebra = true; }
            else if (c.Kind == "junction") junction++;
            else midblock++;
        }

        float optimal = Route != null ? Route.OptimalLength : 0f;
        var sb = new StringBuilder();
        Action<string, string> Add = (k, v) =>
        {
            if (sb.Length > 0) sb.Append("; ");
            sb.Append(k).Append('=').Append(v);
        };
        Add("completed", completed ? "1" : "0");
        Add("time_s", runSeconds.ToString("F1", inv));
        Add("optimal_m", optimal.ToString("F0", inv));
        Add("walked_m", DistanceWalked.ToString("F1", inv));
        Add("efficiency", DistanceWalked > 0.1f ? (optimal / DistanceWalked).ToString("F2", inv) : "");
        Add("max_progress_m", (MaxProgress - (Route != null ? Route.StartDistance : 0f)).ToString("F0", inv));
        Add("backtracked_m", Backtracked.ToString("F1", inv));
        Add("wrong_turns", excursions.Count.ToString(inv));
        Add("at_decisions", atDecision.ToString(inv));
        Add("straight_on", straightOn.ToString(inv));
        Add("self_corrected", selfCorrected.ToString(inv));
        Add("off_route_s", OffRouteTime.ToString("F1", inv));
        Add("off_route_m", OffRouteDistance.ToString("F1", inv));
        Add("road_s", RoadTime.ToString("F1", inv));
        Add("crossings_zebra", zebra.ToString(inv));
        Add("crossings_junction", junction.ToString(inv));
        Add("crossings_midblock", midblock.ToString(inv));
        Add("crossed_at_route_zebra", crossedAtRouteZebra ? "1" : "0");
        foreach (KeyValuePair<string, DecisionStats> kv in decisions)
        {
            string n = kv.Key.Replace("CP_Decision_", "");
            Add(n + "_s", kv.Value.TotalTime.ToString("F1", inv));
            Add(n + "_scan_deg", kv.Value.MaxScanDegrees.ToString("F0", inv));
        }
        return sb.ToString();
    }

    private static Vector2 Horizontal(Vector3 p) => new Vector2(p.x, p.z);
}
