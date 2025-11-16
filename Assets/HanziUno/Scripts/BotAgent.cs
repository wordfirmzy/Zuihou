using UnityEngine;

/// <summary>
/// Simple adapter that lets a bot seat be driven via IPlayerAgent.
/// It just calls TurnManager.BotTurn() for now, so all bot logic
/// stays in one place.
/// </summary>
public class BotAgent : IPlayerAgent
{
    public void OnTurnStarted(TurnManager game, int seatIndex)
    {
        if (game == null) return;
        game.BotTurn();
    }
}
