using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fades the player's whole view to a solid colour and back. Used to cover scene transitions,
/// where the world changes instantly around a stationary player - in VR that is uncomfortable
/// to watch, so the standard practice is simply not to show it.
///
/// Lives on the XR rig, in Bootstrap. It must NOT live in MainMenu or Tutorial: those scenes
/// are unloaded by the very transition this is covering.
///
/// Builds its own quad and material at runtime, so there is nothing to author in the editor -
/// add the component, and it parents a fullscreen quad to the camera on Awake.
///
/// Note this is NOT a Screen Space - Overlay canvas. Overlay canvases do not render to an HMD.
/// </summary>
[DisallowMultipleComponent]
public class ScreenFader : MonoBehaviour
{
    public static ScreenFader Instance { get; private set; }

    [Header("Appearance")]
    [Tooltip("Colour faded to. Pure black reads as 'the headset has died' to some people - a " +
             "very dark neutral is gentler while still hiding the scene completely.")]
    [SerializeField] private Color fadeColour = new Color(0.02f, 0.02f, 0.03f, 1f);

    [Header("Placement")]
    [Tooltip("Camera to attach to. Leave empty to use Camera.main.")]
    [SerializeField] private Transform head;

    [Tooltip("Metres in front of the camera. Must sit beyond the near clip plane but nearer " +
             "than anything else you want covered - world-space UI sits at ~1.5 m.")]
    [SerializeField] private float distance = 0.2f;

    [Tooltip("Quad edge length in metres. Generously oversized on purpose: HMD projections are " +
             "off-axis and wider than Camera.fieldOfView suggests, so this is not computed.")]
    [SerializeField] private float size = 4f;

    [Header("Fallback")]
    [Tooltip("Optional. Assign an unlit transparent shader if the URP one cannot be found.")]
    [SerializeField] private Shader overrideShader;

    private MeshRenderer _renderer;
    private Material _material;
    private float _alpha;

    /// <summary>Current opacity, 0 = clear, 1 = fully hidden.</summary>
    public float Alpha => _alpha;

    /// <summary>True while the view is fully or partly covered.</summary>
    public bool IsCovering => _alpha > 0.001f;

    private void Awake()
    {
        Instance = this;
        BuildOverlay();
        SetAlpha(0f);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_material != null) Destroy(_material);
    }

    private void BuildOverlay()
    {
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (head == null)
        {
            Debug.LogError("[ScreenFader] No camera found - fades will do nothing.", this);
            return;
        }

        var go = new GameObject("ScreenFadeOverlay");
        go.transform.SetParent(head, false);
        go.transform.localPosition = new Vector3(0f, 0f, distance);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        // Built by hand rather than PrimitiveType.Quad so it arrives with no collider.
        float h = size * 0.5f;
        var mesh = new Mesh { name = "ScreenFadeQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-h, -h, 0f), new Vector3(-h, h, 0f),
            new Vector3( h,  h, 0f), new Vector3( h, -h, 0f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        mesh.RecalculateBounds();

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        _renderer = go.AddComponent<MeshRenderer>();
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        _material = new Material(ResolveShader()) { name = "ScreenFadeMaterial" };
        ConfigureTransparent(_material);
        _renderer.sharedMaterial = _material;
    }

    private Shader ResolveShader()
    {
        if (overrideShader != null) return overrideShader;

        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("Unlit/Color");
        if (s == null) s = Shader.Find("Sprites/Default");
        return s;
    }

    private void ConfigureTransparent(Material m)
    {
        // URP surface-type setup. Guarded with HasProperty so a fallback shader without these
        // properties still works rather than spamming errors every frame.
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // 1 = Transparent
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);       // 0 = Alpha
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);         // double-sided: winding can't bite us

        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");

        // Overlay queue: drawn after every other transparent, including world-space UI.
        m.renderQueue = (int)RenderQueue.Overlay;
    }

    /// <summary>Sets opacity immediately. The overlay is disabled entirely at 0 to avoid overdraw.</summary>
    public void SetAlpha(float alpha)
    {
        _alpha = Mathf.Clamp01(alpha);

        if (_material != null)
        {
            Color c = fadeColour;
            c.a = _alpha;
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", c);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", c);
        }

        if (_renderer != null) _renderer.enabled = _alpha > 0.001f;
    }

    /// <summary>
    /// Fades to a target opacity. Uses unscaled time so a paused game still transitions, and
    /// smoothsteps so there is no visible velocity change at either end.
    /// </summary>
    public IEnumerator FadeTo(float target, float duration)
    {
        target = Mathf.Clamp01(target);

        if (duration <= 0f)
        {
            SetAlpha(target);
            yield break;
        }

        float start = _alpha;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            SetAlpha(Mathf.Lerp(start, target, k * k * (3f - 2f * k)));
            yield return null;
        }

        SetAlpha(target);
    }

    /// <summary>Convenience for UnityEvents - fades out over the given seconds.</summary>
    public void FadeOut(float duration) => StartCoroutine(FadeTo(1f, duration));

    /// <summary>Convenience for UnityEvents - fades back in over the given seconds.</summary>
    public void FadeIn(float duration) => StartCoroutine(FadeTo(0f, duration));
}
