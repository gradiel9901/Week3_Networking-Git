using UnityEngine;
using TMPro;
using System.Collections.Generic;
using Fusion;
using UnityEngine.InputSystem;

namespace Com.MyCompany.MyGame
{
    public class ChatManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_InputField chatInputField;
        [SerializeField] private TMP_Text chatHistoryText;
        [SerializeField] private GameObject chatPanel;

        private List<string> _messages = new List<string>();
        private CanvasGroup _chatCanvasGroup;
        public bool IsChatOpen { get; private set; } = false;

        private void Start()
        {
             if (chatPanel != null) 
             {
                 chatPanel.SetActive(true); // Keep GameObject active for children
                 
                 // Handle Background Image
                 var img = chatPanel.GetComponent<UnityEngine.UI.Image>();
                 if (img != null) img.enabled = false; // Hide background initially

                 // Handle Raycasting (Allow clicks to pass through when closed)
                 _chatCanvasGroup = chatPanel.GetComponent<CanvasGroup>();
                 if (_chatCanvasGroup == null) _chatCanvasGroup = chatPanel.AddComponent<CanvasGroup>();
                 _chatCanvasGroup.blocksRaycasts = false; // Start with Raycasts disabled (Closed)
             }
             if (chatInputField != null) chatInputField.gameObject.SetActive(false); // Input starts hidden
             if (chatHistoryText != null) chatHistoryText.text = "";
        }

        private void Update()
        {
            if (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                if (IsChatOpen)
                {
                    // Chat is Open -> Send Message if text is valid, then Close
                    if (chatInputField != null)
                    {
                        if (!string.IsNullOrWhiteSpace(chatInputField.text))
                        {
                            SendChatMessage(chatInputField.text);
                            chatInputField.text = "";
                        }
                        
                        // Close Input & Background & Raycasts
                        chatInputField.gameObject.SetActive(false);
                        chatInputField.DeactivateInputField();
                        
                        if (chatPanel != null)
                        {
                            var img = chatPanel.GetComponent<UnityEngine.UI.Image>();
                            if (img != null) img.enabled = false;
                            
                            if (_chatCanvasGroup != null) _chatCanvasGroup.blocksRaycasts = false;
                        }
                        
                        IsChatOpen = false;
                    }
                }
                else
                {
                    // Chat is Closed -> Open Input & Background & Raycasts
                    IsChatOpen = true;
                    
                    if (chatPanel != null)
                    {
                        var img = chatPanel.GetComponent<UnityEngine.UI.Image>();
                        if (img != null) img.enabled = true;
                        
                        if (_chatCanvasGroup != null) _chatCanvasGroup.blocksRaycasts = true;
                    }

                    if (chatInputField != null)
                    {
                        chatInputField.gameObject.SetActive(true);
                        chatInputField.ActivateInputField();
                    }
                }
            }
        }

        private void SendChatMessage(string message)
        {
            if (PlayerController.Local != null)
            {
                PlayerController.Local.RPC_SendChat(message);
            }
            else
            {
                Debug.LogWarning("Local Player not found. Cannot send message.");
            }
        }

        public void AddMessageToHistory(string message)
        {
            _messages.Add(message);
            
            // Limit history
            if (_messages.Count > 20)
            {
                _messages.RemoveAt(0);
            }

            UpdateChatUI();
        }

        private void UpdateChatUI()
        {
            if (chatHistoryText != null)
            {
                chatHistoryText.text = string.Join("\n", _messages);
            }
        }
    }
}
