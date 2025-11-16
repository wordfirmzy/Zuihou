/// <summary>
/// Minimal description of something a player can do on their turn.
/// Not yet used by TurnManager in this step, but ready for a future
/// server-authoritative core.
/// </summary>
public enum PlayerActionType
{
    PlayCard,
    Draw
}

public struct PlayerAction
{
    public PlayerActionType type;
    public int seatIndex;   // which player
    public int cardIndex;   // index in that player's hand; -1 for Draw

    public static PlayerAction Play(int seatIndex, int cardIndex) =>
        new PlayerAction { type = PlayerActionType.PlayCard, seatIndex = seatIndex, cardIndex = cardIndex };

    public static PlayerAction Draw(int seatIndex) =>
        new PlayerAction { type = PlayerActionType.Draw, seatIndex = seatIndex, cardIndex = -1 };
}
