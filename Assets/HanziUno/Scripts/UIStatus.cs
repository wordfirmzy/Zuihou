using TMPro;
using UnityEngine;

public class UIStatus : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI statusLabel;
    [SerializeField] bool showTimestamps = false;
    [SerializeField] int maxChars = 2000; // trims very long logs

    public void Set(string msg)
    {
        if (!statusLabel) return;
        statusLabel.text = Decorate(msg);
        Trim();
    }

    public void Append(string msg)
    {
        if (!statusLabel) return;
        if (string.IsNullOrEmpty(statusLabel.text))
            statusLabel.text = Decorate(msg);
        else
            statusLabel.text += "\n" + Decorate(msg);
        Trim();
    }

    public void Clear()
    {
        if (statusLabel) statusLabel.text = "";
    }

    string Decorate(string s) =>
        showTimestamps ? $"[{System.DateTime.Now:HH:mm:ss}] {s}" : s;

    void Trim()
    {
        if (!statusLabel) return;
        var t = statusLabel.text;
        if (t.Length > maxChars)
            statusLabel.text = t.Substring(t.Length - maxChars);
    }
}
