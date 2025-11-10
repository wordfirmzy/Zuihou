using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TMPro;
using UnityEngine;

public class ShowHostIP : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField portInput;        // reuse your Menu port field
    public TextMeshProUGUI outputLabel;     // optional multi-line box

    [Header("Shared Status")]
    public UIStatus uiStatus;               // your shared status label helper

    [Header("Options")]
    public bool preferWifi = true;

    string _lastBestAddress = null;

    // Hook this to a button
    public void ShowHostInfo()
    {
        try
        {
            var ipList = GetLocalIPv4s(out _lastBestAddress);
            string port = (portInput && !string.IsNullOrEmpty(portInput.text)) ? portInput.text : "7777";

            if (ipList.Count == 0)
            {
                SetStatus("No LAN IPv4 found. Check Wi-Fi/Ethernet.");
                SetOutput("No LAN IPv4 found.\nEnsure you are connected to Wi-Fi/Ethernet and try again.");
                return;
            }

            var lines = new List<string>();
            foreach (var e in ipList) lines.Add($"{e.label}: {e.address}");

            string best = _lastBestAddress ?? ipList.First().address;

            string text =
                "Give this to players on the same Wi-Fi:\n" +
                string.Join("\n", lines) +
                $"\n\nPort: {port}\n" +
                $"Recommended: {best}:{port}";

            SetOutput(text);
            SetStatus($"LAN IP(s) listed. Recommended {best}:{port}");
        }
        catch (Exception ex)
        {
            SetOutput("Error reading network interfaces.");
            SetStatus("Error getting IPs.");
            Debug.LogException(ex);
        }
    }

    // Optional “Copy” button
    public void CopyRecommendedToClipboard()
    {
        string port = (portInput && !string.IsNullOrEmpty(portInput.text)) ? portInput.text : "7777";
        string addr = _lastBestAddress ?? "127.0.0.1";
        GUIUtility.systemCopyBuffer = $"{addr}:{port}";
        SetStatus($"Copied {addr}:{port}");
    }

    // ---------- helpers ----------
    void SetOutput(string s)
    {
        if (outputLabel) outputLabel.SetText(s);
        else if (uiStatus) uiStatus.Set(s);
    }
    void SetStatus(string s)
    {
        if (uiStatus) uiStatus.Append(s);
        else Debug.Log("[ShowHostIP] " + s);
    }

    struct IpEntry { public string label; public string address; public int score; }

    List<IpEntry> GetLocalIPv4s(out string bestAddress)
    {
        var list = new List<IpEntry>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

            string n = ni.Name.ToLowerInvariant();
            if (n.Contains("virtual") || n.Contains("vmware") || n.Contains("vbox")) continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue; // IPv4 only
                string addr = ua.Address.ToString();
                if (addr.StartsWith("169.254.")) continue; // APIPA

                int score = 100;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score = 300;
                else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score = 200;
                if (addr.StartsWith("192.168.")) score += 5;
                if (addr.StartsWith("10."))      score += 4;
                if (addr.StartsWith("172."))     score += 3;

                list.Add(new IpEntry { label = NiceLabel(ni), address = addr, score = score });
            }
        }

        list = list.OrderByDescending(e => e.score).ThenBy(e => e.label).ToList();
        bestAddress = list.Count > 0 ? list[0].address : null;
        return list;
    }

    string NiceLabel(NetworkInterface ni)
    {
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return "Wi-Fi";
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)      return "Ethernet";
        return ni.Name;
    }
}
