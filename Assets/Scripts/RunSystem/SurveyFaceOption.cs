using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One face on the post-run survey (PostRunSurvey builds these). Gives feedback without
/// changing the face's colour, since the colour is part of the answer:
///   - pointed at: grows a little;
///   - chosen: grows more and gets a white ring;
///   - once one is chosen, the others fade back so the choice stands out.
/// </summary>
[DisallowMultipleComponent]
public class SurveyFaceOption : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const float HoverScale = 1.08f;
    private const float SelectedScale = 1.15f;
    private const float DimAlpha = 0.45f;
    private const float Speed = 14f;

    private Image _icon;
    private GameObject _ring;
    private bool _hovered;
    private bool _selected;
    private bool _dimmed;

    public void Init(Image icon, GameObject ring)
    {
        _icon = icon;
        _ring = ring;
        SetState(false, false, true);
    }

    /// <param name="selected">This is the chosen face.</param>
    /// <param name="anySelected">A face has been chosen (so the others fade back).</param>
    /// <param name="instant">Jump straight to the new look (when a question first appears).</param>
    public void SetState(bool selected, bool anySelected, bool instant)
    {
        _selected = selected;
        _dimmed = anySelected && !selected;
        if (instant) _hovered = false;
        if (_ring != null) _ring.SetActive(selected);
        if (instant) Apply(1f);
    }

    public void OnPointerEnter(PointerEventData eventData) => _hovered = true;
    public void OnPointerExit(PointerEventData eventData) => _hovered = false;

    private void OnDisable() => _hovered = false;

    private void Update() => Apply(1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime));

    private void Apply(float k)
    {
        float scale = _selected ? SelectedScale : _hovered ? HoverScale : 1f;
        float alpha = _dimmed && !_hovered ? DimAlpha : 1f;

        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * scale, k);
        if (_icon != null)
        {
            Color c = _icon.color;
            c.a = Mathf.Lerp(c.a, alpha, k);
            _icon.color = c;
        }
    }
}
