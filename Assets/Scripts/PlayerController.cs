using UnityEngine;
using Fusion;
using TMPro;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

namespace Com.MyCompany.MyGame
{
    [RequireComponent(typeof(NetworkTransform), typeof(NetworkObject))]
    public class PlayerController : NetworkBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Renderer playerRenderer;
        [SerializeField] private GameObject deathVfxPrefab;
        [SerializeField] private Animator animator; // Added for character animations

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

        // PlayerName moved to bottom with OnChanged
        // [Networked] public NetworkString<_16> PlayerName { get; set; }

        [Networked]
        public int PlayerColorIndex { get; set; }

        [Networked]
        public int CurrentHealth { get; set; }

        [Networked]
        public NetworkBool IsPoisoned { get; set; }
        
        [Networked]
        private TickTimer PoisonTimer { get; set; }

        [Networked] 
        public NetworkButtons ButtonsPrevious { get; set; }

        public const int MaxHealth = 100;
        
        [Header("Movement Logic")]
        [SerializeField] private float jumpForce = 2.0f; // This acts as Jump Height in the formula
        [SerializeField] private float gravityValue = -20.0f; // Stronger gravity for snappier jumps
        
        private CharacterController _cc;
        [Networked] 
        private float VerticalVelocity { get; set; } // FIX: Must be Networked for prediction/rollback!

        // ANIMATION SYNC (Networked Properties for Proxies)
        [Networked] private float NetAnimSpeed { get; set; }
        [Networked] private NetworkBool NetIsGrounded { get; set; }
        [Networked] private NetworkBool NetIsCrouching { get; set; }
        [Networked] private NetworkBool NetIsJumping { get; set; }
        
        // private float _verticalVelocity; // Old local variable causing desync
        // private bool _isGrounded; // Removed, using NetIsGrounded now

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

                if (_cc == null) _cc = GetComponent<CharacterController>();

                if (_cc == null) _cc = GetComponent<CharacterController>();

                // --- ROBUST GROUND CHECK ---
                // CharacterController.isGrounded is often flaky (flickers true/false).
                // Use a dedicated SphereCheck at the feet.
                float groundRadius = 0.28f;
                Vector3 groundCheckPos = transform.position + Vector3.down * 0.1f; // Slightly below feet
                // Layer mask: Default (1) + Ground layers. 
                // Adjust mask if you have specific layers!
                NetIsGrounded = Physics.CheckSphere(groundCheckPos, groundRadius, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore);
                
                // Fallback: If no dedicated layer, try everything except Player
                if (LayerMask.GetMask("Default", "Ground") == 0)
                {
                     NetIsGrounded = Physics.CheckSphere(groundCheckPos, groundRadius, ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore);
                }

                // Reset vertical velocity if grounded
                if (NetIsGrounded && VerticalVelocity < 0)
                {
                    VerticalVelocity = -2f; // Small constant downward force to snap to ground
                }

                // --- JUMP CHECK ---
                // Check if Jump button pressed this frame
                if (data.buttons.WasPressed(ButtonsPrevious, InputButton.Jump))
                {
                    if (NetIsGrounded)
                    {
                        VerticalVelocity = Mathf.Sqrt(jumpForce * -2f * gravityValue);
                        // Trigger logic: handled by state change usually, or one-shot? 
                        // For now, let's rely on NetIsJumping state in air
                    }
                }
                
                // --- CROUCH CHECK ---
                bool isCrouching = data.buttons.IsSet(InputButton.Crouch);
                bool isSprinting = data.buttons.IsSet(InputButton.Sprint);
                
                // --- MOVEMENT ---
                // Calculate Rotation based on Camera Look Yaw
                Quaternion cameraRotation = Quaternion.Euler(0, data.lookYaw, 0);
                
                // Transform Input Direction (Local -> World)
                Vector3 inputDir = new Vector3(data.direction.x, 0, data.direction.y);
                Vector3 moveDir = cameraRotation * inputDir;
                
                // Apply Speed Modifiers
                float currentSpeed = moveSpeed;
                if (isCrouching) 
                {
                    currentSpeed *= 0.5f; // Half speed when crouching
                }
                else if (isSprinting && NetIsGrounded) 
                {
                    currentSpeed *= 1.5f; // 1.5x speed when sprinting (and not crouching)
                }
                
