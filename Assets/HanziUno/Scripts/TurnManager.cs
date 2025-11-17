using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum PlayerKind
{
    LocalHuman,
    Bot,
    RemoteHuman   // for future networked seats; actions come from network
}

public class TurnManager : MonoBehaviour
{
    [Header("Startup")]
    [Tooltip("If true, a game is started automatically in Start() using the current seatConfigs. Disable if a lobby will call NewGame() manually.")]
    public bool autoStart = true;

    [Header("Center / Discard")]
    public CardView discardCurrentView;
    public CardView discardPreviousView;
    public TextMeshProUGUI topDetailText;   // initials/finals/tones OR effect info
    public TextMeshProUGUI matchRuleText;   // what matched last turn (also shows tone lock)

    [Header("Deck & Hands")]
    public DeckManager deck;
    public HandPanel playerHandPanel;       // local human’s visible hand
    public HandPanel botHandPanel;          // aggregated "others" card count (for now)

    [Header("Controls & Messages")]
    public Button drawButton;
    public TextMeshProUGUI messageText;     // small log of recent events

    [Header("Tone Picker UI")]
    public TonePickerUI tonePicker; // assign the TonePickerPanel (with TonePickerUI)

    [Header("Agents")]
    [Tooltip("Agent for the local human seat. There should be at most one LocalHuman seat in the config.")]
    public LocalHumanAgent localHumanAgent;

    [Header("Seat Configuration")]
    [Tooltip("Seat definitions in turn order. Supports 3–7 players. If empty, a default 3-seat setup (You + 2 bots) is used.")]
    public SeatConfig[] seatConfigs;

    // ---- State ----

    // These two lists are kept for backward compatibility and represent
    // the local human's hand and the first non-local player's hand.
    // Internally we now use players[i].hand for all seats.
    public List<Card> playerHand = new();
    public List<Card> botHand = new();

    // Generic multi-player model
    List<PlayerState> players = new();
    PlayerOrder turnOrder;

    // Exposed for any existing code that reads it; kept in sync with turnOrder.CurrentIndex.
    public int turn = 0; // index in players list; usually the local human is seat 0

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

    const int MaxLogLines = 8;
    readonly List<string> logLines = new();

    // Convenience: who is currently active?
    PlayerState CurrentPlayer =>
        (turnOrder != null &&
         players.Count > 0 &&
         turnOrder.CurrentIndex >= 0 &&
         turnOrder.CurrentIndex < players.Count)
            ? players[turnOrder.CurrentIndex]
            : null;

    // Index of the local human seat in players list (if any).
    int localSeatIndex = 0;

    void Start()
    {
        // If the menu scene passed us a seat configuration, use it.
        if (SeatConfigPayload.Seats != null && SeatConfigPayload.Seats.Length > 0)
        {
            ConfigureSeats(SeatConfigPayload.Seats);
            SeatConfigPayload.Seats = null; // clear so it doesn't leak into future games
        }

        if (autoStart)
            NewGame();
    }

    /// <summary>
    /// Allow an external setup script to configure seat definitions before NewGame().
    /// </summary>
    public void ConfigureSeats(SeatConfig[] configs)
    {
        seatConfigs = configs;
    }

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

        // Build player list from seatConfigs
        BuildPlayersFromConfig();

        if (players.Count < 2)
        {
            Debug.LogError("TurnManager: Need at least 2 players configured.");
            return;
        }

        // Deal starting hands: 8 cards per player
        for (int i = 0; i < 8; i++)
        {
            foreach (var p in players)
            {
                var c = deck.Draw();
                if (c != null) p.hand.Add(c);
            }
        }

        // Keep public playerHand/botHand synced for backwards compat
        SyncPublicHandsFromPlayers();

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

        // Turn order: start at local seat if found, else 0
        turnOrder = new PlayerOrder(players.Count, localSeatIndex);
        turn = turnOrder.CurrentIndex;
        playing = true;

        SetMessage("New game started.");
        SetMessage("Your turn: play a matching card or Draw");

