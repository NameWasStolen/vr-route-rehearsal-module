using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Which of the Select Module options each participant has completed, so the menu can tick them
/// off, stop them being chosen again, and open them one at a time in order.
///
/// ORDER (Kade, 5 Oct 2026): Map -> 1 Unguided -> 2 Guided -> 3 Unguided 2.a -> 4 Unguided 2.b.
/// Only the next module in Sequence is open; the rest are locked until the one before is done.
/// "Done" is completed or skipped.
///
/// SKIPPED: the researcher screen's Skip module, for a module that can't be finished (a headset
/// problem, say). It moves the participant on without counting the module as completed, so the
/// menu shows no tick for it.
///
/// COMPLETED MEANS:
///   - a module run that reached the end zone (and, for modules with a survey, after it has been
///     answered); leaving early with Back to Menu does not count, so they can try again;
///   - the Map, once its full viewing time has run.
///
/// NO PARTICIPANT ID: nothing is recorded and nothing is ever ticked, so testing without an ID is
/// never blocked.
///
/// STORED in Data/RunData/module_progress.csv (beside run_summaries.csv), one line per event:
///     participant_id,module,event,timestamp
/// event is "completed", "skipped" (researcher screen's Skip module), or "reset" (module "*") when
/// the researcher clears a participant's ticks from the researcher screen. Nothing is ever deleted,
/// so the file is also a record of what was redone and when. A participant's completed and skipped
/// sets are everything since their last reset.
///
/// Module ids are the run types (unguided_1, guided, unguided_2a, unguided_2b) plus "map".
/// </summary>
public static class ModuleProgress
{
    public const string FileName = "module_progress.csv";
    public const string MapModule = "map";
    private const string Header = "participant_id,module,event,timestamp";

    /// <summary>The order the modules are done in. Only the first one not yet done is open.</summary>
    public static readonly string[] Sequence =
    {
        MapModule,
        MenuController.ModuleUnguided1,
        MenuController.ModuleGuided,
        MenuController.ModuleUnguided2a,
        MenuController.ModuleUnguided2b,
    };

    /// <summary>Goes up every time progress changes, so the menu knows to redraw.</summary>
    public static int Version { get; private set; }

    public static string FilePath => Path.Combine(StudySession.DataFolder, FileName);

    private static Dictionary<string, HashSet<string>> _cache;          // completed, per participant
    private static Dictionary<string, HashSet<string>> _skipped;        // skipped, per participant

    /// <summary>Has this participant completed this module (since their last reset)?</summary>
    public static bool IsCompleted(string participantId, string module)
    {
        if (!Counts(participantId) || string.IsNullOrEmpty(module)) return false;
        return Load().TryGetValue(Key(participantId), out HashSet<string> done) && done.Contains(Key(module));
    }

    /// <summary>Did the researcher skip this module for this participant (since their last reset)?</summary>
    public static bool IsSkipped(string participantId, string module)
    {
        if (!Counts(participantId) || string.IsNullOrEmpty(module)) return false;
        Load();
        return _skipped.TryGetValue(Key(participantId), out HashSet<string> s) && s.Contains(Key(module));
    }

    /// <summary>Completed or skipped: either way the participant has moved past it.</summary>
    public static bool IsDone(string participantId, string module) =>
        IsCompleted(participantId, module) || IsSkipped(participantId, module);

    /// <summary>
    /// The module this participant should do next: the first in Sequence not yet done. Null when
    /// all are done, or with no participant ID (nothing is tracked then).
    /// </summary>
    public static string NextModule(string participantId)
    {
        if (!Counts(participantId)) return null;
        foreach (string m in Sequence)
            if (!IsDone(participantId, m)) return m;
        return null;
    }

    /// <summary>
    /// Can this module be started now? With no participant ID, always (so testing is never
    /// blocked). Otherwise only the next module in Sequence. A module that isn't in Sequence (an
    /// extra button wired to onModuleButtonClick) is open unless it is already completed.
    /// </summary>
    public static bool IsUnlocked(string participantId, string module)
    {
        if (!Counts(participantId) || string.IsNullOrEmpty(module)) return true;
        if (Array.IndexOf(Sequence, Key(module)) < 0) return !IsCompleted(participantId, module);
        return Key(module) == NextModule(participantId);
    }

    /// <summary>True when there is a participant ID, so progress is recorded and the order applies.</summary>
    public static bool Tracks(string participantId) => Counts(participantId);