                // Apply Movement Speed
                Vector3 moveDelta = moveDir * currentSpeed * Runner.DeltaTime;

                // Apply Gravity
                VerticalVelocity += gravityValue * Runner.DeltaTime;
                moveDelta.y += VerticalVelocity * Runner.DeltaTime;
                
                // Move Player via CharacterController
                if (_cc != null)
                {
                    // Debug Trace for "Frozen" issue
                    Vector3 posBefore = transform.position;
                    _cc.Move(moveDelta);
                    if (data.direction != Vector2.zero) Debug.Log($"[MOVELOG] In: {moveDelta} | PosDelta: {transform.position - posBefore}");
                }
                else
                {
                    // Fallback to Transform (should not happen if RequireComponent is set)
                    transform.Translate(moveDelta, Space.World);
                }

                // Rotate Player to Face Movement
                if (moveDir.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), Runner.DeltaTime * 10f); // Smooth rotation
                }
                
                // --- SYNC ANIMATION STATE ---
                // We update the Networked Properties here. The Render() method will apply them to the Animator.
                
                // Speed (Horizontal magnitude)
                float inputMag = new Vector3(data.direction.x, 0, data.direction.y).magnitude;
                
                // Use ACTUAL calculated physics speed (e.g. 5.0, 7.5, 2.5) instead of normalized 0-1
                float speedToPass = (inputMag > 0.01f) ? currentSpeed : 0f;

                NetAnimSpeed = speedToPass;
                NetIsCrouching = isCrouching;
                NetIsJumping = !NetIsGrounded && VerticalVelocity > 0;
                
                /* Animator update moved to Render() */
                
                // Store buttons for "WasPressed" check next frame
                ButtonsPrevious = data.buttons;
                
                // Debug log for Server Input Receipt
                if (Runner.IsServer && data.direction != Vector2.zero) 
                {
                   Debug.Log($"[PlayerController] Server Received Input from {Object.InputAuthority}: {data.direction}");
                }
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
            // --- SYNC ANIMATION VISUALS (Runs on All Clients) ---
            if (animator != null)
            {
                // We use the Networked Properties so proxies can see the animation too!
                // Lerp speed for smoothness
                float currentAnimSpeed = animator.GetFloat("Speed");
                float newAnimSpeed = Mathf.Lerp(currentAnimSpeed, NetAnimSpeed, Runner.DeltaTime * 10f);
                animator.SetFloat("Speed", newAnimSpeed);

                animator.SetBool("IsGrounded", NetIsGrounded);
                animator.SetBool("IsCrouching", NetIsCrouching);
                
                // For Jumping, we can use the state. 
                // Note: Triggers are harder to sync without a ChangeDetector, 
                // but the boolean 'IsJumping' state often handles the "falling" loop.
                animator.SetBool("IsJumping", NetIsJumping);
            }

            // Manual Change Detection to avoid Fusion versioning issues with OnChanged
            if (TeamIndex != _lastTeamIndex || PlayerName.ToString() != _lastPlayerName)
            {
                _lastTeamIndex = TeamIndex;
                _lastPlayerName = PlayerName.ToString();
                UpdateVisuals();
            }

            UpdateVisuals(); // Keep this call if it was doing per-frame updates, or rely on change detection? 
            // Original code called UpdateVisuals() every frame. 
            // Note: Calling UpdateVisuals every frame might be expensive if it instantiates particles.
            // Let's look at UpdateVisuals implementation.
            // It calls TeamVisualsHelper.Apply... which checks for existing containers.
            // So it's safe to call often, but the CHANGE detection is better for One-Shot events.
            // However, to be safe and match original behavior + new fix, we can leave the original call OR just rely on the new one.
            // Since the user had issues with visibility, let's trust the change detection mainly, 
            // BUT the original code bad `UpdateVisuals()` at the top. 
            // Let's REMOVE the unconditional `UpdateVisuals()` and rely on the Change Detection + Spawned() call.
            
            // Actually, wait. UpdateVisuals updates the Name Text too. 
            // If we remove it, the name might not update during interpolation?
            // Re-adding the unconditional call effectively masks the issue but is wasteful.
            // The SAFE fix is to keep the unconditional call for now (or move it inside the check if we are sure).
            // Let's just ADD the check at the top and keep the rest flow.
            
            // ... Wait, if I see the code below line 248, it says `UpdateVisuals();`.
            // I will replace it.
            
            base.Render(); // Don't forget base.Render() if needed, though usually empty in NetworkBehaviour.
            
            // MERGED LOGIC
            if (TeamIndex != _lastTeamIndex || PlayerName.ToString() != _lastPlayerName)
            {
                _lastTeamIndex = TeamIndex;
                _lastPlayerName = PlayerName.ToString();
                UpdateVisuals();
            }
            
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
            Debug.Log($"[PlayerController] Spawned! ID: {Id} | InputAuthority: {Object.InputAuthority} | HasInputAuth: {Object.HasInputAuthority} | HasStateAuth: {Object.HasStateAuthority}");

            if (Object.HasStateAuthority)
            {
                CurrentHealth = MaxHealth;
            }
            
            _lastVisibleHealth = CurrentHealth;
            if (playerRenderer != null) playerRenderer.enabled = true;

            // Auto-find Animator if not manually assigned
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>(); // Check children too for model hierarchy

            if (animator != null)
            {
                 // FIX: Disable Root Motion so CharacterController drives the movement!
                 animator.applyRootMotion = false;
                 
                 Debug.Log($"[PlayerController] Animator Found! Listing Parameters:");
                 foreach(var p in animator.parameters)
                 {
                     Debug.Log($"   - Name: '{p.name}' Type: {p.type} Default: {p.defaultFloat}");
                 }
            }
            else
            {
                Debug.LogError("[PlayerController] CRITICAL: Animator NOT found on player prefab!");
            }

            // Setup CharacterController
            _cc = GetComponent<CharacterController>();
            if (_cc != null)
            {
                // Reset CC to ensure it accepts the Spawn position
                _cc.enabled = false;
                _cc.enabled = true;
                Debug.Log($"[PlayerController] CharacterController reset. Pos: {transform.position}");
            }

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
                
                Local = this; // Set static reference
                
                // STANDALONE MODE: Initialize directly from FusionLauncher UI
                FusionLauncher launcher = FindFirstObjectByType<FusionLauncher>();
                if (launcher != null)
                {
                    Debug.Log("[PlayerController] Standalone Mode: Initializing from FusionLauncher UI.");
                    var name = launcher.GetLocalPlayerName();
                    // var teamIndex = launcher.GetLocalPlayerTeamIndex(); // Team is set by NetworkManager, don't override
                    RPC_SetDetails(name, TeamIndex);
                }
                else
                {
                    Debug.LogWarning("[PlayerController] FusionLauncher not found. Using default details.");
                    // Optional: RPC_SetDetails("Player", 0);
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

        [Networked]
        public NetworkString<_16> PlayerName { get; set; }

        [Networked]
        public int TeamIndex { get; set; }

        private int _lastTeamIndex = -1;
        private string _lastPlayerName = "";

        // Render method merged above

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_SetDetails(string name, int teamIndex)
        {
            this.PlayerName = name;
            this.TeamIndex = teamIndex;
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

            // Team Visuals (Procedural Fire/Ice)
            // Team Visuals removed as we migrate to character models
            // if (TeamIndex == 0) ... ApplyFireVisuals
            // if (TeamIndex == 1) ... ApplyIceVisuals
        }

        private void OnGUI()
        {
            if (Runner == null || !Runner.IsRunning) return;

            // Only show for the specific player, projected to screen
            if (Object == null) return;

            Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2.5f);
            if (screenPos.z < 0) return; // Behind camera

            GUI.color = Color.black;
            string debugInfo = $"ID: {Id}\nAuth: {(Object.HasInputAuthority ? "INPUT" : "")} {(Object.HasStateAuthority ? "STATE" : "")}\n" +
                               $"Grounded: {NetIsGrounded}\n" +
                               $"Vel-Y: {VerticalVelocity:F2}\n" +
                               $"AnimSpeed: {NetAnimSpeed:F2}";
            
            // Server-Only Input Check info
            if (Runner.IsServer)
            {
                 // We can't easily see the current frame input here since it's in FixedUpdate,
                 // but we can show the Networked Animation Speed which REFLECTS input.
            }

            GUI.Label(new Rect(screenPos.x - 50, Screen.height - screenPos.y - 50, 200, 100), debugInfo);
        }
    }
}
