using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TurnManager : MonoBehaviour
{
    [Header("Center / Discard")]
    public CardView discardCurrentView;
    public CardView discardPreviousView;
    public TextMeshProUGUI topDetailText;   // initials/finals/tones OR effect info
    public TextMeshProUGUI matchRuleText;   // what matched last turn (also shows tone lock)

    [Header("Deck & Hands")]
    public DeckManager deck;
    public HandPanel playerHandPanel;
    public HandPanel botHandPanel;

    [Header("Controls & Messages")]
    public Button drawButton;
    public TextMeshProUGUI messageText;

    [Header("Tone Picker UI")]
    public TonePickerUI tonePicker; // assign the TonePickerPanel (with TonePickerUI)

    // State
    public List<Card> playerHand = new();
    public List<Card> botHand = new();
    public int turn = 0; // 0=player, 1=bot
    bool playing = false;

    Card previousTop = null;                    // card under current top (for effect-neutral matching)
    RuleEngine.MatchContext lastMatchContext = null;

    // When >0, the NEXT player must play a card that has this tone (bypasses normal matching)
    int pendingToneLock = 0;

    const string HILITE = "#21A0AA";

    void Start() => NewGame();

    public void NewGame()
    {
        var cards = CardsDatabase.Load();
        if (cards == null || cards.Count == 0)
        {
            Debug.LogError("No cards loaded. Check Resources/cards.json");
            return;
        }

        deck.Init(cards);
        playerHand.Clear(); botHand.Clear();

        for (int i = 0; i < 8; i++) { playerHand.Add(deck.Draw()); botHand.Add(deck.Draw()); }

        previousTop = null;
        lastMatchContext = null;
        pendingToneLock = 0;

        var starter = deck.Draw();
        if (starter != null) deck.Play(starter);

        turn = 0;
        playing = true;
        SetMessage("Your turn: play a matching card or Draw");
        RefreshUI();
        UpdateCenterUI();
    }

    // ----- INPUT -----

    public void DrawButton()
    {
        if (!playing || turn != 0) return;

        var c = deck.Draw();
        if (c != null) playerHand.Add(c);
        lastMatchContext = null;

        // Drawing consumes and clears any constraint
        pendingToneLock = 0;

        SetMessage("You drew a card. Bot's turn…");
        turn = 1;
        RefreshUI();
        UpdateCenterUI();
        Invoke(nameof(BotTurn), 0.6f);
    }

    public void TryPlayFromPlayer(int index)
    {
        if (!playing || turn != 0) return;
        if (index < 0 || index >= playerHand.Count) return;

        var card = playerHand[index];

        // 1) Tone lock bypass: only check tone
        if (pendingToneLock > 0)
        {
            if (!RuleEngine.HasTone(card, pendingToneLock))
            {
                SetMessage($"Tone lock: must play tone {pendingToneLock}.");
                return;
            }

            var ctx = new RuleEngine.MatchContext { ok = true, by = "tone", topReading = new Reading("", "", pendingToneLock) };
            pendingToneLock = 0; // satisfied
            ApplyPlay(index, card, ctx, isPlayer: true, deferTonePick: false);
            return;
        }

        // 2) Wild is always legal (if player plays it, we’ll ask tone unless it's the last card)
        if (card.type == CardType.Effect && card.effect == EffectType.WildToneSetter)
        {
            ApplyPlay(index, card, null, isPlayer: true, deferTonePick: true);
            return;
        }

        // 3) Normal matching, but if an effect is on top, compare to previousTop
        var matchTarget = TopForMatching();
        var res = RuleEngine.CanPlayOn(matchTarget, card);
        if (!res.ok) { SetMessage($"Can't play {CardLabel(card)}: {res.reason}"); return; }

        ApplyPlay(index, card, res, isPlayer: true, deferTonePick: false);
    }

    // Which card should rules match against right now?
    Card TopForMatching()
    {
        var top = deck.Top;
        if (top != null && top.type == CardType.Effect && pendingToneLock == 0)
        {
            // While an effect sits on top and no tone lock is active,
            // match against the card under it.
            return previousTop ?? top;
        }
        return top;
    }

    // ----- APPLY -----

    void ApplyPlay(int handIndex, Card card, RuleEngine.MatchContext res, bool isPlayer, bool deferTonePick)
    {
        var top = deck.Top;
        previousTop = top;           // remember what was on the pile before this play
        lastMatchContext = res;

        // remove & place
        if (isPlayer) playerHand.RemoveAt(handIndex);
        else          botHand.RemoveAt(handIndex);
        deck.Play(card);

        // ===== Immediate win checks before any deferred UI =====
        if (isPlayer && playerHand.Count == 0)
        {
            // If last card is Wild, we do NOT block on choosing a tone — you win now.
            playing = false;
            pendingToneLock = 0;
            SetMessage("You win!");
            RefreshUI();
            UpdateCenterUI();
            return;
        }
        if (!isPlayer && botHand.Count == 0)
        {
            playing = false;
            pendingToneLock = 0;
            SetMessage("Bot wins!");
            RefreshUI();
            UpdateCenterUI();
            return;
        }

        // ===== EFFECTS =====
        if (card.type == CardType.Effect)
        {
            if (card.effect == EffectType.DrawTwoToneLinked)
            {
                // Opponent draws 2 and is skipped
                var d1 = deck.Draw(); var d2 = deck.Draw();
                if (turn == 0) { if (d1!=null) botHand.Add(d1); if (d2!=null) botHand.Add(d2); }
                else           { if (d1!=null) playerHand.Add(d1); if (d2!=null) playerHand.Add(d2); }

                // Clear tone lock; subsequent matching will reference previousTop via TopForMatching().
                pendingToneLock = 0;

                // Skip opponent (current player goes again)
                turn = 1 - turn; // to opponent
                turn = 1 - turn; // back to current
                SetMessage($"{(isPlayer ? "You" : "Bot")} played Draw-2. {(isPlayer ? "Bot" : "You")} drew 2 and was skipped.");
            }
            else if (card.effect == EffectType.WildToneSetter)
            {
                if (deferTonePick && isPlayer)
                {
                    // Only ask for tone if it wasn't your last card (we already returned above if last)
                    drawButton.interactable = false;
                    if (playerHandPanel) playerHandPanel.SetInteractable(false);

                    if (tonePicker != null)
                    {
                        tonePicker.ShowPick(tone =>
                        {
                            pendingToneLock = Mathf.Clamp(tone, 1, 4);

                            drawButton.interactable = true;
                            if (playerHandPanel) playerHandPanel.SetInteractable(true);

                            SetMessage($"Tone set to {pendingToneLock}. Opponent must play tone {pendingToneLock}.");
                            turn = 1;
                            RefreshUI();
                            UpdateCenterUI();
                            Invoke(nameof(BotTurn), 0.6f);
                        });

                        RefreshUI();
                        UpdateCenterUI();
                        return; // wait for callback
                    }
                    else
                    {
                        pendingToneLock = 1;
                        SetMessage($"(No tone picker wired) Tone set to 1.");
                    }
                }
                else if (!isPlayer) // Bot wild → choose tone heuristically
                {
                    int t = ChooseBotTone();
                    pendingToneLock = (t >= 1 && t <= 4) ? t : 1;
                    SetMessage($"Bot set tone to {pendingToneLock}. You must play tone {pendingToneLock}.");
                }
            }
        }

        // pass turn (unless we’re in tone-picking defer path)
        if (playing && !(card.type == CardType.Effect && card.effect == EffectType.WildToneSetter && deferTonePick && isPlayer))
        {
            turn = isPlayer ? 1 : 0;
            if (!isPlayer) SetMessage("Bot played a card.");
        }

        RefreshUI();
        UpdateCenterUI();

        if (playing && isPlayer && !(card.type == CardType.Effect && card.effect == EffectType.WildToneSetter && deferTonePick))
            Invoke(nameof(BotTurn), 0.6f);
    }

    // ----- BOT -----

    void BotTurn()
    {
        if (!playing || turn != 1) return;

        // Tone-lock against bot
        if (pendingToneLock > 0)
        {
            for (int i = 0; i < botHand.Count; i++)
            {
                var c = botHand[i];
                if (RuleEngine.HasTone(c, pendingToneLock))
                {
                    var ctx = new RuleEngine.MatchContext { ok = true, by = "tone", topReading = new Reading("", "", pendingToneLock) };
                    pendingToneLock = 0;
                    ApplyPlay(i, c, ctx, isPlayer: false, deferTonePick: false);
                    return;
                }
            }
            // No card satisfies → draw and end bot’s turn (lock clears)
            var d = deck.Draw(); if (d != null) botHand.Add(d);
            pendingToneLock = 0;
            SetMessage("Bot drew (tone lock). Your turn.");
            turn = 0;
            RefreshUI();
            UpdateCenterUI();
            return;
        }

        // Effect-neutral target when an effect sits on top
        var matchTarget = TopForMatching();

        // Try Draw-2 first if legal
        for (int i = 0; i < botHand.Count; i++)
        {
            var c = botHand[i];
            if (c.type == CardType.Effect && c.effect == EffectType.DrawTwoToneLinked)
            {
                var res = RuleEngine.CanPlayOn(matchTarget, c);
                if (res.ok) { ApplyPlay(i, c, res, isPlayer: false, deferTonePick: false); return; }
            }
        }

        // Try Wild (always legal)
        for (int i = 0; i < botHand.Count; i++)
        {
            var c = botHand[i];
            if (c.type == CardType.Effect && c.effect == EffectType.WildToneSetter)
            {
                ApplyPlay(i, c, new RuleEngine.MatchContext { ok = true, by = "effect", reason = "Wild tone setter." }, isPlayer: false, deferTonePick: false);
                return;
            }
        }

        // Else, any Hanzi that’s legal
        for (int i = 0; i < botHand.Count; i++)
        {
            var c = botHand[i];
            if (c.type != CardType.Hanzi) continue;
            var res = RuleEngine.CanPlayOn(matchTarget, c);
            if (res.ok) { ApplyPlay(i, c, res, isPlayer: false, deferTonePick: false); return; }
        }

        // Else draw
        var d2 = deck.Draw(); if (d2 != null) botHand.Add(d2);
        SetMessage("Bot drew a card. Your turn.");
        turn = 0;
        RefreshUI();
        UpdateCenterUI();
    }

    int ChooseBotTone()
    {
        // simple heuristic: most frequent tone in bot’s hand (fallback 1)
        var counts = new int[6];
        foreach (var c in botHand)
            foreach (var r in c.AllReadings())
                if (r.tone >= 1 && r.tone <= 4) counts[r.tone]++;

        int best = 1; int bestC = -1;
        for (int t = 1; t <= 4; t++) if (counts[t] > bestC) { bestC = counts[t]; best = t; }
        return best;
    }

    // ----- UI -----

    void RefreshUI()
    {
        playerHandPanel?.Render(playerHand, i => TryPlayFromPlayer(i));
        botHandPanel?.RenderFaceDown(botHand.Count);
        if (drawButton) drawButton.interactable = (turn == 0 && playing);
    }

    void UpdateCenterUI()
    {
        // Previous
        if (discardPreviousView)
        {
            if (previousTop != null) discardPreviousView.Bind(previousTop, -1, null);
            else discardPreviousView.BindFaceDown(-1);
            var cgPrev = discardPreviousView.GetComponent<CanvasGroup>();
            if (cgPrev) cgPrev.alpha = previousTop != null ? 0.6f : 0f;
        }
        // Current
        var top = deck.Top;
        if (discardCurrentView)
        {
            if (top != null) discardCurrentView.Bind(top, -1, null);
            else discardCurrentView.BindFaceDown(-1);
            var cgTop = discardCurrentView.GetComponent<CanvasGroup>();
            if (cgTop) cgTop.alpha = 1f;
        }
        // Details
        RenderTopDetails(top, lastMatchContext);
    }

    void RenderTopDetails(Card top, RuleEngine.MatchContext match)
    {
        // First, call out tone-lock if any
        if (matchRuleText)
        {
            if (pendingToneLock > 0) matchRuleText.SetText($"Tone lock: <b><color={HILITE}>{pendingToneLock}</color></b>");
            else if (match != null && match.ok && !string.IsNullOrEmpty(match.by))
                matchRuleText.SetText(match.by == "terminal" && !string.IsNullOrEmpty(match.terminalSymbol)
                    ? $"Matched by: <b><color={HILITE}>{match.by}</color></b> ({match.terminalSymbol})"
                    : $"Matched by: <b><color={HILITE}>{match.by}</color></b>");
            else matchRuleText.SetText("");
        }

        // Effects on top
        if (top != null && top.type == CardType.Effect)
        {
            if (topDetailText)
            {
                if (top.effect == EffectType.DrawTwoToneLinked)
                {
                    int t = 0; foreach (var r in top.AllReadings()) { t = r.tone; break; }
                    topDetailText.SetText($"Top: <b><color={HILITE}>Draw-2</color></b>  {(t>=1 && t<=4 ? $"(tone {t})" : "")}");
                }
                else
                {
                    // FIX: ensure string interpolation
                    topDetailText.SetText($"Top: <b><color={HILITE}>Wild</color></b> (set tone)");
                }
            }
            return;
        }

        // Default (hanzi or empty)
        if (top == null)
        {
            if (topDetailText) topDetailText.SetText("initials: -   finals: -   tones: -");
            return;
        }

        var (inis, fins, tones) = top.DistinctReadingSets();
        string Hi(string s) => $"<b><color={HILITE}>{s}</color></b>";

        var initialsRendered = new List<string>(inis);
        var finalsRendered   = new List<string>(fins);
        var tonesRendered    = new List<string>();
        foreach (var t in tones) tonesRendered.Add(t.ToString());

        if (match != null && match.ok)
        {
            if (match.by == "initial" && match.topReading != null)
            {
                string hiIni = match.topReading.initial ?? "";
                for (int i=0;i<initialsRendered.Count;i++) if (initialsRendered[i]==hiIni) initialsRendered[i]=Hi(initialsRendered[i]);
            }
            else if ((match.by == "final" || match.by == "terminal") && match.topReading != null)
            {
                string hiFinal = match.by == "final" ? (match.topReading.final ?? "") : null;
                string term = match.by == "terminal" ? match.terminalSymbol : null;
                for (int i=0;i<finalsRendered.Count;i++)
                {
                    var f = finalsRendered[i];
                    if (!string.IsNullOrEmpty(hiFinal) && f==hiFinal) finalsRendered[i]=Hi(f);
                    else if (!string.IsNullOrEmpty(term) && !string.IsNullOrEmpty(f) && f.Contains(term))
                    {
                        int idx = f.LastIndexOf(term);
                        finalsRendered[i] = idx>=0 ? f[..idx] + Hi(term) + f[(idx+term.Length)..] : f;
                    }
                }
            }
            else if (match.by == "tone" && match.topReading != null)
            {
                for (int i=0;i<tonesRendered.Count;i++)
                {
                    var tStr = tonesRendered[i];
                    if (int.TryParse(tStr, out int t) && t == match.topReading.tone)
                        tonesRendered[i] = Hi(tStr);
                }
            }
        }

        string ii = initialsRendered.Count > 0 ? string.Join("/", initialsRendered) : "-";
        string ff = finalsRendered.Count   > 0 ? string.Join("/", finalsRendered)   : "-";
        string tt = tonesRendered.Count    > 0 ? string.Join("/", tonesRendered)    : "-";

        if (topDetailText) topDetailText.SetText($"initials: {ii}   finals: {ff}   tones: {tt}");
    }

    string CardLabel(Card c) => c.type == CardType.Effect ? $"[{c.effect}]" : c.hanzi;

    void SetMessage(string s) { if (messageText) messageText.SetText(s); }
}
