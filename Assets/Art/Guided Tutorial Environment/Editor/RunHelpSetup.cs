using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRTutorial.EditorTools
{
    /// <summary>
    /// Tools > VR Full Route > Add Help Button to RunSystem
    ///
    /// Runs load RunSystem from the main menu without the Tutorial scene, so the help button
    /// (hold A/X for ~2 s, or Get help in a pause menu) did not exist during a run: the
    /// participant had no way to call the researcher, and the run CSV's assistance column was
    /// always 0.
    ///
    /// This copies the tutorial's help button into RunSystem, under a root called "RunHelp":
    ///   AssisstanceController        - AssistanceController (hold A/X, both hands)
    ///   AssistancePanel_Confirm      - "Hold for assistance" + progress bar
    ///   AssistancePanel_Requested    - "Assistance requested" + Resume
    /// References between the three are pointed at the copies. Everything that belonged to the
    /// tutorial is dropped: TutorialFlow (flow, and the panel hide/restore events), the route
    /// guide line (tap to see the way - not part of runs yet), and the tutorial's pause menu.
    /// Nothing in the Tutorial scene is changed; it is opened read-only and closed unsaved.
    ///
    /// Safe to re-run after the tutorial's panels change: the old copy is replaced, and a Pause
    /// Controller or Spectator Marker that was linked to the run copy is linked again.
    /// </summary>
    public static class RunHelpSetup
    {
        private const string RunScenePath = "Assets/Scenes/RunSystem.unity";
        private const string TutorialScenePath = "Assets/Scenes/Tutorial.unity";
        private const string RootName = "RunHelp";

        [MenuItem("Tools/VR Full Route/Add Help Button to RunSystem", false, 30)]
        public static void AddHelpButton()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene run = EditorSceneManager.OpenScene(RunScenePath, OpenSceneMode.Single);
            Scene tutorial = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Additive);

            try
            {
                Copy(run, tutorial);
            }
            finally
            {
                if (tutorial.IsValid() && tutorial.isLoaded)
                    EditorSceneManager.CloseScene(tutorial, true);   // never saved
                SceneManager.SetActiveScene(run);
            }
        }

        private static void Copy(Scene run, Scene tutorial)
        {
            AssistanceController source = FindIn<AssistanceController>(tutorial);
            if (source == null)
            {
                EditorUtility.DisplayDialog("Add Help Button",
                    "No AssistanceController found in Tutorial.unity, so there is nothing to copy.", "OK");
                return;
            }

            var sourceSo = new SerializedObject(source);
            var confirm = sourceSo.FindProperty("confirmRoot").objectReferenceValue as GameObject;
            var requested = sourceSo.FindProperty("requestedRoot").objectReferenceValue as GameObject;

            // Keep what was linked to the previous run copy by hand, then remove it.
            Object keepPause = null, keepMarker = null;
            foreach (GameObject g in run.GetRootGameObjects())
            {
                if (g.name != RootName) continue;
                var old = g.GetComponentInChildren<AssistanceController>(true);
                if (old != null)
                {
                    var oldSo = new SerializedObject(old);
                    keepPause = oldSo.FindProperty("pauseController").objectReferenceValue;
                    keepMarker = oldSo.FindProperty("spectatorMarker").objectReferenceValue;
                }
                Object.DestroyImmediate(g);
            }

            SceneManager.SetActiveScene(run);
            var root = new GameObject(RootName);
            if (root.scene != run) SceneManager.MoveGameObjectToScene(root, run);

            // Copy each source root under RunHelp, and record original -> copy for every
            // GameObject and component, so references between them can be re-pointed.
            var map = new Dictionary<Object, Object>();
            var copies = new List<GameObject>();
            foreach (GameObject original in new[] { source.gameObject, confirm, requested })
            {
                if (original == null) continue;
                GameObject copy = Object.Instantiate(original, root.transform, true);
                copy.name = original.name;
                copy.SetActive(original.activeSelf);
                MapHierarchy(original.transform, copy.transform, map);
                copies.Add(copy);
            }

            int dropped = 0;
            foreach (GameObject copy in copies)
                foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                    if (c != null && !(c is Transform))
                        dropped += Remap(c, map, tutorial);

            // Put back what was linked to the old copy.
            AssistanceController runController = root.GetComponentInChildren<AssistanceController>(true);
            if (runController != null && (keepPause != null || keepMarker != null))
            {
                var so = new SerializedObject(runController);
                if (keepPause != null) so.FindProperty("pauseController").objectReferenceValue = keepPause;
                if (keepMarker != null) so.FindProperty("spectatorMarker").objectReferenceValue = keepMarker;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(run);
            EditorSceneManager.SaveScene(run);

            string relinked = keepPause != null ? " The run pause menu was linked again." : "";
            Debug.Log($"[RunHelp] Help button copied into RunSystem ({copies.Count} objects under '{RootName}'). " +
                      $"Dropped {dropped} tutorial-only reference(s) and event call(s).{relinked}", root);
            Selection.activeGameObject = runController != null ? runController.gameObject : root;
        }

        private static void MapHierarchy(Transform original, Transform copy, Dictionary<Object, Object> map)
        {
            // Instantiate preserves hierarchy and component order, so a parallel walk pairs them.
            Transform[] a = original.GetComponentsInChildren<Transform>(true);
            Transform[] b = copy.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < a.Length && i < b.Length; i++)
            {
                map[a[i].gameObject] = b[i].gameObject;
                Component[] ca = a[i].GetComponents<Component>();
                Component[] cb = b[i].GetComponents<Component>();
                for (int j = 0; j < ca.Length && j < cb.Length; j++)
                    if (ca[j] != null && cb[j] != null) map[ca[j]] = cb[j];
            }
        }

        /// <summary>
        /// Points references at the copies; clears any that still point into the Tutorial scene
        /// (Unity cannot keep a reference across scenes); removes event calls left with no
        /// target. Returns how many things were dropped.
        /// </summary>
        private static int Remap(Component c, Dictionary<Object, Object> map, Scene tutorial)
        {
            int dropped = 0;
            var so = new SerializedObject(c);
            var callLists = new List<string>();

            SerializedProperty it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference)
                {
                    Object value = it.objectReferenceValue;
                    if (value == null || EditorUtility.IsPersistent(value)) continue;   // assets stay
                    if (map.TryGetValue(value, out Object copy))
                        it.objectReferenceValue = copy;
                    else if (InScene(value, tutorial))
                    {
                        it.objectReferenceValue = null;
                        if (!it.propertyPath.Contains("m_PersistentCalls")) dropped++;
                    }
                }
                else if (it.isArray && it.name == "m_Calls" && it.propertyPath.EndsWith("m_PersistentCalls.m_Calls"))
                {
                    callLists.Add(it.propertyPath);
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // Second pass: event calls whose target was in the tutorial are now empty - remove
            // them rather than leave "Missing" entries that log errors when the event fires.
            if (callLists.Count > 0)
            {
                so.Update();
                foreach (string path in callLists)
                {
                    SerializedProperty calls = so.FindProperty(path);
                    if (calls == null) continue;
                    for (int i = calls.arraySize - 1; i >= 0; i--)
                    {
                        SerializedProperty target = calls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Target");
                        if (target != null && target.objectReferenceValue == null)
                        {
                            calls.DeleteArrayElementAtIndex(i);
                            dropped++;
                        }
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return dropped;
        }

        private static bool InScene(Object value, Scene scene)
        {
            if (value is GameObject g) return g.scene == scene;
            if (value is Component c) return c.gameObject.scene == scene;
            return false;
        }

        private static T FindIn<T>(Scene scene) where T : Component
        {
            foreach (GameObject g in scene.GetRootGameObjects())
            {
                T found = g.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }
    }
}
