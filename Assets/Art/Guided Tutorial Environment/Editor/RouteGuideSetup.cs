using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace VRTutorial.EditorTools
{
    /// <summary>
    /// Tools > VR Tutorial > Route Guide > Add To Tutorial Scene
    ///
    /// Creates (or reuses) the M_RouteGuide material, adds a RouteGuideLine to the scene that
    /// holds TutorialFlow, and wires:
    ///   StepGoToExit  TutorialStep.onStepEnter          -> RouteGuideLine.Show
    ///   end zone      TutorialZoneTrigger.onPlayerEntered -> RouteGuideLine.Hide
    ///   A/X tap       AssistanceController.onTapped       -> RouteGuideLine.RequestShow
    ///   wrong turn    RouteGuideLine.onWrongTurn          -> TutorialFlow.RevealStep("WrongWay")
    /// and creates StepWrongWay (a copy of StepGoToExit with the wrong-way wording) if missing.
    /// Re-running it is safe: it reuses the existing object and never adds a listener twice.
    /// Anything it cannot find is reported in the Console for wiring by hand.
    ///
    /// Tutorial scene only - do not run this with a study scene open.
    /// </summary>
    public static class RouteGuideSetup
    {
        private const string MaterialPath = "Assets/VRTutorial/Materials/M_RouteGuide.mat";
        private const string ObjectName   = "RouteGuide";
        private const string ExitStepName = "GoToExit";
        private const string WrongStepName = "WrongWay";
        private const float  WrongStepSeconds = 6f;

        // Short, plain, and naming the line by what they can see. Participant-facing, so keep it
        // in step with the rest of the module's wording if that changes.
        private const string WrongTitle = "Not this way";
        private const string WrongBody  = "Please turn around. Follow the blue line.";

        [MenuItem("Tools/VR Tutorial/Route Guide/Add To Tutorial Scene", false, 50)]
        public static void AddToScene()
        {
            TutorialFlow flow = Object.FindAnyObjectByType<TutorialFlow>(FindObjectsInactive.Include);
            if (flow == null)
            {
                EditorUtility.DisplayDialog("Route Guide",
                    "No TutorialFlow found in the open scenes. Open Tutorial.unity first.", "OK");
                return;
            }
            Scene scene = flow.gameObject.scene;

            Material mat = GetOrCreateMaterial();

            RouteGuideLine guide = Object.FindAnyObjectByType<RouteGuideLine>(FindObjectsInactive.Include);
            if (guide == null)
            {
                var go = new GameObject(ObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Add Route Guide");
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                guide = Undo.AddComponent<RouteGuideLine>(go);
            }

            var so = new SerializedObject(guide);
            so.FindProperty("material").objectReferenceValue = mat;
            so.ApplyModifiedProperties();

            WireShow(guide);
            WireHide(guide);
            WireTap(guide);
            WireWrongWay(guide, flow);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = guide.gameObject;
        }

        private static Material GetOrCreateMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogError("[RouteGuide] URP Unlit shader not found.");
                return null;
            }

            string folder = System.IO.Path.GetDirectoryName(MaterialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }

            // The texture is generated at runtime by RouteGuideLine; this asset exists so the
            // shader is referenced by the scene and therefore included in the build.
            mat = new Material(shader) { name = "M_RouteGuide" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static void WireShow(RouteGuideLine guide)
        {
            foreach (TutorialStep step in Object.FindObjectsByType<TutorialStep>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (step.StepName != ExitStepName) continue;
                if (HasListener(step.onStepEnter, guide, nameof(RouteGuideLine.Show)))
                {
                    Debug.Log("[RouteGuide] StepGoToExit already shows the line.", step);
                    return;
                }
                Undo.RecordObject(step, "Wire Route Guide Show");
                UnityEventTools.AddPersistentListener(step.onStepEnter, guide.Show);
                EditorUtility.SetDirty(step);
                Debug.Log("[RouteGuide] Wired StepGoToExit.onStepEnter -> RouteGuideLine.Show", step);
                return;
            }
            Debug.LogWarning("[RouteGuide] No step named \"" + ExitStepName + "\". Build it first " +
                             "(see go-to-exit-step.md), or wire the step that tells them to walk to " +
                             "the finish: TutorialStep.onStepEnter -> RouteGuideLine.Show.");
        }

        private static void WireHide(RouteGuideLine guide)
        {
            foreach (TutorialZoneTrigger zone in Object.FindObjectsByType<TutorialZoneTrigger>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!IsEndZone(zone)) continue;
                if (HasListener(zone.onPlayerEntered, guide, nameof(RouteGuideLine.Hide)))
                {
                    Debug.Log("[RouteGuide] End zone already hides the line.", zone);
                    return;
                }
                Undo.RecordObject(zone, "Wire Route Guide Hide");
                UnityEventTools.AddPersistentListener(zone.onPlayerEntered, guide.Hide);
                EditorUtility.SetDirty(zone);
                Debug.Log("[RouteGuide] Wired " + zone.name + ".onPlayerEntered -> RouteGuideLine.Hide", zone);
                return;
            }
            Debug.LogWarning("[RouteGuide] Could not identify the end-zone trigger. Wire it by hand: " +
                             "TutorialZoneTrigger.onPlayerEntered -> RouteGuideLine.Hide. (The line " +
                             "also hides itself within 2 m of the end-zone centre.)");
        }

        /// <summary>The end zone is the trigger that reveals the "End" step, or one named for it.</summary>
        private static bool IsEndZone(TutorialZoneTrigger zone)
        {
            var calls = new SerializedObject(zone).FindProperty("onPlayerEntered.m_PersistentCalls.m_Calls");
            if (calls != null)
            {
                for (int i = 0; i < calls.arraySize; i++)
                {
                    SerializedProperty call = calls.GetArrayElementAtIndex(i);
                    string method = call.FindPropertyRelative("m_MethodName").stringValue;
                    string arg = call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
                    if (method == "RevealStep" && arg == "End") return true;
                }
            }
            string n = zone.name.ToLowerInvariant();
            return n.Contains("endzone") || n.Contains("end zone") || n.Contains("end_zone");
        }

        private static void WireTap(RouteGuideLine guide)
        {
            var assistance = Object.FindAnyObjectByType<AssistanceController>(FindObjectsInactive.Include);
            if (assistance == null)
            {
                Debug.LogWarning("[RouteGuide] No AssistanceController found. Wire a tap by hand: " +
                                 "AssistanceController.onTapped -> RouteGuideLine.RequestShow.");
                return;
            }
            if (HasListener(assistance.onTapped, guide, nameof(RouteGuideLine.RequestShow))) return;
            Undo.RecordObject(assistance, "Wire Route Guide Tap");
            UnityEventTools.AddPersistentListener(assistance.onTapped, guide.RequestShow);
            EditorUtility.SetDirty(assistance);
            Debug.Log("[RouteGuide] Wired AssistanceController.onTapped -> RouteGuideLine.RequestShow", assistance);
        }

        private static void WireWrongWay(RouteGuideLine guide, TutorialFlow flow)
        {
            TutorialStep wrong = FindStep(WrongStepName);
            if (wrong == null) wrong = CreateWrongWayStep(flow);
            if (wrong == null) return;

            if (HasListener(guide.onWrongTurn, flow, nameof(TutorialFlow.RevealStep))) return;
            Undo.RecordObject(guide, "Wire Route Guide Wrong Turn");
            UnityEventTools.AddStringPersistentListener(guide.onWrongTurn,
                new UnityAction<string>(flow.RevealStep), WrongStepName);
            EditorUtility.SetDirty(guide);
            Debug.Log("[RouteGuide] Wired RouteGuideLine.onWrongTurn -> TutorialFlow.RevealStep(\"" +
                      WrongStepName + "\")", guide);
        }

        /// <summary>
        /// Copies StepGoToExit - already the right shape: a message with no controls and no task -
        /// and gives it the wrong-way wording and its own timed dismissal. Inserted straight after
        /// StepGoToExit in the hierarchy and in TutorialFlow.steps. It has no ITutorialTask, so
        /// review-return logic never lands on it.
        /// </summary>
        private static TutorialStep CreateWrongWayStep(TutorialFlow flow)
        {
            TutorialStep exit = FindStep(ExitStepName);
            if (exit == null)
            {
                Debug.LogWarning("[RouteGuide] Cannot create StepWrongWay: no \"" + ExitStepName +
                                 "\" step to copy. Build that first (go-to-exit-step.md).");
                return null;
            }

            GameObject go = Object.Instantiate(exit.gameObject, exit.transform.parent);
            go.name = "StepWrongWay";
            Undo.RegisterCreatedObjectUndo(go, "Create StepWrongWay");
            go.transform.SetSiblingIndex(exit.transform.GetSiblingIndex() + 1);

            TutorialStep step = go.GetComponent<TutorialStep>();
            var so = new SerializedObject(step);
            so.FindProperty("stepName").stringValue = WrongStepName;
            // The copy inherits GoToExit's listeners (RouteGuideLine.Show, the 8 s dismissal).
            // Neither belongs here.
            so.FindProperty("onStepEnter.m_PersistentCalls.m_Calls").ClearArray();
            so.FindProperty("onStepExit.m_PersistentCalls.m_Calls").ClearArray();
            so.ApplyModifiedPropertiesWithoutUndo();

            UnityEventTools.AddFloatPersistentListener(step.onStepEnter, flow.FinishAndDismissAfter,
                                                       WrongStepSeconds);

            SetText(go.transform, "TitleText", WrongTitle);
            SetText(go.transform, "BodyText", WrongBody);

            // Insert into the flow right after GoToExit, so indices of later steps stay in order.
            var flowSo = new SerializedObject(flow);
            SerializedProperty steps = flowSo.FindProperty("steps");
            int at = steps.arraySize;
            for (int i = 0; i < steps.arraySize; i++)
                if (steps.GetArrayElementAtIndex(i).objectReferenceValue == exit) { at = i + 1; break; }
            steps.InsertArrayElementAtIndex(at);
            steps.GetArrayElementAtIndex(at).objectReferenceValue = step;
            flowSo.ApplyModifiedProperties();

            EditorUtility.SetDirty(step);
            Debug.Log("[RouteGuide] Created StepWrongWay after StepGoToExit. Check its wording and " +
                      "panel size in the Inspector.", go);
            return step;
        }

        private static void SetText(Transform root, string child, string text)
        {
            Transform t = FindDeep(root, child);
            TMP_Text label = t != null ? t.GetComponent<TMP_Text>() : null;
            if (label == null)
            {
                Debug.LogWarning("[RouteGuide] StepWrongWay has no " + child + " - set its wording by hand.", root);
                return;
            }
            Undo.RecordObject(label, "Set Wrong Way Text");
            label.text = text;
            EditorUtility.SetDirty(label);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                Transform hit = FindDeep(c, name);
                if (hit != null) return hit;
            }
            return null;
        }

        private static TutorialStep FindStep(string stepName)
        {
            foreach (TutorialStep s in Object.FindObjectsByType<TutorialStep>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (s.StepName == stepName) return s;
            return null;
        }

        private static bool HasListener(UnityEngine.Events.UnityEventBase evt, Object target, string method)
        {
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
                if (evt.GetPersistentTarget(i) == target && evt.GetPersistentMethodName(i) == method)
                    return true;
            return false;
        }
    }
}
