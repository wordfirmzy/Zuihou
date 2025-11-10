using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BackToMenu : MonoBehaviour
{
    [Header("Optional UI")]
    public Button backButton;  // Wire your UI Button here (or call ReturnToMenu from OnClick)

    void Awake()
    {
        if (backButton) {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(ReturnToMenu);
        }
    }

    public void ReturnToMenu()
    {
        // In case gameplay paused time
        Time.timeScale = 1f;

        // If networking is running, shut it down first
        var nm = NetworkManager.Singleton;
        if (nm && nm.IsListening)
        {
            // Stop host or client cleanly
            nm.Shutdown();
        }

        // Go back to Menu scene (index/name must match your Build Profiles)
        SceneManager.LoadScene("Menu", LoadSceneMode.Single);
    }
}
