using TMPro;
using UnityEngine;

/// <summary>
/// One label on the route map: an icon, an English name and (for the end-zone picture) a photo,
/// on a white card that turns to face the participant.
///
/// Built by Tools > VR Full Route > Build Route Map Scene. The root's origin is the bottom
/// centre of the card, where its pole meets it, so turning it never moves the pole.
///
/// The card is re-fitted around the text whenever the text's size changes, so it keeps up with
/// the accessibility font-size setting (the text carries a ScalableText).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class RouteMapLabel : MonoBehaviour
{
    [SerializeField] private SpriteRenderer card;
    [SerializeField] private SpriteRenderer icon;
    [SerializeField] private TMP_Text text;
    [SerializeField] private SpriteRenderer photo;

    [Tooltip("Metres.")]
    [SerializeField] private float iconSize = 0.034f;
    [SerializeField] private float padding = 0.008f;
    [SerializeField] private float gap = 0.008f;
    [Tooltip("Width of the photo, metres (0 = no photo).")]
    [SerializeField] private float photoWidth = 0f;

    [Tooltip("Turn to face the participant (yaw only).")]
    [SerializeField] private bool faceViewer = true;

    /// <summary>Width and height of the card (or the icon, with no card), metres.</summary>
    public Vector2 Size { get; private set; }

    private float _laidOutFontSize = -1f;
    private string _laidOutText;

    /// <summary>Called by the editor tool.</summary>
    public void SetUp(SpriteRenderer cardRenderer, SpriteRenderer iconRenderer, TMP_Text label,
                      SpriteRenderer photoRenderer, float iconMetres, float photoMetres)
    {
        card = cardRenderer;
        icon = iconRenderer;
        text = label;
        photo = photoRenderer;
        iconSize = iconMetres;
        photoWidth = photoMetres;
        Layout();
    }

    private void LateUpdate()
    {
        if (text != null && (!Mathf.Approximately(text.fontSize, _laidOutFontSize) || text.text != _laidOutText))
            Layout();

        if (!faceViewer || !Application.isPlaying) return;
        Transform cam = RouteMapView.ViewCamera != null
            ? RouteMapView.ViewCamera
            : (Camera.main != null ? Camera.main.transform : null);
        if (cam == null) return;

        // A sprite or TMP text reads correctly when the camera looks along its +Z.
        Vector3 away = transform.position - cam.position;
        away.y = 0f;
        if (away.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
    }

    /// <summary>Places the icon, text and photo, and sizes the card around them.</summary>
    public void Layout()
    {
        bool hasIcon = icon != null && icon.sprite != null;
        bool hasText = text != null && !string.IsNullOrEmpty(text.text);
        bool hasPhoto = photo != null && photo.sprite != null && photoWidth > 0f;

        float textScale = hasText ? text.transform.localScale.x : 1f;
        Vector2 textSize = Vector2.zero;
        if (hasText)
        {
            Vector2 pref = text.GetPreferredValues(text.text);
            textSize = pref * textScale;
            _laidOutFontSize = text.fontSize;
            _laidOutText = text.text;
        }

        float iconW = hasIcon ? iconSize : 0f;
        float rowW = iconW + (hasIcon && hasText ? gap : 0f) + textSize.x;
        float rowH = Mathf.Max(iconW, textSize.y);

        float photoH = 0f;
        if (hasPhoto)
        {
            Vector2 b = photo.sprite.bounds.size;
            photoH = b.x > 0f ? photoWidth * b.y / b.x : 0f;
        }

        float contentW = Mathf.Max(rowW, hasPhoto ? photoWidth : 0f);
        float contentH = rowH + (hasPhoto ? gap + photoH : 0f);
        bool hasCard = card != null;
        float pad = hasCard ? padding : 0f;
        float cardW = contentW + 2f * pad;
        float cardH = contentH + 2f * pad;

        if (hasCard)
        {
            card.drawMode = SpriteDrawMode.Sliced;
            card.size = new Vector2(cardW, cardH);
            card.transform.localPosition = new Vector3(0f, cardH * 0.5f, 0f);
            card.transform.localRotation = Quaternion.identity;
            card.transform.localScale = Vector3.one;
        }

        Size = new Vector2(cardW, cardH);
        float rowCentreY = cardH - pad - rowH * 0.5f;
        float x = -rowW * 0.5f;

        if (hasIcon)
        {
            float spriteW = Mathf.Max(1e-5f, icon.sprite.bounds.size.x);
            icon.transform.localScale = Vector3.one * (iconSize / spriteW);
            icon.transform.localPosition = new Vector3(x + iconW * 0.5f, rowCentreY, -0.001f);
            icon.transform.localRotation = Quaternion.identity;
            x += iconW + (hasText ? gap : 0f);
        }

        if (hasText)
        {
            RectTransform rt = text.rectTransform;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = textSize / Mathf.Max(1e-5f, textScale);
            rt.localPosition = new Vector3(x, rowCentreY, -0.002f);
            rt.localRotation = Quaternion.identity;
        }

        if (hasPhoto)
        {
            float spriteW = Mathf.Max(1e-5f, photo.sprite.bounds.size.x);
            photo.transform.localScale = Vector3.one * (photoWidth / spriteW);
            photo.transform.localPosition = new Vector3(0f, pad + photoH * 0.5f, -0.001f);
            photo.transform.localRotation = Quaternion.identity;
        }
    }
}
