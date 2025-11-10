using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MenuUI : MonoBehaviour
{
    [Header("UI")]
    public Button playBotButton;
    public Button hostButton;
    public Button joinButton;
    public TMP_InputField addressInput;
    public TMP_InputField portInput;

    [Header("Shared Status")]
    public UIStatus uiStatus;   // assign the same StatusText via UIStatus

    void Awake()
    {
        if (playBotButton) playBotButton.onClick.AddListener(PlayBot);
        if (hostButton)    hostButton.onClick.AddListener(Host);
        if (joinButton)    joinButton.onClick.AddListener(Join);

#if UNITY_EDITOR
        if (addressInput && string.IsNullOrEmpty(addressInput.text)) addressInput.text = "127.0.0.1";
        if (portInput && string.IsNullOrEmpty(portInput.text))       portInput.text    = "7777";
#endif
    }

    public void PlayBot()
    {
        RuntimeConfig.Mode = GameMode.Bot;
        Log("Starting bot game…");
        SceneManager.LoadScene("Game", LoadSceneMode.Single);
    }

    public void Host()
    {
        RuntimeConfig.Mode = GameMode.Host;

        var nm = NetworkManager.Singleton;
        if (!nm) { SetStatus("No NetworkManager on NetworkRunner."); return; }

        var ut = nm.GetComponent<UnityTransport>();
        if (!ut) { SetStatus("UnityTransport missing on NetworkRunner."); return; }

        nm.NetworkConfig.NetworkTransport = ut;

        ushort port = GetPort();
        ut.SetConnectionData("0.0.0.0", port, "0.0.0.0");

        nm.NetworkConfig.EnableSceneManagement = true;
        nm.NetworkConfig.PlayerPrefab = null;

        if (nm.IsListening) nm.Shutdown();

        bool ok = nm.StartHost();
        if (!ok)
        {
            SetStatus("Host failed (see Console).");
            Debug.LogError("[MenuUI] StartHost() returned false.");
            return;
        }

        SetStatus($"Hosting on :{port}");
        nm.SceneManager.LoadScene("Game", LoadSceneMode.Single);
    }

    public void Join()
    {
        RuntimeConfig.Mode = GameMode.Client;

        var nm = NetworkManager.Singleton;
        if (!nm) { SetStatus("No NetworkManager on NetworkRunner."); return; }

        var ut = nm.GetComponent<UnityTransport>();
        if (!ut) { SetStatus("UnityTransport missing on NetworkRunner."); return; }

        nm.NetworkConfig.NetworkTransport = ut;

        string addr = addressInput ? addressInput.text : "127.0.0.1";
        ushort port = GetPort();
        ut.SetConnectionData(addr, port);

        nm.NetworkConfig.EnableSceneManagement = true;
        nm.NetworkConfig.PlayerPrefab = null;

        if (nm.IsListening) nm.Shutdown();

        bool ok = nm.StartClient();
        if (!ok)
        {
            SetStatus("Client start failed (see Console).");
            Debug.LogError("[MenuUI] StartClient() returned false.");
            return;
        }

        SetStatus($"Connecting to {addr}:{port}…");
        // Host scene load will sync us into Game automatically.
    }

    ushort GetPort()
    {
        if (portInput && ushort.TryParse(portInput.text, out var p)) return p;
        return 7777;
    }

    // ---- status helpers ----
    void SetStatus(string s)
    {
        if (uiStatus) uiStatus.Set(s);
        else Debug.Log("[MenuUI] " + s);
    }

    void Log(string s)
    {
        if (uiStatus) uiStatus.Append(s);
        else Debug.Log("[MenuUI] " + s);
    }
}
