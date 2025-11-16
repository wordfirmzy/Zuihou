using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum PlayerKind
{
    LocalHuman,
    Bot
}

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

    [Header("Agents")]
    [Tooltip("Optional agent for the local human seat (seat 0).")]
    public LocalHumanAgent localHumanAgent;

    // ---- State ----

    // Backing hand lists for first two seats (You + Bot).
    public List<Card> playerHand = new();
    public List<Card> botHand = new();

    // Generic multi-player model
    List<PlayerState> players = new();
    PlayerOrder turnOrder;

    // Exposed for any existing code that reads it; kept in sync with turnOrder.CurrentIndex.
    public int turn = 0; // index in players list; 0 = local human in current setup

    bool playing = false;

    // Card under current top (used when an effect sits on top and there is no tone lock)
    Card previousTop = null;

    RuleEngine.MatchContext lastMatchContext = null;

    // When >0, the NEXT player must play a card that has this tone (bypasses normal matching)
    int pendingToneLock = 0;

    const string HILITE = "#21A0AA";

    // Convenience: who is currently active?
    PlayerState CurrentPlayer =>
        (turnOrder != null &&
         players.Count > 0 &&
         turnOrder.CurrentIndex >= 0 &&
         turnOrder.CurrentIndex < players.Count)
            ? players[turnOrder.CurrentIndex]
            : null;

    void Start() => NewGame();

    public void NewGame()
    {
        var cards = CardsDatabase.Load();
        if (cards == null || cards.Count == 0)
        {
            Debug.LogError("No cards loaded.\nCheck Resources/cards.json");
            return;
        }

        deck.Init(cards);

        // Clear backing hand lists
        playerHand.Clear();
        botHand.Clear();

        // Build player list (currently: You + Bot, but ready for more seats)
        players.Clear();

        // Seat 0: local human
        players.Add(new PlayerState(
            PlayerKind.LocalHuman,
            "You",
            playerHandPanel,
            playerHand,
            agent: localHumanAgent));

        // Seat 1: bot
        players.Add(new PlayerState(
            PlayerKind.Bot,
            "Bot",
            botHandPanel,
            botHand,
            agent: new BotAgent()));

        // Deal starting hands: 8 cards per player
        for (int i = 0; i < 8; i++)
        {
            foreach (var p in players)
            {
                var c = deck.Draw();
                if (c != null) p.hand.Add(c);
            }
        }

        previousTop = null;
        lastMatchContext = null;
        pendingToneLock = 0;

        var starter = deck.Draw();
        if (starter != null)
            deck.Play(starter);

        // Turn order: start at seat 0 (local human)
        turnOrder = new PlayerOrder(players.Count, 0);
        turn = turnOrder.CurrentIndex;
        playing = true;

        SetMessage("Your turn: play a matching card or Draw");
        RefreshUI();
        UpdateCenterUI();
    }

    // ----- INPUT (local human) -----

    public void DrawButton()
    {
        if (!playing) return;

        var current = CurrentPlayer;
        if (current == null || current.kind != PlayerKind.LocalHuman) return;

        var c = deck.Draw();
        if (c != null) current.hand.Add(c);

        // Drawing consumes & clears any constraint
        lastMatchContext = null;
        pendingToneLock = 0;

        SetMessage("You drew a card.\nNext player's turn…");

        AdvanceTurn(1);

        RefreshUI();
        UpdateCenterUI();
        TriggerAutoTurnIfNeeded();
    }

    public void TryPlayFromPlayer(int index)
    {
        if (!playing) return;

        var current = CurrentPlayer;
        if (current == null || current.kind != PlayerKind.LocalHuman) return;

        var hand = current.hand;
        if (index < 0 || index >= hand.Count) return;

        var card = hand[index];

        // 1) Tone lock bypass: only check tone
        if (pendingToneLock > 0)
        {
            if (!RuleEngine.HasTone(card, pendingToneLock))
            {
                SetMessage($"Tone lock: must play tone {pendingToneLock}.");
                return;
            }

            var ctx = new RuleEngine.MatchContext
            {
                ok = true,
                by = "tone",
                topReading = new Reading("", "", pendingToneLock)
            };

            pendingToneLock = 0; // satisfied
            ApplyPlay(current, index, card, ctx, deferTonePick: false);
            return;
        }

        // 2) Wild is always legal (if it’s your last card you win immediately; otherwise we’ll pick tone)
        if (card.type == CardType.Effect && card.effect == EffectType.WildToneSetter)
        {
            ApplyPlay(current, index, card, null, deferTonePick: true);
            return;
        }

        // 3) Normal matching — but if an effect is on top and no tone lock, match against previousTop
        var matchTarget = TopForMatching();
        var res = RuleEngine.CanPlayOn(matchTarget, card);
        if (!res.ok)
        {
            SetMessage($"Can't play {CardLabel(card)}: {res.reason}");
            return;
        }

        ApplyPlay(current, index, card, res, deferTonePick: false);
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

    void ApplyPlay(PlayerState actor, int handIndex, Card card, RuleEngine.MatchContext res, bool deferTonePick)
    {
        if (actor == null) return;

        // Remember what was on the pile before this play
        var top = deck.Top;
        previousTop = top;

        lastMatchContext = res;

        // Remove from hand and place onto discard
        actor.hand.RemoveAt(handIndex);
        deck.Play(card);

        // ===== Immediate win checks before any deferred UI =====
        if (actor.hand.Count == 0)
        {
            // If last card is Wild, we do NOT block on choosing a tone — you win now.
            playing = false;
            pendingToneLock = 0;

            if (actor.kind == PlayerKind.LocalHuman)
                SetMessage("You win!");
            else
                SetMessage($"{actor.displayName} wins!");

            RefreshUI();
            UpdateCenterUI();
            return;
        }

        // Track whether this play skips the next seat
        bool skipNextSeat = false;

        // ===== EFFECTS =====
        if (card.type == CardType.Effect)
        {
            if (card.effect == EffectType.DrawTwoToneLinked)
            {
                // Next player draws 2 and is skipped
                int targetIndex = turnOrder.PeekOffset(1);
                var target = (targetIndex >= 0 && targetIndex < players.Count) ? players[targetIndex] : null;

                var d1 = deck.Draw();
                var d2 = deck.Draw();

                if (target != null)
                {
                    if (d1 != null) target.hand.Add(d1);
                    if (d2 != null) target.hand.Add(d2);
                }

                // Clear tone lock; subsequent matching will reference previousTop via TopForMatching()
                pendingToneLock = 0;

                skipNextSeat = true;

                string actorLabel = actor.kind == PlayerKind.LocalHuman ? "You" : actor.displayName;
                string targetLabel = (target != null && target.kind == PlayerKind.LocalHuman)
                    ? "You"
                    : target?.displayName ?? "Next player";

                SetMessage($"{actorLabel} played Draw-2.\n{targetLabel} drew 2 and was skipped.");
            }
            else if (card.effect == EffectType.WildToneSetter)
            {
                if (deferTonePick && actor.kind == PlayerKind.LocalHuman)
                {
                    // Only ask for tone if it wasn't your last card (we already returned above if last)
                    if (drawButton) drawButton.interactable = false;
                    if (playerHandPanel) playerHandPanel.SetInteractable(false);

                    if (tonePicker != null)
                    {
                        tonePicker.ShowPick(tone =>
                        {
                            pendingToneLock = Mathf.Clamp(tone, 1, 4);

                            if (drawButton) drawButton.interactable = true;
                            if (playerHandPanel) playerHandPanel.SetInteractable(true);

                            SetMessage($"Tone set to {pendingToneLock}.\nNext player must play tone {pendingToneLock}.");

                            AdvanceTurn(1);
                            RefreshUI();
                            UpdateCenterUI();
                            TriggerAutoTurnIfNeeded();
                        });

                        RefreshUI();
                        UpdateCenterUI();
                        return; // wait for picker callback
                    }
                    else
                    {
                        // Fallback if picker not wired
                        pendingToneLock = 1;
                        SetMessage($"(No tone picker wired) Tone set to 1.");
                    }
                }
                else if (actor.kind == PlayerKind.Bot)
                {
                    // Bot wild → choose tone heuristically
                    int t = ChooseBotTone(actor.hand);
                    pendingToneLock = (t >= 1 && t <= 4) ? t : 1;
                    SetMessage($"Bot set tone to {pendingToneLock}.\nYou must play tone {pendingToneLock}.");
                }
            }
        }

        // ----- Decide whose turn is next -----
        if (!playing) return;

        // Advance by 2 if we skip the next seat (Draw-2 / skip effects), else by 1
        AdvanceTurn(skipNextSeat ? 2 : 1);

        RefreshUI();
        UpdateCenterUI();
        TriggerAutoTurnIfNeeded();
    }

    // ----- BOT -----

    /// <summary>
    /// Core bot-turn logic. Public so BotAgent can call it via IPlayerAgent.
    /// </summary>
    public void BotTurn()
    {
        if (!playing || turnOrder == null || players.Count == 0) return;

        var actor = CurrentPlayer;
        if (actor == null || actor.kind != PlayerKind.Bot) return;

        var botHandLocal = actor.hand;

        // Tone-lock against bot
        if (pendingToneLock > 0)
        {
            for (int i = 0; i < botHandLocal.Count; i++)
            {
                var c = botHandLocal[i];
                if (RuleEngine.HasTone(c, pendingToneLock))
                {
                    var ctx = new RuleEngine.MatchContext
                    {
                        ok = true,
                        by = "tone",
                        topReading = new Reading("", "", pendingToneLock)
                    };

                    pendingToneLock = 0;
                    ApplyPlay(actor, i, c, ctx, deferTonePick: false);
                    return;
                }
            }

            // No card satisfies → draw and end bot’s turn (lock clears)
            var d = deck.Draw();
            if (d != null) botHandLocal.Add(d);
            pendingToneLock = 0;

            SetMessage("Bot drew (tone lock).\nNext player's turn.");
            AdvanceTurn(1);

            RefreshUI();
            UpdateCenterUI();
            TriggerAutoTurnIfNeeded();
            return;
        }

        // Effect-neutral target when an effect sits on top
        var matchTarget = TopForMatching();

        // Try Draw-2 first if legal
        for (int i = 0; i < botHandLocal.Count; i++)
        {
            var c = botHandLocal[i];
            if (c.type == CardType.Effect && c.effect == EffectType.DrawTwoToneLinked)
            {
                var res = RuleEngine.CanPlayOn(matchTarget, c);
                if (res.ok)
                {
                    ApplyPlay(actor, i, c, res, deferTonePick: false);
                    return;
                }
            }
        }

        // Try Wild (always legal)
        for (int i = 0; i < botHandLocal.Count; i++)
        {
            var c = botHandLocal[i];
            if (c.type == CardType.Effect && c.effect == EffectType.WildToneSetter)
            {
                ApplyPlay(actor, i, c, new RuleEngine.MatchContext
                {
                    ok = true,
                    by = "effect",
                    reason = "Wild tone setter."
                }, deferTonePick: false);
                return;
            }
        }

        // Else, any Hanzi that’s legal
        for (int i = 0; i < botHandLocal.Count; i++)
        {
            var c = botHandLocal[i];
            if (c.type != CardType.Hanzi) continue;

            var res = RuleEngine.CanPlayOn(matchTarget, c);
            if (res.ok)
            {
                ApplyPlay(actor, i, c, res, deferTonePick: false);
                return;
            }
        }

        // Else draw
        var d2 = deck.Draw();
        if (d2 != null) botHandLocal.Add(d2);

        SetMessage("Bot drew a card.\nNext player's turn.");
        AdvanceTurn(1);

        RefreshUI();
        UpdateCenterUI();
        TriggerAutoTurnIfNeeded();
    }

    int ChooseBotTone(List<Card> hand)
    {
        // simple heuristic: most frequent tone in this bot's hand (fallback 1)
        var counts = new int[6];

        foreach (var c in hand)
        {
            foreach (var r in c.AllReadings())
            {
                if (r.tone >= 1 && r.tone <= 4)
                    counts[r.tone]++;
            }
        }

        int best = 1;
        int bestC = -1;
        for (int t = 1; t <= 4; t++)
        {
            if (counts[t] > bestC)
            {
                bestC = counts[t];
                best = t;
            }
        }

        return best;
    }

    // ----- UI -----

    void RefreshUI()
    {
        // Seat 0 is the local player in current setup
        playerHandPanel?.Render(playerHand, i => TryPlayFromPlayer(i));
        botHandPanel?.RenderFaceDown(botHand.Count);

        if (drawButton)
        {
            var cur = CurrentPlayer;
            drawButton.interactable = playing &&
                                      cur != null &&
                                      cur.kind == PlayerKind.LocalHuman;
        }
    }

    void UpdateCenterUI()
    {
        // Previous
        if (discardPreviousView)
        {
            if (previousTop != null)
                discardPreviousView.Bind(previousTop, -1, null);
            else
                discardPreviousView.BindFaceDown(-1);

            var cgPrev = discardPreviousView.GetComponent<CanvasGroup>();
            if (cgPrev)
                cgPrev.alpha = previousTop != null ? 0.6f : 0f;
        }

        // Current
        var top = deck.Top;
        if (discardCurrentView)
        {
            if (top != null)
                discardCurrentView.Bind(top, -1, null);
            else
                discardCurrentView.BindFaceDown(-1);

            var cgTop = discardCurrentView.GetComponent<CanvasGroup>();
            if (cgTop)
                cgTop.alpha = 1f;
        }

        // Details
        RenderTopDetails(top, lastMatchContext);
    }

    void RenderTopDetails(Card top, RuleEngine.MatchContext match)
    {
        // Tone lock line
        if (matchRuleText)
        {
            if (pendingToneLock > 0)
            {
                matchRuleText.SetText($"Tone lock: {pendingToneLock}");
            }
            else if (match != null && match.ok && !string.IsNullOrEmpty(match.by))
            {
                if (match.by == "terminal" && !string.IsNullOrEmpty(match.terminalSymbol))
                    matchRuleText.SetText($"Matched by: {match.by} ({match.terminalSymbol})");
                else
                    matchRuleText.SetText($"Matched by: {match.by}");
            }
            else
            {
                matchRuleText.SetText("");
            }
        }

        // Top card details
        if (top != null && top.type == CardType.Effect)
        {
            if (topDetailText)
            {
                if (top.effect == EffectType.DrawTwoToneLinked)
                {
                    int t = 0;
                    foreach (var r in top.AllReadings())
                    {
                        t = r.tone;
                        break;
                    }

                    // Be explicit that matching compares against previous while an effect is on top.
                    var prevLabel = previousTop != null
                        ? $" (matching against previous: {previousTop.hanzi})"
                        : "";

                    topDetailText.SetText($"Top: Draw-2 {(t >= 1 && t <= 4 ? $"(tone {t})" : "")}{prevLabel}");
                }
                else
                {
                    topDetailText.SetText("Top: Wild (set tone)");
                }
            }

            return;
        }

        // Default (hanzi or empty)
        if (top == null)
        {
            if (topDetailText)
                topDetailText.SetText("initials: -  finals: -  tones: -");

            return;
        }

        var (inis, fins, tones) = top.DistinctReadingSets();

        string ii = inis.Count > 0 ? string.Join("/", inis) : "-";
        string ff = fins.Count > 0 ? string.Join("/", fins) : "-";
        string tt = tones.Count > 0 ? string.Join("/", tones) : "-";

        if (topDetailText)
            topDetailText.SetText($"initials: {ii}  finals: {ff}  tones: {tt}");
    }

    void AdvanceTurn(int steps)
    {
        if (turnOrder == null) return;
        int idx = turnOrder.Advance(steps);
        turn = idx;
    }

    void TriggerAutoTurnIfNeeded()
    {
        if (!playing || turnOrder == null || players.Count == 0) return;

        var cur = CurrentPlayer;
        if (cur != null && cur.agent != null)
        {
            // Keep the small delay so the bot (or any automated agent)
            // doesn't feel completely instant.
            Invoke(nameof(InvokeAgentForCurrentPlayer), 0.6f);
        }
    }

    void InvokeAgentForCurrentPlayer()
    {
        if (!playing || turnOrder == null || players.Count == 0) return;

        var cur = CurrentPlayer;
        if (cur == null || cur.agent == null) return;

        cur.agent.OnTurnStarted(this, turnOrder.CurrentIndex);
    }

    string CardLabel(Card c) =>
        c.type == CardType.Effect ? $"[{c.effect}]" : c.hanzi;

    void SetMessage(string s)
    {
        if (messageText) messageText.SetText(s);
    }

    // ----- Helper types -----

    [System.Serializable]
    class PlayerState
    {
        public PlayerKind kind;
        public string displayName;
        public HandPanel panel;
        public List<Card> hand;
        public IPlayerAgent agent;

        public PlayerState(PlayerKind kind,
                           string displayName,
                           HandPanel panel,
                           List<Card> backingHand,
                           IPlayerAgent agent)
        {
            this.kind = kind;
            this.displayName = displayName;
            this.panel = panel;
            this.hand = backingHand ?? new List<Card>();
            this.agent = agent;
        }
    }

    class PlayerOrder
    {
        int playerCount;

        public int CurrentIndex { get; private set; }
        public int Direction { get; private set; } = 1; // 1 = clockwise, -1 = counter-clockwise

        public PlayerOrder(int playerCount, int startingIndex = 0)
        {
            Reset(playerCount, startingIndex);
        }

        public void Reset(int playerCount, int startingIndex = 0)
        {
            this.playerCount = Mathf.Max(playerCount, 0);
            if (this.playerCount == 0)
            {
                CurrentIndex = 0;
            }
            else
            {
                CurrentIndex = Mod(startingIndex, this.playerCount);
            }
        }

        public int PeekOffset(int steps)
        {
            if (playerCount <= 0) return 0;
            int raw = CurrentIndex + steps * Direction;
            return Mod(raw, playerCount);
        }

        public int Advance(int steps = 1)
        {
            CurrentIndex = PeekOffset(steps);
            return CurrentIndex;
        }

        public void Reverse()
        {
            Direction *= -1;
        }

        int Mod(int x, int m)
        {
            if (m == 0) return 0;
            int r = x % m;
            if (r < 0) r += m;
            return r;
        }
    }
}