    /// <summary>The current participant's completed modules.</summary>
    public static IReadOnlyCollection<string> CompletedBy(string participantId)
    {
        if (Counts(participantId) && Load().TryGetValue(Key(participantId), out HashSet<string> done))
            return done;
        return Array.Empty<string>();
    }

    /// <summary>Records a completion for a participant. Does nothing with no ID set.</summary>
    public static void MarkCompleted(string participantId, string module)
    {
        string id = participantId;
        if (!Counts(id) || string.IsNullOrEmpty(module)) return;
        if (Append(id, module, "completed"))
        {
            Remember(id, module);
            SessionLog.Record("module_completed", $"{module}, participant {id}");
        }
    }

    /// <summary>
    /// Moves a participant past a module without completing it (the researcher screen's Skip
    /// module), so the next one opens. Does nothing with no ID set.
    /// </summary>
    public static void MarkSkipped(string participantId, string module)
    {
        if (!Counts(participantId) || string.IsNullOrEmpty(module)) return;
        if (Append(participantId, module, "skipped"))
        {
            Load();
            if (!_skipped.TryGetValue(Key(participantId), out HashSet<string> s))
                _skipped[Key(participantId)] = s = new HashSet<string>();
            s.Add(Key(module));
            Version++;
            SessionLog.Record("module_skipped", $"{module}, participant {participantId}");
        }
    }

    /// <summary>Has this participant completed or skipped anything (since their last reset)?</summary>
    public static bool HasProgress(string participantId)
    {
        if (!Counts(participantId)) return false;
        Load();
        string k = Key(participantId);
        return (_cache.TryGetValue(k, out HashSet<string> c) && c.Count > 0) ||
               (_skipped.TryGetValue(k, out HashSet<string> s) && s.Count > 0);
    }

    /// <summary>Clears every tick for a participant (the researcher screen's Reset progress).</summary>
    public static void ResetParticipant(string participantId)
    {
        if (!Counts(participantId)) return;
        if (Append(participantId, "*", "reset"))
        {
            Load().Remove(Key(participantId));
            _skipped.Remove(Key(participantId));
            Version++;
            SessionLog.Record("progress_reset", participantId);
        }
    }

    /// <summary>Forget the cached file, e.g. after it was edited by hand on the PC.</summary>
    public static void Reload()
    {
        _cache = null;
        _skipped = null;
        Version++;
    }

    // ------------------------------------------------------------------ internals

    private static bool Counts(string participantId) =>
        !string.IsNullOrWhiteSpace(participantId) && participantId != StudySession.Unset;

    private static string Key(string s) => s.Trim().ToLowerInvariant();

    private static void Remember(string participantId, string module)
    {
        Dictionary<string, HashSet<string>> all = Load();
        if (!all.TryGetValue(Key(participantId), out HashSet<string> done))
            all[Key(participantId)] = done = new HashSet<string>();
        done.Add(Key(module));
        Version++;
    }

    private static Dictionary<string, HashSet<string>> Load()
    {
        if (_cache != null) return _cache;
        _cache = new Dictionary<string, HashSet<string>>();
        _skipped = new Dictionary<string, HashSet<string>>();
        string path = FilePath;
        if (!File.Exists(path)) return _cache;

        try
        {
            using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                reader.ReadLine();                               // header
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] f = line.Split(',');
                    if (f.Length < 3) continue;
                    string id = Key(Unquote(f[0]));
                    string module = Key(f[1]);
                    string evt = Key(f[2]);
                    if (evt == "reset")
                    {
                        _cache.Remove(id);
                        _skipped.Remove(id);
                    }
                    else if (evt == "completed" || evt == "skipped")
                    {
                        Dictionary<string, HashSet<string>> set = evt == "completed" ? _cache : _skipped;
                        if (!set.TryGetValue(id, out HashSet<string> done))
                            set[id] = done = new HashSet<string>();
                        done.Add(module);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ModuleProgress] Could not read {path} ({e.Message}); no modules will show as done.");
        }
        return _cache;
    }

    private static bool Append(string participantId, string module, string evt)
    {
        string path = FilePath;
        string line = string.Join(",", RunCsvLogger.Escape(participantId), module, evt,
                                  DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            bool isNew = !File.Exists(path);
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                if (isNew) writer.WriteLine(Header);
                writer.WriteLine(line);
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ModuleProgress] Could not write to {path} ({e.Message}) - is it open in Excel? " +
                           $"'{module}' {evt} for {participantId} was not saved, so the menu will not show it.");
            return false;
        }
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            s = s.Substring(1, s.Length - 2).Replace("\"\"", "\"");
        return s;
    }
}
