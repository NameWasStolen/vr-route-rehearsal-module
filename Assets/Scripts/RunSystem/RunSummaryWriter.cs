using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Adds one row per run to Data/RunData/run_summaries.csv (under persistentDataPath), so every
/// participant and run sits in one table. Written when a run ends, whether it was completed or
/// left from the pause menu (completed = 0). A run that never started (the participant never
/// left the bus stop) writes nothing.
///
/// If the file's header does not match this version's columns (an older layout), rows go to
/// run_summaries_2.csv, _3 ... instead, so one file never mixes layouts. If the file cannot be
/// written - usually because it is open in Excel on the PC - the row is saved on its own as
/// run_summary_{participant}_{date}_{time}.csv beside it, and the Console says so.
///
/// The columns are listed in Columns below; see statistics-integration-plan.md, step 5.
/// </summary>
public static class RunSummaryWriter
{
    public const string BaseName = "run_summaries";

    /// <summary>The six decision points, in route order. Fixed so the header never changes.</summary>
    public static readonly string[] DecisionNodes = { "N2", "N4", "N6", "N10", "N12", "N15" };

    private static string header;
    public static string Header
    {
        get
        {
            if (header == null) header = BuildHeader();
            return header;
        }
    }

    private static string BuildHeader()
    {
        var cols = new List<string>
        {
            "participant_id", "run_type", "run_index", "date", "start_time",
            "completed", "completion_time_s",
            "optimal_length_m", "distance_walked_m", "route_efficiency", "max_progress_m", "backtrack_m",
            "wrong_turns", "wrong_turns_at_decisions", "wrong_turns_straight_on", "self_corrected",
            "detour_time_s", "detour_distance_m", "max_detour_depth_m",
            "off_route_time_s", "off_route_distance_m",
            "hesitations", "hesitation_time_s", "hesitations_at_decisions",
            "assistance_requests", "guide_requests", "guide_visible_time_s",
            "pause_menu_opens", "pause_menu_time_s", "awaiting_help_time_s",
            "road_time_s", "crossings_zebra", "crossings_junction", "crossings_midblock", "crossed_at_route_zebra",
        };
        foreach (string n in DecisionNodes)
        {
            cols.Add(n + "_time_s");
            cols.Add(n + "_visits");
            cols.Add(n + "_scan_deg");
        }
        cols.AddRange(new[] { "brightness", "volume", "font_size", "usage_mode", "handedness", "rotation_mode", "sample_file" });
        return string.Join(",", cols);
    }

