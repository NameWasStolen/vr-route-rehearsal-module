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
    /// Lays out the main menu's "Select Module" page (MainMenuScreen prefab, RunModeSelection):
    /// the Map and the intervention's four modules, in session order (decided with Kade).
    ///
    ///   [Back]         Select Module
    ///   [               Map               ]
    ///   [ 1  Unguided   ] [ 2  Guided      ]
    ///   [ 3  Unguided 2.a] [ 4  Unguided 2.b]
    ///
    ///   Map               -> MenuController.onMapButtonClick        (tabletop route model, 60 s)
    ///   1  Unguided       -> MenuController.onUnguidedButtonClick   (run type unguided_1)
    ///   2  Guided         -> MenuController.onGuidedButtonClick     (run type guided)
    ///   3  Unguided 2.a   -> MenuController.onUnguided2aButtonClick (run type unguided_2a)
    ///   4  Unguided 2.b   -> MenuController.onUnguided2bButtonClick (run type unguided_2b)
    ///
    /// Layout v2 (2 Oct 2026). The first layout was one column of five 42-unit buttons that
    /// reached the bottom edge of the 600 x 400 menu. The menu magnifies as a whole with the text
    /// size (ScalableUIRoot, up to 1.6x about its centre), so at the largest size the bottom
    /// button went into the floor (Kade). Now:
    ///   - the four modules sit in a 2 x 2 grid under a full-width Map button. They read left to
    ///     right, top to bottom, in session order. The lowest button ends 107 units below the
    ///     centre instead of 198;
    ///   - the buttons are bigger targets (64 tall), with larger, centred labels sized to fit;
    ///   - the "Select Module" title sits on one line beside Back. Before, it wrapped onto two
    ///     lines in its 200-wide box and crowded the top button;
    ///   - the menu's ScalableUIRoot is told to keep the whole panel above the floor (see
    ///     ScalableUIRoot.keepAboveFloor), so no page can be magnified into it.
    ///
    /// The existing buttons are reused (renamed, relabelled, moved). Missing ones are copies of
    /// the first module button, so all look the same. Back is not touched.
    ///
    /// Safe to re-run: it finds the buttons by name and only updates them.
    /// </summary>
    public static class ModuleMenuSetup
    {
        private const string PrefabPath = "Assets/Prefabs/UI/MainMenuScreen.prefab";
        private const string SectionName = "RunModeSelection";

        // Canvas units, relative to the menu canvas centre (the canvas is 600 x 400).
        private const float ButtonHeight = 64f;
        private const float WideWidth = 520f;          // Map
        private const float CellWidth = 255f;          // each module button
        private const float ColumnX = 132.5f;          // module columns at -132.5 / +132.5 (10 apart)
        private const float MapY = 85f, Row1Y = 5f, Row2Y = -75f;

        // Labels: centred, as large as fits every button at once (same size on all five).
        private const float LabelMaxSize = 30f, LabelMinSize = 22f, LabelPadX = 16f;

        // Title beside Back (Back spans x -280..-130 at y 161).
        private static readonly Vector2 TitlePos = new Vector2(75f, 161f);
        private static readonly Vector2 TitleSize = new Vector2(390f, 56f);
        private const float TitleMaxSize = 52f, TitleMinSize = 36f;

        // Floor clearance for the whole menu, given to its ScalableUIRoot (world metres).
        private const float FloorY = 0f, FloorClearance = 0.2f;

        private struct Slot
        {
            public string Name;      // GameObject name
            public string OldName;   // existing button to reuse, if any
            public string Label;
            public string Method;    // MenuController method
            public Vector2 Pos;      // canvas units
            public float Width;
        }

        private static readonly Slot[] Slots =
        {
            new Slot { Name = "ModuleButton0_Map",        OldName = "ModuleButton5",  Label = "Map",             Method = "onMapButtonClick",        Pos = new Vector2(0f, MapY),         Width = WideWidth },
            new Slot { Name = "ModuleButton1_Unguided",   OldName = "UnguidedButton", Label = "1  Unguided",     Method = "onUnguidedButtonClick",   Pos = new Vector2(-ColumnX, Row1Y),  Width = CellWidth },
            new Slot { Name = "ModuleButton2_Guided",     OldName = "GuidedButton",   Label = "2  Guided",       Method = "onGuidedButtonClick",     Pos = new Vector2(ColumnX, Row1Y),   Width = CellWidth },
            new Slot { Name = "ModuleButton3_Unguided2a", OldName = null,             Label = "3  Unguided 2.a", Method = "onUnguided2aButtonClick", Pos = new Vector2(-ColumnX, Row2Y),  Width = CellWidth },
            new Slot { Name = "ModuleButton4_Unguided2b", OldName = null,             Label = "4  Unguided 2.b", Method = "onUnguided2bButtonClick", Pos = new Vector2(ColumnX, Row2Y),   Width = CellWidth },
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

            Transform template = Child(section, "UnguidedButton") ?? Child(section, "ModuleButton1_Unguided")
                                 ?? Child(section, "GuidedButton") ?? Child(section, "ModuleButton2_Guided");
            if (template == null) { report = "No existing module button to copy in RunModeSelection."; return false; }

            int baseIndex = template.GetSiblingIndex();
            foreach (Slot s0 in Slots)
            {
                Transform existing = Child(section, s0.Name) ?? (s0.OldName != null ? Child(section, s0.OldName) : null);
                if (existing != null) baseIndex = Mathf.Min(baseIndex, existing.GetSiblingIndex());
            }

            var done = new List<string>();
            var labels = new List<TMP_Text>();
            for (int i = 0; i < Slots.Length; i++)
            {
                Slot slot = Slots[i];
                Transform t = Child(section, slot.Name);
                if (t == null && slot.OldName != null) t = Child(section, slot.OldName);
                if (t == null) t = Object.Instantiate(template.gameObject, section).transform;
                t.name = slot.Name;
                t.SetSiblingIndex(baseIndex + i);
                t.gameObject.SetActive(true);

                var rt = (RectTransform)t;
                Place(rt, canvasRect, section, slot.Pos);
                rt.sizeDelta = new Vector2(slot.Width, ButtonHeight);

                foreach (TMP_Text label in t.GetComponentsInChildren<TMP_Text>(true))
                {
                    label.text = slot.Label;
                    label.name = slot.Name + "_Text";
                    RectTransform lr = label.rectTransform;
                    lr.anchorMin = Vector2.zero;
                    lr.anchorMax = Vector2.one;
                    lr.offsetMin = Vector2.zero;
                    lr.offsetMax = Vector2.zero;
                    label.horizontalAlignment = HorizontalAlignmentOptions.Center;
                    label.verticalAlignment = VerticalAlignmentOptions.Middle;
                    label.margin = new Vector4(LabelPadX, 0f, LabelPadX, 0f);
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                    label.enableAutoSizing = false;    // measured below at a fixed size
                    label.overflowMode = TextOverflowModes.Overflow;
                    labels.Add(label);
                    break;
                }

                var button = t.GetComponent<Button>();
                if (button != null)
                {
                    for (int k = button.onClick.GetPersistentEventCount() - 1; k >= 0; k--)
                        UnityEventTools.RemovePersistentListener(button.onClick, k);
                    var action = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), menu, slot.Method);
                    UnityEventTools.AddPersistentListener(button.onClick, action);
                    EditorUtility.SetDirty(button);
                }
                done.Add($"{slot.Label.Replace("  ", " ")} -> {slot.Method}");
            }

            // One label size for all five: the largest that fits the narrowest button's longest label.
            float size = LabelMaxSize;
            for (; size > LabelMinSize; size -= 1f)
            {
                bool fits = true;
                for (int i = 0; i < labels.Count && fits; i++)
                {
                    labels[i].fontSize = size;
                    float room = Slots[i].Width - 2f * LabelPadX;
                    fits = labels[i].GetPreferredValues(labels[i].text).x <= room;
                }
                if (fits) break;
            }
            foreach (TMP_Text l in labels)
            {
                l.fontSize = size;
                l.enableAutoSizing = true;          // a safety net if a label ever outgrows its button
                l.fontSizeMin = LabelMinSize - 4f;
                l.fontSizeMax = size;
                EditorUtility.SetDirty(l);
            }
            done.Add($"labels {size:0} pt");

            // Title: one line, beside Back.
            Transform titleT = Child(section, "SelectionTitle");
            if (titleT != null)
            {
                var title = titleT.GetComponent<TMP_Text>();
                var tr = (RectTransform)titleT;
                Place(tr, canvasRect, section, TitlePos);
                tr.sizeDelta = TitleSize;
                if (title != null)
                {
                    // The title carried a right margin of about -364 units from earlier editing,
                    // which stretched its text area far past the right edge of its box, so the
                    // "centred" title sat off the edge of the menu (Kade's screenshot, 2 Oct 2026).
                    title.margin = Vector4.zero;
                    title.textWrappingMode = TextWrappingModes.NoWrap;
                    title.overflowMode = TextOverflowModes.Overflow;
                    title.horizontalAlignment = HorizontalAlignmentOptions.Center;
                    title.verticalAlignment = VerticalAlignmentOptions.Middle;
                    float ts = TitleMaxSize;
                    title.fontSize = ts;
                    while (ts > TitleMinSize && title.GetPreferredValues(title.text).x > TitleSize.x)
                        title.fontSize = (ts -= 1f);
                    // And let TMP shrink it at runtime too, in case the measurement here was off.
                    title.enableAutoSizing = true;
                    title.fontSizeMin = TitleMinSize;
                    title.fontSizeMax = ts;
                    EditorUtility.SetDirty(title);
                    done.Add($"title {ts:0} pt");
                }
            }

            // Keep the whole menu above the floor when it is magnified.
            var scaler = root.GetComponent<ScalableUIRoot>();
            if (scaler != null)
            {
                var so = new SerializedObject(scaler);
                SetBool(so, "keepAboveFloor", true);
                SetFloat(so, "floorY", FloorY);
                SetFloat(so, "floorClearance", FloorClearance);
                so.ApplyModifiedPropertiesWithoutUndo();
                done.Add("menu kept above the floor");
            }
            else
            {
                done.Add("no ScalableUIRoot on the menu root, so it is not kept above the floor");
            }

            report = "Module buttons set up in MainMenuScreen: " + string.Join("; ", done) +
                     ". Check the Select Module page in the prefab or at runtime, at the largest text size too.";
            return true;
        }

        /// <summary>Puts a child of the section at a point given in canvas units from the canvas centre.</summary>
        private static void Place(RectTransform rt, RectTransform canvasRect, Transform section, Vector2 canvasPos)
        {
            Vector3 world = canvasRect.TransformPoint(new Vector3(canvasPos.x, canvasPos.y, 0f));
            Vector3 local = section.InverseTransformPoint(world);
            rt.localPosition = new Vector3(local.x, local.y, rt.localPosition.z);
        }

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.boolValue = value;
        }

        private static void SetFloat(SerializedObject so, string name, float value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.floatValue = value;
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
