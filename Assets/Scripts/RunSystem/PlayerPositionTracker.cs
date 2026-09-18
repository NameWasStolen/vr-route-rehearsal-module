using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct PlayerPositionSample
{
    public PlayerPositionSample(float elapsedTime, Vector3 position)
    {
        ElapsedTime = elapsedTime;
        Position = position;
    }

    public float ElapsedTime { get; }
    public Vector3 Position { get; }
}

public class PlayerPositionTracker : MonoBehaviour
{
    [SerializeField, Min(0.02f)] private float sampleInterval = 1f;

    private readonly List<PlayerPositionSample> samples = new();
    private Transform playerTransform;
    private float runStartTime;
    private float nextSampleTime;

    public bool IsTracking { get; private set; }
    public IReadOnlyList<PlayerPositionSample> Samples => samples;

    public void StartTracking(Transform player)
    {
        if (player == null)
        {
            Debug.LogError("PlayerPositionTracker cannot track a null transform.", this);
            return;
        }

        playerTransform = player;
        samples.Clear();
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
        samples.Add(new PlayerPositionSample(elapsedTime, currentPosition));
        Debug.Log(
            $"Player position at {elapsedTime:F2}s: " +
            $"x={currentPosition.x:F2}, y={currentPosition.y:F2}, z={currentPosition.z:F2}",
            this
        );
    }
}