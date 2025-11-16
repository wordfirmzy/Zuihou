using UnityEngine;

/// <summary>
/// Agent that represents the local human player.
/// For now, it just forwards UI events (click card / click Draw)
/// into the TurnManager via PlayerAction.
/// </summary>
public class LocalHumanAgent : MonoBehaviour, IPlayerAgent
{
    [Tooltip("TurnManager that owns the game logic.")]
    public TurnManager game;

    [Tooltip("Seat index for this local human (currently 0).")]
    public int seatIndex = 0;

    void Awake()
    {
        // Fallback: if not explicitly wired, try to find a TurnManager on the same GameObject.
        if (game == null)
            game = GetComponent<TurnManager>();
    }

    public void OnTurnStarted(TurnManager game, int seatIndex)
    {
        // Future home for:
        // - enabling/disabling local UI
        // - focus/highlights, hints, etc.
        //
        // For now, TurnManager.RefreshUI() already takes care of enabling
        // the Draw button and hand interactions for the local player.
    }

    /// <summary>
    /// Hook this up as the HandPanel click handler for the local player.
    /// </summary>
    public void OnCardClicked(int handIndex)
    {
        if (game == null) return;
        if (handIndex < 0) return;

        var action = PlayerAction.Play(seatIndex, handIndex);
        game.HandleAction(action);
    }

    /// <summary>
    /// Hook this up to the Draw button onClick for the local player.
    /// </summary>
    public void OnDrawClicked()
    {
        if (game == null) return;

        var action = PlayerAction.Draw(seatIndex);
        game.HandleAction(action);
    }
}
