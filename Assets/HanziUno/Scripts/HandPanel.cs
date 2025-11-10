using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HandPanel : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Parent RectTransform under which CardViews are spawned. If left empty, this object's RectTransform is used.")]
    public RectTransform container;

    [Tooltip("Prefab with Image+Button+TMP and a CardView script that exposes Bind(...) / BindFaceDown(...).")]
    public CardView cardPrefab;

    [Tooltip("If true, this hand is the opponent's and should be rendered face-down (via RenderFaceDown).")]
    public bool isOpponent = false;

    [Header("Layout")]
    [Tooltip("When ON, uses the Horizontal/Vertical Layout Group(s) on 'container'. When OFF, uses a simple manual fan layout.")]
    public bool useLayoutGroup = true;

    [Tooltip("Manual layout only: approximate visual card width for spacing.")]
    public float manualCardWidth = 140f;

    [Tooltip("Manual layout only: 0..1 overlap factor; higher = more overlap = tighter hand.")]
    [Range(0.0f, 0.95f)] public float manualOverlap = 0.65f;

    [Tooltip("Manual layout only: vertical offset for all cards (pixels).")]
    public float manualY = 0f;

    // --- internal ---
    readonly List<CardView> _items = new List<CardView>();
    CanvasGroup _cg;
    bool _interactable = true;

    void Awake()
    {
        if (container == null)
            container = (RectTransform)transform;

        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        _cg.interactable   = true;
        _cg.blocksRaycasts = true;
        _cg.alpha          = 1f;
    }

    /// <summary>Enable/disable the entire hand's interactivity.</summary>
    public void SetInteractable(bool on)
    {
        _interactable = on;

        if (_cg != null)
        {
            _cg.interactable   = on;
            _cg.blocksRaycasts = on;
            // Visual dim if you want:
            // _cg.alpha = on ? 1f : 0.8f;
        }

        // Also toggle child buttons
        for (int i = 0; i < _items.Count; i++)
        {
            var cv = _items[i];
            if (!cv) continue;
            var btn = cv.GetComponent<Button>();
            if (btn) btn.interactable = on;
        }
    }

    /// <summary>Render a face-up hand (player). Clicks call onClick(index).</summary>
    public void Render(List<Card> cards, Action<int> onClick)
    {
        if (cards == null) { Clear(); return; }

        EnsurePool(cards.Count);

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (i < cards.Count)
            {
                item.gameObject.SetActive(true);

                int capturedIndex = i; // capture for closure
                item.Bind(cards[i], capturedIndex, idx =>
                {
                    if (!_interactable) return;
                    onClick?.Invoke(idx);
                });

                var btn = item.GetComponent<Button>();
                if (btn) btn.interactable = _interactable;
            }
            else
            {
                item.gameObject.SetActive(false);
            }
        }

        ApplyLayout(cards.Count);
    }

    /// <summary>Render a face-down opponent hand (by count only).</summary>
    public void RenderFaceDown(int count)
    {
        EnsurePool(count);

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (i < count)
            {
                item.gameObject.SetActive(true);
                item.BindFaceDown(i);

                var btn = item.GetComponent<Button>();
                if (btn) btn.interactable = false; // never clickable
            }
            else
            {
                item.gameObject.SetActive(false);
            }
        }

        ApplyLayout(count);
    }

    /// <summary>Disable all pooled items (kept for reuse).</summary>
    public void Clear()
    {
        for (int i = 0; i < _items.Count; i++)
            if (_items[i]) _items[i].gameObject.SetActive(false);

        // If using a layout group, still rebuild once so previous layout doesn’t “stick”
        if (useLayoutGroup && container)
            LayoutRebuilder.ForceRebuildLayoutImmediate(container);
    }

    // --- helpers ---

    void EnsurePool(int needed)
    {
        if (!cardPrefab)
        {
            Debug.LogError("[HandPanel] Card Prefab not assigned.", this);
            return;
        }

        while (_items.Count < needed)
        {
            var cv = Instantiate(cardPrefab, container);
            cv.gameObject.SetActive(false);

            // Ensure a Button exists on root (CardView typically already has one)
            if (!cv.GetComponent<Button>()) cv.gameObject.AddComponent<Button>();

            // Good defaults if prefab lacks them
            var le = cv.GetComponent<LayoutElement>();
            if (le == null) le = cv.gameObject.AddComponent<LayoutElement>();
            if (le.preferredWidth  <= 0f) le.preferredWidth  = manualCardWidth;
            if (le.preferredHeight <= 0f) le.preferredHeight = manualCardWidth * 1.4f;

            _items.Add(cv);
        }
    }

    void ApplyLayout(int activeCount)
    {
        if (container == null) return;

        if (useLayoutGroup)
        {
            // Force Unity to recompute layout immediately so items don’t stack or drift
            LayoutRebuilder.ForceRebuildLayoutImmediate(container);
            return;
        }

        // Manual simple "fan" layout (centered, overlapped, no rotation)
        float w = manualCardWidth;
        float step = w * (1f - Mathf.Clamp01(manualOverlap));
        float total = (activeCount <= 0) ? 0f : (w + (activeCount - 1) * step);
        float startX = -total * 0.5f;

        int idx = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (!item.gameObject.activeSelf) continue;

            var rt = (RectTransform)item.transform;
            rt.anchoredPosition = new Vector2(startX + idx * step, manualY);
            rt.localRotation = Quaternion.identity;

            idx++;
        }
    }
}
