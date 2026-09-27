using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct PlayerPositionSample
{
    public PlayerPositionSample(float elapsedTime, Vector3 position, int pause)
    {
        ElapsedTime = elapsedTime;
        Position = position;
        Pause = pause;
    }

    public float ElapsedTime { get; }
    public Vector3 Position { get; }
    public int Pause { get; }
}

public class PlayerPositionTracker : MonoBehaviour
{
    [SerializeField, Min(0.02f)] private float sampleInterval = 1f;
    [SerializeField, Min(0.01f)] private float pauseThreshold = 0.1f;
    [SerializeField, Min(0.1f)] private float pauseDurationSeconds = 3f;

    private readonly List<PlayerPositionSample> samples = new();
    private Transform playerTransform;
    private float runStartTime;
    private float nextSampleTime;
    private float stationaryDuration;
    private Vector3? previousPosition;
    private RunSettingsSnapshot runSettings;
    private string runType;

    public bool IsTracking { get; private set; }
    public IReadOnlyList<PlayerPositionSample> Samples => samples;

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
        previousPosition = null;
        stationaryDuration = 0f;
        runStartTime = Time.time;
        nextSampleTime = runStartTime;
        IsTracking = true;
        CaptureSample();
    }

    public void StopTracking()
    {
        if (!IsTracking)
            return;

        CaptureSample();
        IsTracking = false;
        RunCsvLogger.Write(samples, runSettings, runType);
        Debug.Log($"Player position tracking ended with {samples.Count} samples.", this);
    }

    private void Update()
    {
        if (!IsTracking || playerTransform == null || Time.time < nextSampleTime)
            return;

        CaptureSample();
        nextSampleTime = Time.time + sampleInterval;
    }

    private void CaptureSample()
    {
        Vector3 currentPosition = playerTransform.position;
        float elapsedTime = Time.time - runStartTime;

        if (previousPosition.HasValue)
        {
            float distanceMoved = Vector3.Distance(currentPosition, previousPosition.Value);
            if (distanceMoved <= pauseThreshold)
                stationaryDuration += sampleInterval;
            else
                stationaryDuration = 0f;
        }
        else
        {
            stationaryDuration = 0f;
        }

        int pauseValue = stationaryDuration > pauseDurationSeconds ? 1 : 0;
        previousPosition = currentPosition;

        samples.Add(new PlayerPositionSample(elapsedTime, currentPosition, pauseValue));
        Debug.Log(
            $"Player position at {elapsedTime:F2}s: " +
            $"x={currentPosition.x:F2}, y={currentPosition.y:F2}, z={currentPosition.z:F2}, pause={pauseValue}",
            this
        );
    }
}