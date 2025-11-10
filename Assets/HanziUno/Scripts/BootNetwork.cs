using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

[DisallowMultipleComponent]
public class BootNetwork : MonoBehaviour
{
    void Awake()
    {
        DontDestroyOnLoad(gameObject);

        // Make sure we have NetworkManager + UnityTransport
        var nm = GetComponent<NetworkManager>();
        var ut = GetComponent<UnityTransport>();
        if (!nm || !ut)
        {
            Debug.LogError("[BootNetwork] NetworkManager/UnityTransport missing on NetworkRunner.");
        }

        // NGO scene management stays enabled (default) so server can sync Game scene to clients.
        // No auto-host/client here—MenuUI drives it.
    }
}
