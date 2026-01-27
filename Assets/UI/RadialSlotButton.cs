using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RadialSlotButton : MonoBehaviour
{
    [SerializeField] private Image bg;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text label;

    public RectTransform Rect { get; private set; }

    void Awake() => Rect = (RectTransform)transform;

    public void Set(Sprite iconSprite, string labelText)
    {
        if (icon)
        {
            icon.sprite = iconSprite;
            icon.enabled = iconSprite != null;
        }

        if (label)
            label.text = labelText ?? "";
    }
    public void SetHighlight(bool on)
    {
        // super simple feedback: scale
        transform.localScale = on ? Vector3.one * 1.15f : Vector3.one;
        if (bg)
        {
            var c = bg.color;
            c.a = on ? 1f : 0.6f;
            bg.color = c;
        }
    }
}
