using UnityEngine;

namespace Com.MyCompany.MyGame
{
    public class DeathUIManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject deathPanel;
        [SerializeField] private UnityEngine.UI.Button spectateButton;

        public static DeathUIManager Instance;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            // Ensure starts hidden
            if (deathPanel != null) deathPanel.SetActive(false);
            
            if (spectateButton != null)
            {
                spectateButton.onClick.AddListener(OnSpectateClicked);
            }
        }

        private void OnSpectateClicked()
        {
            // Find local player and tell them to spectate next
            if (PlayerController.Local != null)
            {
                PlayerController.Local.SpectateNextPlayer();
            }
            // Hide the death screen so we can see the view
            ToggleDeathScreen(false);
        }

        public void ToggleDeathScreen(bool show)
        {
            if (deathPanel != null)
            {
                deathPanel.SetActive(show);
            }
        }
    }
}
