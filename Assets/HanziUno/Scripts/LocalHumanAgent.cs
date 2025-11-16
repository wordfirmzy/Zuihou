using UnityEngine;

/// <summary>
/// Placeholder for local human control. Right now TurnManager still
/// directly wires UI (HandPanel + Draw button) to its own methods,
/// so this agent does not override that behavior yet.
///
/// In a later step, you can move all "on-click" logic here and have
/// this agent send PlayerActions to a server-authoritative core.
/// </summary>
public class LocalHumanAgent : MonoBehaviour, IPlayerAgent
{
    public void OnTurnStarted(TurnManager game, int seatIndex)
    {
        // Future home for:
        //  - enabling/disabling local UI
        //  - wiring HandPanel and Draw button to send actions
        //  - optional hints / tutorials for the player
        //
        // For now, TurnManager already enables the local player's
        // controls in RefreshUI(), so we don't need to do anything.
    }
}
