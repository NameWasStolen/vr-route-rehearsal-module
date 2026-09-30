using System;
using System.Collections.Generic;
using UnityEngine;
using VRTutorial;

/// <summary>One row of the per-sample run CSV.</summary>
public struct PlayerPositionSample
{
    public float ElapsedTime;
    public Vector3 Position;
    /// <summary>Head yaw, 0-360 degrees, world space.</summary>
    public float Yaw;

    // From RouteProgressTracker; HasRoute is false when there is no baked route.
    public bool HasRoute;
    /// <summary>Distance along the ideal walking line from the start trigger (m). Negative before it.</summary>
    public float Progress;
    /// <summary>Horizontal distance from the walking line (m).</summary>
    public float Lateral;
    public bool OnRoute;
    public string NearestNode;
    /// <summary>Decision zone or zebra, or empty.</summary>
    public string Zone;
    /// <summary>Paving / Grass / Asphalt, or empty.</summary>
    public string Surface;
    /// <summary>The side street of the detour in progress (its WrongTurn_ trigger), or empty.</summary>
    public string Detour;

    /// <summary>1 if the participant was hesitating (standing still, see below) at any point in this sample.</summary>
    public int Stationary;
    /// <summary>1 if the pause menu was open at any point in this sample.</summary>
    public int MenuPaused;
    /// <summary>1 if a help request was confirmed and waiting for the researcher at any point in this sample.</summary>
    public int AwaitingHelp;
    /// <summary>1 if the Guided route line was showing at any point in this sample (step 6; 0 until then).</summary>
    public int GuideVisible;

    /// <summary>Number of help requests placed during this sample.</summary>
    public int Assistance;
    /// <summary>Number of wrong turns taken during this sample.</summary>
    public int Error;
    /// <summary>Number of taps asking for the route line during this sample (step 6; 0 until then).</summary>
    public int GuideRequests;
}

/// <summary>
/// Samples the participant during a run (every 0.2 s) and works out when they hesitate.
///
/// HESITATION ("stationary"): horizontal head speed below stationarySpeed (0.2 m/s) for at
/// least stationarySeconds (3 s). Measured every frame on x/z only, and smoothed, so looking
/// around, leaning or bobbing the head does not break up a real stop. Time in the pause menu or
/// with a help request open (holding the button, or waiting for the researcher) never counts:
/// locomotion is suspended then, so standing still is not a choice.
///
/// EVENTS (assistance, error, guide requests) are counted, not flagged, so two in one sample are
/// two. Route columns come from RouteProgressTracker when there is one.
///
/// Also keeps the run totals the summary needs that the route tracker does not: hesitations,
/// pause-menu opens and time, time waiting for help, guide requests and time the line was up.
/// </summary>
public class PlayerPositionTracker : MonoBehaviour
{
    // Renamed from sampleInterval (1 s) on purpose, without FormerlySerializedAs: the old value
    // saved in RunSystem is dropped and the new 0.2 s default applies.
    [Tooltip("Seconds between rows in the run CSV.")]
    [SerializeField, Min(0.02f)] private float sampleIntervalSeconds = 0.2f;

    [Tooltip("Write every sample to the Console as well (5 lines a second - for debugging only).")]
    [SerializeField] private bool logEachSample = false;

    [Header("Hesitation")]
    [Tooltip("Below this horizontal speed (m/s) the participant counts as standing still.")]
    [SerializeField, Min(0.01f)] private float stationarySpeed = 0.2f;
    [Tooltip("Standing still for at least this long (s) is a hesitation.")]
    [SerializeField, Min(0.1f)] private float stationarySeconds = 3f;
    [Tooltip("Smoothing time (s) for the speed, so head jitter does not read as walking.")]
    [SerializeField, Min(0.05f)] private float speedSmoothing = 0.5f;

    private readonly List<PlayerPositionSample> samples = new();
    private Transform playerTransform;
    private float runStartTime;
    private float nextSampleTime;

    // Hesitation state, per frame.
    private Vector2 lastHorizontal;
    private float smoothedSpeed;
    private float stillTime;
    private bool isStationary;
    private float hesitationStartTime;
    private bool wasMenuPaused;

    // Accumulated since the last sample.
    private bool stationaryThisSample;
    private bool menuPausedThisSample;
    private bool awaitingHelpThisSample;
    private bool guideVisibleThisSample;
    private int assistanceThisSample;
    private int errorsThisSample;
    private int guideRequestsThisSample;

    public bool IsTracking { get; private set; }

