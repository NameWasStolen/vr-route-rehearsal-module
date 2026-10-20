using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRTutorial;

/// <summary>
/// The "Turn around" signal in Guided runs: a large U-turn icon with the words "Turn around",
/// shown in front of the participant wherever they are looking when they take a wrong turn.
/// Needed because the guide line starts behind them at that moment, where they cannot see it.
///
/// Shown by RunGuidance when the route tracker starts a detour; hidden when they are back on
/// the route street, after showSeconds, or when the pause menu or a help request opens.
///
/// BUILT AT RUNTIME, no scene setup. A small world-space canvas with a HeadLockedUI. Placement
/// and comfort settings (smoothing, obstacle layers, draw-on-top) are copied from the run's
/// help panel (RunHelp), so it behaves like every other head-locked panel; the background and
/// font are copied from that panel too. It scales with the font-size setting (ScalableUIRoot).
/// The icon is Resources/Guidance/TurnAround.png - the same one the tutorial's "Not this way"
/// step shows, so participants have seen it before.
/// </summary>
public class TurnAroundSignal : MonoBehaviour
{
    public const string IconResource = "Guidance/TurnAround";

    [SerializeField] private string label = "Turn around";
    [SerializeField, Min(1f)] private float showSeconds = 6f;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.25f;
    [Tooltip("Head-local placement: X right, Y up, Z forward (metres).")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, -0.1f, 1.5f);
    [SerializeField] private Vector3 rotationOffset = new Vector3(10f, 0f, 0f);

    private GameObject panel;
    private CanvasGroup group;
    private Coroutine fade;
    private float hideAt = -1f;

    public bool IsShowing { get; private set; }

    /// <summary>Builds the signal in the given scene, copying look and placement from 'template'.</summary>
    public static TurnAroundSignal Create(GameObject owner, HeadLockedUI template)
    {
        var s = owner.AddComponent<TurnAroundSignal>();
        s.Build(template);
        return s;
    }

    public void Show()
    {
        if (panel == null) return;
        hideAt = Time.unscaledTime + showSeconds;
        if (IsShowing && fade == null) return;
        IsShowing = true;
        if (!panel.activeSelf) panel.SetActive(true);        // HeadLockedUI snaps in front on enable
        StartFade(1f, false);
    }

    public void Hide()
    {
        if (panel == null || !IsShowing) return;
        IsShowing = false;
        hideAt = -1f;
        StartFade(0f, true);
    }

    private void Update()
    {
        if (!IsShowing) return;
        if (Time.unscaledTime >= hideAt || TutorialPause.IsPaused || AssistanceRequest.IsActive)
            Hide();
    }

    private void OnDestroy()
    {
        if (panel != null) Destroy(panel);
    }

    private void StartFade(float to, bool deactivateAtEnd)
    {
        if (fade != null) StopCoroutine(fade);
        fade = StartCoroutine(Fade(to, deactivateAtEnd));
    }

    private IEnumerator Fade(float to, bool deactivateAtEnd)
    {
        float from = group.alpha;
        float t = 0f;
        while (t < fadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / fadeSeconds));
            yield return null;
        }
        group.alpha = to;
        if (deactivateAtEnd) panel.SetActive(false);
        fade = null;
    }

    // ------------------------------------------------------------------ building
    private void Build(HeadLockedUI template)
    {
        Texture icon = Resources.Load<Texture2D>(IconResource);
        if (icon == null)
            Debug.LogWarning($"[TurnAroundSignal] Icon Resources/{IconResource}.png not found; showing the words only.", this);

        // Built inactive, so HeadLockedUI and ScalableUIRoot only wake once everything is set.
        panel = new GameObject("TurnAroundSignal", typeof(RectTransform));
        panel.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(panel, gameObject.scene);

        var canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        group = panel.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var rt = (RectTransform)panel.transform;
        rt.sizeDelta = new Vector2(600f, 600f);

        Canvas templateCanvas = template != null ? template.GetComponent<Canvas>() : null;
        if (templateCanvas != null)
        {
            canvas.sortingLayerID = templateCanvas.sortingLayerID;
            canvas.sortingOrder = templateCanvas.sortingOrder;
            // Same physical size per UI unit as the other panels. If the template is already
            // magnified by the font-size setting, take that back out: ours applies it itself.
            Vector3 scale = template.transform.localScale;
            if (template.GetComponent<ScalableUIRoot>() != null)
            {
                float f = Mathf.Max(1f, Mathf.Min(AccessibilitySettings.CurrentOrDefault(AccessibilitySettings.DefaultFontScale), 1.6f));
                scale /= f;
            }
            panel.transform.localScale = scale;
        }
        else panel.transform.localScale = Vector3.one * 0.001f;

        // Background: the template's full-size background image, if it has one.
        Image bgTemplate = null;
        TMP_Text fontTemplate = null;
        if (template != null)
        {
            foreach (Image img in template.GetComponentsInChildren<Image>(true))
                if (img.gameObject.name == "Background") { bgTemplate = img; break; }
            if (bgTemplate == null)
                foreach (Image img in template.GetComponentsInChildren<Image>(true))
                {
                    RectTransform r = img.rectTransform;
                    if (r.anchorMin == Vector2.zero && r.anchorMax == Vector2.one) { bgTemplate = img; break; }
                }
            fontTemplate = template.GetComponentInChildren<TMP_Text>(true);
        }

        var bgGo = new GameObject("Background", typeof(RectTransform));
        var bgRt = (RectTransform)bgGo.transform;
        bgRt.SetParent(rt, false);
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
        var bg = bgGo.AddComponent<Image>();
        bg.raycastTarget = false;
        if (bgTemplate != null)
        {
            bg.sprite = bgTemplate.sprite;
            bg.type = bgTemplate.type;
            bg.color = bgTemplate.color;
            bg.pixelsPerUnitMultiplier = bgTemplate.pixelsPerUnitMultiplier;
        }
        else bg.color = new Color(0.12f, 0.14f, 0.18f, 0.92f);

        if (icon != null)
        {
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.SetParent(rt, false);
            iconRt.anchorMin = iconRt.anchorMax = iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.anchoredPosition = new Vector2(0f, 60f);
            iconRt.sizeDelta = new Vector2(340f, 340f);
            var raw = iconGo.AddComponent<RawImage>();
            raw.texture = icon;
            raw.raycastTarget = false;
        }

        var textGo = new GameObject("Label", typeof(RectTransform));
        var textRt = (RectTransform)textGo.transform;
        textRt.SetParent(rt, false);
        textRt.anchorMin = textRt.anchorMax = textRt.pivot = new Vector2(0.5f, 0.5f);
        textRt.anchoredPosition = new Vector2(0f, icon != null ? -195f : 0f);
        textRt.sizeDelta = new Vector2(560f, 110f);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        if (fontTemplate != null)
        {
            text.font = fontTemplate.font;
            text.color = fontTemplate.color;
        }
        else text.color = Color.white;
        text.text = label;
        text.fontSize = 64f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        // Placement and comfort exactly like the run's other head-locked panels.
        var head = panel.AddComponent<HeadLockedUI>();
        if (template != null)
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(template), head);
        else
            Debug.LogWarning("[TurnAroundSignal] No head-locked panel in RunSystem to copy settings from " +
                             "(run Tools > VR Full Route > Add Help and Pause Menu to RunSystem); using defaults.", this);
        head.LocalOffset = localOffset;
        head.RotationOffset = rotationOffset;

        panel.AddComponent<ScalableUIRoot>();
    }
}
