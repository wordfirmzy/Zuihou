using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 2-player, host-authoritative turn manager using NGO,
/// with UI parity to offline mode:
///  - Shows current & previous top cards
///  - Shows details line for initials/finals/tones with highlighting
///  - Displays "Matched by: ..." rule (initial / final / terminal / tone / compound)
/// Requires: RuleEngine with MatchContext (multi-reading + terminal rule).
/// </summary>
public class NetTurnManager : NetworkBehaviour
{
    [Header("Center / Discard")]
    public CardView discardCurrentView;   // shows current top card
    public CardView discardPreviousView;  // shows previous top card (dimmed via CanvasGroup)
    public TextMeshProUGUI topDetailText; // "initials: ㄌ/ㄏ | finals: ㄧㄠ/ㄤ | tones: 2/3"
    public TextMeshProUGUI matchRuleText; // "Matched by: terminal (ㄠ)"

    [Header("Deck & Hands")]
    public DeckManager deck;
    public HandPanel playerHandPanel;     // local player's hand (face-up)
    public HandPanel opponentHandPanel;   // opponent hand (face-down count)

    [Header("Controls & Messages")]
    public Button drawButton;
    public TextMeshProUGUI messageText;

    // ---- Server authoritative state ----
    readonly List<Card> seat0 = new(); // host hand
    readonly List<Card> seat1 = new(); // first client hand
    int  turnSeat = 0;                 // 0 host, 1 client
    bool playing = false;

    ulong hostClientId;
    ulong clientClientId = ulong.MaxValue;

    // ---- Snapshot cache for local UI ----
    readonly List<Card> myHandLocal = new();
    int   oppCountLocal = 0;
    Card  topLocal = null;
    Card  previousTopLocal = null;

    // Last match context (serialized in snapshot so both sides see same details)
    string lastByLocal = "";
    string lastTerminalLocal = null;
    Reading lastTopReadingLocal = null;
    Reading lastPlayReadingLocal = null;

    const string HILITE_COLOR = "#21A0AA"; // teal in your palette

    void Awake()
    {
        if (drawButton)
        {
            drawButton.onClick.RemoveAllListeners();
            drawButton.onClick.AddListener(() =>
            {
                if (IsServer) Server_DrawForLocalSeat();
                else RequestDrawServerRpc();
            });
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            hostClientId = NetworkManager.Singleton.LocalClientId;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            Server_NewGame();
        }
        else
        {
            SafeSetMessage("Connecting to host…");
        }
    }

    public override void OnDestroy()
    {
        if (NetworkManager.Singleton)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        base.OnDestroy();
    }

    // ----------------- SERVER SIDE -----------------

    void OnClientConnected(ulong clientId)
    {
        if (clientId == hostClientId) return;
        if (clientClientId == ulong.MaxValue)
            clientClientId = clientId;

        Server_BroadcastSnapshots((turnSeat == 0 ? "Host’s" : "Client’s") + " turn.");
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (clientId == clientClientId)
        {
            clientClientId = ulong.MaxValue;
            playing = false;
            Server_BroadcastSnapshots("Opponent disconnected.");
        }
    }

    void Server_NewGame()
    {
        var cards = CardsDatabase.Load();
        if (cards == null || cards.Count == 0)
        {
            SafeSetMessage("No cards loaded. Put cards.json in Resources.");
            return;
        }

        deck.Init(cards);
        seat0.Clear(); seat1.Clear();

        for (int i = 0; i < 8; i++)
        {
            var a = deck.Draw(); if (a != null) seat0.Add(a);
            var b = deck.Draw(); if (b != null) seat1.Add(b);
        }

        // Start discard
        var starter = deck.Draw();
        if (starter != null) deck.Play(starter);

        turnSeat = 0;
        playing = true;

        // No match yet
        Server_ClearLastMatch();
        Server_BroadcastSnapshots("Game start: Host’s turn.");
    }