    /// <summary>Optional. When set, samples carry route columns and hesitations say where they happened.</summary>
    public RouteProgressTracker RouteTracker { get; set; }

    /// <summary>Set by the Guided route line (step 6) while it is showing.</summary>
    public bool GuideVisible { get; set; }

    public IReadOnlyList<PlayerPositionSample> Samples => samples;
    public RunSettingsSnapshot Settings { get; private set; }
    public string RunType { get; private set; }
    public string ParticipantId { get; private set; }
    public int RunIndex { get; private set; }

    // ------------------------------------------------------------------ run totals
    public int HesitationCount { get; private set; }
    public float HesitationSeconds { get; private set; }
    public int HesitationsAtDecisions { get; private set; }
    public int AssistanceCount { get; private set; }
    public int WrongTurnCount { get; private set; }
    public int PauseMenuOpens { get; private set; }
    public float PauseMenuSeconds { get; private set; }
    public float AwaitingHelpSeconds { get; private set; }
    public int GuideRequestCount { get; private set; }
    public float GuideVisibleSeconds { get; private set; }

    public void StartTracking(
        Transform player,
        RunSettingsSnapshot settings,
        string selectedRunType,
        string participantId,
        int runIndex)
    {
        if (player == null)
        {
            Debug.LogError("PlayerPositionTracker cannot track a null transform.", this);
            return;
        }

        playerTransform = player;
        Settings = settings;
        RunType = selectedRunType;
        ParticipantId = participantId;
        RunIndex = runIndex;
        samples.Clear();

        lastHorizontal = Horizontal(player.position);
        smoothedSpeed = 0f;
        stillTime = 0f;
        isStationary = false;
        wasMenuPaused = false;
        GuideVisible = false;
        ResetSampleAccumulators();
        HesitationCount = HesitationsAtDecisions = AssistanceCount = WrongTurnCount = 0;
        PauseMenuOpens = GuideRequestCount = 0;
        HesitationSeconds = PauseMenuSeconds = AwaitingHelpSeconds = GuideVisibleSeconds = 0f;

        runStartTime = Time.time;
        IsTracking = true;
        CaptureSample();
        nextSampleTime = Time.time + sampleIntervalSeconds;
    }

    /// <summary>Stops sampling and writes the run CSV. Returns its path, or null if nothing was written.</summary>
    /// <param name="completed">False when the run was abandoned from the pause menu; the file
    /// name then says "_incomplete" so it is not mistaken for a finished run.</param>
    public string StopTracking(bool completed = true)
    {
        if (!IsTracking)
            return null;

        if (isStationary)
            EndHesitation();

        CaptureSample();
        IsTracking = false;
        string path = RunCsvLogger.Write(samples, Settings, RunType, ParticipantId, RunIndex, completed);
        Debug.Log(
            $"Player position tracking ended with {samples.Count} samples: " +
            $"{WrongTurnCount} wrong turn(s), {AssistanceCount} help request(s), " +
            $"{HesitationCount} hesitation(s) totalling {HesitationSeconds:F1}s.",
            this);
        return path;
    }

    public void RecordAssistance()
    {
        if (!IsTracking)
            return;
        assistanceThisSample++;
        AssistanceCount++;
    }

    public void RecordError()
    {
        if (!IsTracking)
            return;
        errorsThisSample++;
        WrongTurnCount++;
    }

    /// <summary>A tap asking for the route line (Guided, step 6).</summary>
    public void RecordGuideRequest()
    {
        if (!IsTracking)
            return;
        guideRequestsThisSample++;
        GuideRequestCount++;
    }

    private void Update()
    {
        if (!IsTracking || playerTransform == null)
            return;

        float dt = Time.deltaTime;
        UpdateStates(dt);
        UpdateHesitation(dt);

        if (Time.time >= nextSampleTime)
        {
            CaptureSample();
            nextSampleTime += sampleIntervalSeconds;
            if (nextSampleTime <= Time.time)                  // after a hitch, don't fire a burst
                nextSampleTime = Time.time + sampleIntervalSeconds;
        }
    }

    /// <summary>Pause menu, help and guide line: per-sample flags and run totals.</summary>
    private void UpdateStates(float dt)
    {
        bool awaitingHelp = AssistanceRequest.State == AssistanceState.Requested;
        bool helpOpen = AssistanceRequest.IsActive;       // holding the button, or waiting
        bool menuPaused = TutorialPause.IsPaused && !helpOpen;

        if (menuPaused && !wasMenuPaused)
            PauseMenuOpens++;
        wasMenuPaused = menuPaused;

        if (menuPaused) PauseMenuSeconds += dt;
        if (awaitingHelp) AwaitingHelpSeconds += dt;
        if (GuideVisible) GuideVisibleSeconds += dt;

        menuPausedThisSample |= menuPaused;
        awaitingHelpThisSample |= awaitingHelp;
        guideVisibleThisSample |= GuideVisible;
    }

