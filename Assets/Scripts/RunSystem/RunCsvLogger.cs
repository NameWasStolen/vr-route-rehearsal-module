using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class RunCsvLogger
{
    public static string Write(
        IReadOnlyList<PlayerPositionSample> samples,
        RunSettingsSnapshot settings,
        string runType)
    {
        string directory = Path.Combine(Application.dataPath, "Data", "RunData");
        Directory.CreateDirectory(directory);

        string filePath = Path.Combine(
            directory,
            $"{SanitizeFileName(runType)}_run_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        StringBuilder csv = new();
        csv.AppendLine(
            "elapsed_seconds,x,y,z,brightness,volume,font_size,usage_mode,handedness,rotation_mode");

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

        File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
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