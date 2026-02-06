using UnityEngine;
using Fusion;
using TMPro;
using UnityEngine.UI;

namespace Com.MyCompany.MyGame
{
    public class FusionLauncher : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_InputField nameInputField;
        [SerializeField] private TMP_Dropdown colorDropdown;
        [SerializeField] private Button startButton;
        [SerializeField] private GameObject controlPanel;
        [SerializeField] private GameObject progressLabel;

        private void Start()
        {
            startButton.onClick.AddListener(StartGame);
            controlPanel.SetActive(true);
            progressLabel.SetActive(false);
        }

        private void StartGame()
        {
            controlPanel.SetActive(false);
            progressLabel.SetActive(true);

            if (NetworkManager.Instance == null)
            {
                Debug.LogError("[FusionLauncher] NetworkManager Instance is NULL! Make sure it is in the scene.");
                return;
            }

            // Using AutoHostOrClient as requested for testing
            NetworkManager.Instance.StartGame(GameMode.AutoHostOrClient);
        }

        public string GetLocalPlayerName()
        {
            return nameInputField != null ? nameInputField.text : "Player";
        }

        public int GetLocalPlayerTeamIndex()
        {
            return colorDropdown != null ? colorDropdown.value : 0;
        }
    }
}
