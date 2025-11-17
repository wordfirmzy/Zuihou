using UnityEngine;

/// <summary>
/// Simple helper to show/hide a status overlay panel (like the host IP scrollbox).
/// </summary>
public class StatusOverlay : MonoBehaviour
{
    [Tooltip("Root panel GameObject to show/hide (e.g., Row_Status).")]
    public GameObject panel;

    public void Show()
    {
        if (panel != null)
            panel.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null)
            panel.SetActive(false);
    }

    public void Toggle()
    {
        if (panel != null)
            panel.SetActive(!panel.activeSelf);
    }
}
