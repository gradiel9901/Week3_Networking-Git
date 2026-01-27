using UnityEngine;
using Fusion;
using TMPro;

namespace Com.MyCompany.MyGame
{
    public class PlayerController : NetworkBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Renderer playerRenderer;

        [Header("Camera")]
        [SerializeField] private Camera fpsCamera; 

        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 5f;

        [Header("Color Settings")]
        [SerializeField] private Color[] availableColors = new Color[] 
        { 
            Color.red, Color.blue, Color.green, Color.yellow, 
            Color.cyan, Color.magenta, Color.white, Color.black 
        };

        [Networked]
        public NetworkString<_16> PlayerName { get; set; }

        [Networked]
        public int PlayerColorIndex { get; set; }

        private TMP_Text _spawnedNameText;

        public override void FixedUpdateNetwork()
        {
            if (GetInput(out NetworkInputData data))
            {
                data.direction.Normalize();
                Vector3 moveDelta = new Vector3(data.direction.x, 0, data.direction.y) * moveSpeed * Runner.DeltaTime;
                transform.position += moveDelta;
                
                if (data.direction != Vector2.zero) Debug.Log("Moving Player based on input!");
            }
        }

        public override void Render()
        {
            UpdateVisuals();
        }

        public override void Spawned()
        {
            GameObject textObj = new GameObject("DynamicNameTag");
            textObj.transform.SetParent(this.transform);
            textObj.transform.localPosition = new Vector3(0, 2.0f, 0); 
            
            _spawnedNameText = textObj.AddComponent<TextMeshPro>();
            _spawnedNameText.alignment = TextAlignmentOptions.Center;
            _spawnedNameText.fontSize = 4;
            _spawnedNameText.color = Color.white;
            _spawnedNameText.rectTransform.sizeDelta = new Vector2(5, 1);

            if (fpsCamera != null)
            {
                fpsCamera.gameObject.SetActive(Object.HasInputAuthority);

                if (Object.HasInputAuthority && Camera.main != null)
                {
                    Camera.main.gameObject.SetActive(false);
                }
            }

            if (Object.HasInputAuthority)
            {
                FusionLauncher launcher = FindFirstObjectByType<FusionLauncher>();
                if (launcher != null)
                {
                    string myName = launcher.GetLocalPlayerName();
                    int myColor = launcher.GetLocalPlayerColorIndex();
                    
                    RPC_SetDetails(myName, myColor);
                }
            }
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_SetDetails(string name, int colorIndex)
        {
            this.PlayerName = name;
            this.PlayerColorIndex = colorIndex;
        }

        private void UpdateVisuals()
        {
            if (_spawnedNameText != null)
            {
                _spawnedNameText.text = PlayerName.ToString();
            }

            if (PlayerColorIndex >= 0 && PlayerColorIndex < availableColors.Length)
            {
                playerRenderer.material.color = availableColors[PlayerColorIndex];
            }
        }
    }
}
