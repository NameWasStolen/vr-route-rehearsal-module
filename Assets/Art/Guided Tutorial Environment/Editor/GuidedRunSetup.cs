using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRTutorial.EditorTools
{
    /// <summary>
    /// Step 6 of the statistics plan: the guide line in Guided runs.
    ///
    /// Tools > VR Full Route > Add Guide Line to RunSystem
    ///   Adds a RouteGuideLine called "RunGuideLine" to RunSystem, using the tutorial's
    ///   M_RouteGuide material, in OnRequest mode with no built-in wrong arms. Everything else
    ///   is done at runtime by RunGuidance (added by RunSystemController): the route comes from
    ///   RouteDefinition, it is armed only in Guided runs, and wrong turns come from the route
    ///   tracker. Safe to re-run; it reuses the existing object.
    ///
    /// Tools > VR Tutorial > Route Guide > Add Turn-Around Icon to Wrong-Way Step
    ///   Puts the U-turn icon (Resources/Guidance/TurnAround.png) on the tutorial's "Not this
    ///   way" step, under the wording, so participants meet the same icon the Guided runs show.
    ///   Safe to re-run; it updates the existing icon.
    /// </summary>
    public static class GuidedRunSetup
    {
        private const string RunScenePath = "Assets/Scenes/RunSystem.unity";
        private const string TutorialScenePath = "Assets/Scenes/Tutorial.unity";
        private const string MaterialPath = "Assets/VRTutorial/Materials/M_RouteGuide.mat";
        private const string IconPath = "Assets/Resources/Guidance/TurnAround.png";
        private const string GuideName = "RunGuideLine";
        private const string IconName = "TurnAroundIcon";
        private const string WrongStepName = "WrongWay";

        [MenuItem("Tools/VR Full Route/Add Guide Line to RunSystem", false, 31)]
        public static void AddGuideLine()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene run = EditorSceneManager.OpenScene(RunScenePath, OpenSceneMode.Single);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
                Debug.LogWarning($"[GuidedRun] {MaterialPath} not found. Run Tools > VR Tutorial > Route Guide > " +
                                 "Add To Tutorial Scene once to create it, then re-run this. Without it the line " +
                                 "uses a runtime material, which can go missing in a Quest build.");

            RouteGuideLine guide = null;
            foreach (GameObject root in run.GetRootGameObjects())
            {
                guide = root.GetComponentInChildren<RouteGuideLine>(true);
                if (guide != null) break;
            }

            bool created = false;
            if (guide == null)
            {
                var go = new GameObject(GuideName);
                SceneManager.MoveGameObjectToScene(go, run);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                guide = go.AddComponent<RouteGuideLine>();
                created = true;
            }

            var so = new SerializedObject(guide);
            if (mat != null) so.FindProperty("material").objectReferenceValue = mat;
            so.FindProperty("mode").enumValueIndex = (int)RouteGuideLine.GuideMode.OnRequest;
            so.FindProperty("showOnStart").boolValue = false;
            so.FindProperty("wrongArms").arraySize = 0;           // the route tracker finds wrong turns
            so.FindProperty("requestLogEvent").stringValue = "guide_requested";
            so.FindProperty("wrongTurnLogEvent").stringValue = "guide_wrong_turn";
            so.FindProperty("maxDrawLength").floatValue = 60f;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(run);
            EditorSceneManager.SaveScene(run);
            Selection.activeGameObject = guide.gameObject;
            Debug.Log($"[GuidedRun] {(created ? "Added" : "Updated")} {guide.name} in RunSystem " +
                      $"(material {(mat != null ? mat.name : "missing")}). Guided runs arm it; Unguided never shows it.",
                      guide);
        }

        [MenuItem("Tools/VR Tutorial/Route Guide/Add Turn-Around Icon to Wrong-Way Step", false, 51)]
        public static void AddTurnAroundIcon()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene tutorial = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Single);

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                EditorUtility.DisplayDialog("Turn-around icon", $"{IconPath} was not found.", "OK");
                return;
            }

            TutorialStep wrong = null;
            foreach (TutorialStep step in Object.FindObjectsByType<TutorialStep>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (step.gameObject.scene == tutorial && step.StepName == WrongStepName) { wrong = step; break; }
            if (wrong == null)
            {
                EditorUtility.DisplayDialog("Turn-around icon",
                    "No \"WrongWay\" step in Tutorial.unity. Run Tools > VR Tutorial > Route Guide > Add To " +
                    "Tutorial Scene first.", "OK");
                return;
            }

            Transform existing = wrong.transform.Find(IconName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(IconName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            if (existing == null) rt.SetParent(wrong.transform, false);

            // Under the wording ("Not this way" at y 167, "Please turn around. Follow the blue
            // line." from y 104), inside the step's 600 x 600 frame.
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -160f);
            rt.sizeDelta = new Vector2(220f, 220f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;

            var raw = go.GetComponent<RawImage>();
            if (raw == null) raw = go.AddComponent<RawImage>();
            raw.texture = icon;
            raw.raycastTarget = false;
            EditorUtility.SetDirty(raw);

            EditorSceneManager.MarkSceneDirty(tutorial);
            EditorSceneManager.SaveScene(tutorial);
            Selection.activeGameObject = go;
            Debug.Log($"[GuidedRun] {(existing != null ? "Updated" : "Added")} the turn-around icon on StepWrongWay. " +
                      "Check it sits clear of the wording in the Scene view.", go);
        }
    }
}
