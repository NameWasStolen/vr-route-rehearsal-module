using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes one run's samples (one row every 0.2 s) to Data/RunData under persistentDataPath,
/// as {participant}_{run type}_run_{date}_{time}.csv, with "_incomplete" after the run type if
/// the run was left from the pause menu.
///
/// Columns:
///   participant_id, run_type, run_index - the same on every row, so files can be stacked;
///   elapsed_seconds; x, y, z - head position; yaw - head direction, 0-360 degrees;
///   progress_m - distance along the ideal walking line from the start trigger;
///   lateral_m - distance from that line; on_route - 1 if on the route street;
///   nearest_node; zone - decision zone or zebra, if in one; surface - Paving / Grass / Asphalt;
///   detour - the side street of a wrong turn in progress, if any;
///   stationary - 1 if hesitating (standing still 3 s+) during the sample;
///   menu_paused - 1 if the pause menu was open; awaiting_help - 1 if a help request was waiting;
///   guide_visible - 1 if the Guided route line was showing (from step 6);
///   assistance, error, guide_requests - number of help requests, wrong turns and route-line taps
///   in the sample;
///   then the participant's settings for the run.
/// The route columns are blank if the scene has no RouteDefinition.
/// </summary>
public static class RunCsvLogger
{
    public const string Header =
        "participant_id,run_type,run_index,elapsed_seconds,x,y,z,yaw,progress_m,lateral_m,on_route," +
        "nearest_node,zone,surface,detour,stationary,menu_paused,awaiting_help,guide_visible," +
        "assistance,error,guide_requests,brightness,volume,font_size,usage_mode,handedness,rotation_mode";

    public static string Write(
        IReadOnlyList<PlayerPositionSample> samples,
        RunSettingsSnapshot settings,
        string runType,
        string participantId,
        int runIndex,
        bool completed = true)
    {
        // persistentDataPath, not dataPath: on Android/Quest dataPath points inside the
        // APK and is read-only, so the write throws on device while working in the Editor.
        string directory = StudySession.DataFolder;

        string filePath = Path.Combine(
            directory,
            $"{SanitizeFileName(participantId)}_{SanitizeFileName(runType)}{(completed ? "" : "_incomplete")}_run_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        var inv = CultureInfo.InvariantCulture;
        string fixedStart = $"{Escape(participantId)},{Escape(runType)},{runIndex.ToString(inv)},";
        string settingsEnd =
            $"{settings.Brightness.ToString("F3", inv)},{settings.Volume.ToString("F3", inv)}," +
            $"{settings.FontSize.ToString("F3", inv)},{Escape(settings.UsageMode)}," +
            $"{Escape(settings.Handedness)},{Escape(settings.RotationMode)}";

        StringBuilder csv = new();
        csv.AppendLine(Header);

        foreach (PlayerPositionSample s in samples)
        {
            csv.Append(fixedStart);
            csv.Append(s.ElapsedTime.ToString("F3", inv)).Append(',');
            csv.Append(s.Position.x.ToString("F3", inv)).Append(',');
            csv.Append(s.Position.y.ToString("F3", inv)).Append(',');
            csv.Append(s.Position.z.ToString("F3", inv)).Append(',');
            csv.Append(s.Yaw.ToString("F1", inv)).Append(',');
            if (s.HasRoute)
            {
                csv.Append(s.Progress.ToString("F2", inv)).Append(',');
                csv.Append(s.Lateral.ToString("F2", inv)).Append(',');
                csv.Append(s.OnRoute ? '1' : '0').Append(',');
                csv.Append(Escape(s.NearestNode)).Append(',');
                csv.Append(Escape(s.Zone)).Append(',');
                csv.Append(Escape(s.Surface)).Append(',');
                csv.Append(Escape(s.Detour)).Append(',');
            }
            else
            {
                csv.Append(",,,,,,,");
            }
            csv.Append(s.Stationary.ToString(inv)).Append(',');
            csv.Append(s.MenuPaused.ToString(inv)).Append(',');
            csv.Append(s.AwaitingHelp.ToString(inv)).Append(',');
            csv.Append(s.GuideVisible.ToString(inv)).Append(',');
            csv.Append(s.Assistance.ToString(inv)).Append(',');
            csv.Append(s.Error.ToString(inv)).Append(',');
            csv.Append(s.GuideRequests.ToString(inv)).Append(',');
            csv.Append(settingsEnd);
            csv.AppendLine();
        }

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        }
        catch (Exception exception)
        {
            // A failed write must not take the run down with it - the participant is still
            // in the headset and StopTracking has more to do after this.
            Debug.LogError($"Run CSV could not be written to {filePath}: {exception.Message}");
            return null;
        }

        Debug.Log($"Run CSV saved to: {filePath}");
        return filePath;
    }

    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    public static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "run";

        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            value = value.Replace(invalidCharacter, '_');

        return value.Replace(' ', '_');
    }
}