    /// <summary>
    /// The summary file new rows go to: run_summaries.csv, or the first _2, _3 ... whose header
    /// matches (or that does not exist yet).
    /// </summary>
    public static string CurrentFile()
    {
        string dir = StudySession.DataFolder;
        for (int i = 1; i < 100; i++)
        {
            string path = Path.Combine(dir, i == 1 ? BaseName + ".csv" : $"{BaseName}_{i}.csv");
            if (!File.Exists(path)) return path;
            try
            {
                using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    string first = reader.ReadLine();
                    if (first != null && first.TrimStart('﻿') == Header) return path;
                }
            }
            catch (Exception)
            {
                // Unreadable (locked). Assume it is the right one; the write falls back if needed.
                return path;
            }
        }
        return Path.Combine(dir, BaseName + "_new.csv");
    }

    /// <summary>Builds the row and appends it. Returns the file written to, or null.</summary>
    public static string Write(
        PlayerPositionTracker p,
        RouteProgressTracker r,
        DateTime runStartedAt,
        bool completed,
        float runSeconds,
        string sampleFilePath)
    {
        if (p == null) return null;
        string row = BuildRow(p, r, runStartedAt, completed, runSeconds, sampleFilePath);

        string path = CurrentFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            bool isNew = !File.Exists(path);
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                if (isNew) writer.WriteLine(Header);
                writer.WriteLine(row);
            }
            Debug.Log($"[RunSummary] Run {p.RunIndex} for {p.ParticipantId} added to {path}");
            return path;
        }
        catch (Exception e)
        {
            string fallback = Path.Combine(StudySession.DataFolder,
                $"run_summary_{RunCsvLogger.SanitizeFileName(p.ParticipantId)}_{runStartedAt:yyyyMMdd_HHmmss}.csv");
            try
            {
                File.WriteAllText(fallback, Header + "\n" + row + "\n", new UTF8Encoding(false));
                Debug.LogError($"[RunSummary] Could not add this run to {path} ({e.Message}) - is it open in Excel? " +
                               $"The row was saved on its own to {fallback}; paste it into the summary file later.");
                return fallback;
            }
            catch (Exception e2)
            {
                Debug.LogError($"[RunSummary] Could not save the run summary at all ({e2.Message}). Row: {row}");
                return null;
            }
        }
    }

    public static string BuildRow(
        PlayerPositionTracker p,
        RouteProgressTracker r,
        DateTime runStartedAt,
        bool completed,
        float runSeconds,
        string sampleFilePath)
    {
        var inv = CultureInfo.InvariantCulture;
        var v = new List<string>();
        Func<float, string, string> Fmt = (x, fmt) => x.ToString(fmt, inv);
        Func<float, string> F = x => x.ToString("F1", inv);
        Func<int, string> I = x => x.ToString(inv);

        v.Add(RunCsvLogger.Escape(p.ParticipantId));
        v.Add(RunCsvLogger.Escape(p.RunType));
        v.Add(I(p.RunIndex));
        v.Add(runStartedAt.ToString("yyyy-MM-dd", inv));
        v.Add(runStartedAt.ToString("HH:mm:ss", inv));
        v.Add(completed ? "1" : "0");
        v.Add(F(runSeconds));

        bool route = r != null && r.Route != null;
        int atDecision = 0, straightOn = 0, selfCorrected = 0;
        float detourTime = 0f, detourDist = 0f, maxDepth = 0f;
        int zebra = 0, junction = 0, midblock = 0;
        bool routeZebra = false;
        if (route)
        {
            foreach (RouteProgressTracker.Excursion e in r.Excursions)
            {
                if (e.AtDecision) atDecision++; else straightOn++;
                if (e.EndedBy == "returned") selfCorrected++;
                detourTime += e.Duration;
                detourDist += e.Walked;
                if (e.Depth > maxDepth) maxDepth = e.Depth;
            }
            foreach (RouteProgressTracker.Crossing c in r.Crossings)
            {
                if (c.Kind == "zebra") { zebra++; if (c.Where == "Zebra_RouteCrossing") routeZebra = true; }
                else if (c.Kind == "junction") junction++;
                else midblock++;
            }
        }

        if (route)
        {
            float optimal = r.Route.OptimalLength;
            v.Add(F(optimal));
            v.Add(F(r.DistanceWalked));
            v.Add(r.DistanceWalked > 0.1f ? Fmt(optimal / r.DistanceWalked, "F3") : "");
            v.Add(F(Mathf.Max(0f, r.MaxProgress - r.Route.StartDistance)));
            v.Add(F(r.Backtracked));
        }
        else v.AddRange(new[] { "", "", "", "", "" });

        v.Add(I(p.WrongTurnCount));
        if (route)
        {
            v.Add(I(atDecision));
            v.Add(I(straightOn));
            v.Add(I(selfCorrected));
            v.Add(F(detourTime));
            v.Add(F(detourDist));
            v.Add(F(maxDepth));
            v.Add(F(r.OffRouteTime));
            v.Add(F(r.OffRouteDistance));
        }
        else v.AddRange(new[] { "", "", "", "", "", "", "", "" });

        v.Add(I(p.HesitationCount));
        v.Add(F(p.HesitationSeconds));
        v.Add(route ? I(p.HesitationsAtDecisions) : "");
        v.Add(I(p.AssistanceCount));
        v.Add(I(p.GuideRequestCount));
        v.Add(F(p.GuideVisibleSeconds));
        v.Add(I(p.PauseMenuOpens));
        v.Add(F(p.PauseMenuSeconds));
        v.Add(F(p.AwaitingHelpSeconds));

        if (route)
        {
            v.Add(F(r.RoadTime));
            v.Add(I(zebra));
            v.Add(I(junction));
            v.Add(I(midblock));
            v.Add(routeZebra ? "1" : "0");
        }
        else v.AddRange(new[] { "", "", "", "", "" });

        foreach (string n in DecisionNodes)
        {
            if (!route) { v.AddRange(new[] { "", "", "" }); continue; }
            if (r.Decisions.TryGetValue("CP_Decision_" + n, out RouteProgressTracker.DecisionStats s))
            {
                v.Add(F(s.TotalTime));
                v.Add(I(s.Visits));
                v.Add(Fmt(s.MaxScanDegrees, "F0"));
            }
            else v.AddRange(new[] { "0.0", "0", "0" });
        }

        RunSettingsSnapshot st = p.Settings;
        v.Add(Fmt(st.Brightness, "F3"));
        v.Add(Fmt(st.Volume, "F3"));
        v.Add(Fmt(st.FontSize, "F3"));
        v.Add(RunCsvLogger.Escape(st.UsageMode));
        v.Add(RunCsvLogger.Escape(st.Handedness));
        v.Add(RunCsvLogger.Escape(st.RotationMode));
        v.Add(RunCsvLogger.Escape(string.IsNullOrEmpty(sampleFilePath) ? "" : Path.GetFileName(sampleFilePath)));

        return string.Join(",", v);
    }
}
