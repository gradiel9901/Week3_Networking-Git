using UnityEngine;
using Fusion;
using TMPro;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

namespace Com.MyCompany.MyGame
{
    public class PlayerController : NetworkBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Renderer playerRenderer;
        [SerializeField] private GameObject deathVfxPrefab;

        [Header("Camera")]
        [SerializeField] private Camera fpsCamera;
        [SerializeField] private CinemachineCamera virtualCamera; 

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

        [Networked]
        public int CurrentHealth { get; set; }

        [Networked]
        public NetworkBool IsPoisoned { get; set; }
        
        [Networked]
        private TickTimer PoisonTimer { get; set; }

        public const int MaxHealth = 100;

        private TMP_Text _spawnedNameText;
        private HPBarController _hpBar;
        private int _lastVisibleHealth;
        
        [SerializeField] private GameObject graveVisualPrefab;
        private GameObject _spawnedGrave;
        
        // Overhead HP Bar References
        private Transform _overheadHpBarTransform;

        private void CheckForResurrection()
        {
            // Simple distance check against all other players
            var allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            foreach (var p in allPlayers)
            {
                if (p != this && p.CurrentHealth <= 0)
                {
                    float dist = Vector3.Distance(transform.position, p.transform.position);
                    if (dist < 3.0f) // Interaction Range
                    {
                        Debug.Log($"Attempting to resurrect {p.PlayerName}");
                        RPC_RequestResurrect(p);
                        return; // One at a time
                    }
                }
            }
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_RequestResurrect(PlayerController target)
        {
            // Server Side Verification
            if (target != null && target.CurrentHealth <= 0)
            {
                if (Vector3.Distance(transform.position, target.transform.position) < 5.0f) 
                {
                    target.CurrentHealth = MaxHealth;
                    Debug.Log($"{PlayerName} resurrected {target.PlayerName}!");
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (CurrentHealth > 0 && GetInput(out NetworkInputData data))
            {
                data.direction.Normalize();
                
                // Calculate Rotation based on Camera Look Yaw
                Quaternion cameraRotation = Quaternion.Euler(0, data.lookYaw, 0);
                
                // Transform Input Direction (Local -> World)
                Vector3 inputDir = new Vector3(data.direction.x, 0, data.direction.y);
                Vector3 moveDir = cameraRotation * inputDir;
                
                // Apply Movement
                Vector3 moveDelta = moveDir * moveSpeed * Runner.DeltaTime;
                transform.position += moveDelta;
                
                // Rotate Player to Face Movement
                if (moveDir.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), Runner.DeltaTime * 10f); // Smooth rotation
                }
                
                if (data.direction != Vector2.zero) Debug.Log("Moving Player based on input!");
            }

            // Poison Logic (Server Only)
            // ... (Only active if alive)
            if (Object.HasStateAuthority && IsPoisoned && CurrentHealth > 0)
            {
                if (PoisonTimer.ExpiredOrNotRunning(Runner))
                {
                    CurrentHealth -= 5;
                    if (CurrentHealth < 0) CurrentHealth = 0;
                    
                    // Reset timer for next tick (1 second)
                    PoisonTimer = TickTimer.CreateFromSeconds(Runner, 1f);
                    
                    if (CurrentHealth <= 0)
                    {
                        IsPoisoned = false; // Stop poisoning if dead
                    }
                }
            }

            // Debug Self Damage via New Input System
            if (Object.HasInputAuthority && Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
            {
                 Debug.Log("Self Damage Requested (FUN)");
                 RPC_DataDamage(10);
            }
        }
        
        private void Update()
        {
            if (Object.HasInputAuthority && Keyboard.current != null)
            {
                // ... Input logic
                if (Keyboard.current.kKey.wasPressedThisFrame) { RPC_DataDamage(10); }
                if (Keyboard.current.pKey.wasPressedThisFrame) { RPC_TogglePoison(); }
                if (Keyboard.current.fKey.wasPressedThisFrame) 
                { 
                    Debug.Log("F Key Pressed (Hardcoded). Checking for resurrection...");
                    CheckForResurrection(); 
                }
            }
        }

        // ... ApplyPoison / RPC_TogglePoison / RPC_DataDamage ...

        public void ApplyPoison()
        {
            if (Object.HasInputAuthority) RPC_TogglePoison();
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_TogglePoison()
        {
            IsPoisoned = !IsPoisoned;
            if (IsPoisoned) PoisonTimer = TickTimer.CreateFromSeconds(Runner, 1f);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_DataDamage(int damage)
        {
             // ...
             CurrentHealth -= damage;
             if (CurrentHealth < 0) CurrentHealth = 0;
        }


        
        // ...
        
        private void Die()
        {
            if (deathVfxPrefab != null)
            {
                Instantiate(deathVfxPrefab, transform.position, Quaternion.identity);
            }
            
            // Spawn Grave Visual (Local)
            if (graveVisualPrefab != null && _spawnedGrave == null)
            {
                _spawnedGrave = Instantiate(graveVisualPrefab, transform.position, transform.rotation);
            }
            
            // Hide Player Model
            if (playerRenderer != null)
            {
                playerRenderer.enabled = false;
            }

            // Hide UI
            if (_spawnedNameText != null) _spawnedNameText.gameObject.SetActive(false);
            if (_overheadHpBarTransform != null && _overheadHpBarTransform.parent != null) 
            {
                _overheadHpBarTransform.parent.gameObject.SetActive(false); // Hide BG (Parent)
            }
            
            // ENSURE CAMERA STAYS ON
            if (Object.HasInputAuthority)
            {
                if (virtualCamera != null) virtualCamera.gameObject.SetActive(true);
                else if (fpsCamera != null) fpsCamera.gameObject.SetActive(true);
                Debug.Log("Die() called. Enforcing Camera Active.");
            }
            
            // Show Death Screen (Local)
            if (Object.HasInputAuthority)
            {
                if (DeathUIManager.Instance != null) DeathUIManager.Instance.ToggleDeathScreen(true);
            }
        }

        public override void Render()
        {
            UpdateVisuals();
            
            if (_lastVisibleHealth != CurrentHealth)
            {
                // Update Local UI
                if (Object.HasInputAuthority && _hpBar != null)
                {
                    _hpBar.UpdateHealth(CurrentHealth, MaxHealth);
                }

                // Update Overhead HP Bar
                if (_overheadHpBarTransform != null)
                {
                    float pct = (float)CurrentHealth / MaxHealth;
                    _overheadHpBarTransform.localScale = new Vector3(pct, 1, 1);
                }

                // Detect Death
                if (CurrentHealth <= 0 && _lastVisibleHealth > 0)
                {
                    Die();
                }
                
                // Detect Respawn/Alive
                if (CurrentHealth > 0 && _lastVisibleHealth <= 0)
                {
                     if (playerRenderer != null) playerRenderer.enabled = true;
                     
                     // Helper: Cleanup Grave
                     if (_spawnedGrave != null) Destroy(_spawnedGrave);

                     // Show UI
                     if (_spawnedNameText != null) _spawnedNameText.gameObject.SetActive(true);
                     if (_overheadHpBarTransform != null && _overheadHpBarTransform.parent != null)
                     {
                         _overheadHpBarTransform.parent.gameObject.SetActive(true);
                     }

                     // Hide Death Screen (Local)
                     if (Object.HasInputAuthority)
                     {
                         if (DeathUIManager.Instance != null) DeathUIManager.Instance.ToggleDeathScreen(false);
                         
                         // Reset Camera if spectating
                         if (fpsCamera != null) fpsCamera.gameObject.SetActive(true);
                         if (virtualCamera != null) virtualCamera.gameObject.SetActive(true);
                     }
                }

                _lastVisibleHealth = CurrentHealth;
            }

            // Billboard Effect: Rotate Name and HP Bar to face camera
            Camera cameraToFace = Camera.main;
            if (cameraToFace == null) cameraToFace = fpsCamera;
            if (cameraToFace == null && virtualCamera != null) {
                // If using Cinemachine, finding the Unity Camera can be tricky if not tagged MainCamera,
                // but usually Cinemachine Brain is on MainCamera.
                cameraToFace = FindFirstObjectByType<Camera>();
            }

            if (cameraToFace != null)
            {
                 if (_spawnedNameText != null)
                 {
                     // Rotate Text 180 degrees to face camera correctly (TMP often needs this for world space)
                     // Or just LookAt and then rotate 180 Y if needed.
                     _spawnedNameText.transform.rotation = cameraToFace.transform.rotation;
                 }
                 
                 if (_overheadHpBarTransform != null)
                 {
                     // Parent of the HP Bar parts is the transform. The parts are children.
                     // But _overheadHpBarTransform is the Foreground Quad. The Background is its parent's sibling?
                     // In Spawned, we set:
                     // hpBg.transform.SetParent(this.transform); ...
                     // hpFg.transform.SetParent(this.transform); ...
                     // So we need to rotate both or rotate a common parent.
                     // Currently they are independent children of 'this'.
                     // Let's find the BG too, or just rotate this FG and hope BG matches?
                     // Better solution: Rotate the FG, and try to find BG if possible or just rotate FG.
                     // Actually, if we just rotate the FG, the BG stays flat? Yes.
                     // Quick fix: Rotate FG.
                     _overheadHpBarTransform.rotation = cameraToFace.transform.rotation;
                     
                     // Also try to find the BG by name or sibling index?
                     Transform hpBg = transform.Find("HealthBarBG");
                     if (hpBg != null) hpBg.rotation = cameraToFace.transform.rotation;
                 }
            }
        }

        public static PlayerController Local { get; private set; }

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                CurrentHealth = MaxHealth;
            }
            
            _lastVisibleHealth = CurrentHealth;
            if (playerRenderer != null) playerRenderer.enabled = true;

            // 1. Name Tag
            GameObject textObj = new GameObject("DynamicNameTag");
            textObj.transform.SetParent(this.transform);
            textObj.transform.localPosition = new Vector3(0, 2.0f, 0); 
            _spawnedNameText = textObj.AddComponent<TextMeshPro>();
            _spawnedNameText.alignment = TextAlignmentOptions.Center;
            _spawnedNameText.fontSize = 4;
            _spawnedNameText.color = Color.white;
            _spawnedNameText.rectTransform.sizeDelta = new Vector2(5, 1);

            // 2. Overhead HP Bar (Hardcoded)
            // Create a background (Red/Black)
            GameObject hpBg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            hpBg.name = "HealthBarBG";
            hpBg.transform.SetParent(this.transform);
            hpBg.transform.localPosition = new Vector3(0, 1.7f, 0);
            hpBg.transform.localScale = new Vector3(1.0f, 0.15f, 1f);
            Destroy(hpBg.GetComponent<Collider>()); // Remove collider
            
            var bgRenderer = hpBg.GetComponent<Renderer>();
            bgRenderer.material = new Material(Shader.Find("Sprites/Default"));
            bgRenderer.material.color = Color.black;
            bgRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bgRenderer.receiveShadows = false;

            // Create a foreground (Green)
            GameObject hpFg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            hpFg.name = "HealthBarFG";
            hpFg.transform.SetParent(hpBg.transform); // Parent to BG to maintain relative Z-order
            hpFg.transform.localPosition = new Vector3(0, 0, -0.01f); // Slightly in front (relative to BG)
            hpFg.transform.localScale = Vector3.one; // Full size of parent (BG)
            hpFg.transform.localRotation = Quaternion.identity;
            Destroy(hpFg.GetComponent<Collider>());
            
            var fgRenderer = hpFg.GetComponent<Renderer>();
            fgRenderer.material = new Material(Shader.Find("Sprites/Default"));
            fgRenderer.material.color = Color.green;
            fgRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fgRenderer.receiveShadows = false;
            
            _overheadHpBarTransform = hpFg.transform;

            if (fpsCamera != null)
            {
                // Default behavior: Camera connects to local player
                fpsCamera.gameObject.SetActive(Object.HasInputAuthority);
                
                // If we are using Cinemachine, we likely want the MainCamera (with Brain) to render, 
                // so we disable the raw FPS camera if a VC is present.
                if (virtualCamera != null)
                {
                     // Use Virtual Camera logic
                     virtualCamera.gameObject.SetActive(Object.HasInputAuthority);
                     
                     // Disable raw camera so Brain controls the view via Main Camera
                     if (Object.HasInputAuthority)
                     {
                         fpsCamera.gameObject.SetActive(false);
                     }
                }
                else
                {
                    // Fallback to raw camera if no Cinemachine
                    if (Object.HasInputAuthority && Camera.main != null) 
                    {
                        Camera.main.gameObject.SetActive(false);
                    }
                }
            }
            else if (virtualCamera != null)
            {
                // Cinemachine only setup
                virtualCamera.gameObject.SetActive(Object.HasInputAuthority);
            }

            if (Object.HasInputAuthority)
            {
                Local = this; // Set static reference
                
                FusionLauncher launcher = FindFirstObjectByType<FusionLauncher>();
                if (launcher != null)
                {
                    var name = launcher.GetLocalPlayerName();
                    var col = launcher.GetLocalPlayerColorIndex();
                    RPC_SetDetails(name, col);
                }
                _hpBar = FindFirstObjectByType<HPBarController>();
                if (_hpBar != null) _hpBar.UpdateHealth(CurrentHealth, MaxHealth);
            }
        }
        
        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Local == this) Local = null;
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_SendChat(string message, RpcInfo info = default)
        {
             // Server receives this, then broadcasts to everyone
             RPC_ReceiveChat(message);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_ReceiveChat(string message, RpcInfo info = default)
        {
            string formattedMessage = $"{PlayerName}: {message}";
            
            ChatManager chatMgr = FindFirstObjectByType<ChatManager>();
            if (chatMgr != null)
            {
                chatMgr.AddMessageToHistory(formattedMessage);
            }
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_SetDetails(string name, int colorIndex)
        {
            this.PlayerName = name;
            this.PlayerColorIndex = colorIndex;
        }

        private int _spectatingIndex = -1;

        public void SpectateNextPlayer()
        {
            // Only allow spectating if dead
            if (CurrentHealth > 0) return;

            var allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            var otherPlayers = new System.Collections.Generic.List<PlayerController>();

            foreach (var p in allPlayers)
            {
                // Must be a different player and must be alive (optional, maybe spectate dead bodies too?)
                // Let's spectate anyone else for now.
                if (p != this) 
                {
                    otherPlayers.Add(p);
                }
            }

            if (otherPlayers.Count == 0) return;

            // Increment index
            _spectatingIndex = (_spectatingIndex + 1) % otherPlayers.Count;
            var target = otherPlayers[_spectatingIndex];

            // Disable my camera
            if (fpsCamera != null) fpsCamera.gameObject.SetActive(false);
            if (virtualCamera != null) virtualCamera.gameObject.SetActive(false);

            // Disable all other cameras first (to switch cleanly)
            foreach (var p in otherPlayers)
            {
                if (p.fpsCamera != null) p.fpsCamera.gameObject.SetActive(false);
                if (p.virtualCamera != null) p.virtualCamera.gameObject.SetActive(false);
            }

            // Enable target camera locally
            // Note: We are enabling a camera on a remote object. This works locally.
            if (target.virtualCamera != null)
            {
                target.virtualCamera.gameObject.SetActive(true);
            }
            else if (target.fpsCamera != null)
            {
                 target.fpsCamera.gameObject.SetActive(true);
            }
            
            Debug.Log($"Spectating: {target.PlayerName}");
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
