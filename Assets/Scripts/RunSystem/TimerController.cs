using UnityEngine;

public class TimerController : MonoBehaviour
{
    public event System.Action<float> RunEnded;

    public bool IsRunning { get; private set; }
    public float ElapsedTime { get; private set; }

    private float startTime;

    private void Awake()
    {
        Debug.Log($"TimerController ready on '{name}'.", this);
    }

    public void startTimer()
    {
        if (IsRunning)
        {
            Debug.Log("Run timer start ignored because it is already running.", this);
            return;
        }

        startTime = Time.time;
        ElapsedTime = 0f;
        IsRunning = true;
        Debug.Log("Run Timer started");
    }

    public void endTimer()
    {
        if (!IsRunning)
        {
            Debug.LogWarning("Run timer end ignored because it has not started.", this);
            return;
        }

        ElapsedTime = Time.time - startTime;
        IsRunning = false;
        Debug.Log("Run Timer ended");
        Debug.Log($"Run completed in {ElapsedTime:F2} seconds");
        RunEnded?.Invoke(ElapsedTime);
    }

    public void resetTime()
    {
        startTime = 0f;
        ElapsedTime = 0f;
        IsRunning = false;
        Debug.Log("Run Timer has been reset");
    }
}
