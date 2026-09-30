using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Who the current participant is, for the run data.
///
/// The researcher sets the ID on the PC before each participant with
/// Tools > VR Study > Participant ID (an Editor window that also works during Play mode). It is
/// kept in PlayerPrefs, so it survives restarting Play mode and Unity until it is changed. Runs
/// with no ID are written as "unset", with a Console warning, rather than being lost.
///
/// RUN INDEX: which run this is for the participant (1, 2, 3 ...), counted from the rows already
/// in the shared summary file, so it survives restarts and matches what is in the data. Deleting
/// test rows from the summary file resets it.
/// </summary>
public static class StudySession
{
    public const string PrefsKey = "study.participant_id";
    public const string Unset = "unset";

    /// <summary>The participant ID, trimmed, or "unset".</summary>
    public static string ParticipantId
    {
        get
        {
            string id = PlayerPrefs.GetString(PrefsKey, "").Trim();
            return string.IsNullOrEmpty(id) ? Unset : id;
        }
    }

    public static bool HasParticipant => ParticipantId != Unset;

    public static void SetParticipantId(string id)
    {
        PlayerPrefs.SetString(PrefsKey, (id ?? "").Trim());
        PlayerPrefs.Save();
    }

    /// <summary>Where the run CSVs and the shared summary file go.</summary>
    public static string DataFolder => Path.Combine(Application.persistentDataPath, "Data", "RunData");

    /// <summary>
    /// The number the next run for this participant will get: completed or exited runs already
    /// in the summary file, plus one.
    /// </summary>
    public static int NextRunIndex(string participantId)
    {
        string file = RunSummaryWriter.CurrentFile();
        if (file == null || !File.Exists(file)) return 1;

        int count = 0;
        try
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 1; i < lines.Length; i++)           // line 0 is the header
            {
                string first = FirstField(lines[i]);
                if (string.Equals(first, participantId, StringComparison.OrdinalIgnoreCase))
                    count++;
            }
        }
        catch (Exception e)
        {
            // Open in Excel on the PC, most likely. The run still goes ahead.
            Debug.LogWarning($"[StudySession] Could not read {file} to count earlier runs ({e.Message}); " +
                             "run_index may be wrong for this run.");
        }
        return count + 1;
    }

    private static string FirstField(string line)
    {
        if (string.IsNullOrEmpty(line)) return "";
        if (line[0] != '"')
        {
            int comma = line.IndexOf(',');
            return comma < 0 ? line : line.Substring(0, comma);
        }
        // Quoted: "a ""b"" c",...
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else break;
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
