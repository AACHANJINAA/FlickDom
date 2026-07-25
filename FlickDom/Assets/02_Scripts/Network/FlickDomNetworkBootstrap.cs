using System;
using System.Collections.Generic;
using FlickDom.Gameplay;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FlickDom.Networking
{
    public sealed class FlickDomNetworkBootstrap : MonoBehaviour
    {
        [Header("Network")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private UnityTransport unityTransport;
        [SerializeField] private string networkSceneName = DefaultNetworkSceneName;
        [SerializeField] private string connectAddress = "127.0.0.1";
        [SerializeField] private ushort port = 7777;
        [SerializeField] private int maxPlayers = 2;
        [SerializeField] private bool persistAcrossScenes;

        [Header("Local Test Controls")]
        [SerializeField] private bool enableKeyboardShortcuts = true;
        [SerializeField] private bool showRuntimeStatus = true;
        [SerializeField] private bool showLobbyUi = true;
        [SerializeField] private bool disableLocalAutoStartForNetworkLobby = true;
        [SerializeField] private bool enableCommandLineAutoStart = true;
        [SerializeField] private Key startHostKey = Key.S;
        [SerializeField] private Key startClientKey = Key.C;
        [SerializeField] private Key shutdownKey = Key.X;

        private const string DefaultNetworkSceneName = "good_Scene";
        private const string StartGameMessageName = "FlickDom.StartGame";
        private const string LobbyStateMessageName = "FlickDom.LobbyState";
        private const string GameStateMessageName = "FlickDom.GameState";
        private const string FlickRequestMessageName = "FlickDom.FlickRequest";
        private const string FlickAcceptedMessageName = "FlickDom.FlickAccepted";
        private const string PieceOrderSelectionMessageName = "FlickDom.PieceOrderSelection";
        private const string PieceTransformMessageName = "FlickDom.PieceTransform";

        public event Action<FlickDomPlayerId> LocalPlayerRoleChanged;

        public static FlickDomNetworkBootstrap Active { get; private set; }

        private GameModeManager gameModeManager;
        private string addressInput = "127.0.0.1";
        private string portInput = "7777";
        private bool startGameMessageHandlerRegistered;
        private bool lobbyStateMessageHandlerRegistered;
        private bool gameStateMessageHandlerRegistered;
        private bool flickRequestMessageHandlerRegistered;
        private bool flickAcceptedMessageHandlerRegistered;
        private bool pieceOrderSelectionMessageHandlerRegistered;
        private bool pieceTransformMessageHandlerRegistered;
        private bool gameModeEventsSubscribed;
        private bool networkGameStarted;
        private bool localGameStartedFromNetwork;
        private int lobbyPlayerCount;
        private float nextTransformBroadcastTime;
        private const float TransformBroadcastInterval = 0.05f;

        public NetworkManager NetworkManager
        {
            get { return networkManager; }
        }

        public FlickDomPlayerId LocalPlayerId { get; private set; } = FlickDomPlayerId.None;

        public bool IsRunning
        {
            get { return networkManager != null && networkManager.IsListening; }
        }

        public bool IsHost
        {
            get { return networkManager != null && networkManager.IsHost; }
        }

        public bool IsClientOnly
        {
            get { return networkManager != null && networkManager.IsClient && !networkManager.IsHost; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrapInScene()
        {
            if (!IsSceneNameNetworkEnabled(SceneManager.GetActiveScene().name, DefaultNetworkSceneName))
            {
                return;
            }

            if (FindAnyObjectByType<FlickDomNetworkBootstrap>() != null)
            {
                return;
            }

            GameObject bootstrapObject = new GameObject("FlickDom Network Bootstrap");
            bootstrapObject.AddComponent<FlickDomNetworkBootstrap>();
        }

        private void Awake()
        {
            if (!IsNetworkEnabledForActiveScene())
            {
                Debug.Log("[Network] FlickDom network lobby is disabled outside scene '" + networkSceneName + "'.", this);
                enabled = false;
                return;
            }

            Active = this;
            ResolveNetworkManager();
            ConfigureNetworkManager();
            ResolveGameModeManager();
            ConfigureGameModeAutoStart();
            addressInput = connectAddress;
            portInput = port.ToString();

            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
                DontDestroyOnLoad(networkManager.gameObject);
            }
        }

        private void Start()
        {
            if (!IsNetworkEnabledForActiveScene())
            {
                return;
            }

            if (enableCommandLineAutoStart)
            {
                TryAutoStartFromCommandLine();
            }
        }

        private void OnEnable()
        {
            SubscribeNetworkEvents(true);
        }

        private void OnDisable()
        {
            SubscribeNetworkEvents(false);
            SubscribeGameModeEvents(false);
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        private void Update()
        {
            RegisterNetworkMessageHandlersIfReady();
            BroadcastPieceTransformsIfNeeded();

            if (!enableKeyboardShortcuts || showLobbyUi || Keyboard.current == null)
            {
                return;
            }

            if (WasPressedThisFrame(startHostKey))
            {
                Debug.Log("[Network] S pressed. Trying to start Host.", this);
                StartHost();
            }
            else if (WasPressedThisFrame(startClientKey))
            {
                Debug.Log("[Network] C pressed. Trying to start Client.", this);
                StartClient();
            }
            else if (WasPressedThisFrame(shutdownKey))
            {
                Debug.Log("[Network] X pressed. Shutting down network.", this);
                Shutdown();
            }
        }

        private void OnGUI()
        {
            if (!showRuntimeStatus || !IsNetworkEnabledForActiveScene())
            {
                return;
            }

            string mode = "Offline";
            if (networkManager != null)
            {
                if (networkManager.IsHost)
                {
                    mode = "Host";
                }
                else if (networkManager.IsClient)
                {
                    mode = "Client";
                }
                else if (networkManager.IsServer)
                {
                    mode = "Server";
                }
            }

            GUI.Box(new Rect(16f, 16f, 320f, 92f), "FlickDom Network");
            GUI.Label(new Rect(28f, 42f, 296f, 22f), "Mode: " + mode + " / LocalRole: " + LocalPlayerId);
            GUI.Label(new Rect(28f, 64f, 296f, 22f), "Target: " + connectAddress + ":" + port);
            GUI.Label(new Rect(28f, 86f, 296f, 22f), "S: Host   C: Client   X: Shutdown");

            if (showLobbyUi && !networkGameStarted)
            {
                DrawLobbyUi(mode);
            }
        }

        private void DrawLobbyUi(string mode)
        {
            float width = Mathf.Min(460f, Screen.width - 32f);
            float height = 330f;
            Rect area = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("FlickDom Lobby");
            GUILayout.Label("Mode: " + mode + " / Role: " + LocalPlayerId);
            GUILayout.Label("Players: " + GetVisiblePlayerCount() + " / " + maxPlayers);

            GUILayout.Space(8f);
            GUILayout.Label("Host IP");
            addressInput = GUILayout.TextField(addressInput);

            GUILayout.Label("Port");
            portInput = GUILayout.TextField(portInput);

            GUILayout.Space(8f);
            using (new GuiEnabledScope(!IsRunning))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Create Room"))
                {
                    ApplyLobbyConnectionInput();
                    StartHost();
                }

                if (GUILayout.Button("Join Room"))
                {
                    ApplyLobbyConnectionInput();
                    StartClient();
                }

                GUILayout.EndHorizontal();
            }

            bool canStartGame = networkManager != null
                && networkManager.IsHost
                && GetHostConnectedPlayerCount() >= maxPlayers
                && !networkGameStarted;

            GUILayout.Space(8f);
            using (new GuiEnabledScope(canStartGame))
            {
                if (GUILayout.Button("Start Game", GUILayout.Height(32f)))
                {
                    StartNetworkGame();
                }
            }

            using (new GuiEnabledScope(IsRunning))
            {
                if (GUILayout.Button("Shutdown", GUILayout.Height(24f)))
                {
                    Shutdown();
                }
            }

            GUILayout.Label(GetLobbyHint(canStartGame));
            GUILayout.EndArea();
        }

        [ContextMenu("Start Host")]
        public void StartHost()
        {
            if (!CanStartNetwork())
            {
                return;
            }

            Debug.Log("[Network] Starting Host on " + connectAddress + ":" + port + ".", this);
            ConfigureTransport(connectAddress, port);
            bool started = networkManager.StartHost();
            if (!started)
            {
                Debug.LogError("[Network] Failed to start Host.", this);
                return;
            }

            RegisterNetworkMessageHandlersIfReady();
            SetLocalPlayerRole(FlickDomPlayerId.Player1);
            BroadcastLobbyState();
            Debug.Log("[Network] Host started. Local role is Player1.", this);
        }

        [ContextMenu("Start Client")]
        public void StartClient()
        {
            if (!CanStartNetwork())
            {
                return;
            }

            Debug.Log("[Network] Starting Client. Target is " + connectAddress + ":" + port + ".", this);
            ConfigureTransport(connectAddress, port);
            bool started = networkManager.StartClient();
            if (!started)
            {
                Debug.LogError("[Network] Failed to start Client.", this);
                return;
            }

            RegisterNetworkMessageHandlersIfReady();
            SetLocalPlayerRole(FlickDomPlayerId.Player2);
            Debug.Log("[Network] Client start requested. Local role is Player2.", this);
        }

        [ContextMenu("Shutdown")]
        public void Shutdown()
        {
            UnregisterNetworkMessageHandlers();

            if (networkManager != null && networkManager.IsListening)
            {
                networkManager.Shutdown();
                Debug.Log("[Network] NetworkManager shutdown complete.", this);
            }
            else
            {
                Debug.Log("[Network] Shutdown requested, but NetworkManager was not running.", this);
            }

            SetLocalPlayerRole(FlickDomPlayerId.None);
            networkGameStarted = false;
            localGameStartedFromNetwork = false;
            lobbyPlayerCount = 0;
        }

        public void SetConnectionTarget(string address, ushort targetPort)
        {
            if (networkManager != null && networkManager.IsListening)
            {
                Debug.LogWarning("[Network] Cannot change connection target while networking is running.", this);
                return;
            }

            connectAddress = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            port = targetPort;
        }

        public bool AllowsLocalInputFor(FlickDomPlayerId playerId)
        {
            if (!IsRunning)
            {
                return true;
            }

            return LocalPlayerId != FlickDomPlayerId.None && LocalPlayerId == playerId;
        }

        public bool AllowsLocalStateControl()
        {
            return !IsRunning || IsHost;
        }

        public void SubmitFlickRequestToHost(FlickDomPlayerId owner, string pieceId, Vector3 impulse)
        {
            if (networkManager == null
                || !networkManager.IsClient
                || networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            FixedString64Bytes fixedPieceId = new FixedString64Bytes(pieceId ?? string.Empty);
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) + 64 + sizeof(float) * 3, Allocator.Temp))
            {
                writer.WriteValueSafe((int)owner);
                writer.WriteValueSafe(fixedPieceId);
                writer.WriteValueSafe(impulse);
                networkManager.CustomMessagingManager.SendNamedMessage(FlickRequestMessageName, NetworkManager.ServerClientId, writer);
            }

            Debug.Log("[Network] Flick request sent to Host. Piece: " + pieceId + ", Impulse: " + impulse + ".", this);
        }

        public void SubmitPieceOrderSelectionToHost(FlickDomPlayerId owner, string pieceId)
        {
            if (networkManager == null
                || !networkManager.IsClient
                || networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            FixedString64Bytes fixedPieceId = new FixedString64Bytes(pieceId ?? string.Empty);
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) + 64, Allocator.Temp))
            {
                writer.WriteValueSafe((int)owner);
                writer.WriteValueSafe(fixedPieceId);
                networkManager.CustomMessagingManager.SendNamedMessage(PieceOrderSelectionMessageName, NetworkManager.ServerClientId, writer);
            }

            Debug.Log("[Network] Piece order selection sent to Host. Player: " + owner + ", Piece: " + pieceId + ".", this);
        }

        public void NotifyHostPieceOrderSelected(FlickDomPlayerId owner, string pieceId)
        {
            if (networkManager == null
                || !networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            SendPieceOrderSelectionToClients(owner, pieceId);
            Debug.Log("[Network] Piece order selection broadcast. Player: " + owner + ", Piece: " + pieceId + ".", this);
        }

        public void NotifyHostFlickAccepted(FlickDomPlayerId owner, string pieceId)
        {
            if (networkManager == null
                || !networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            SendFlickAcceptedToClients(owner, pieceId);
            SendAllPieceTransformsToClients();
            Debug.Log("[Network] Flick accepted broadcast. Player: " + owner + ", Piece: " + pieceId + ".", this);
        }

        private void ResolveNetworkManager()
        {
            if (networkManager == null)
            {
                networkManager = NetworkManager.Singleton != null
                    ? NetworkManager.Singleton
                    : FindAnyObjectByType<NetworkManager>();
            }

            if (networkManager == null)
            {
                GameObject managerObject = new GameObject("NetworkManager");
                networkManager = managerObject.AddComponent<NetworkManager>();
                unityTransport = managerObject.AddComponent<UnityTransport>();
                EnsureNetworkConfig();
                networkManager.NetworkConfig.NetworkTransport = unityTransport;
                Debug.Log("[Network] Runtime NetworkManager created.", this);
                return;
            }

            EnsureNetworkConfig();

            if (unityTransport == null)
            {
                unityTransport = networkManager.GetComponent<UnityTransport>();
            }

            if (unityTransport == null)
            {
                unityTransport = networkManager.gameObject.AddComponent<UnityTransport>();
            }
        }

        private void ConfigureNetworkManager()
        {
            if (networkManager == null || unityTransport == null)
            {
                return;
            }

            EnsureNetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = unityTransport;
            networkManager.NetworkConfig.ConnectionApproval = true;
        }

        private void ResolveGameModeManager()
        {
            if (gameModeManager == null)
            {
                gameModeManager = FindAnyObjectByType<GameModeManager>();
            }
        }

        private void ConfigureGameModeAutoStart()
        {
            if (!disableLocalAutoStartForNetworkLobby || !IsNetworkEnabledForActiveScene())
            {
                return;
            }

            ResolveGameModeManager();
            if (gameModeManager != null)
            {
                gameModeManager.SetStartLocalGameOnStart(false);
                SubscribeGameModeEvents(true);
                Debug.Log("[Network] Disabled GameModeManager local auto start for lobby flow.", this);
            }
        }

        private void EnsureNetworkConfig()
        {
            if (networkManager == null || networkManager.NetworkConfig != null)
            {
                return;
            }

            networkManager.NetworkConfig = new NetworkConfig();
        }

        private bool CanStartNetwork()
        {
            ResolveNetworkManager();
            ConfigureNetworkManager();

            if (networkManager == null || unityTransport == null)
            {
                Debug.LogError("[Network] NetworkManager and UnityTransport are required.", this);
                return false;
            }

            if (networkManager.IsListening)
            {
                Debug.LogWarning("[Network] NetworkManager is already running.", this);
                return false;
            }

            return true;
        }

        private void ConfigureTransport(string address, ushort targetPort)
        {
            unityTransport.SetConnectionData(address, targetPort);
        }

        private void SubscribeNetworkEvents(bool subscribe)
        {
            if (networkManager == null)
            {
                return;
            }

            if (subscribe)
            {
                networkManager.ConnectionApprovalCallback += HandleConnectionApproval;
                networkManager.OnClientConnectedCallback += HandleClientConnected;
                networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            }
            else
            {
                networkManager.ConnectionApprovalCallback -= HandleConnectionApproval;
                networkManager.OnClientConnectedCallback -= HandleClientConnected;
                networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        private void HandleConnectionApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            int connectedClients = networkManager != null ? networkManager.ConnectedClientsIds.Count : 0;
            bool canJoin = connectedClients < maxPlayers;

            response.Approved = canJoin;
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = canJoin ? string.Empty : "Room is full.";
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (networkManager == null)
            {
                return;
            }

            if (networkManager.IsHost && clientId == networkManager.LocalClientId)
            {
                SetLocalPlayerRole(FlickDomPlayerId.Player1);
            }
            else if (networkManager.IsClient && clientId == networkManager.LocalClientId)
            {
                SetLocalPlayerRole(FlickDomPlayerId.Player2);
            }

            Debug.Log("[Network] Client connected: " + clientId + ".", this);
            RegisterNetworkMessageHandlersIfReady();
            BroadcastLobbyState();

            if (networkManager.IsHost && networkGameStarted)
            {
                SendStartGameMessageToClient(clientId);
                BroadcastGameState();
                SendAllPieceTransformsToClient(clientId);
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            Debug.Log("[Network] Client disconnected: " + clientId + ".", this);

            if (networkManager != null && clientId == networkManager.LocalClientId)
            {
                SetLocalPlayerRole(FlickDomPlayerId.None);
            }

            BroadcastLobbyState();
        }

        private void SetLocalPlayerRole(FlickDomPlayerId playerId)
        {
            if (LocalPlayerId == playerId)
            {
                return;
            }

            LocalPlayerId = playerId;
            LocalPlayerRoleChanged?.Invoke(LocalPlayerId);
            Debug.Log("[Network] Local player role: " + LocalPlayerId + ".", this);
        }

        private void ApplyLobbyConnectionInput()
        {
            string trimmedAddress = string.IsNullOrWhiteSpace(addressInput) ? "127.0.0.1" : addressInput.Trim();
            if (!ushort.TryParse(portInput, out ushort parsedPort))
            {
                parsedPort = 7777;
                portInput = parsedPort.ToString();
            }

            SetConnectionTarget(trimmedAddress, parsedPort);
        }

        private int GetVisiblePlayerCount()
        {
            if (networkManager == null || !networkManager.IsListening)
            {
                return 0;
            }

            if (networkManager.IsServer)
            {
                return GetHostConnectedPlayerCount();
            }

            return Mathf.Max(lobbyPlayerCount, networkManager.IsConnectedClient ? 1 : 0);
        }

        private int GetHostConnectedPlayerCount()
        {
            if (networkManager == null || !networkManager.IsServer)
            {
                return 0;
            }

            return networkManager.ConnectedClientsIds.Count;
        }

        private string GetLobbyHint(bool canStartGame)
        {
            if (!IsRunning)
            {
                return "Create a room or enter Host IP, then join.";
            }

            if (networkGameStarted)
            {
                return "Game started.";
            }

            if (networkManager != null && networkManager.IsHost)
            {
                return canStartGame ? "Two players connected. Start Game is ready." : "Waiting for Player 2.";
            }

            return "Connected. Waiting for Host to start.";
        }

        private void StartNetworkGame()
        {
            if (networkManager == null || !networkManager.IsHost)
            {
                Debug.LogWarning("[Network] Only Host can start the game.", this);
                return;
            }

            if (GetHostConnectedPlayerCount() < maxPlayers)
            {
                Debug.LogWarning("[Network] Cannot start game until two players are connected.", this);
                return;
            }

            networkGameStarted = true;
            BroadcastLobbyState();
            StartGameLocally();
            SendStartGameMessageToClients();
            BroadcastGameState();
            SendAllPieceTransformsToClients();
            Debug.Log("[Network] Host started network game for " + GetHostConnectedPlayerCount() + " players.", this);
        }

        private void StartGameLocally()
        {
            if (localGameStartedFromNetwork)
            {
                return;
            }

            ResolveGameModeManager();
            if (gameModeManager == null)
            {
                Debug.LogWarning("[Network] GameModeManager was not found. Network game start signal was received, but local game could not start.", this);
                return;
            }

            if (gameModeManager.CurrentState != FlickDomGameState.NotStarted)
            {
                localGameStartedFromNetwork = true;
                Debug.Log("[Network] GameModeManager already started. Current state: " + gameModeManager.CurrentState + ".", this);
                return;
            }

            gameModeManager.StartLocalGame();
            localGameStartedFromNetwork = true;
            SubscribeGameModeEvents(true);
            BroadcastGameState();
            Debug.Log("[Network] Local GameModeManager started.", this);
        }

        private void SendStartGameMessageToClients()
        {
            if (networkManager == null
                || !networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            using (FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp))
            {
                networkManager.CustomMessagingManager.SendNamedMessageToAll(StartGameMessageName, writer);
            }
        }

        private void SendStartGameMessageToClient(ulong clientId)
        {
            if (networkManager == null
                || !networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            using (FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp))
            {
                networkManager.CustomMessagingManager.SendNamedMessage(StartGameMessageName, clientId, writer);
            }
        }

        private void BroadcastLobbyState()
        {
            if (networkManager == null
                || !networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            lobbyPlayerCount = GetHostConnectedPlayerCount();
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) + sizeof(bool), Allocator.Temp))
            {
                writer.WriteValueSafe(lobbyPlayerCount);
                writer.WriteValueSafe(networkGameStarted);
                networkManager.CustomMessagingManager.SendNamedMessageToAll(LobbyStateMessageName, writer);
            }

            Debug.Log("[Network] Lobby state broadcast. Players: " + lobbyPlayerCount + "/" + maxPlayers + ", Started: " + networkGameStarted + ".", this);
        }

        private void RegisterNetworkMessageHandlersIfReady()
        {
            if (networkManager == null
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            if (!startGameMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(StartGameMessageName, HandleStartGameMessage);
                startGameMessageHandlerRegistered = true;
                Debug.Log("[Network] Start game message handler registered.", this);
            }

            if (!lobbyStateMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(LobbyStateMessageName, HandleLobbyStateMessage);
                lobbyStateMessageHandlerRegistered = true;
                Debug.Log("[Network] Lobby state message handler registered.", this);
            }

            if (!gameStateMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(GameStateMessageName, HandleGameStateMessage);
                gameStateMessageHandlerRegistered = true;
                Debug.Log("[Network] Game state message handler registered.", this);
            }

            if (!flickRequestMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(FlickRequestMessageName, HandleFlickRequestMessage);
                flickRequestMessageHandlerRegistered = true;
                Debug.Log("[Network] Flick request message handler registered.", this);
            }

            if (!flickAcceptedMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(FlickAcceptedMessageName, HandleFlickAcceptedMessage);
                flickAcceptedMessageHandlerRegistered = true;
                Debug.Log("[Network] Flick accepted message handler registered.", this);
            }

            if (!pieceOrderSelectionMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PieceOrderSelectionMessageName, HandlePieceOrderSelectionMessage);
                pieceOrderSelectionMessageHandlerRegistered = true;
                Debug.Log("[Network] Piece order selection message handler registered.", this);
            }

            if (!pieceTransformMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PieceTransformMessageName, HandlePieceTransformMessage);
                pieceTransformMessageHandlerRegistered = true;
                Debug.Log("[Network] Piece transform message handler registered.", this);
            }
        }

        private void UnregisterNetworkMessageHandlers()
        {
            if (networkManager == null || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            if (startGameMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(StartGameMessageName);
                startGameMessageHandlerRegistered = false;
            }

            if (lobbyStateMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyStateMessageName);
                lobbyStateMessageHandlerRegistered = false;
            }

            if (gameStateMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(GameStateMessageName);
                gameStateMessageHandlerRegistered = false;
            }

            if (flickRequestMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(FlickRequestMessageName);
                flickRequestMessageHandlerRegistered = false;
            }

            if (flickAcceptedMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(FlickAcceptedMessageName);
                flickAcceptedMessageHandlerRegistered = false;
            }

            if (pieceOrderSelectionMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(PieceOrderSelectionMessageName);
                pieceOrderSelectionMessageHandlerRegistered = false;
            }

            if (pieceTransformMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(PieceTransformMessageName);
                pieceTransformMessageHandlerRegistered = false;
            }
        }

        private void HandleStartGameMessage(ulong senderClientId, FastBufferReader reader)
        {
            networkGameStarted = true;
            StartGameLocally();
            Debug.Log("[Network] Start game message received from client " + senderClientId + ".", this);
        }

        private void HandleLobbyStateMessage(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int playerCount);
            reader.ReadValueSafe(out bool gameStarted);

            lobbyPlayerCount = playerCount;
            networkGameStarted = gameStarted;

            if (networkGameStarted)
            {
                StartGameLocally();
            }

            Debug.Log("[Network] Lobby state received from client " + senderClientId + ". Players: " + lobbyPlayerCount + "/" + maxPlayers + ", Started: " + networkGameStarted + ".", this);
        }

        private void BroadcastGameState()
        {
            if (networkManager == null
                || !networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            ResolveGameModeManager();
            if (gameModeManager == null)
            {
                return;
            }

            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) * 3, Allocator.Temp))
            {
                writer.WriteValueSafe((int)gameModeManager.CurrentState);
                writer.WriteValueSafe((int)gameModeManager.ActivePlayer);
                writer.WriteValueSafe(gameModeManager.RoundNumber);
                networkManager.CustomMessagingManager.SendNamedMessageToAll(GameStateMessageName, writer);
            }

            Debug.Log("[Network] Game state broadcast. State: " + gameModeManager.CurrentState + ", Active: " + gameModeManager.ActivePlayer + ", Round: " + gameModeManager.RoundNumber + ".", this);
        }

        private void HandleGameStateMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager != null && networkManager.IsHost)
            {
                return;
            }

            reader.ReadValueSafe(out int stateValue);
            reader.ReadValueSafe(out int activePlayerValue);
            reader.ReadValueSafe(out int roundNumber);

            ResolveGameModeManager();
            if (gameModeManager == null)
            {
                return;
            }

            FlickDomGameState state = (FlickDomGameState)stateValue;
            FlickDomPlayerId activePlayer = (FlickDomPlayerId)activePlayerValue;
            gameModeManager.ApplyNetworkStateSnapshot(state, activePlayer, roundNumber);
            Debug.Log("[Network] Game state received from client " + senderClientId + ". State: " + state + ", Active: " + activePlayer + ", Round: " + roundNumber + ".", this);
        }

        private void HandleFlickRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager == null || !networkManager.IsHost)
            {
                return;
            }

            reader.ReadValueSafe(out int ownerValue);
            reader.ReadValueSafe(out FixedString64Bytes fixedPieceId);
            reader.ReadValueSafe(out Vector3 impulse);

            FlickDomPlayerId owner = (FlickDomPlayerId)ownerValue;
            string pieceId = fixedPieceId.ToString();

            ResolveGameModeManager();
            if (gameModeManager == null
                || gameModeManager.CurrentState != FlickDomGameState.PlayerFlicking
                || gameModeManager.ActivePlayer != owner)
            {
                Debug.LogWarning("[Network] Rejected flick request from client " + senderClientId + ". Owner: " + owner + ", Piece: " + pieceId + ".", this);
                return;
            }

            TurnBasedFlickPiece piece = FindFlickPiece(owner, pieceId);
            if (piece == null)
            {
                Debug.LogWarning("[Network] Rejected flick request because piece was not found. Owner: " + owner + ", Piece: " + pieceId + ".", this);
                return;
            }

            if (piece.TryQueueAuthoritativeFlick(impulse))
            {
                SendFlickAcceptedToClients(owner, pieceId);
                Debug.Log("[Network] Host accepted flick request from client " + senderClientId + ". Piece: " + pieceId + ", Impulse: " + impulse + ".", this);
            }
            else
            {
                Debug.LogWarning("[Network] Host could not queue flick request. Piece may already be launched or queued. Piece: " + pieceId + ".", this);
            }
        }

        private void SendFlickAcceptedToClients(FlickDomPlayerId owner, string pieceId)
        {
            if (networkManager == null
                || !networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            List<ulong> clients = GetRemoteClientIds();
            if (clients.Count <= 0)
            {
                return;
            }

            FixedString64Bytes fixedPieceId = new FixedString64Bytes(pieceId ?? string.Empty);
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) + 64, Allocator.Temp))
            {
                writer.WriteValueSafe((int)owner);
                writer.WriteValueSafe(fixedPieceId);
                networkManager.CustomMessagingManager.SendNamedMessage(FlickAcceptedMessageName, clients, writer);
            }
        }

        private void HandleFlickAcceptedMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager != null && networkManager.IsHost)
            {
                return;
            }

            reader.ReadValueSafe(out int ownerValue);
            reader.ReadValueSafe(out FixedString64Bytes fixedPieceId);

            FlickDomPlayerId owner = (FlickDomPlayerId)ownerValue;
            string pieceId = fixedPieceId.ToString();
            LocalFlickTurnTestRig turnRig = FindAnyObjectByType<LocalFlickTurnTestRig>();
            if (turnRig != null)
            {
                turnRig.TryMarkFlickAcceptedFromNetwork(owner, pieceId);
            }

            TurnBasedFlickPiece piece = FindFlickPiece(owner, pieceId);
            if (piece != null)
            {
                piece.MarkNetworkFlickAccepted();
            }

            Debug.Log("[Network] Flick accepted received from Host. Player: " + owner + ", Piece: " + pieceId + ".", this);
        }

        private void HandlePieceOrderSelectionMessage(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int ownerValue);
            reader.ReadValueSafe(out FixedString64Bytes fixedPieceId);

            FlickDomPlayerId owner = (FlickDomPlayerId)ownerValue;
            string pieceId = fixedPieceId.ToString();

            if (networkManager != null && !networkManager.IsHost)
            {
                LocalFlickTurnTestRig clientTurnRig = FindAnyObjectByType<LocalFlickTurnTestRig>();
                if (clientTurnRig != null)
                {
                    clientTurnRig.TrySelectPieceForNetwork(owner, pieceId);
                }

                Debug.Log("[Network] Piece order selection received from Host. Player: " + owner + ", Piece: " + pieceId + ".", this);
                return;
            }

            ResolveGameModeManager();

            if (gameModeManager == null
                || gameModeManager.CurrentState != FlickDomGameState.PieceOrderSelection
                || gameModeManager.ActivePlayer != owner)
            {
                Debug.LogWarning("[Network] Rejected piece order selection from client " + senderClientId + ". Player: " + owner + ", Piece: " + pieceId + ".", this);
                return;
            }

            LocalFlickTurnTestRig turnRig = FindAnyObjectByType<LocalFlickTurnTestRig>();
            if (turnRig == null || !turnRig.TrySelectPieceForNetwork(owner, pieceId))
            {
                Debug.LogWarning("[Network] Failed to apply piece order selection from client " + senderClientId + ". Player: " + owner + ", Piece: " + pieceId + ".", this);
                return;
            }

            BroadcastGameState();
            Debug.Log("[Network] Host accepted piece order selection from client " + senderClientId + ". Player: " + owner + ", Piece: " + pieceId + ".", this);
        }

        private void SendPieceOrderSelectionToClients(FlickDomPlayerId owner, string pieceId)
        {
            if (networkManager == null
                || !networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            List<ulong> clients = GetRemoteClientIds();
            if (clients.Count <= 0)
            {
                return;
            }

            FixedString64Bytes fixedPieceId = new FixedString64Bytes(pieceId ?? string.Empty);
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) + 64, Allocator.Temp))
            {
                writer.WriteValueSafe((int)owner);
                writer.WriteValueSafe(fixedPieceId);
                networkManager.CustomMessagingManager.SendNamedMessage(PieceOrderSelectionMessageName, clients, writer);
            }
        }

        private void BroadcastPieceTransformsIfNeeded()
        {
            if (!networkGameStarted
                || networkManager == null
                || !networkManager.IsHost
                || Time.unscaledTime < nextTransformBroadcastTime)
            {
                return;
            }

            nextTransformBroadcastTime = Time.unscaledTime + TransformBroadcastInterval;
            SendAllPieceTransformsToClients();
        }

        private void SendAllPieceTransformsToClients()
        {
            List<ulong> clients = GetRemoteClientIds();
            if (clients.Count <= 0)
            {
                return;
            }

            List<TurnBasedFlickPiece> pieces = CollectUniqueFlickPieces();
            for (int i = 0; i < pieces.Count; i++)
            {
                SendPieceTransformToClients(pieces[i], clients);
            }
        }

        private void SendAllPieceTransformsToClient(ulong clientId)
        {
            List<TurnBasedFlickPiece> pieces = CollectUniqueFlickPieces();
            for (int i = 0; i < pieces.Count; i++)
            {
                SendPieceTransformToClient(pieces[i], clientId);
            }
        }

        private void SendPieceTransformToClients(TurnBasedFlickPiece piece, IReadOnlyList<ulong> clients)
        {
            if (piece == null
                || networkManager == null
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            FixedString64Bytes fixedPieceId = new FixedString64Bytes(piece.PieceId ?? string.Empty);
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) + 64 + sizeof(float) * 7, Allocator.Temp))
            {
                writer.WriteValueSafe((int)piece.Owner);
                writer.WriteValueSafe(fixedPieceId);
                writer.WriteValueSafe(piece.transform.position);
                writer.WriteValueSafe(piece.transform.rotation);
                networkManager.CustomMessagingManager.SendNamedMessage(PieceTransformMessageName, clients, writer);
            }
        }

        private void SendPieceTransformToClient(TurnBasedFlickPiece piece, ulong clientId)
        {
            List<ulong> clients = new List<ulong>(1) { clientId };
            SendPieceTransformToClients(piece, clients);
        }

        private void HandlePieceTransformMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager != null && networkManager.IsHost)
            {
                return;
            }

            reader.ReadValueSafe(out int ownerValue);
            reader.ReadValueSafe(out FixedString64Bytes fixedPieceId);
            reader.ReadValueSafe(out Vector3 position);
            reader.ReadValueSafe(out Quaternion rotation);

            TurnBasedFlickPiece piece = FindFlickPiece((FlickDomPlayerId)ownerValue, fixedPieceId.ToString());
            if (piece != null)
            {
                piece.ApplyNetworkPose(position, rotation);
            }
        }

        private List<ulong> GetRemoteClientIds()
        {
            List<ulong> clients = new List<ulong>(maxPlayers);
            if (networkManager == null || !networkManager.IsHost)
            {
                return clients;
            }

            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                if (clientId != networkManager.LocalClientId)
                {
                    clients.Add(clientId);
                }
            }

            return clients;
        }

        private static TurnBasedFlickPiece FindFlickPiece(FlickDomPlayerId owner, string pieceId)
        {
            TurnBasedFlickPiece[] pieces = FindObjectsByType<TurnBasedFlickPiece>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece != null
                    && piece.Owner == owner
                    && string.Equals(piece.PieceId, pieceId, StringComparison.Ordinal))
                {
                    return piece;
                }
            }

            return null;
        }

        private static List<TurnBasedFlickPiece> CollectUniqueFlickPieces()
        {
            TurnBasedFlickPiece[] pieces = FindObjectsByType<TurnBasedFlickPiece>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            List<TurnBasedFlickPiece> uniquePieces = new List<TurnBasedFlickPiece>(pieces.Length);
            HashSet<string> seenPieceKeys = new HashSet<string>();

            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece == null || string.IsNullOrEmpty(piece.PieceId))
                {
                    continue;
                }

                string key = ((int)piece.Owner).ToString() + "|" + piece.PieceId;
                if (!seenPieceKeys.Add(key))
                {
                    Debug.LogWarning("[Network] Duplicate flick piece ignored for transform sync. Key: " + key + ", Object: " + piece.name + ".", piece);
                    continue;
                }

                uniquePieces.Add(piece);
            }

            return uniquePieces;
        }

        private void SubscribeGameModeEvents(bool subscribe)
        {
            ResolveGameModeManager();
            if (gameModeManager == null)
            {
                return;
            }

            if (subscribe)
            {
                if (gameModeEventsSubscribed)
                {
                    return;
                }

                gameModeManager.StateChanged += HandleGameModeStateChanged;
                gameModeManager.ActivePlayerChanged += HandleGameModeActivePlayerChanged;
                gameModeManager.RoundStarted += HandleGameModeRoundStarted;
                gameModeEventsSubscribed = true;
                return;
            }

            if (!gameModeEventsSubscribed)
            {
                return;
            }

            gameModeManager.StateChanged -= HandleGameModeStateChanged;
            gameModeManager.ActivePlayerChanged -= HandleGameModeActivePlayerChanged;
            gameModeManager.RoundStarted -= HandleGameModeRoundStarted;
            gameModeEventsSubscribed = false;
        }

        private void HandleGameModeStateChanged(FlickDomGameState previousState, FlickDomGameState nextState)
        {
            BroadcastGameState();
        }

        private void HandleGameModeActivePlayerChanged(FlickDomPlayerId activePlayer)
        {
            BroadcastGameState();
        }

        private void HandleGameModeRoundStarted(int roundNumber, System.Collections.Generic.IReadOnlyList<FlickDomPlayerId> turnOrder)
        {
            BroadcastGameState();
        }

        private static bool WasPressedThisFrame(Key key)
        {
            return Keyboard.current[key].wasPressedThisFrame;
        }

        private bool IsNetworkEnabledForActiveScene()
        {
            return IsSceneNameNetworkEnabled(SceneManager.GetActiveScene().name, networkSceneName);
        }

        private static bool IsSceneNameNetworkEnabled(string sceneName, string targetSceneName)
        {
            return string.Equals(sceneName, targetSceneName, StringComparison.OrdinalIgnoreCase);
        }

        private void TryAutoStartFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool startHost = HasArgument(args, "-host");
            bool startClient = HasArgument(args, "-client");

            if (TryGetArgumentValue(args, "-address", out string address))
            {
                connectAddress = address;
            }

            if (TryGetArgumentValue(args, "-port", out string portValue)
                && ushort.TryParse(portValue, out ushort parsedPort))
            {
                port = parsedPort;
            }

            if (startHost && startClient)
            {
                Debug.LogWarning("[Network] Both -host and -client were provided. Ignoring command line auto start.", this);
                return;
            }

            if (startHost)
            {
                Debug.Log("[Network] Command line requested Host start.", this);
                StartHost();
            }
            else if (startClient)
            {
                Debug.Log("[Network] Command line requested Client start.", this);
                StartClient();
            }
        }

        private static bool HasArgument(string[] args, string argument)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], argument, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetArgumentValue(string[] args, string argument, out string value)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], argument, StringComparison.OrdinalIgnoreCase))
                {
                    value = args[i + 1];
                    return true;
                }
            }

            value = null;
            return false;
        }

        private readonly struct GuiEnabledScope : IDisposable
        {
            private readonly bool previousEnabled;

            public GuiEnabledScope(bool enabled)
            {
                previousEnabled = GUI.enabled;
                GUI.enabled = enabled;
            }

            public void Dispose()
            {
                GUI.enabled = previousEnabled;
            }
        }
    }
}
