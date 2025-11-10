using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CardView : MonoBehaviour
{
    [Header("Refs")]
    public Image image;                 // Root Image (card front/back)
    public TextMeshProUGUI hanziText;   // Main label
    public Button button;               // Click target

    [Header("Optional UI")]
    public TextMeshProUGUI cornerBadge; // Tiny badge (top-right) for tone/W

    [Header("Sprites")]
    public Sprite backSprite;           // Sprite when face-down
    public Sprite frontSpriteOverride;  // Optional front override

    int _handIndex = -1;
    Action<int> _onClick;
    Sprite _frontSpriteInitial;

    void Reset()
    {
        image = GetComponent<Image>();
        if (!button) button = GetComponent<Button>();
        if (!hanziText) hanziText = GetComponentInChildren<TextMeshProUGUI>();
    }

    void Awake()
    {
        if (!image) image = GetComponent<Image>();
        if (!button) button = GetComponent<Button>();
        if (!hanziText) hanziText = GetComponentInChildren<TextMeshProUGUI>();
        _frontSpriteInitial = image ? image.sprite : null;

        // Badge should never impact layout or intercept clicks
        if (cornerBadge)
        {
            var le = cornerBadge.GetComponent<LayoutElement>();
            if (!le) le = cornerBadge.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            cornerBadge.raycastTarget = false;
            HideBadge();
        }
    }

    /// <summary>Bind a face-up card instance.</summary>
    public void Bind(Card c, int handIndex, Action<int> onClick)
    {
        _handIndex = handIndex;
        _onClick = onClick;

        if (button)
        {
            button.onClick.RemoveAllListeners();
            if (onClick != null) button.onClick.AddListener(() => _onClick?.Invoke(_handIndex));
            button.interactable = true;
        }

        // Reset adornments every time
        HideBadge();

        // Ensure front sprite is visible
        if (image)
            image.sprite = frontSpriteOverride
                ? frontSpriteOverride
                : (_frontSpriteInitial ? _frontSpriteInitial : image.sprite);

        if (c.type == CardType.Effect)
        {
            // Prefer title from cards.json; fallback to short defaults
            string label =
                !string.IsNullOrWhiteSpace(c.hanzi) ? c.hanzi :
                (c.effect == EffectType.DrawTwoToneLinked ? "＋2" :
                 c.effect == EffectType.WildToneSetter   ? "變調" :
                 "Effect");

            if (hanziText) hanziText.text = label;

            // Show a small badge only for effects
            if (cornerBadge)
            {
                if (c.effect == EffectType.DrawTwoToneLinked)
                {
                    int tone = 0; foreach (var r in c.AllReadings()) { tone = r.tone; break; }
                    cornerBadge.text = (tone >= 1 && tone <= 4) ? tone.ToString() : "?";
                    cornerBadge.gameObject.SetActive(true);
                }
                else if (c.effect == EffectType.WildToneSetter)
                {
                    cornerBadge.text = "W";
                    cornerBadge.gameObject.SetActive(true);
                }
            }
        }
        else
        {
            // Regular Hanzi
            if (hanziText) hanziText.text = c.hanzi;
            // badge remains hidden
        }

        SetFaceVisual(true);
    }

    /// <summary>Bind a face-down slot (opponent hand).</summary>
    public void BindFaceDown(int handIndex)
    {
        _handIndex = handIndex;
        _onClick = null;

        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = false;
        }

        if (image && backSprite) image.sprite = backSprite;
        if (hanziText) hanziText.text = "";
        HideBadge();

        SetFaceVisual(false);
    }

    // --- helpers ---

    void HideBadge()
    {
        if (!cornerBadge) return;
        cornerBadge.text = "";
        cornerBadge.gameObject.SetActive(false);

        var le = cornerBadge.GetComponent<LayoutElement>();
        if (le) le.ignoreLayout = true;
    }

    void SetFaceVisual(bool faceUp)
    {
        var cg = GetComponent<CanvasGroup>();
        if (cg) cg.alpha = 1f; // tweak if you want to dim face-down
    }
}
