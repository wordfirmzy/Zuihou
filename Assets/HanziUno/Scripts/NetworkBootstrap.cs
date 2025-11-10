using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NetworkBootstrap : MonoBehaviour
{
    [Header("Optional simple UI (drag if you have them)")]
    public Button hostButton;
    public Button clientButton;
    public TMP_InputField addressInput;
    public TMP_InputField portInput;
    public TextMeshProUGUI statusText;

    void Awake()
    {
        if (hostButton) hostButton.onClick.AddListener(StartHost);
        if (clientButton) clientButton.onClick.AddListener(StartClient);
#if UNITY_EDITOR
        if (addressInput && string.IsNullOrEmpty(addressInput.text)) addressInput.text = "127.0.0.1";
        if (portInput && string.IsNullOrEmpty(portInput.text)) portInput.text = "7777";
#endif
    }

    void StartHost()
    {
        var transport = GetComponent<UnityTransport>();
        if (transport && portInput && ushort.TryParse(portInput.text, out ushort port))
            transport.SetConnectionData(addressInput ? addressInput.text : "0.0.0.0", port, "0.0.0.0");

        bool ok = NetworkManager.Singleton.StartHost();
        if (statusText) statusText.SetText(ok ? "Host started" : "Host failed");
    }

    void StartClient()
    {
        var transport = GetComponent<UnityTransport>();
        string addr = addressInput ? addressInput.text : "127.0.0.1";
        ushort port = 7777;
        if (portInput && ushort.TryParse(portInput.text, out var p)) port = p;
        if (transport) transport.SetConnectionData(addr, port);

        bool ok = NetworkManager.Singleton.StartClient();
        if (statusText) statusText.SetText(ok ? $"Client connecting {addr}:{port}" : "Client start failed");
    }
}
