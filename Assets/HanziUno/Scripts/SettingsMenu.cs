using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class SettingsMenu : MonoBehaviour
{
    [Header("Wiring")]
    public Button settingsButton;      // Small gear button in HUD
    public GameObject panelRoot;       // The Settings panel root (inactive by default)
    public Button resumeButton;        // Closes the panel
    public Button endGameButton;       // Returns to Menu scene
    public Image dimmer;               // Optional: full-screen Image to dim background & block clicks

    [Header("Audio")]
    public Toggle muteToggle;
    public Slider masterVolumeSlider;  // 0..1  (uses AudioListener.volume for simplicity)
    public TextMeshProUGUI volumeValueLabel;

    [Header("Video / Quality")]
    public TMP_Dropdown qualityDropdown;  // Populated at runtime

    [Header("Behavior")]
    public bool pauseWhenOpen = true;     // Time.timeScale = 0 while open

    const string Key_Mute = "settings.mute";
    const string Key_Vol =  "settings.masterVolume";
    const string Key_Qual = "settings.qualityIndex";

    float _timeScaleBeforePause = 1f;

    void Awake()
    {
        // Wire buttons
        if (settingsButton) settingsButton.onClick.AddListener(TogglePanel);
        if (resumeButton)   resumeButton.onClick.AddListener(ClosePanel);
        if (endGameButton)  endGameButton.onClick.AddListener(EndGame);

        // Hide panel on start
        SetPanelVisible(false, applyPause:false);

        // Init quality dropdown
        if (qualityDropdown)
        {
            qualityDropdown.ClearOptions();
            qualityDropdown.AddOptions(new System.Collections.Generic.List<string>(QualitySettings.names));
            qualityDropdown.onValueChanged.AddListener(OnQualityChanged);
        }

        // Init audio controls
        if (muteToggle) muteToggle.onValueChanged.AddListener(OnMuteChanged);
        if (masterVolumeSlider)
        {
            masterVolumeSlider.minValue = 0f;
            masterVolumeSlider.maxValue = 1f;
            masterVolumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        }

        // Load saved settings
        LoadSettings();
        ApplyAudioUI();
        ApplyQualityUI();
    }

    // ---------- Panel visibility ----------
    public void TogglePanel()
    {
        bool wantOpen = !(panelRoot && panelRoot.activeSelf);
        SetPanelVisible(wantOpen, applyPause:true);
    }

    public void ClosePanel()
    {
        SetPanelVisible(false, applyPause:true);
    }

    void SetPanelVisible(bool visible, bool applyPause)
    {
        if (panelRoot) panelRoot.SetActive(visible);
        if (dimmer)    dimmer.enabled = visible;

        if (!applyPause || !pauseWhenOpen) return;

        if (visible)
        {
            _timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = _timeScaleBeforePause <= 0f ? 1f : _timeScaleBeforePause;
        }
    }

    // ---------- Audio ----------
    void OnMuteChanged(bool isMuted)
    {
        // Simple global mute using AudioListener
        AudioListener.pause = isMuted;
        SaveSettings();
        ApplyAudioUI();
    }

    void OnVolumeChanged(float v)
    {
        AudioListener.volume = Mathf.Clamp01(v);
        SaveSettings();
        ApplyAudioUI();
    }

    void ApplyAudioUI()
    {
        // Update label
        if (volumeValueLabel && masterVolumeSlider)
            volumeValueLabel.SetText(Mathf.RoundToInt(masterVolumeSlider.value * 100f) + "%");
        // Keep toggle consistent with pause state
        if (muteToggle) muteToggle.isOn = AudioListener.pause;
    }

    // ---------- Quality ----------
    void OnQualityChanged(int idx)
    {
        idx = Mathf.Clamp(idx, 0, QualitySettings.names.Length - 1);
        QualitySettings.SetQualityLevel(idx, true);
        SaveSettings();
    }

    void ApplyQualityUI()
    {
        if (qualityDropdown)
        {
            int idx = QualitySettings.GetQualityLevel();
            qualityDropdown.SetValueWithoutNotify(idx);
            qualityDropdown.RefreshShownValue();
        }
    }

    // ---------- Persistence ----------
    void SaveSettings()
    {
        PlayerPrefs.SetInt(Key_Mute, AudioListener.pause ? 1 : 0);
        PlayerPrefs.SetFloat(Key_Vol, AudioListener.volume);
        if (qualityDropdown)
            PlayerPrefs.SetInt(Key_Qual, QualitySettings.GetQualityLevel());
        PlayerPrefs.Save();
    }

    void LoadSettings()
    {
        bool hasMute = PlayerPrefs.HasKey(Key_Mute);
        bool hasVol  = PlayerPrefs.HasKey(Key_Vol);
        bool hasQual = PlayerPrefs.HasKey(Key_Qual);

        if (hasMute) AudioListener.pause  = PlayerPrefs.GetInt(Key_Mute, 0) != 0;
        if (hasVol)  AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(Key_Vol, 1f));

        if (qualityDropdown && hasQual)
        {
            int idx = Mathf.Clamp(PlayerPrefs.GetInt(Key_Qual, QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1);
            QualitySettings.SetQualityLevel(idx, true);
        }

        // Reflect loaded values into UI controls
        if (muteToggle) muteToggle.isOn = AudioListener.pause;
        if (masterVolumeSlider) masterVolumeSlider.value = AudioListener.volume;
    }

    // ---------- End Game ----------
    public void EndGame()
    {
        // Ensure time runs after we leave
        Time.timeScale = 1f;

        // If networking is running, shut it down cleanly
        var nm = NetworkManager.Singleton;
        if (nm && nm.IsListening)
        {
            nm.Shutdown();
        }

        // Load Menu (make sure "Menu" is in Build Profiles)
        SceneManager.LoadScene("Menu", LoadSceneMode.Single);
    }
}
