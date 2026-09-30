using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes one run's samples to Data/RunData under persistentDataPath.
///
/// Columns: elapsed_seconds, x, y, z (head position);
/// stationary - 1 if hesitating (standing still 3 s+, see PlayerPositionTracker) during the sample;
/// menu_paused - 1 if the pause menu was open during the sample;
/// awaiting_help - 1 if a confirmed help request was waiting for the researcher;
/// assistance - number of help requests placed during the sample;
/// error - number of wrong turns taken during the sample;
/// then the participant's settings for the run.
/// </summary>
public static class RunCsvLogger
{
    public static string Write(
        IReadOnlyList<PlayerPositionSample> samples,
        RunSettingsSnapshot settings,
        string runType)
    {
        // persistentDataPath, not dataPath: on Android/Quest dataPath points inside the
        // APK and is read-only, so the write throws on device while working in the Editor.
        string directory = Path.Combine(Application.persistentDataPath, "Data", "RunData");

        string filePath = Path.Combine(
            directory,
            $"{SanitizeFileName(runType)}_run_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        StringBuilder csv = new();
        csv.AppendLine(
            "elapsed_seconds,x,y,z,stationary,menu_paused,awaiting_help,assistance,error,brightness,volume,font_size,usage_mode,handedness,rotation_mode");

        foreach (PlayerPositionSample sample in samples)
        {
            csv.Append(sample.ElapsedTime.ToString("F3", CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(sample.Position.x.ToString("F3", CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(sample.Position.y.ToString("F3", CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(sample.Position.z.ToString("F3", CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(sample.Stationary.ToString(CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(sample.MenuPaused.ToString(CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(sample.AwaitingHelp.ToString(CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(sample.Assistance.ToString(CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(sample.Error.ToString(CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(settings.Brightness.ToString("F3", CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(settings.Volume.ToString("F3", CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(settings.FontSize.ToString("F3", CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.Append(Escape(settings.UsageMode));
            csv.Append(',');
            csv.Append(Escape(settings.Handedness));
            csv.Append(',');
            csv.Append(Escape(settings.RotationMode));
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

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Contains(",") || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "run";

        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            value = value.Replace(invalidCharacter, '_');

        return value;
    }
}