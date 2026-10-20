using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Adds one row per post-run survey to Data/RunData/survey_responses.csv (under
/// persistentDataPath, beside run_summaries.csv). participant_id, run_type and run_index match
/// the run's row in run_summaries.csv, so the two tables join on those three columns.
///
/// With the default questions the columns are:
///   participant_id, run_type, run_index, date, time,
///   calm, stress, confidence, ease, difficulty, guide_help, guidance_help,
///                                   answers 1-5 (stress = 6 - calm, difficulty = 6 - ease)
///   calm_time_s ... guidance_help_time_s   seconds to answer (help panel time left out)
///   calm_changes ... guidance_help_changes times the participant changed their pick
///   guide_shown                     times the guide appeared in the run (Guided only)
///
/// Every row has every column, so all four modules share one file: a question not asked in that
/// run has blank cells. ease is Unguided 1 only; guide_help is Guided only (and blank there if the
/// guide never appeared); guidance_help is Unguided 2.a only. guide_shown is blank for runs with
/// no guide.
///
/// The header follows the questions in PostRunSurvey. If the file's header does not match (the
/// questions were changed), rows go to survey_responses_2.csv, _3 ... so one file never mixes
/// layouts. If the file cannot be written - usually because it is open in Excel - the row is
/// saved on its own beside it and the Console says so.
/// </summary>
public static class SurveyResponseWriter
{
    public const string BaseName = "survey_responses";

    public static string BuildHeader(IList<PostRunSurvey.Question> questions)
    {
        var cols = new List<string> { "participant_id", "run_type", "run_index", "date", "time" };
        foreach (PostRunSurvey.Question q in questions)
        {
            cols.Add(q.id);
            if (!string.IsNullOrWhiteSpace(q.reversedColumn)) cols.Add(q.reversedColumn.Trim());
        }
        foreach (PostRunSurvey.Question q in questions) cols.Add(q.id + "_time_s");
        foreach (PostRunSurvey.Question q in questions) cols.Add(q.id + "_changes");
        cols.Add("guide_shown");
        return string.Join(",", cols);
    }

    /// <summary>Builds the row and appends it. Returns the file written to, or null.</summary>
    public static string Write(string participantId, string runType, int runIndex, DateTime startedAt,
                               IList<PostRunSurvey.Question> questions, int[] answers, float[] seconds, int[] changes,
                               int guideShownCount = -1)
    {
        string header = BuildHeader(questions);
        var inv = CultureInfo.InvariantCulture;
        var v = new List<string>
        {
            RunCsvLogger.Escape(participantId),
            RunCsvLogger.Escape(runType),
            runIndex.ToString(inv),
            startedAt.ToString("yyyy-MM-dd", inv),
            startedAt.ToString("HH:mm:ss", inv),
        };
        for (int i = 0; i < questions.Count; i++)
        {
            int a = answers[i];
            v.Add(a > 0 ? a.ToString(inv) : "");
            if (!string.IsNullOrWhiteSpace(questions[i].reversedColumn))
                v.Add(a > 0 ? (PostRunSurvey.ScaleSize + 1 - a).ToString(inv) : "");
        }
        // An answer of 0 means the question was not asked in this run: its time and changes are blank too.
        for (int i = 0; i < questions.Count; i++) v.Add(answers[i] > 0 ? seconds[i].ToString("F1", inv) : "");
        for (int i = 0; i < questions.Count; i++) v.Add(answers[i] > 0 ? changes[i].ToString(inv) : "");
        v.Add(guideShownCount >= 0 ? guideShownCount.ToString(inv) : "");
        string row = string.Join(",", v);

        string path = CurrentFile(header);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            bool isNew = !File.Exists(path);
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                if (isNew) writer.WriteLine(header);
                writer.WriteLine(row);
            }
            Debug.Log($"[Survey] {runType} run {runIndex} for {participantId} added to {path}");
            return path;
        }
        catch (Exception e)
        {
            string fallback = Path.Combine(StudySession.DataFolder,
                $"survey_response_{RunCsvLogger.SanitizeFileName(participantId)}_{startedAt:yyyyMMdd_HHmmss}.csv");
            try
            {
                File.WriteAllText(fallback, header + "\n" + row + "\n", new UTF8Encoding(false));
                Debug.LogError($"[Survey] Could not add this survey to {path} ({e.Message}) - is it open in Excel? " +
                               $"The row was saved on its own to {fallback}; paste it into the survey file later.");
                return fallback;
            }
            catch (Exception e2)
            {
                Debug.LogError($"[Survey] Could not save the survey at all ({e2.Message}). Row: {row}");
                return null;
            }
        }
    }

    /// <summary>
    /// survey_responses.csv, or the first _2, _3 ... whose header matches (or that does not
    /// exist yet).
    /// </summary>
    public static string CurrentFile(string header)
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
                    if (first != null && first.TrimStart('﻿') == header) return path;
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
}
