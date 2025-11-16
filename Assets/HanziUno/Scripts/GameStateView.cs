using System.Collections.Generic;

/// <summary>
/// Lightweight snapshot of the public game state. This is not wired
/// into agents yet, but it's here so you can migrate TurnManager to
/// a server-authoritative core later without changing every caller.
/// </summary>
public class GameStateView
{
    public int currentSeatIndex;
    public int playerCount;
    public int pendingToneLock;
    public Card topCard;
    public Card previousTopCard;
    public IReadOnlyList<Card> localHand;

    public GameStateView(int currentSeatIndex,
                         int playerCount,
                         int pendingToneLock,
                         Card topCard,
                         Card previousTopCard,
                         IReadOnlyList<Card> localHand)
    {
        this.currentSeatIndex = currentSeatIndex;
        this.playerCount = playerCount;
        this.pendingToneLock = pendingToneLock;
        this.topCard = topCard;
        this.previousTopCard = previousTopCard;
        this.localHand = localHand;
    }
}
