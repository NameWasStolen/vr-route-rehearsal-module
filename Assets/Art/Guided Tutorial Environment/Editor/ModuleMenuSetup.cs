using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VRTutorial.EditorTools
{
    /// <summary>
    /// Tools > VR Full Route > Set Up Module Buttons
    ///
    /// Turns the main menu's "Select Module" page (MainMenuScreen prefab, RunModeSelection) into
    /// the Map plus the intervention's four modules, in session order (decided with Kade):
    ///
    ///   Map               -> MenuController.onMapButtonClick        (tabletop route model, 60 s)
    ///   1  Unguided       -> MenuController.onUnguidedButtonClick   (run type unguided_1)
    ///   2  Guided         -> MenuController.onGuidedButtonClick     (run type guided)
    ///   3  Unguided 2.a   -> MenuController.onUnguided2aButtonClick (run type unguided_2a)
    ///   4  Unguided 2.b   -> MenuController.onUnguided2bButtonClick (run type unguided_2b)
    ///
    /// The Map is the fifth button (2 Oct 2026). It takes over the old hidden ModuleButton5 if
    /// the prefab has one, and moves to the top, because it is seen before the first unguided
    /// run. It is unnumbered: it is not a module and records no run.
    ///
    /// The existing UnguidedButton and GuidedButton are reused (renamed, relabelled, moved); the
    /// others are copies of UnguidedButton, so they look and behave the same. The column sits
    /// under the "Select Module" title, centred like the old buttons, 42 units tall on a 48 pitch
    /// so all five fit on the 600 x 400 menu. They are only as wide as the longest label plus
    /// PaddingX either side, and every label starts at that padding, so the names line up.
    /// Title and Back are not touched.
    ///
    /// Needs Tools > VR Full Route > Build Route Map Scene for the Map to have a scene to load.
    ///
    /// Safe to re-run: it finds the buttons by name and only updates them.
    /// </summary>
    public static class ModuleMenuSetup
    {
        private const string PrefabPath = "Assets/Prefabs/UI/MainMenuScreen.prefab";
        private const string SectionName = "RunModeSelection";

        // Canvas units, relative to the menu canvas centre (the canvas is 600 x 400).
        private static readonly float[] SlotY = { 15f, -33f, -81f, -129f, -177f };
        private const float ButtonHeight = 42f;

        // Buttons are only as wide as the longest label needs, plus this much space either side
        // (canvas units). Labels are left-aligned at the same padding, so the numbers and the
        // names after them line up in a column.
        private const float PaddingX = 32f;
        private const float MinButtonWidth = 200f;

        private struct Slot
        {
            public string Name;      // GameObject name
            public string OldName;   // existing button to reuse, if any
            public string Label;
            public string Method;    // MenuController method, or null
        }

        private static readonly Slot[] Slots =
        {
            new Slot { Name = "ModuleButton0_Map",        OldName = "ModuleButton5",  Label = "Map",             Method = "onMapButtonClick" },
            new Slot { Name = "ModuleButton1_Unguided",   OldName = "UnguidedButton", Label = "1  Unguided",     Method = "onUnguidedButtonClick" },
            new Slot { Name = "ModuleButton2_Guided",     OldName = "GuidedButton",   Label = "2  Guided",       Method = "onGuidedButtonClick" },
            new Slot { Name = "ModuleButton3_Unguided2a", OldName = null,             Label = "3  Unguided 2.a", Method = "onUnguided2aButtonClick" },
            new Slot { Name = "ModuleButton4_Unguided2b", OldName = null,             Label = "4  Unguided 2.b", Method = "onUnguided2bButtonClick" },
        };

        [MenuItem("Tools/VR Full Route/Set Up Module Buttons", false, 40)]
        public static void SetUp()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                EditorUtility.DisplayDialog("Module buttons", $"Could not open {PrefabPath}.", "OK");
                return;
            }

            try
            {
                if (Apply(root, out string report))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log("[ModuleMenu] " + report);
                }
                else
                {
                    EditorUtility.DisplayDialog("Module buttons", report, "OK");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool Apply(GameObject root, out string report)
        {
            Transform section = FindDeep(root.transform, SectionName);
            if (section == null) { report = $"No '{SectionName}' in the menu prefab."; return false; }

            var menu = root.GetComponentInChildren<MenuController>(true);
            if (menu == null) { report = "No MenuController in the menu prefab."; return false; }

            RectTransform canvasRect = root.GetComponent<RectTransform>();

            // Template for the new buttons: the old unguided button (or whichever slot exists).
            Transform template = Child(section, "UnguidedButton") ?? Child(section, "ModuleButton1_Unguided")
                                 ?? Child(section, "GuidedButton") ?? Child(section, "ModuleButton2_Guided");
            if (template == null) { report = "No existing module button to copy in RunModeSelection."; return false; }

            // Keep the five together in the hierarchy, in order, where the old buttons were.
            int baseIndex = template.GetSiblingIndex();
            foreach (Slot s0 in Slots)
            {
                Transform existing = Child(section, s0.Name) ?? (s0.OldName != null ? Child(section, s0.OldName) : null);
                if (existing != null) baseIndex = Mathf.Min(baseIndex, existing.GetSiblingIndex());
            }

            var done = new List<string>();
            var rects = new List<RectTransform>();
            float widest = 0f;
            for (int i = 0; i < Slots.Length; i++)
            {
                Slot slot = Slots[i];
                Transform t = Child(section, slot.Name);
                if (t == null && slot.OldName != null) t = Child(section, slot.OldName);
                if (t == null)
                {
                    t = Object.Instantiate(template.gameObject, section).transform;
                }
                t.name = slot.Name;
                t.SetSiblingIndex(baseIndex + i);

                // Position: the column under the title, centred on the canvas like the old buttons.
                var rt = (RectTransform)t;
                Vector3 world = canvasRect.TransformPoint(new Vector3(0f, SlotY[i], 0f));
                Vector3 local = section.InverseTransformPoint(world);
                rt.localPosition = new Vector3(local.x, local.y, rt.localPosition.z);
                rects.Add(rt);

                // Label.
                foreach (TMP_Text label in t.GetComponentsInChildren<TMP_Text>(true))
                {
                    label.text = slot.Label;
                    label.name = slot.Name + "_Text";

                    // Fill the button, then start every label at the same distance from its left edge.
                    RectTransform lr = label.rectTransform;
                    lr.anchorMin = Vector2.zero;
                    lr.anchorMax = Vector2.one;
                    lr.offsetMin = Vector2.zero;
                    lr.offsetMax = Vector2.zero;
                    label.horizontalAlignment = HorizontalAlignmentOptions.Left;
                    label.verticalAlignment = VerticalAlignmentOptions.Middle;
                    label.margin = new Vector4(PaddingX, 0f, 0f, 0f);
                    widest = Mathf.Max(widest, label.GetPreferredValues(label.text).x);
                    EditorUtility.SetDirty(label);
                    break;
                }

                // Click.
                var button = t.GetComponent<Button>();
                if (button != null)
                {
                    for (int k = button.onClick.GetPersistentEventCount() - 1; k >= 0; k--)
                        UnityEventTools.RemovePersistentListener(button.onClick, k);
                    if (slot.Method != null)
                    {
                        var action = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), menu, slot.Method);
                        UnityEventTools.AddPersistentListener(button.onClick, action);
                    }
                    EditorUtility.SetDirty(button);
                }

                // Every slot is in use now; a slot without a method would wait, switched off.
                t.gameObject.SetActive(slot.Method != null);
                done.Add($"{slot.Label.Replace("  ", " ")}{(slot.Method != null ? " -> " + slot.Method : " (hidden)")}");
            }

            // One width for all five, from the longest label, so they still form a neat column.
            float width = Mathf.Max(MinButtonWidth, Mathf.Ceil(widest + 2f * PaddingX));
            foreach (RectTransform r in rects) r.sizeDelta = new Vector2(width, ButtonHeight);
            done.Add($"buttons {width:0} x {ButtonHeight:0} (longest label {widest:0})");

            report = "Module buttons set up in MainMenuScreen: " + string.Join("; ", done) +
                     ". Check the Select Module page in the prefab or at runtime.";
            return true;
        }

        private static Transform Child(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) return parent.GetChild(i);
            return null;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
