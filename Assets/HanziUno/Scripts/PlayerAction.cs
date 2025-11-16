/// <summary>
/// Minimal description of something a player can do on their turn.
/// This is the shape you'd eventually send over the network.
/// </summary>
public enum PlayerActionType
{
    PlayCard,
    Draw
}

public struct PlayerAction
{
    public PlayerActionType type;
    public int seatIndex;   // which player/seat
    public int cardIndex;   // index in that player's hand; -1 for Draw

    public static PlayerAction Play(int seatIndex, int cardIndex) =>
        new PlayerAction { type = PlayerActionType.PlayCard, seatIndex = seatIndex, cardIndex = cardIndex };

    public static PlayerAction Draw(int seatIndex) =>
        new PlayerAction { type = PlayerActionType.Draw, seatIndex = seatIndex, cardIndex = -1 };
}
