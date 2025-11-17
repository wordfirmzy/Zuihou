using UnityEngine;

/// <summary>
/// Static payload for passing seat configuration from the menu scene
/// into the game scene.
/// </summary>
public static class SeatConfigPayload
{
    // The seats to be used for the next game.
    public static TurnManager.SeatConfig[] Seats;
}
