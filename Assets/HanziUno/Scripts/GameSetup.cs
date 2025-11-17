using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Menu-side setup for configuring 3–7 seats before starting an offline game.
/// Currently wired for the "Play vs Bot" path, using TextMeshPro dropdowns.
/// </summary>
public class GameSetup : MonoBehaviour
{
    [Header("Scene")]
    [Tooltip("Name of the game scene to load (case-sensitive, must be in Build Settings).")]
    public string gameSceneName = "game";   // set this in Inspector to your actual game scene name

    [Header("Player Count")]
    [Tooltip("TMP_Dropdown with options '3', '4', '5', '6', '7'.")]
    public TMP_Dropdown playerCountDropdown;

    [Header("Seat UI (up to 7 seats)")]
    [Tooltip("UI rows for each seat. Size this to 7 in the inspector.")]
    public SeatUI[] seatUIs;

    [System.Serializable]
    public class SeatUI
    {
        [Tooltip("Root GameObject for this seat row (e.g., SeatRow_1).")]
        public GameObject rowRoot;

        [Tooltip("Label for this seat (e.g., 'Seat 1').")]
        public TextMeshProUGUI label;

        [Tooltip("TMP_Dropdown with options: 0=Local, 1=Bot (for offline).")]
        public TMP_Dropdown kindDropdown;
    }

    void Awake()
    {
        if (seatUIs == null || seatUIs.Length < 3)
        {
            Debug.LogWarning("GameSetup: seatUIs should have at least 3 entries for 3–7 players.");
        }

        // Hook up the player-count change so rows hide/show live.
        if (playerCountDropdown != null)
        {
            playerCountDropdown.onValueChanged.AddListener(_ => UpdateSeatRowVisibility());
        }

        // Initial state
        UpdateSeatRowVisibility();
    }

    /// <summary>
    /// Called by the 'Play vs Bot' button.
    /// Configures a purely local+bot game and loads the game scene.
    /// </summary>
    public void OnPlayOffline()
    {
        int playerCount = ReadPlayerCount();
        if (playerCount < 3 || playerCount > 7)
        {
            Debug.LogError($"GameSetup: playerCount {playerCount} is out of range (3–7).");
            return;
        }

        var configs = new TurnManager.SeatConfig[playerCount];

        // Build configs from UI
        for (int i = 0; i < playerCount; i++)
        {
            var cfg = new TurnManager.SeatConfig();

            SeatUI ui = (seatUIs != null && i < seatUIs.Length) ? seatUIs[i] : null;

            string baseName = ui != null && ui.label != null ? ui.label.text : $"Player {i + 1}";
            cfg.displayName = string.IsNullOrWhiteSpace(baseName) ? $"Player {i + 1}" : baseName;

            PlayerKind kind = PlayerKind.Bot;

            if (ui != null && ui.kindDropdown != null)
            {
                // For offline we're only using Local/Bot.
                int val = ui.kindDropdown.value;
                // Map dropdown indices: 0=Local, 1=Bot
                switch (val)
                {
                    case 0: kind = PlayerKind.LocalHuman; break;
                    case 1: kind = PlayerKind.Bot; break;
                    default: kind = PlayerKind.Bot; break;
                }
            }
            else
            {
                // Fallback: seat 0 = Local, others = Bot
                kind = (i == 0) ? PlayerKind.LocalHuman : PlayerKind.Bot;
            }

            cfg.kind = kind;
            configs[i] = cfg;
        }

        // Ensure exactly one local human
        EnsureSingleLocalHuman(configs);

        // Store for the game scene
        SeatConfigPayload.Seats = configs;

        // Load game scene
        if (string.IsNullOrWhiteSpace(gameSceneName))
        {
            Debug.LogError("GameSetup: gameSceneName is empty. Set it in the inspector.");
            return;
        }

        SceneManager.LoadScene(gameSceneName);
    }

    int ReadPlayerCount()
    {
        if (playerCountDropdown == null)
            return 3; // default fallback

        // Assumes dropdown options are "3", "4", ..., "7"
        var option = playerCountDropdown.options[playerCountDropdown.value].text;
        if (int.TryParse(option, out int n))
            return Mathf.Clamp(n, 3, 7);

        return 3;
    }

    void EnsureSingleLocalHuman(TurnManager.SeatConfig[] configs)
    {
        int localIndex = -1;

        // Find the first LocalHuman
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].kind == PlayerKind.LocalHuman)
            {
                localIndex = i;
                break;
            }
        }

        if (localIndex == -1)
        {
            // No local found → force seat 0 to be local
            configs[0].kind = PlayerKind.LocalHuman;
            localIndex = 0;
        }

        // Any other LocalHuman seats → downgrade to Bot
        for (int i = 0; i < configs.Length; i++)
        {
            if (i == localIndex) continue;
            if (configs[i].kind == PlayerKind.LocalHuman)
                configs[i].kind = PlayerKind.Bot;
        }
    }

    /// <summary>
    /// Show only as many SeatRow_* as the current player count,
    /// hide the rest.
    /// </summary>
    void UpdateSeatRowVisibility()
    {
        int count = ReadPlayerCount();

        if (seatUIs == null) return;

        for (int i = 0; i < seatUIs.Length; i++)
        {
            var ui = seatUIs[i];
            if (ui == null || ui.rowRoot == null) continue;

            bool shouldShow = (i < count);
            ui.rowRoot.SetActive(shouldShow);
        }
    }
}
