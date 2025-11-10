using Unity.Netcode;
using UnityEngine;

public class GameModeSwitcher : MonoBehaviour
{
    public TurnManager localTurnManager;     // your existing single-player manager
    public NetTurnManager netTurnManager;    // the NGO version we just built

    void Awake()
    {
        // Safety: ensure both refs exist
        if (!localTurnManager || !netTurnManager)
        {
            localTurnManager = GetComponent<TurnManager>();
            netTurnManager   = GetComponent<NetTurnManager>();
        }

        switch (RuntimeConfig.Mode)
        {
            case GameMode.Bot:
                EnableLocal();  break;
            case GameMode.Host:
            case GameMode.Client:
                EnableNet();    break;
        }
    }

    void EnableLocal()
    {
        if (netTurnManager)   netTurnManager.enabled = false;
        if (localTurnManager) localTurnManager.enabled = true;

        // If a NetworkManager is running because we hosted then returned,
        // it's OK to keep it running; local mode ignores it.
    }

    void EnableNet()
    {
        if (localTurnManager) localTurnManager.enabled = false;
        if (netTurnManager)   netTurnManager.enabled = true;

        // Ensure the object has a NetworkObject (Unity prompts you if missing).
        var no = GetComponent<NetworkObject>();
        if (!no) gameObject.AddComponent<NetworkObject>();
    }
}
