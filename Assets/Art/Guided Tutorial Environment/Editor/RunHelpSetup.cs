using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRTutorial.EditorTools
{
    /// <summary>
    /// Tools > VR Full Route > Add Help and Pause Menu to RunSystem
    ///
    /// Runs load RunSystem from the main menu without the Tutorial scene, so neither the help
    /// button (hold A/X, or Get Help in the pause menu) nor the pause menu (B/Y) existed during
    /// a run. This copies both from the tutorial into RunSystem, under a root called "RunHelp":
    ///
    ///   AssisstanceController        - AssistanceController (hold A/X, both hands)
    ///   AssistancePanel_Confirm      - "Hold for assistance" + progress bar
    ///   AssistancePanel_Requested    - "Assistance requested" + Resume
    ///   PauseController              - PauseController (B/Y, both hands)
    ///   PauseMenuPanel               - the pause menu, trimmed for the module
    ///
    /// The pause menu is made into the module's version, not the tutorial's:
    ///   - lesson buttons removed (any button that called TutorialFlow - Walking Guide, Turning
    ///     Guide), and TutorialResetController removed (its runtime Start Again button);
    ///   - Resume -> the copied PauseController.Close, Get Help -> the copied
    ///     AssistanceController.Request (both re-pointed automatically);
    ///   - Back to Menu -> RunSystemController.ExitRun, replacing ReturnToMainMenu, which is the
    ///     tutorial's scene switch;
    ///   - the remaining buttons laid out in one centred column, and a stray leading apostrophe
    ///     stripped from button labels.
    /// The help button is linked to the pause menu, so a help request closes the menu and pausing
    /// is off while the help panel is up - the same as in the tutorial.
    ///
    /// Everything else that belonged to the tutorial is dropped: TutorialFlow references and
    /// events, and the route guide line tap (not part of runs yet). Nothing in the Tutorial scene
    /// is changed; it is opened read-only and closed unsaved.
    ///
    /// Safe to re-run whenever the tutorial's panels change: the old RunHelp is replaced.
    /// </summary>
    public static class RunHelpSetup
    {
        private const string RunScenePath = "Assets/Scenes/RunSystem.unity";
        private const string TutorialScenePath = "Assets/Scenes/Tutorial.unity";
        private const string RootName = "RunHelp";
        private const float ButtonSpacing = 200f;

        [MenuItem("Tools/VR Full Route/Add Help and Pause Menu to RunSystem", false, 30)]
        public static void AddHelpAndPause()
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
                EditorUtility.DisplayDialog("Add Help and Pause Menu",
                    "No AssistanceController found in Tutorial.unity, so there is nothing to copy.", "OK");
                return;
            }
            var sourceSo = new SerializedObject(source);
            var confirm = sourceSo.FindProperty("confirmRoot").objectReferenceValue as GameObject;
            var requested = sourceSo.FindProperty("requestedRoot").objectReferenceValue as GameObject;

            PauseController pauseSource = FindIn<PauseController>(tutorial);
            GameObject menuSource = null;
            if (pauseSource != null)
                menuSource = new SerializedObject(pauseSource).FindProperty("menuRoot").objectReferenceValue as GameObject;
            else
                Debug.LogWarning("[RunHelp] No PauseController in Tutorial.unity - copying the help button only.");

            // Lesson buttons: any pause-menu button that calls into TutorialFlow.
            var lessonButtons = new HashSet<Button>();
            if (menuSource != null)
                foreach (Button b in menuSource.GetComponentsInChildren<Button>(true))
                    if (CallsType<TutorialFlow>(b.onClick)) lessonButtons.Add(b);

            // Keep a spectator marker linked by hand to the old copy, then remove the old copy.
            Object keepMarker = null;
            foreach (GameObject g in run.GetRootGameObjects())
            {
                if (g.name != RootName) continue;
                var old = g.GetComponentInChildren<AssistanceController>(true);
                if (old != null)
                    keepMarker = new SerializedObject(old).FindProperty("spectatorMarker").objectReferenceValue;
                Object.DestroyImmediate(g);
            }

            // A pause menu added to RunSystem by hand would answer B/Y as well - two menus.
            foreach (GameObject g in run.GetRootGameObjects())
                foreach (PauseController other in g.GetComponentsInChildren<PauseController>(true))
                    Debug.LogWarning($"[RunHelp] RunSystem already has a PauseController on '{other.name}' " +
                                     "(outside RunHelp). Delete it - otherwise B/Y opens two pause menus.", other);

            SceneManager.SetActiveScene(run);
            var root = new GameObject(RootName);
            if (root.scene != run) SceneManager.MoveGameObjectToScene(root, run);

            // Copy each source root under RunHelp, and record original -> copy for every
            // GameObject and component, so references between them can be re-pointed.
            var map = new Dictionary<Object, Object>();
            var copies = new List<GameObject>();
            var originals = new List<GameObject> { source.gameObject, confirm, requested };
            if (pauseSource != null) originals.Add(pauseSource.gameObject);
            if (menuSource != null && menuSource != pauseSource.gameObject) originals.Add(menuSource);
            foreach (GameObject original in originals)
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

            AssistanceController runHelp = root.GetComponentInChildren<AssistanceController>(true);
            if (runHelp != null && keepMarker != null)
            {
                var so = new SerializedObject(runHelp);
                so.FindProperty("spectatorMarker").objectReferenceValue = keepMarker;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            string pauseReport = "no pause menu";
            if (pauseSource != null && menuSource != null)
                pauseReport = MakeRunPauseMenu(root, run, map, lessonButtons, runHelp);

            EditorSceneManager.MarkSceneDirty(run);
            EditorSceneManager.SaveScene(run);

            Debug.Log($"[RunHelp] Copied into RunSystem under '{RootName}': help button, {pauseReport}. " +
                      $"Dropped {dropped} tutorial-only reference(s) and event call(s).", root);
            Selection.activeGameObject = root;
        }

        /// <summary>Turns the copied tutorial pause menu into the module's. Returns a summary.</summary>
        private static string MakeRunPauseMenu(GameObject root, Scene run, Dictionary<Object, Object> map,
                                               HashSet<Button> lessonButtons, AssistanceController runHelp)
        {
            PauseController pause = root.GetComponentInChildren<PauseController>(true);
            if (pause == null) return "no pause menu";
            GameObject menu = new SerializedObject(pause).FindProperty("menuRoot").objectReferenceValue as GameObject;
            if (menu == null) return "pause controller without a menu";

            // Start Again belongs to the tutorial (it is added at runtime by this component).
            foreach (var reset in pause.GetComponents<TutorialResetController>())
                Object.DestroyImmediate(reset);

            // Lesson buttons.
            int removed = 0;
            foreach (Button original in lessonButtons)
                if (map.TryGetValue(original.gameObject, out Object copy) && copy != null)
                {
                    Object.DestroyImmediate(copy);
                    removed++;
                }

            // Back to Menu: end the run properly instead of the tutorial's scene switch.
            RunSystemController runController = FindIn<RunSystemController>(run);
            bool exitWired = false;
            foreach (Button b in menu.GetComponentsInChildren<Button>(true))
            {
                var returner = b.GetComponent<ReturnToMainMenu>();
                if (returner == null && !CallsType<ReturnToMainMenu>(b.onClick)) continue;
                for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                    if (b.onClick.GetPersistentTarget(i) is ReturnToMainMenu)
                        UnityEventTools.RemovePersistentListener(b.onClick, i);
                if (returner != null) Object.DestroyImmediate(returner);
                if (runController != null)
                {
                    UnityEventTools.AddPersistentListener(b.onClick, runController.ExitRun);
                    exitWired = true;
                }
                EditorUtility.SetDirty(b);
            }
            if (!exitWired)
                Debug.LogWarning("[RunHelp] Could not wire Back to Menu: no RunSystemController in RunSystem, " +
                                 "or no button used ReturnToMainMenu.", menu);

            // Tidy labels ("'Get Help" -> "Get Help") and lay the buttons out in one column,
            // keeping their top-to-bottom order.
            var buttons = menu.GetComponentsInChildren<Button>(true)
                              .Where(b => b.transform.parent == menu.transform)
                              .Select(b => (RectTransform)b.transform)
                              .OrderByDescending(r => r.anchoredPosition.y)
                              .ToList();
            float top = (buttons.Count - 1) * 0.5f * ButtonSpacing;
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].anchoredPosition = new Vector2(0f, top - i * ButtonSpacing);
                foreach (TMP_Text label in buttons[i].GetComponentsInChildren<TMP_Text>(true))
                {
                    string trimmed = label.text.TrimStart('\'', '\u2018', '\u2019').TrimStart();
                    if (trimmed != label.text) { label.text = trimmed; EditorUtility.SetDirty(label); }
                }
            }

            // Help closes the menu and pausing is off while the help panel is up.
            if (runHelp != null)
            {
                var so = new SerializedObject(runHelp);
                so.FindProperty("pauseController").objectReferenceValue = pause;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            string names = string.Join(", ", buttons.Select(b => b.name));
            return $"pause menu with {buttons.Count} buttons ({names}), {removed} lesson button(s) removed" +
                   (exitWired ? ", Back to Menu -> RunSystemController.ExitRun" : "");
        }

        /// <summary>Does any persistent call on this event target a component of type T?</summary>
        private static bool CallsType<T>(UnityEventBase evt) where T : Object
        {
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
                if (evt.GetPersistentTarget(i) is T) return true;
            return false;
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
