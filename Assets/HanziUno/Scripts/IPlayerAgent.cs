public interface IPlayerAgent
{
    /// <summary>
    /// Called when it becomes this seat's turn.
    /// For now we pass the TurnManager itself; later this can be replaced
    /// by a pure GameStateView when you split server/client.
    /// </summary>
    void OnTurnStarted(TurnManager game, int seatIndex);
}
