using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// An append-only record of the notable things that happened during a session.
///
/// Built as a shared service rather than as part of any one feature, because the project needs
/// exactly one of these and three places already want it: the assistance request is the first
/// real caller, and TimerController.startTimer, TimerController.endTimer and
/// WrongTurnController.logWrongTurn are each a single Record call away from doing the thing their
/// names promise. Writing a bespoke logger for assistance alone would mean writing this again.
///
/// Static, for the same reason TutorialPause is: the callers are scattered across additively
/// loaded scenes, they enable in an unpredictable order, and Unity cannot serialise a reference
/// across a scene boundary anyway.
///
/// Every Record opens the file, appends one line and closes it. That is slower than holding a
/// stream open, and it is the right trade here: events are rare (a handful per session), and a
/// session that ends with a participant lifting the headset off may never reach
/// OnApplicationQuit. A buffered line that is never flushed is a line that never existed.
///
/// Nothing here throws. A study session must not be interrupted because a disk write failed, so
/// failures warn once and the module carries on.
/// </summary>
public static class SessionLog
{
    private const string HeaderLine = "timestamp_iso,seconds_since_start,event,detail,x,y,z";

    private static string _path;
    private static float _sessionStartUnscaled;
    private static Transform _positionSource;
    private static bool _warnedWriteFailure;
    private static bool _warnedNoPositionSource;

    /// <summary>Full path of the file being written, or null before the first Record.</summary>
    public static string CurrentPath => _path;

    /// <summary>
    /// Points the position column at a specific transform. Optional: without it the rig is found
    /// by its Player tag on first use, which is how XRPlayerRig is already tagged.
    /// </summary>
    public static void SetPositionSource(Transform source) => _positionSource = source;

    /// <summary>
    /// Starts a new file. Call when a run begins if each run should be its own file; otherwise
    /// the first Record opens one and everything lands there.
    /// </summary>
    public static void BeginSession()
    {
        _path = null;
        _warnedWriteFailure = false;
        EnsureFile();
    }

    /// <summary>
    /// Appends one event. <paramref name="detail"/> is free text for whatever context the caller
    /// has - the current tutorial step, a trigger name - and is quoted, so commas in it are safe.
    /// </summary>
    public static void Record(string eventName, string detail = null)
    {
        if (string.IsNullOrEmpty(eventName)) return;
        if (!EnsureFile()) return;

        Vector3 p = ResolvePosition();
        float elapsed = Time.unscaledTime - _sessionStartUnscaled;

        var line = new StringBuilder();
        line.Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
        line.Append(',').Append(elapsed.ToString("F3", CultureInfo.InvariantCulture));
        line.Append(',').Append(Quote(eventName));
        line.Append(',').Append(Quote(detail ?? string.Empty));
        line.Append(',').Append(p.x.ToString("F3", CultureInfo.InvariantCulture));
        line.Append(',').Append(p.y.ToString("F3", CultureInfo.InvariantCulture));
        line.Append(',').Append(p.z.ToString("F3", CultureInfo.InvariantCulture));
        line.Append('\n');

        Append(line.ToString());

        // Mirrored to the console as well, so a researcher watching the Editor sees events go by
        // without having to pull the file off the headset.
        Debug.Log($"[SessionLog] {eventName}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
    }

    /// <summary>
    /// Clears static state between sessions. Static fields survive a scene load, so without this
    /// a second run would keep appending to the first run's file and measure its elapsed seconds
    /// from the first run's start.
    /// </summary>
    public static void ResetState()
    {
        _path = null;
        _positionSource = null;
        _warnedWriteFailure = false;
        _warnedNoPositionSource = false;
    }

    // ---------------------------------------------------------------------------- internals

    private static bool EnsureFile()
    {
        if (_path != null) return true;

        try
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string dir = Path.Combine(Application.persistentDataPath, "SessionLogs");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, $"session_{stamp}.csv");
            _sessionStartUnscaled = Time.unscaledTime;

            File.WriteAllText(_path, HeaderLine + "\n");

            // Logged at full volume on purpose: on a headset this path is buried, and whoever is
            // running the study needs to be able to find the file afterwards.
            Debug.Log($"[SessionLog] Writing to {_path}");
            return true;
        }
        catch (Exception e)
        {
            _path = null;
            Warn($"could not create the log file ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    private static void Append(string line)
    {
        try
        {
            File.AppendAllText(_path, line);
        }
        catch (Exception e)
        {
            Warn($"could not append to {_path} ({e.GetType().Name}: {e.Message})");
        }
    }

    private static Vector3 ResolvePosition()
    {
        if (_positionSource != null) return _positionSource.position;

        GameObject rig = GameObject.FindWithTag("Player");
        if (rig != null)
        {
            _positionSource = rig.transform;
            return _positionSource.position;
        }

        if (!_warnedNoPositionSource)
        {
            _warnedNoPositionSource = true;
            Debug.LogWarning("[SessionLog] No object tagged Player, so positions will log as zero. " +
                             "Call SessionLog.SetPositionSource if the rig is tagged differently.");
        }
        return Vector3.zero;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static void Warn(string message)
    {
        if (_warnedWriteFailure) return;
        _warnedWriteFailure = true;
        Debug.LogWarning($"[SessionLog] {message}. Further write failures will not be reported.");
    }
}
