using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRTutorial
{
    /// <summary>
    /// Lets a world-space Canvas draw over scenery instead of being hidden by it.
    ///
    /// World-space UI depth-tests against the scene like any other object, so a hedge nearer
    /// than the panel covers it. Switching this on gives every Graphic under this object a
    /// material copy whose depth test is Always; switching it off puts the original materials
    /// back and destroys the copies, so nothing is left modified when it is not needed.
    ///
    /// Driven by HeadLockedUI, which only switches it on while scenery is nearer than the
    /// panel's minimum distance.
    /// </summary>
    [DisallowMultipleComponent]
    public class UIDrawOnTop : MonoBehaviour
    {
        // The property UI/Default and the TextMeshPro shaders read for their ZTest.
        private static readonly int ZTestModeId = Shader.PropertyToID("unity_GUIZTestMode");

        private struct Swap
        {
            public Material original;
            public Material copy;
            public bool originalWasDefault;
        }

        private readonly Dictionary<UnityEngine.UI.Graphic, Swap> _swaps =
            new Dictionary<UnityEngine.UI.Graphic, Swap>();

        private bool _onTop;

        public bool OnTop
        {
            get => _onTop;
            set
            {
                if (_onTop == value) return;
                _onTop = value;
                if (!isActiveAndEnabled) return;
                if (_onTop) Apply();
                else Restore();
            }
        }

        private void OnEnable()
        {
            if (_onTop) Apply();
        }

        private void OnDisable() => Restore();

        private void Apply()
        {
            // Inactive children are included, so elements that appear later in a lesson (a
            // progress ring, a banner) are already covered when they switch on.
            foreach (var graphic in GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            {
                if (_swaps.ContainsKey(graphic)) continue;

                Material original = GetMaterial(graphic);
                if (original == null) continue;

                var copy = new Material(original)
                {
                    name = original.name + " (On Top)",
                    hideFlags = HideFlags.DontSave
                };
                copy.SetInt(ZTestModeId, (int)CompareFunction.Always);

                _swaps[graphic] = new Swap
                {
                    original = original,
                    copy = copy,
                    originalWasDefault = !(graphic is TMP_Text) &&
                                         !(graphic is TMP_SubMeshUI) &&
                                         original == graphic.defaultMaterial
                };
                SetMaterial(graphic, copy);
            }
        }

        private void Restore()
        {
            foreach (var pair in _swaps)
            {
                var graphic = pair.Key;
                var swap = pair.Value;

                // Only put the original back if nothing else has replaced the copy meanwhile.
                if (graphic != null && GetMaterial(graphic) == swap.copy)
                    SetMaterial(graphic, swap.originalWasDefault ? null : swap.original);

                if (swap.copy != null)
                {
                    if (Application.isPlaying) Destroy(swap.copy);
                    else DestroyImmediate(swap.copy);
                }
            }
            _swaps.Clear();
        }

        // TextMeshPro's `material` getter creates a new instance every time it is first read,
        // so its shared material is used directly instead.
        private static Material GetMaterial(UnityEngine.UI.Graphic graphic)
        {
            if (graphic is TMP_Text text) return text.fontSharedMaterial;
            if (graphic is TMP_SubMeshUI sub) return sub.sharedMaterial;
            return graphic.material;
        }

        private static void SetMaterial(UnityEngine.UI.Graphic graphic, Material material)
        {
            if (graphic is TMP_Text text) text.fontSharedMaterial = material;
            else if (graphic is TMP_SubMeshUI sub) sub.sharedMaterial = material;
            else graphic.material = material;
        }
    }
}