        RefreshUI();
        UpdateCenterUI();
    }

    void BuildPlayersFromConfig()
    {
        players.Clear();
        localSeatIndex = 0;

        const int MinSeats = 3;
        const int MaxSeats = 7;

        int configuredCount = (seatConfigs != null) ? seatConfigs.Length : 0;
        int seatCount = Mathf.Clamp(configuredCount > 0 ? configuredCount : 3, MinSeats, MaxSeats);

        for (int i = 0; i < seatCount; i++)
        {
            PlayerKind kind;
            string displayName;

            if (seatConfigs != null && i < seatConfigs.Length && seatConfigs[i] != null)
            {
                kind = seatConfigs[i].kind;
                displayName = string.IsNullOrWhiteSpace(seatConfigs[i].displayName)
                    ? DefaultSeatName(i, seatConfigs[i].kind)
                    : seatConfigs[i].displayName;
            }
            else
            {
                // Default seating if config missing
                if (i == 0)
                {
                    kind = PlayerKind.LocalHuman;
                    displayName = "You";
                }
                else
                {
                    kind = PlayerKind.Bot;
                    displayName = "Bot " + i;
                }
            }

            var handList = new List<Card>();
            IPlayerAgent agent = null;

            if (kind == PlayerKind.LocalHuman)
            {
                agent = localHumanAgent;
                localSeatIndex = i;
            }
            else if (kind == PlayerKind.Bot)
            {
                agent = new BotAgent();
            }
            // RemoteHuman → agent stays null; network layer will drive actions.

            players.Add(new PlayerState(
                kind,
                displayName,
                panel: null,
                backingHand: handList,
                agent: agent));
        }
    }

    string DefaultSeatName(int index, PlayerKind kind)
    {
        switch (kind)
        {
            case PlayerKind.LocalHuman: return "You";
            case PlayerKind.RemoteHuman: return "Player " + (index + 1);
            case PlayerKind.Bot:
            default:
                return "Bot " + (index + 1);
        }
    }

    void SyncPublicHandsFromPlayers()
    {
        // Local player's hand
        if (players.Count > 0)
        {
            if (localSeatIndex < 0 || localSeatIndex >= players.Count)
                localSeatIndex = 0;

            playerHand = players[localSeatIndex].hand;
        }

        // "botHand" is kept as the first non-local player's hand for backwards compat;
        // if there are multiple others, this is just the first of them.
        PlayerState firstOther = null;
        for (int i = 0; i < players.Count; i++)
        {
            if (i == localSeatIndex) continue;
            firstOther = players[i];
            break;
        }

        if (firstOther != null)
            botHand = firstOther.hand;
        else
            botHand = new List<Card>();
    }

    // ----- central action handler -----

    public void HandleAction(PlayerAction action)
    {
        if (!playing || turnOrder == null || players.Count == 0) return;

        if (action.seatIndex < 0 || action.seatIndex >= players.Count)
            return;

        if (action.seatIndex != turnOrder.CurrentIndex)
        {
            Debug.LogWarning($"HandleAction: seat {action.seatIndex} tried to act on seat {turnOrder.CurrentIndex}'s turn.");
            return;
        }

        var actor = players[action.seatIndex];
        if (actor.kind != PlayerKind.LocalHuman)
        {
            // For now, only the local human uses PlayerAction. RemoteHuman seats
            // will eventually be driven by the network layer.
            Debug.LogWarning("HandleAction: only the local human seat should call this.");
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

    // ----- INPUT wrappers -----

    public void DrawButton()
    {
        int seatIndex = turnOrder != null ? turnOrder.CurrentIndex : 0;
        var action = PlayerAction.Draw(seatIndex);
        HandleAction(action);
    }

    public void TryPlayFromPlayer(int index)
    {
        int seatIndex = turnOrder != null ? turnOrder.CurrentIndex : 0;
        var action = PlayerAction.Play(seatIndex, index);
        HandleAction(action);
    }

    // ----- EXECUTION -----

    void ExecuteDraw(PlayerState actor)
    {
        if (!playing) return;
        if (actor == null) return;

        var c = deck.Draw();
        if (c != null) actor.hand.Add(c);

        // Keep public lists synced
        SyncPublicHandsFromPlayers();

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

        // Normal matching
        var matchTarget = TopForMatching(card);
        var res = RuleEngine.CanPlayOn(matchTarget, card);
        if (!res.ok)
        {
            SetMessage($"Can't play {CardLabel(card)}: {res.reason}");
            return;
        }

        string whoLabel = actor.kind == PlayerKind.LocalHuman ? "You" : actor.displayName;
        string by = string.IsNullOrEmpty(res.by) ? "rule" : res.by;
        SetMessage($"{whoLabel} played {CardLabel(card)} (matched by {by}).");

        ApplyPlay(actor, handIndex, card, res, deferTonePick: false);
    }

    /// <summary>
    /// Decide which card the rules should match against for the given play card.
    /// - If an effect is on top and we have a lastHanziTop, always match against lastHanziTop.
    /// - If an effect is on top and we have never seen a Hanzi yet:
    ///     * For HANZI plays → treat as no top (start).
    ///     * For EFFECT plays → match against the effect itself (so Draw-2 tone rules still apply).
    /// - Otherwise match directly against the actual top.
    /// </summary>
    Card TopForMatching(Card playCard)
    {
        var top = deck.Top;

        if (pendingToneLock == 0 && top != null && top.type == CardType.Effect)
        {
            if (lastHanziTop != null)
                return lastHanziTop;

            if (playCard != null && playCard.type == CardType.Hanzi)
                return null;        // "start" for first real Hanzi
            else
                return top;         // effect-on-effect before first Hanzi
        }

        return top;
    }

    void ApplyPlay(PlayerState actor, int handIndex, Card card, RuleEngine.MatchContext res, bool deferTonePick)
    {
        if (actor == null) return;

        var oldTop = deck.Top;

        previousTop = oldTop;

        if (oldTop != null && oldTop.type == CardType.Hanzi)
        {
            lastHanziTop = oldTop;
        }

        lastMatchContext = res;

        actor.hand.RemoveAt(handIndex);
        deck.Play(card);

        if (card.type == CardType.Hanzi)
        {
            lastHanziTop = card;
        }

        // Keep public lists synced
        SyncPublicHandsFromPlayers();

        // ===== Immediate win checks before any deferred UI =====
        if (actor.hand.Count == 0)
        {
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

        bool skipNextSeat = false;

        if (card.type == CardType.Effect)
        {
            if (card.effect == EffectType.DrawTwoToneLinked)
            {
                int targetIndex = turnOrder.PeekOffset(1);
                var target = (targetIndex >= 0 && targetIndex < players.Count) ? players[targetIndex] : null;

                var d1 = deck.Draw();
                var d2 = deck.Draw();

                if (target != null)
                {
                    if (d1 != null) target.hand.Add(d1);
                    if (d2 != null) target.hand.Add(d2);
                }

                SyncPublicHandsFromPlayers();

                pendingToneLock = 0;
                skipNextSeat = true;

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
                        return;
                    }
                    else
                    {
                        pendingToneLock = 1;
                        SetMessage($"(No tone picker wired) Tone lock set to 1.");
                    }
                }
                else if (actor.kind == PlayerKind.Bot)
                {
                    int t = ChooseBotTone(actor.hand);
                    pendingToneLock = (t >= 1 && t <= 4) ? t : 1;
                    SetMessage($"Bot set tone lock to {pendingToneLock}.\nYou must play tone {pendingToneLock}.");
                }
            }
        }

        if (!playing) return;

        AdvanceTurn(skipNextSeat ? 2 : 1);

        RefreshUI();
        UpdateCenterUI();
        TriggerAutoTurnIfNeeded();
    }

    // ----- BOT -----

    public void BotTurn()
    {
        if (!playing || turnOrder == null || players.Count == 0) return;

        var actor = CurrentPlayer;
        if (actor == null || actor.kind != PlayerKind.Bot) return;

        var botHandLocal = actor.hand;

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

            var d = deck.Draw();
            if (d != null) botHandLocal.Add(d);
            SyncPublicHandsFromPlayers();
            pendingToneLock = 0;

            SetMessage("Bot drew (no card matched the tone lock).\nNext player's turn.");
            AdvanceTurn(1);

            RefreshUI();
            UpdateCenterUI();
            TriggerAutoTurnIfNeeded();
            return;
        }

        // Try Draw-2
        for (int i = 0; i < botHandLocal.Count; i++)
        {
            var c = botHandLocal[i];
            if (c.type == CardType.Effect && c.effect == EffectType.DrawTwoToneLinked)
            {
                var matchTarget = TopForMatching(c);
                var res = RuleEngine.CanPlayOn(matchTarget, c);
                if (res.ok)
                {
                    ApplyPlay(actor, i, c, res, deferTonePick: false);
                    return;
                }
            }
        }

        // Try Wild
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

        // Any legal Hanzi
        for (int i = 0; i < botHandLocal.Count; i++)
        {
            var c = botHandLocal[i];
            if (c.type != CardType.Hanzi) continue;

            var matchTarget = TopForMatching(c);
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
        SyncPublicHandsFromPlayers();

        SetMessage("Bot drew a card.\nNext player's turn.");
        AdvanceTurn(1);

        RefreshUI();
        UpdateCenterUI();
        TriggerAutoTurnIfNeeded();
    }

    int ChooseBotTone(List<Card> hand)
    {
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
        // Local human
        if (playerHandPanel != null)
        {
            if (localHumanAgent != null)
            {
                playerHandPanel.Render(playerHand, idx => localHumanAgent.OnCardClicked(idx));
            }
            else
            {
                playerHandPanel.Render(playerHand, idx => TryPlayFromPlayer(idx));
            }
        }

        // Aggregate all non-local players for the "opponent" panel
        if (botHandPanel != null)
        {
            int otherCount = 0;
            for (int i = 0; i < players.Count; i++)
            {
                if (i == localSeatIndex) continue;
                otherCount += players[i].hand.Count;
            }

            botHandPanel.RenderFaceDown(otherCount);
        }

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

        RenderTopDetails(top, lastMatchContext);
    }

    void RenderTopDetails(Card top, RuleEngine.MatchContext match)
    {
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
    public class SeatConfig
    {
        public string displayName = "Player";
        public PlayerKind kind = PlayerKind.Bot;
    }

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
