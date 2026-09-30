using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > VR Study > Participant ID. Set before each participant; every run's CSV rows and its
/// row in run_summaries.csv carry it. Works in and out of Play mode (it is stored in PlayerPrefs,
/// which the Editor and Play mode share), so it can be changed between runs without stopping.
/// </summary>
public class StudySessionWindow : EditorWindow
{
    private string draft;

    [MenuItem("Tools/VR Study/Participant ID")]
    public static void Open()
    {
        var w = GetWindow<StudySessionWindow>(true, "Participant ID");
        w.minSize = new Vector2(380, 190);
        w.draft = StudySession.HasParticipant ? StudySession.ParticipantId : "";
        w.Show();
    }

    private void OnGUI()
    {
        if (draft == null) draft = StudySession.HasParticipant ? StudySession.ParticipantId : "";

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Current participant", StudySession.ParticipantId, EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        draft = EditorGUILayout.TextField("New ID", draft);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = draft != null && draft.Trim() != (StudySession.HasParticipant ? StudySession.ParticipantId : "");
            if (GUILayout.Button("Save"))
            {
                StudySession.SetParticipantId(draft);
                Debug.Log($"[StudySession] Participant ID set to '{StudySession.ParticipantId}'. " +
                          $"Their next run will be run {StudySession.NextRunIndex(StudySession.ParticipantId)}.");
                GUI.FocusControl(null);
            }
            GUI.enabled = StudySession.HasParticipant;
            if (GUILayout.Button("Clear"))
            {
                StudySession.SetParticipantId("");
                draft = "";
                GUI.FocusControl(null);
            }
            GUI.enabled = true;
        }

        EditorGUILayout.Space(4);
        if (StudySession.HasParticipant)
            EditorGUILayout.HelpBox(
                $"Next run for {StudySession.ParticipantId}: run {StudySession.NextRunIndex(StudySession.ParticipantId)}.",
                MessageType.None);
        else
            EditorGUILayout.HelpBox("No participant set. Runs will be saved as 'unset'.", MessageType.Warning);

        EditorGUILayout.Space(4);
        EditorGUILayout.SelectableLabel(StudySession.DataFolder, EditorStyles.miniLabel, GUILayout.Height(16));
        if (GUILayout.Button("Open data folder"))
        {
            Directory.CreateDirectory(StudySession.DataFolder);
            EditorUtility.OpenWithDefaultApp(StudySession.DataFolder);
        }
    }
}
