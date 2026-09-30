using System;
using System.Collections.Generic;
using UnityEngine;
using VRTutorial;

public readonly struct PlayerPositionSample
{
    public PlayerPositionSample(
        float elapsedTime,
        Vector3 position,
        int stationary,
        int menuPaused,
        int awaitingHelp,
        int assistance,
        int error)
    {
        ElapsedTime = elapsedTime;
        Position = position;
        Stationary = stationary;
        MenuPaused = menuPaused;
        AwaitingHelp = awaitingHelp;
        Assistance = assistance;
        Error = error;
    }

    public float ElapsedTime { get; }
    public Vector3 Position { get; }

    /// <summary>1 if the participant was hesitating (standing still, see below) at any point in this sample.</summary>
    public int Stationary { get; }

    /// <summary>1 if the pause menu was open at any point in this sample.</summary>
    public int MenuPaused { get; }

    /// <summary>1 if a help request was confirmed and waiting for the researcher at any point in this sample.</summary>
    public int AwaitingHelp { get; }

    /// <summary>Number of help requests placed during this sample.</summary>
    public int Assistance { get; }

    /// <summary>Number of wrong turns taken during this sample.</summary>
    public int Error { get; }
}

/// <summary>
/// Samples the participant's position during a run and works out when they hesitate.
///
/// HESITATION ("stationary"): horizontal head speed below stationarySpeed (0.2 m/s) for at
/// least stationarySeconds (3 s). Measured every frame on x/z only, and smoothed, so looking
/// around, leaning or bobbing the head does not break up a real stop - the old version
/// compared 3-D head positions one sample apart, which it did. Time in the pause menu or with
/// a help request open (holding the button, or waiting for the researcher) never counts:
/// locomotion is suspended then, so standing still is not a choice.
///
/// EVENTS (assistance, error) are counted, not flagged, so two in one sample are two.
/// </summary>
public class PlayerPositionTracker : MonoBehaviour
{
    [SerializeField, Min(0.02f)] private float sampleInterval = 1f;

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
    private RunSettingsSnapshot runSettings;
    private string runType;

    // Hesitation state, per frame.
    private Vector2 lastHorizontal;
    private float smoothedSpeed;
    private float stillTime;
    private bool isStationary;
    private float hesitationStartTime;

    // Accumulated since the last sample.
    private bool stationaryThisSample;
    private bool menuPausedThisSample;
    private bool awaitingHelpThisSample;
    private int assistanceThisSample;
    private int errorsThisSample;

    // Run totals, for the end-of-run summary.
    private int hesitationCount;
    private float hesitationTotal;
    private int assistanceTotal;
    private int errorTotal;

    public bool IsTracking { get; private set; }

    /// <summary>Optional. When set, hesitations are logged with where they happened.</summary>
    public RouteProgressTracker RouteTracker { get; set; }
    public IReadOnlyList<PlayerPositionSample> Samples => samples;

    public int HesitationCount => hesitationCount;
    public float HesitationSeconds => hesitationTotal;
    public int AssistanceCount => assistanceTotal;
    public int WrongTurnCount => errorTotal;

    public void StartTracking(
        Transform player,
        RunSettingsSnapshot settings,
        string selectedRunType)
    {
        if (player == null)
        {
            Debug.LogError("PlayerPositionTracker cannot track a null transform.", this);
            return;
        }

        playerTransform = player;
        runSettings = settings;
        runType = selectedRunType;
        samples.Clear();

        lastHorizontal = Horizontal(player.position);
        smoothedSpeed = 0f;
        stillTime = 0f;
        isStationary = false;
        ResetSampleAccumulators();
        hesitationCount = 0;
        hesitationTotal = 0f;
        assistanceTotal = 0;
        errorTotal = 0;

        runStartTime = Time.time;
        nextSampleTime = runStartTime;
        IsTracking = true;
        CaptureSample();
        nextSampleTime = Time.time + sampleInterval;
    }

    /// <param name="completed">False when the run was abandoned from the pause menu; the file
    /// name then says "_incomplete" so it is not mistaken for a finished run.</param>
    public void StopTracking(bool completed = true)
    {
        if (!IsTracking)
            return;

        if (isStationary)
            EndHesitation();

        CaptureSample();
        IsTracking = false;
        RunCsvLogger.Write(samples, runSettings, completed ? runType : runType + "_incomplete");
        Debug.Log(
            $"Player position tracking ended with {samples.Count} samples: " +
            $"{errorTotal} wrong turn(s), {assistanceTotal} help request(s), " +
            $"{hesitationCount} hesitation(s) totalling {hesitationTotal:F1}s.",
            this);
    }

    public void RecordAssistance()
    {
        if (!IsTracking)
            return;
        assistanceThisSample++;
        assistanceTotal++;
    }

    public void RecordError()
    {
        if (!IsTracking)
            return;
        errorsThisSample++;
        errorTotal++;
    }

    private void Update()
    {
        if (!IsTracking || playerTransform == null)
            return;

        UpdateHesitation(Time.deltaTime);

        if (Time.time >= nextSampleTime)
        {
            CaptureSample();
            nextSampleTime = Time.time + sampleInterval;
        }
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

        bool awaitingHelp = AssistanceRequest.State == AssistanceState.Requested;
        bool helpOpen = AssistanceRequest.IsActive;       // holding the button, or waiting
        bool menuPaused = TutorialPause.IsPaused && !helpOpen;
        menuPausedThisSample |= menuPaused;
        awaitingHelpThisSample |= awaitingHelp;

        if (TutorialPause.IsPaused || helpOpen)
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
                hesitationCount++;
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

    private string HesitationPlace()
    {
        if (RouteTracker == null || !RouteTracker.IsTracking) return null;
        if (!string.IsNullOrEmpty(RouteTracker.Zone)) return RouteTracker.Zone;
        return RouteTracker.OnRoute ? "near " + RouteTracker.NearestNode : "off route near " + RouteTracker.NearestNode;
    }

    private void EndHesitation()
    {
        float duration = Time.time - hesitationStartTime;
        hesitationTotal += duration;
        isStationary = false;
        SessionLog.Record("hesitation_end", duration.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s");
    }

    private void CaptureSample()
    {
        Vector3 currentPosition = playerTransform.position;
        float elapsedTime = Time.time - runStartTime;
        stationaryThisSample |= isStationary;

        var sample = new PlayerPositionSample(
            elapsedTime,
            currentPosition,
            stationaryThisSample ? 1 : 0,
            menuPausedThisSample ? 1 : 0,
            awaitingHelpThisSample ? 1 : 0,
            assistanceThisSample,
            errorsThisSample);
        samples.Add(sample);
        ResetSampleAccumulators();

        Debug.Log(
            $"Player position at {elapsedTime:F2}s: " +
            $"x={currentPosition.x:F2}, y={currentPosition.y:F2}, z={currentPosition.z:F2}, " +
            $"stationary={sample.Stationary}, menu={sample.MenuPaused}, awaiting_help={sample.AwaitingHelp}, " +
            $"assistance={sample.Assistance}, error={sample.Error}",
            this
        );
    }

    private void ResetSampleAccumulators()
    {
        stationaryThisSample = false;
        menuPausedThisSample = false;
        awaitingHelpThisSample = false;
        assistanceThisSample = 0;
        errorsThisSample = 0;
    }

    private static Vector2 Horizontal(Vector3 p) => new Vector2(p.x, p.z);
}
