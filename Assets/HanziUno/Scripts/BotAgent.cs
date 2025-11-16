using UnityEngine;

/// <summary>
/// Simple adapter that lets a bot seat be driven via IPlayerAgent.
/// It just calls TurnManager.BotTurn() for now, so all bot logic
/// stays where it is.
/// </summary>
public class BotAgent : IPlayerAgent
{
    public void OnTurnStarted(TurnManager game, int seatIndex)
    {
        if (game == null) return;
        // In this project, TurnManager only ever calls agents for the
        // currently-active seat, so seatIndex isn't used yet.
        game.BotTurn();
    }
}