    private void UpdateHesitation(float dt)
    {
        Vector2 now = Horizontal(playerTransform.position);
        if (dt > 0f)
        {
            float speed = Vector2.Distance(now, lastHorizontal) / dt;
            float k = 1f - Mathf.Exp(-dt / speedSmoothing);
            smoothedSpeed += (speed - smoothedSpeed) * k;
        }
        lastHorizontal = now;

        if (TutorialPause.IsPaused || AssistanceRequest.IsActive)
        {
            // Not a choice to stand still: start counting afresh once they can move again.
            if (isStationary)
                EndHesitation();
            stillTime = 0f;
            return;
        }

        if (smoothedSpeed < stationarySpeed)
        {
            stillTime += dt;
            if (!isStationary && stillTime >= stationarySeconds)
            {
                isStationary = true;
                HesitationCount++;
                if (InDecisionZone())
                    HesitationsAtDecisions++;
                hesitationStartTime = Time.time - stillTime;
                SessionLog.Record("hesitation_start", HesitationPlace());
            }
        }
        else
        {
            if (isStationary)
                EndHesitation();
            stillTime = 0f;
        }

        stationaryThisSample |= isStationary;
    }

    private bool InDecisionZone()
    {
        return RouteTracker != null && RouteTracker.IsTracking &&
               RouteTracker.Zone != null && RouteTracker.Zone.StartsWith("CP_Decision_");
    }

    private string HesitationPlace()
    {
        if (RouteTracker == null || !RouteTracker.IsTracking) return null;
        if (!string.IsNullOrEmpty(RouteTracker.Zone)) return RouteTracker.Zone;
        return RouteTracker.OnRoute ? "near " + RouteTracker.NearestNode : "off route near " + RouteTracker.NearestNode;
    }

    private void EndHesitation()
    {
        float duration = Time.time - hesitationStartTime;
        HesitationSeconds += duration;
        isStationary = false;
        SessionLog.Record("hesitation_end", duration.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s");
    }

    private void CaptureSample()
    {
        stationaryThisSample |= isStationary;
        guideVisibleThisSample |= GuideVisible;

        var sample = new PlayerPositionSample
        {
            ElapsedTime = Time.time - runStartTime,
            Position = playerTransform.position,
            Yaw = Mathf.Repeat(playerTransform.eulerAngles.y, 360f),
            Stationary = stationaryThisSample ? 1 : 0,
            MenuPaused = menuPausedThisSample ? 1 : 0,
            AwaitingHelp = awaitingHelpThisSample ? 1 : 0,
            GuideVisible = guideVisibleThisSample ? 1 : 0,
            Assistance = assistanceThisSample,
            Error = errorsThisSample,
            GuideRequests = guideRequestsThisSample,
        };

        RouteProgressTracker rt = RouteTracker;
        if (rt != null && rt.IsTracking && rt.Route != null)
        {
            sample.HasRoute = true;
            sample.Progress = rt.Progress - rt.Route.StartDistance;
            sample.Lateral = rt.Lateral;
            sample.OnRoute = rt.OnRoute;
            sample.NearestNode = rt.NearestNode ?? "";
            sample.Zone = rt.Zone ?? "";
            sample.Surface = rt.Surface.HasValue ? rt.Surface.Value.ToString() : "";
            sample.Detour = rt.CurrentExcursion != null ? rt.CurrentExcursion.Branch : "";
        }

        samples.Add(sample);
        ResetSampleAccumulators();

        if (logEachSample)
            Debug.Log(
                $"Sample {sample.ElapsedTime:F2}s: pos={sample.Position}, progress={sample.Progress:F1}, " +
                $"zone={sample.Zone}, stationary={sample.Stationary}, menu={sample.MenuPaused}, " +
                $"awaiting_help={sample.AwaitingHelp}, assistance={sample.Assistance}, error={sample.Error}",
                this);
    }

    private void ResetSampleAccumulators()
    {
        stationaryThisSample = false;
        menuPausedThisSample = false;
        awaitingHelpThisSample = false;
        guideVisibleThisSample = false;
        assistanceThisSample = 0;
        errorsThisSample = 0;
        guideRequestsThisSample = 0;
    }

    private static Vector2 Horizontal(Vector3 p) => new Vector2(p.x, p.z);
}
