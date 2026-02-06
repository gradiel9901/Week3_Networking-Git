using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System.Collections.Generic;
using System;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Com.MyCompany.MyGame
{
    public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        public static NetworkManager Instance { get; private set; }

        [Header("References")]
        [SerializeField] private NetworkPrefabRef playerPrefab;
        [SerializeField] private Transform teamASpawnPoint;
        [SerializeField] private Transform teamBSpawnPoint;

        private NetworkRunner _runner;
        private InputSystem_Actions _inputActions;

        private void Awake()
        {
            // Singleton
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _inputActions = new InputSystem_Actions();
            
            // CRITICAL for Local Testing: Keep game running when not focused!
            Application.runInBackground = true; 
        }

        private void OnEnable()
        {
            if (_inputActions != null) _inputActions.Player.Enable();
        }

        private void OnDisable()
        {
            if (_inputActions != null) _inputActions.Player.Disable();
        }
        
        // VISUAL DEBUG: Show what Input we are sending
        private Vector2 _lastInputSent;
        
        private void OnGUI()
        {
            if (_runner != null && _runner.IsRunning && !_runner.IsServer)
            {
                GUI.color = Color.yellow;
                GUI.Label(new Rect(10, Screen.height - 40, 300, 30), $"CLIENT INPUT: {_lastInputSent}");
            }
        }

        public async void StartGame(GameMode mode)
        {
            if (_runner != null) return;

            // Create NetworkRunner as a child or separate object? 
            // Better to let it be on this object or a new one.
            // Let's create a NEW object for the runner to keep this manager clean, 
            // OR just add it to this GO. 
            // Adding to this GO is simpler for now.
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);

            var scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);
            var sceneInfo = new NetworkSceneInfo();
            if (scene.IsValid)
            {
                sceneInfo.AddSceneRef(scene, LoadSceneMode.Single);
            }
            
            await _runner.StartGame(new StartGameArgs()
            {
                GameMode = mode,
                SessionName = "TestRoom", // Hardcoded for simplicity as requested
                Scene = sceneInfo,
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
            });
            
            Debug.Log($"[NetworkManager] Started Game in Mode: {mode}");
        }

        #region INetworkRunnerCallbacks

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner.IsServer)
            {
                Debug.Log($"[NetworkManager] Player Joined: {player.PlayerId}");

                // Spawn Logic
                // 1. Determine Team (Host = 0, Client = 1)
                // In a real lobby, we'd check a networked list. 
                // For this simple prototype: Host is 0, everyone else is 1.
                int teamIndex = (player == runner.LocalPlayer) ? 0 : 1;

                Transform spawnTransform = (teamIndex == 0) ? teamASpawnPoint : teamBSpawnPoint;
                
                // Fallback Finding (Same as before)
                if (spawnTransform == null)
                {
                    string targetName = (teamIndex == 0) ? "TeamASpawn" : "TeamBSpawn";
                    GameObject foundObj = GameObject.Find(targetName);
                    if (foundObj == null) foundObj = GameObject.Find((teamIndex == 0) ? "Team A Spawn" : "Team B Spawn");
                    if (foundObj != null) spawnTransform = foundObj.transform;
                }

                Vector3 spawnPosition = new Vector3(0, 5, 0);
                if (spawnTransform != null)
                {
                    Vector3 randomLocalPos = new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 2f, UnityEngine.Random.Range(-0.5f, 0.5f));
                    spawnPosition = spawnTransform.TransformPoint(randomLocalPos);
                }

                NetworkObject playerObj = runner.Spawn(playerPrefab, spawnPosition, Quaternion.identity, player);
                if (playerObj != null)
                {
                    // Initialize Player Details (Name/Team)
                    // ACTION: Direct Assignment because we are on the Server (State Authority)
                    // The Server does NOT need to use an RPC to change Networked properties.
                    PlayerController pc = playerObj.GetComponent<PlayerController>();
                    if (pc != null)
                    {
                        pc.PlayerName = $"Player {player.PlayerId}";
                        pc.TeamIndex = teamIndex;
                        pc.CurrentHealth = PlayerController.MaxHealth; // FIX: Prevent instant death!
                        
                        Debug.Log($"[NetworkManager] Spawning {pc.PlayerName} (Team {pc.TeamIndex}) with {pc.CurrentHealth} HP");
                    }
                }
            }
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            var data = new NetworkInputData();

            // Stop movement if chat is open
            var chatMgr = FindFirstObjectByType<ChatManager>();
            if (chatMgr != null && chatMgr.IsChatOpen)
            {
                data.direction = Vector2.zero;
                input.Set(data);
                return;
            }

            // Input System
            if (_inputActions != null)
            {
                data.direction = _inputActions.Player.Move.ReadValue<Vector2>();
                data.isInteractPressed = _inputActions.Player.Interact.IsPressed();
                
                data.buttons.Set(InputButton.Jump, _inputActions.Player.Jump.IsPressed());
                data.buttons.Set(InputButton.Crouch, _inputActions.Player.Crouch.IsPressed() || (Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed));
                data.buttons.Set(InputButton.Sprint, _inputActions.Player.Sprint.IsPressed() || (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed));
            }

            // Fallback: WASD
            if (data.direction == Vector2.zero && Keyboard.current != null)
            {
                float x = 0;
                float y = 0;
                if (Keyboard.current.wKey.isPressed) y += 1;
                if (Keyboard.current.sKey.isPressed) y -= 1;
                if (Keyboard.current.aKey.isPressed) x -= 1;
                if (Keyboard.current.dKey.isPressed) x += 1;
                data.direction = new Vector2(x, y);
            }

            if (Camera.main != null)
            {
                data.lookYaw = Camera.main.transform.eulerAngles.y;
            }

            // Debug Trace
            if (data.direction != Vector2.zero)
            {
                 // Debug.Log($"[NetworkManager] Input Sent: {data.direction}");
            }
            _lastInputSent = data.direction;

            input.Set(data);
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

        #endregion
    }
}