    void Server_ClearLastMatch()
    {
        lastByLocal = "";
        lastTerminalLocal = null;
        lastTopReadingLocal = null;
        lastPlayReadingLocal = null;
    }

    void Server_DrawForLocalSeat()
    {
        int seat = 0; // host is local on server
        if (!playing || turnSeat != seat) return;

        var c = deck.Draw(); if (c != null) seat0.Add(c);
        // drawing clears last match context
        Server_ClearLastMatch();

        turnSeat = 1 - turnSeat;
        Server_BroadcastSnapshots("Drew a card.");
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestDrawServerRpc(ServerRpcParams p = default)
    {
        int seat = (p.Receive.SenderClientId == hostClientId) ? 0 : 1;
        if (!playing || turnSeat != seat) return;

        var c = deck.Draw(); if (c != null) GetSeat(seat).Add(c);
        Server_ClearLastMatch();

        turnSeat = 1 - turnSeat;
        Server_BroadcastSnapshots("Drew a card.");
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestPlayServerRpc(int handIndex, ServerRpcParams p = default)
    {
        int seat = (p.Receive.SenderClientId == hostClientId) ? 0 : 1;
        if (!playing || turnSeat != seat) return;

        var hand = GetSeat(seat);
        if (handIndex < 0 || handIndex >= hand.Count) return;

        var card = hand[handIndex];
        var top  = deck.Top;
        var res  = RuleEngine.CanPlayOn(top, card);
        if (!res.ok) { Server_BroadcastSnapshots($"Can't play {card.hanzi}: {res.reason}"); return; }

        // Set last-match info for snapshot consumers
        lastByLocal         = res.by ?? "";
        lastTerminalLocal   = res.terminalSymbol;
        lastTopReadingLocal = res.topReading;
        lastPlayReadingLocal= res.playReading;

        hand.RemoveAt(handIndex);
        deck.Play(card);

        if (hand.Count == 0)
        {
            playing = false;
            Server_BroadcastSnapshots(seat == 0 ? "Host wins!" : "Client wins!");
            return;
        }

        turnSeat = 1 - turnSeat;
        Server_BroadcastSnapshots($"Played {card.hanzi} by {res.by}.");
    }

    void Server_BroadcastSnapshots(string message)
    {
        // Host snapshot
        var snapHost = BuildSnapshotForSeat(0, message);
        var jsonHost = JsonUtility.ToJson(snapHost);
        ApplySnapshotLocal(jsonHost); // host applies immediately

        // Client snapshot
        if (clientClientId != ulong.MaxValue)
        {
            var snapClient = BuildSnapshotForSeat(1, message);
            var jsonClient = JsonUtility.ToJson(snapClient);
            var target = new ClientRpcParams {
                Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { clientClientId } }
            };
            ReceiveSnapshotClientRpc(jsonClient, target);
        }
    }

    Snapshot BuildSnapshotForSeat(int seat, string message)
    {
        var my = (seat == 0) ? seat0 : seat1;
        var op = (seat == 0) ? seat1 : seat0;

        // Compute previous top: DeckManager.Top is current top; let DeckManager expose a way to know previous if you want.
        // Here, we track it client-side from last snapshots; for network parity we include only current top in snapshot
        // BUT we can also include previous by having DeckManager keep it. If you already track previous in DeckManager, set it here.
        // For simplicity, we infer previous on clients from last snapshot (see ApplySnapshotLocal). To make it explicit, uncomment and fill:
        // Card previousTop = deck.PreviousTop; // if you added this in DeckManager

        return new Snapshot
        {
            message = message,
            playing = playing,
            turnSeat = turnSeat,
            mySeat = seat,
            myHand = new List<Card>(my),
            opponentCount = op.Count,
            top = deck.Top,
            // Serialize last match context (so both sides render same highlight)
            lastBy = lastByLocal,
            lastTerminal = lastTerminalLocal,
            lastTopReading = lastTopReadingLocal,
            lastPlayReading = lastPlayReadingLocal,
        };
    }

    List<Card> GetSeat(int seat) => seat == 0 ? seat0 : seat1;

    // ----------------- CLIENT (and host UI) -----------------

    [ClientRpc]
    void ReceiveSnapshotClientRpc(string json, ClientRpcParams _ = default)
    {
        if (IsServer) return; // host already applied
        ApplySnapshotLocal(json);
    }

    void ApplySnapshotLocal(string json)
    {
        var snap = JsonUtility.FromJson<Snapshot>(json);
        if (snap == null) return;

        // Preserve previous top for UI (before we overwrite topLocal)
        var previous = topLocal;

        // Local mirrors
        myHandLocal.Clear();
        if (snap.myHand != null) myHandLocal.AddRange(snap.myHand);
        oppCountLocal = Mathf.Max(0, snap.opponentCount);
        topLocal = snap.top;

        // If a new top arrived, update previousTopLocal
        if (previous != null && topLocal != null && previous != topLocal)
            previousTopLocal = previous;
        // If no previous known and we just started, keep null

        // Carry last match context
        lastByLocal          = snap.lastBy ?? "";
        lastTerminalLocal    = snap.lastTerminal;
        lastTopReadingLocal  = snap.lastTopReading;
        lastPlayReadingLocal = snap.lastPlayReading;

        // Render center UI
        RenderCenterUI();

        // Player hand (face-up)
        playerHandPanel?.Render(myHandLocal, idx =>
        {
            bool myTurn = (snap.playing && snap.turnSeat == snap.mySeat);
            if (!myTurn) return;

            if (IsServer) Server_PlayLocalCard(idx);
            else RequestPlayServerRpc(idx);
        });

        // Opponent hand (face-down)
        opponentHandPanel?.RenderFaceDown(oppCountLocal);

        // Controls & message
        if (drawButton) drawButton.interactable = (snap.playing && snap.turnSeat == snap.mySeat);
        if (messageText) messageText.SetText(snap.message ?? "");
    }

    // Host clicking its own hand uses server path directly
    void Server_PlayLocalCard(int handIndex)
    {
        if (!IsServer) return;
        int seat = 0;
        var hand = GetSeat(seat);
        if (handIndex < 0 || handIndex >= hand.Count) return;

        var card = hand[handIndex];
        var res  = RuleEngine.CanPlayOn(deck.Top, card);
        if (!res.ok) { Server_BroadcastSnapshots($"Can't play {card.hanzi}: {res.reason}"); return; }

        // last match context
        lastByLocal          = res.by ?? "";
        lastTerminalLocal    = res.terminalSymbol;
        lastTopReadingLocal  = res.topReading;
        lastPlayReadingLocal = res.playReading;

        hand.RemoveAt(handIndex);
        deck.Play(card);

        if (hand.Count == 0) { playing = false; Server_BroadcastSnapshots("Host wins!"); return; }
        turnSeat = 1 - turnSeat;
        Server_BroadcastSnapshots($"Played {card.hanzi} by {res.by}.");
    }

    // ----------------- UI RENDERING -----------------

    void RenderCenterUI()
    {
        // Previous (dim)
        if (discardPreviousView)
        {
            if (previousTopLocal != null) discardPreviousView.Bind(previousTopLocal, -1, null);
            else discardPreviousView.BindFaceDown(-1);

            var cgPrev = discardPreviousView.GetComponent<CanvasGroup>();
            if (cgPrev) cgPrev.alpha = previousTopLocal != null ? 0.6f : 0.0f;
        }

        // Current (full)
        if (discardCurrentView)
        {
            if (topLocal != null) discardCurrentView.Bind(topLocal, -1, null);
            else discardCurrentView.BindFaceDown(-1);

            var cgTop = discardCurrentView.GetComponent<CanvasGroup>();
            if (cgTop) cgTop.alpha = 1f;
        }

        // Details + match label
        RenderTopDetails(topLocal, lastByLocal, lastTerminalLocal, lastTopReadingLocal);
    }

    void RenderTopDetails(Card top, string matchBy, string terminal, Reading topReadingForMatch)
    {
        // Rule label
        if (matchRuleText)
        {
            if (string.IsNullOrEmpty(matchBy))
                matchRuleText.SetText("");
            else if (matchBy == "terminal" && !string.IsNullOrEmpty(terminal))
                matchRuleText.SetText($"Matched by: <b><color={HILITE_COLOR}>{matchBy}</color></b> ({terminal})");
            else
                matchRuleText.SetText($"Matched by: <b><color={HILITE_COLOR}>{matchBy}</color></b>");
        }

        if (!topDetailText)
            return;

        if (top == null)
        {
            topDetailText.SetText("initials: -   finals: -   tones: -");
            return;
        }

        var (inis, fins, tones) = top.DistinctReadingSets();
        string Hi(string s) => $"<b><color={HILITE_COLOR}>{s}</color></b>";

        // INITIALS
        var initialsRendered = inis;
        if (!string.IsNullOrEmpty(matchBy) && matchBy == "initial" && topReadingForMatch != null)
        {
            string hiIni = topReadingForMatch.initial ?? "";
            initialsRendered = new List<string>();
            foreach (var s in inis)
                initialsRendered.Add(s == hiIni ? Hi(s) : s);
        }

        // FINALS (exact or terminal highlight)
        var finalsRendered = fins;
        if (!string.IsNullOrEmpty(matchBy) && (matchBy == "final" || matchBy == "terminal") && topReadingForMatch != null)
        {
            string hiFinal = matchBy == "final" ? (topReadingForMatch.final ?? "") : null;
            string term    = matchBy == "terminal" ? terminal : null;

            finalsRendered = new List<string>();
            foreach (var f in fins)
            {
                if (!string.IsNullOrEmpty(hiFinal) && f == hiFinal)
                {
                    finalsRendered.Add(Hi(f));
                }
                else if (!string.IsNullOrEmpty(term) && !string.IsNullOrEmpty(f) && f.Contains(term))
                {
                    int idx = f.LastIndexOf(term);
                    if (idx >= 0)
                        finalsRendered.Add(f.Substring(0, idx) + Hi(term) + f.Substring(idx + term.Length));
                    else
                        finalsRendered.Add(f);
                }
                else finalsRendered.Add(f);
            }
        }

        // TONES
        var tonesRendered = new List<string>();
        foreach (var t in tones)
        {
            if (!string.IsNullOrEmpty(matchBy) && matchBy == "tone" && topReadingForMatch != null && topReadingForMatch.tone == t)
                tonesRendered.Add(Hi(t.ToString()));
            else
                tonesRendered.Add(t.ToString());
        }

        string ii = initialsRendered.Count > 0 ? string.Join("/", initialsRendered) : "-";
        string ff = finalsRendered.Count   > 0 ? string.Join("/", finalsRendered)   : "-";
        string tt = tonesRendered.Count    > 0 ? string.Join("/", tonesRendered)    : "-";

        topDetailText.SetText($"initials: {ii}   finals: {ff}   tones: {tt}");
    }

    // ----- Snapshot payload -----
    [Serializable]
    class Snapshot
    {
        public string message;
        public bool playing;
        public int turnSeat;         // 0 host, 1 client
        public int mySeat;           // seat index for this client
        public List<Card> myHand;    // only my cards
        public int opponentCount;    // just a number
        public Card top;             // current top card

        // Match context for UI (serialized)
        public string  lastBy;
        public string  lastTerminal;
        public Reading lastTopReading;
        public Reading lastPlayReading;
    }

    // ---- Helpers ----
    void SafeSetMessage(string s) { if (messageText) messageText.SetText(s); }
}
