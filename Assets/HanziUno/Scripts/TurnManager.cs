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
    public TextMeshProUGUI messageText;     // now acts as a small log of recent events

    [Header("Tone Picker UI")]
    public TonePickerUI tonePicker; // assign the TonePickerPanel (with TonePickerUI)

    [Header("Agents")]
    [Tooltip("Agent for the local human seat (seat 0). Optional, but recommended.")]
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

    // Card that was on top immediately before the current top.
    Card previousTop = null;

    // The most recent HANZI card that has been on the top of the discard.
    // Used when one or more effect cards are stacked: matching should look
    // through the effect stack down to this card.
    Card lastHanziTop = null;

    RuleEngine.MatchContext lastMatchContext = null;

    // When >0, the NEXT player must play a card that has this tone (bypasses normal matching)
    int pendingToneLock = 0;

    const string HILITE = "#21A0AA";

    // Simple log of last few messages.
    readonly List<string> logLines = new();
    const int MaxLogLines = 8;

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

        // Clear log
        logLines.Clear();
        RefreshLogUI();

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
        lastHanziTop = null;
        lastMatchContext = null;
        pendingToneLock = 0;

        var starter = deck.Draw();
        if (starter != null)
        {
            deck.Play(starter);
            if (starter.type == CardType.Hanzi)
                lastHanziTop = starter;
        }

        // Turn order: start at seat 0 (local human)
        turnOrder = new PlayerOrder(players.Count, 0);
        turn = turnOrder.CurrentIndex;
        playing = true;

        SetMessage("New game started.");
        SetMessage("Your turn: play a matching card or Draw");

        RefreshUI();
        UpdateCenterUI();
    }

    // ----- NEW: central action handler -----

    /// <summary>
    /// Central entry point for player actions (local human now, network later).
    /// This is what LocalHumanAgent calls, and DrawButton/TryPlayFromPlayer
    /// now just wrap this.
    /// </summary>
    public void HandleAction(PlayerAction action)
    {
        if (!playing || turnOrder == null || players.Count == 0) return;

        if (action.seatIndex < 0 || action.seatIndex >= players.Count)
            return;

        if (action.seatIndex != turnOrder.CurrentIndex)
        {
            // Not this player's turn; ignore. In future, you could queue or log this.
            Debug.LogWarning($"HandleAction: seat {action.seatIndex} tried to act on seat {turnOrder.CurrentIndex}'s turn.");
            return;
        }

        var actor = players[action.seatIndex];
        if (actor.kind != PlayerKind.LocalHuman)
        {
            // For now, only local human uses PlayerAction. Bots still use BotTurn().
            Debug.LogWarning("HandleAction: currently only local human actions are supported via PlayerAction.");
            return;
        }

        switch (action.type)
        {
            case PlayerActionType.Draw:
                ExecuteDraw(actor);
                break;

            case PlayerActionType.PlayCard:
                ExecutePlay(actor, action.cardIndex);
                break;
        }
    }

    // ----- INPUT (wrappers for legacy UI wiring) -----

    // If your Draw button is still wired to this in the inspector, it will keep working.
    public void DrawButton()
    {
        int seatIndex = turnOrder != null ? turnOrder.CurrentIndex : 0;
        var action = PlayerAction.Draw(seatIndex);
        HandleAction(action);
    }

    // HandPanel still calls this via RefreshUI() if no LocalHumanAgent is assigned.
    public void TryPlayFromPlayer(int index)
    {
        int seatIndex = turnOrder != null ? turnOrder.CurrentIndex : 0;
        var action = PlayerAction.Play(seatIndex, index);
        HandleAction(action);
    }

    // ----- INTERNAL EXECUTION (logic moved from old DrawButton/TryPlayFromPlayer) -----

    void ExecuteDraw(PlayerState actor)
    {
        if (!playing) return;
        if (actor == null) return;

        var c = deck.Draw();
        if (c != null) actor.hand.Add(c);

        // Drawing consumes & clears any constraint
        lastMatchContext = null;
        pendingToneLock = 0;

        string actorLabel = actor.kind == PlayerKind.LocalHuman ? "You" : actor.displayName;
        SetMessage($"{actorLabel} drew a card.\nNext player's turn…");

        AdvanceTurn(1);

        RefreshUI();
        UpdateCenterUI();
        TriggerAutoTurnIfNeeded();
    }

    void ExecutePlay(PlayerState actor, int handIndex)
    {
        if (!playing) return;
        if (actor == null) return;

        var hand = actor.hand;
        if (handIndex < 0 || handIndex >= hand.Count) return;

        var card = hand[handIndex];

        // Tone lock bypass
        if (pendingToneLock > 0)
        {
            if (!RuleEngine.HasTone(card, pendingToneLock))
            {
                SetMessage($"Can't play {CardLabel(card)}: Tone lock requires tone {pendingToneLock}.");
                return;
            }

            var ctxTone = new RuleEngine.MatchContext
            {
                ok = true,
                by = "tone",
                topReading = new Reading("", "", pendingToneLock)
            };

            pendingToneLock = 0; // satisfied
            string who = actor.kind == PlayerKind.LocalHuman ? "You" : actor.displayName;
            SetMessage($"{who} played {CardLabel(card)} (satisfying tone lock {ctxTone.topReading.tone}).");

            ApplyPlay(actor, handIndex, card, ctxTone, deferTonePick: false);
            return;
        }

        // Wild from a hand that still has other cards → allow and pick tone.
        if (card.type == CardType.Effect && card.effect == EffectType.WildToneSetter)
        {
            string whoWild = actor.kind == PlayerKind.LocalHuman ? "You" : actor.displayName;
            SetMessage($"{whoWild} played Wild.");
            ApplyPlay(actor, handIndex, card, null, deferTonePick: true);
            return;
        }

        // Normal matching — but if an effect is on top and no tone lock, match against deepest HANZI under effects
        var matchTarget = TopForMatching();
        var res = RuleEngine.CanPlayOn(matchTarget, card);
        if (!res.ok)
        {
            SetMessage($"Can't play {CardLabel(card)}: {res.reason}");
            return;
        }

        // Successful non-effect play — log how it matched.
        string whoLabel = actor.kind == PlayerKind.LocalHuman ? "You" : actor.displayName;
        string by = string.IsNullOrEmpty(res.by) ? "rule" : res.by;
        SetMessage($"{whoLabel} played {CardLabel(card)} (matched by {by}).");

        ApplyPlay(actor, handIndex, card, res, deferTonePick: false);
    }

    // Which card should rules match against right now?
    Card TopForMatching()
    {
        var top = deck.Top;

        if (pendingToneLock == 0 && top != null && top.type == CardType.Effect)
        {
            // If we've already had at least one HANZI on top, always match
            // against that HANZI (handles stacked Draw-2 / Wild).
            if (lastHanziTop != null)
                return lastHanziTop;

            // IMPORTANT FIX:
            // If an effect is the very first card (no Hanzi has ever been played),
            // treat it as if there is *no* top card. That way the first real Hanzi
            // behaves like a normal starter and any matching rule can apply.
            return null;
        }

        return top;
    }

    // ----- APPLY -----

    void ApplyPlay(PlayerState actor, int handIndex, Card card, RuleEngine.MatchContext res, bool deferTonePick)
    {
        if (actor == null) return;

        // Remember what was on the pile before this play
        var oldTop = deck.Top;

        previousTop = oldTop;

        // Track the most recent HANZI top. If the old top was Hanzi, update;
        // if the old top was an effect, leave lastHanziTop as-is (we want the
        // deepest Hanzi under any number of stacked effects).
        if (oldTop != null && oldTop.type == CardType.Hanzi)
        {
            lastHanziTop = oldTop;
        }

        lastMatchContext = res;

        // Remove from hand and place onto discard
        actor.hand.RemoveAt(handIndex);
        deck.Play(card);

        // If the newly played card is Hanzi, it becomes the latest Hanzi top.
        if (card.type == CardType.Hanzi)
        {
            lastHanziTop = card;
        }

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

                // Clear tone lock; subsequent matching will reference lastHanziTop via TopForMatching()
                pendingToneLock = 0;

                skipNextSeat = true;

                // Work out the tone of the Draw-2 for logging
                int effectTone = 0;
                if (res != null && res.playReading != null)
                    effectTone = res.playReading.tone;
                else
                {
                    foreach (var r in card.AllReadings())
                    {
                        effectTone = r.tone;
                        break;
                    }
                }

                string actorLabel = actor.kind == PlayerKind.LocalHuman ? "You" : actor.displayName;
                string targetLabel = (target != null && target.kind == PlayerKind.LocalHuman)
                    ? "You"
                    : target?.displayName ?? "Next player";

                if (effectTone > 0)
                    SetMessage($"{actorLabel} played Draw-2 (tone {effectTone}).\n{targetLabel} drew 2 and was skipped.");
                else
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

                            SetMessage($"You set tone lock to {pendingToneLock}.\nNext player must play tone {pendingToneLock}.");

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
                        SetMessage($"(No tone picker wired) Tone lock set to 1.");
                    }
                }
                else if (actor.kind == PlayerKind.Bot)
                {
                    // Bot wild → choose tone heuristically
                    int t = ChooseBotTone(actor.hand);
                    pendingToneLock = (t >= 1 && t <= 4) ? t : 1;
                    SetMessage($"Bot set tone lock to {pendingToneLock}.\nYou must play tone {pendingToneLock}.");
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
                    SetMessage($"Bot played {CardLabel(c)} (satisfying tone lock {ctx.topReading.tone}).");
                    ApplyPlay(actor, i, c, ctx, deferTonePick: false);
                    return;
                }
            }

            // No card satisfies → draw and end bot’s turn (lock clears)
            var d = deck.Draw();
            if (d != null) botHandLocal.Add(d);
            pendingToneLock = 0;

            SetMessage("Bot drew (no card matched the tone lock).\nNext player's turn.");
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
                SetMessage("Bot played Wild.");
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
                string who = actor.displayName;
                string by = string.IsNullOrEmpty(res.by) ? "rule" : res.by;
                SetMessage($"Bot played {CardLabel(c)} (matched by {by}).");
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
        if (playerHandPanel != null)
        {
            if (localHumanAgent != null)
            {
                // Route clicks through the agent, which sends PlayerActions.
                playerHandPanel.Render(playerHand, idx => localHumanAgent.OnCardClicked(idx));
            }
            else
            {
                // Fallback: old direct path
                playerHandPanel.Render(playerHand, idx => TryPlayFromPlayer(idx));
            }
        }

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
        // Previous (just the immediate previous top, for visual context)
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

                    // Show which HANZI we're matching against, even if multiple
                    // effects are stacked.
                    Card matchCard = lastHanziTop;
                    string prevLabel = matchCard != null
                        ? $" (matching against: {matchCard.hanzi})"
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
            // Keep the small delay so an automated agent (bot) doesn't feel too instant.
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

    // ----- Logging helpers -----

    void SetMessage(string s) => AppendLogLine(s);

    void AppendLogLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return;

        logLines.Add(s);
        if (logLines.Count > MaxLogLines)
            logLines.RemoveAt(0);

        RefreshLogUI();
    }

    void RefreshLogUI()
    {
        if (!messageText) return;
        messageText.SetText(string.Join("\n", logLines));
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
