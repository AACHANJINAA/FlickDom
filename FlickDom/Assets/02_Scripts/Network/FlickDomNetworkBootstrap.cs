using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using FlickDom.Gameplay;
using Unity.Collections;
using Unity.Networking.Transport.Relay;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
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
        [SerializeField] private string hostListenAddress = "0.0.0.0";
        [SerializeField] private bool forceHostListenOnAllInterfaces = true;
        [SerializeField] private bool useWebSocketTransport = true;
        [SerializeField] private bool useUnityRelay = true;
        [SerializeField] private string relayConnectionType = "wss";
        [SerializeField] private ushort port = 7777;
        [SerializeField] private int maxPlayers = 2;
        [SerializeField] private bool persistAcrossScenes;
        [SerializeField] private float maxNetworkFlickImpulseMagnitude = 30f;
        [SerializeField, Min(1)] private int networkTickRate = 60;
        [SerializeField, Min(0.01f)] private float transformBroadcastInterval = 1f / 30f;

        [Header("Local Test Controls")]
        [SerializeField] private bool showRuntimeStatus;
        [SerializeField] private bool showLobbyUi;
        [SerializeField] private bool disableLocalAutoStartForNetworkLobby = true;
        [SerializeField] private bool enableCommandLineAutoStart = true;

        private const string DefaultNetworkSceneName = "good_Scene";
        private const string StartGameMessageName = "FlickDom.StartGame";
        private const string LobbyStateMessageName = "FlickDom.LobbyState";
        private const string GameStateMessageName = "FlickDom.GameState";
        private const string FlickRequestMessageName = "FlickDom.FlickRequest";
        private const string FlickAcceptedMessageName = "FlickDom.FlickAccepted";
        private const string MonkeyInputMessageName = "FlickDom.MonkeyInput";
        private const string MonkeyPoseMessageName = "FlickDom.MonkeyPose";
        private const string PieceOrderSelectionMessageName = "FlickDom.PieceOrderSelection";
        private const string PieceOrderStateMessageName = "FlickDom.PieceOrderState";
        private const string PieceTransformMessageName = "FlickDom.PieceTransform";
        private const string PlacementRequestMessageName = "FlickDom.PlacementRequest";
        private const string PlacementAcceptedMessageName = "FlickDom.PlacementAccepted";
        private const string PlacementCandidatesMessageName = "FlickDom.PlacementCandidates";
        private const string BoardStateMessageName = "FlickDom.BoardState";
        private const string ScoreStateMessageName = "FlickDom.ScoreState";
        private const string CardStateMessageName = "FlickDom.CardState";
        private const string RestartRequestMessageName = "FlickDom.RestartRequest";
        private const string RestartMatchMessageName = "FlickDom.RestartMatch";
        private const string ReturnToLobbyRequestMessageName = "FlickDom.ReturnToLobbyRequest";
        private const string ReturnToLobbyMessageName = "FlickDom.ReturnToLobby";
        private const string LoopbackAddress = "127.0.0.1";
        private const string AnyListenAddress = "0.0.0.0";
        private const int MaxHostPortSearchAttempts = 64;
        private const string DefaultRelayConnectionType = "wss";
        private const string Player1MonkeyObjectName = "Player1_Monkey";
        private const string Player2MonkeyObjectName = "Player2_Monkey";

        public event Action<FlickDomPlayerId> LocalPlayerRoleChanged;

        public static FlickDomNetworkBootstrap Active { get; private set; }

        private GameModeManager gameModeManager;
        private TokenMapManager tokenMapManager;
        private TokenMapPlacementSelector placementSelector;
        private PatternCardManager patternCardManager;
        private string addressInput = "127.0.0.1";
        private string portInput = "7777";
        private string relayJoinCodeInput = string.Empty;
        private string relayJoinCode = string.Empty;
        private string networkStatusMessage = string.Empty;
        private bool startGameMessageHandlerRegistered;
        private bool lobbyStateMessageHandlerRegistered;
        private bool gameStateMessageHandlerRegistered;
        private bool flickRequestMessageHandlerRegistered;
        private bool flickAcceptedMessageHandlerRegistered;
        private bool monkeyInputMessageHandlerRegistered;
        private bool monkeyPoseMessageHandlerRegistered;
        private bool pieceOrderSelectionMessageHandlerRegistered;
        private bool pieceOrderStateMessageHandlerRegistered;
        private bool pieceTransformMessageHandlerRegistered;
        private bool placementRequestMessageHandlerRegistered;
        private bool placementAcceptedMessageHandlerRegistered;
        private bool placementCandidatesMessageHandlerRegistered;
        private bool boardStateMessageHandlerRegistered;
        private bool scoreStateMessageHandlerRegistered;
        private bool cardStateMessageHandlerRegistered;
        private bool restartRequestMessageHandlerRegistered;
        private bool restartMatchMessageHandlerRegistered;
        private bool returnToLobbyRequestMessageHandlerRegistered;
        private bool returnToLobbyMessageHandlerRegistered;
        private bool gameModeEventsSubscribed;
        private bool patternCardEventsSubscribed;
        private bool networkGameStarted;
        private bool localGameStartedFromNetwork;
        private bool localSinglePlayerModeActive;
        private bool networkStartInProgress;
        private int lobbyPlayerCount;
        private float nextTransformBroadcastTime;

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

        public bool IsLocalSinglePlayerModeActive
        {
            get { return localSinglePlayerModeActive; }
        }

        public bool IsGameActive
        {
            get { return networkGameStarted || localSinglePlayerModeActive; }
        }

        public bool IsNetworkStartInProgress
        {
            get { return networkStartInProgress; }
        }

        public int VisiblePlayerCount
        {
            get { return GetVisiblePlayerCount(); }
        }

        public int MaxPlayers
        {
            get { return maxPlayers; }
        }

        public bool CanStartNetworkGame
        {
            get
            {
                return networkManager != null
                    && networkManager.IsHost
                    && GetHostConnectedPlayerCount() >= maxPlayers
                    && !networkGameStarted;
            }
        }

        public string CurrentNetworkModeText
        {
            get { return GetCurrentNetworkModeText(); }
        }

        public string LobbyStatusText
        {
            get { return GetLobbyHint(CanStartNetworkGame); }
        }

        public bool UsesUnityRelay
        {
            get { return useUnityRelay; }
        }

        public string RelayJoinCode
        {
            get { return relayJoinCode; }
        }

        public string RelayJoinCodeInput
        {
            get { return relayJoinCodeInput; }
        }

        public string CurrentConnectAddress
        {
            get { return connectAddress; }
        }

        public string CurrentShareableHostAddresses
        {
            get { return GetShareableHostAddresses(); }
        }

        public ushort CurrentPort
        {
            get { return port; }
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
            ResolveTokenMapManager();
            ResolvePlacementSelector();
            ResolvePatternCardManager();
            ConfigureGameModeAutoStart();
            EnsureSceneMonkeyControllers();
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

            EnsureSceneMonkeyControllers();

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
            SubscribePatternCardEvents(false);
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
        }

        private void OnGUI()
        {
            if (!showRuntimeStatus || !IsNetworkEnabledForActiveScene())
            {
                return;
            }

            string mode = GetCurrentNetworkModeText();

            GUI.Box(new Rect(16f, 16f, 320f, 92f), "FlickDom Network");
            GUI.Label(new Rect(28f, 42f, 296f, 22f), "Mode: " + mode + " / LocalRole: " + LocalPlayerId);
            GUI.Label(new Rect(28f, 64f, 296f, 22f), "Target: " + connectAddress + ":" + port);
            GUI.Label(new Rect(28f, 86f, 296f, 22f), "Listen: " + hostListenAddress);

            if (showLobbyUi && !networkGameStarted && !localSinglePlayerModeActive)
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
            GUILayout.Label("Host IP / Join IP");
            addressInput = GUILayout.TextField(addressInput);

            string shareableHostAddress = GetShareableHostAddress();
            if (!string.IsNullOrEmpty(shareableHostAddress))
            {
                GUILayout.Label("LAN Share IP: " + shareableHostAddress);
            }

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

            GUILayout.Space(6f);
            using (new GuiEnabledScope(!IsRunning))
            {
                if (GUILayout.Button("Single Mode", GUILayout.Height(30f)))
                {
                    StartSinglePlayerMode();
                }
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
            if (useUnityRelay)
            {
                _ = StartHostWithRelayAsync();
                return;
            }

            if (!CanStartNetwork())
            {
                return;
            }

            if (IsWebGlRuntime())
            {
                Debug.LogWarning("[Network] Browser Host mode is disabled in WebGL. Start Host from the Editor or a standalone build, then join from WebGL.", this);
                return;
            }

            networkStartInProgress = true;
            string listenAddress = GetHostListenAddress();
            Debug.Log("[Network] Preparing Host. Requested port: " + port
                + ", Listen: " + listenAddress + ", Share IPs: " + GetShareableHostAddresses() + ".", this);

            try
            {
                if (!TryPrepareHostPort())
                {
                    CleanupFailedNetworkStart();
                    return;
                }

                listenAddress = GetHostListenAddress();
                Debug.Log("[Network] Host will use port " + port + ". Local client address: " + LoopbackAddress
                    + ":" + port + ", Listen: " + listenAddress + ":" + port + ".", this);

                ConfigureTransportForHost(port);
                bool started = networkManager.StartHost();
                if (!started)
                {
                    Debug.LogError("[Network] Failed to start Host.", this);
                    CleanupFailedNetworkStart();
                    return;
                }

                RegisterNetworkMessageHandlersIfReady();
                SetLocalPlayerRole(FlickDomPlayerId.Player1);
                BroadcastLobbyState();
                Debug.Log("[Network] Host started. Local role is Player1.", this);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Network] Host start threw an exception: " + exception.Message, this);
                CleanupFailedNetworkStart();
            }
            finally
            {
                networkStartInProgress = false;
            }
        }

        [ContextMenu("Start Client")]
        public void StartClient()
        {
            if (useUnityRelay)
            {
                _ = StartClientWithRelayAsync(relayJoinCodeInput);
                return;
            }

            if (!CanStartNetwork())
            {
                return;
            }

            networkStartInProgress = true;
            Debug.Log("[Network] Starting Client. Target is " + connectAddress + ":" + port + ".", this);

            try
            {
                ConfigureTransportForClient(connectAddress, port);
                bool started = networkManager.StartClient();
                if (!started)
                {
                    Debug.LogError("[Network] Failed to start Client.", this);
                    CleanupFailedNetworkStart();
                    return;
                }

                RegisterNetworkMessageHandlersIfReady();
                SetLocalPlayerRole(FlickDomPlayerId.Player2);
                Debug.Log("[Network] Client start requested. Local role is Player2.", this);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Network] Client start threw an exception: " + exception.Message, this);
                CleanupFailedNetworkStart();
            }
            finally
            {
                networkStartInProgress = false;
            }
        }

        [ContextMenu("Shutdown")]
        public void Shutdown()
        {
            networkStartInProgress = false;
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
            relayJoinCode = string.Empty;
            networkStatusMessage = string.Empty;
        }

        public void SetConnectionTarget(string address, ushort targetPort)
        {
            if (networkManager != null && networkManager.IsListening)
            {
                Debug.LogWarning("[Network] Cannot change connection target while networking is running.", this);
                return;
            }

            connectAddress = string.IsNullOrWhiteSpace(address) ? LoopbackAddress : address.Trim();
            port = targetPort;
        }

        public void SetRelayJoinCodeInput(string joinCode)
        {
            if (networkManager != null && networkManager.IsListening)
            {
                Debug.LogWarning("[Network] Cannot change Relay join code while networking is running.", this);
                return;
            }

            relayJoinCodeInput = NormalizeRelayJoinCode(joinCode);
        }

        public bool AllowsLocalInputFor(FlickDomPlayerId playerId)
        {
            if (localSinglePlayerModeActive)
            {
                return true;
            }

            if (!IsRunning)
            {
                return true;
            }

            return LocalPlayerId != FlickDomPlayerId.None && LocalPlayerId == playerId;
        }

        public bool AllowsLocalStateControl()
        {
            if (localSinglePlayerModeActive)
            {
                return true;
            }

            return !IsRunning || IsHost;
        }

        public void SubmitFlickRequestToHost(
            FlickDomPlayerId owner,
            string pieceId,
            Vector3 impulse,
            Vector3 launchPosition)
        {
            if (networkManager == null
                || !networkManager.IsClient
                || networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            FixedString64Bytes fixedPieceId = new FixedString64Bytes(pieceId ?? string.Empty);
            Vector3 safeImpulse = ClampNetworkFlickImpulse(impulse);
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) + 64 + sizeof(float) * 6, Allocator.Temp))
            {
                writer.WriteValueSafe((int)owner);
                writer.WriteValueSafe(fixedPieceId);
                writer.WriteValueSafe(safeImpulse);
                writer.WriteValueSafe(launchPosition);
                networkManager.CustomMessagingManager.SendNamedMessage(FlickRequestMessageName, NetworkManager.ServerClientId, writer);
            }

            Debug.Log("[Network] Flick request sent to Host. Piece: " + pieceId + ", Impulse: " + safeImpulse + ", LaunchPosition: " + launchPosition + ".", this);
        }

        public void SubmitMonkeyMovementInputToHost(FlickDomPlayerId owner, Vector3 moveDirection, bool sprint)
        {
            if (networkManager == null
                || !networkManager.IsClient
                || networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            Vector3 safeMoveDirection = ClampNetworkMoveDirection(moveDirection);
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) + sizeof(float) * 3 + sizeof(bool), Allocator.Temp))
            {
                writer.WriteValueSafe((int)owner);
                writer.WriteValueSafe(safeMoveDirection);
                writer.WriteValueSafe(sprint);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MonkeyInputMessageName,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.UnreliableSequenced);
            }
        }

        private async Task StartHostWithRelayAsync()
        {
            if (!CanStartNetwork())
            {
                return;
            }

            networkStartInProgress = true;
            networkStatusMessage = "Creating Relay room...";
            relayJoinCode = string.Empty;

            try
            {
                await EnsureUnityServicesSignedInAsync();

                int maxConnections = Mathf.Max(1, maxPlayers - 1);
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
                relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                ConfigureTransportForRelay(allocation.ToRelayServerData(GetRelayConnectionType()));
                bool started = networkManager.StartHost();
                if (!started)
                {
                    Debug.LogError("[Network] Failed to start Relay Host.", this);
                    networkStatusMessage = "Failed to start Relay Host.";
                    CleanupFailedNetworkStart();
                    return;
                }

                RegisterNetworkMessageHandlersIfReady();
                SetLocalPlayerRole(FlickDomPlayerId.Player1);
                BroadcastLobbyState();
                networkStatusMessage = "Relay room created. Join Code: " + relayJoinCode;
                Debug.Log("[Network] Relay Host started. Join Code: " + relayJoinCode + ".", this);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Network] Relay Host start failed: " + exception.Message, this);
                networkStatusMessage = "Relay Host failed: " + exception.Message;
                CleanupFailedNetworkStart();
            }
            finally
            {
                networkStartInProgress = false;
            }
        }

        private async Task StartClientWithRelayAsync(string joinCode)
        {
            if (!CanStartNetwork())
            {
                return;
            }

            string safeJoinCode = NormalizeRelayJoinCode(joinCode);
            if (string.IsNullOrEmpty(safeJoinCode))
            {
                networkStatusMessage = "Enter a Relay join code.";
                Debug.LogWarning("[Network] Cannot join Relay room without a join code.", this);
                return;
            }

            networkStartInProgress = true;
            networkStatusMessage = "Joining Relay room...";

            try
            {
                await EnsureUnityServicesSignedInAsync();

                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(safeJoinCode);
                ConfigureTransportForRelay(allocation.ToRelayServerData(GetRelayConnectionType()));
                bool started = networkManager.StartClient();
                if (!started)
                {
                    Debug.LogError("[Network] Failed to start Relay Client.", this);
                    networkStatusMessage = "Failed to start Relay Client.";
                    CleanupFailedNetworkStart();
                    return;
                }

                relayJoinCodeInput = safeJoinCode;
                RegisterNetworkMessageHandlersIfReady();
                SetLocalPlayerRole(FlickDomPlayerId.Player2);
                networkStatusMessage = "Relay Client started. Waiting for connection...";
                Debug.Log("[Network] Relay Client start requested. Join Code: " + safeJoinCode + ".", this);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Network] Relay Client start failed: " + exception.Message, this);
                networkStatusMessage = "Relay join failed: " + exception.Message;
                CleanupFailedNetworkStart();
            }
            finally
            {
                networkStartInProgress = false;
            }
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

        public void SubmitPlacementRequestToHost(FlickDomPlayerId owner, string pieceId, Vector2Int destination, Vector2Int? relocationSource)
        {
            if (networkManager == null
                || !networkManager.IsClient
                || networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            FixedString64Bytes fixedPieceId = new FixedString64Bytes(pieceId ?? string.Empty);
            bool hasRelocationSource = relocationSource.HasValue;
            Vector2Int source = relocationSource.GetValueOrDefault();
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) * 6 + sizeof(bool) + 64, Allocator.Temp))
            {
                writer.WriteValueSafe((int)owner);
                writer.WriteValueSafe(fixedPieceId);
                writer.WriteValueSafe(destination.x);
                writer.WriteValueSafe(destination.y);
                writer.WriteValueSafe(hasRelocationSource);
                writer.WriteValueSafe(source.x);
                writer.WriteValueSafe(source.y);
                networkManager.CustomMessagingManager.SendNamedMessage(PlacementRequestMessageName, NetworkManager.ServerClientId, writer);
            }

            Debug.Log("[Network] Placement request sent to Host. Player: " + owner + ", Piece: " + pieceId + ", Destination: " + destination + ".", this);
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
            BroadcastPieceOrderState();
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
            BroadcastPieceOrderState();
            SendAllPieceTransformsToClients();
            SendAllMonkeyPosesToClients();
            Debug.Log("[Network] Flick accepted broadcast. Player: " + owner + ", Piece: " + pieceId + ".", this);
        }

        public void NotifyHostPlacementApplied(FlickDomPlayerId owner, string pieceId, Vector2Int destination, Vector2Int? relocationSource)
        {
            if (networkManager == null
                || !networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            SendPlacementAcceptedToClients(owner, pieceId, destination, relocationSource);
            BroadcastBoardState();
            BroadcastScoreState();
            BroadcastGameState();
            Debug.Log("[Network] Placement accepted broadcast. Player: " + owner + ", Piece: " + pieceId + ", Destination: " + destination + ".", this);
        }

        public void RestartMatchFromUi()
        {
            if (!IsRunning)
            {
                RestartLocalGameOnly();
                return;
            }

            if (IsHost)
            {
                RestartNetworkMatchAsHost();
                return;
            }

            SendEmptyMessageToHost(RestartRequestMessageName);
            Debug.Log("[Network] Restart match requested from Client.", this);
        }

        public void ReturnToLobbyFromUi()
        {
            if (!IsRunning)
            {
                ReturnLocalGameToMenu();
                return;
            }

            if (IsHost)
            {
                ReturnNetworkMatchToLobbyAsHost();
                return;
            }

            SendEmptyMessageToHost(ReturnToLobbyRequestMessageName);
            Debug.Log("[Network] Return to lobby requested from Client.", this);
        }

        public void NotifyHostScoreStateChanged(string reason)
        {
            if (networkManager == null || !networkManager.IsHost)
            {
                return;
            }

            BroadcastScoreState();
            BroadcastGameState();
            Debug.Log("[Network] Score state forced broadcast. Reason: " + reason + ".", this);
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
            networkManager.NetworkConfig.TickRate = (uint)Mathf.Clamp(networkTickRate, 1, 120);
            ConfigureTransportProtocol();
        }

        private void ResolveGameModeManager()
        {
            if (gameModeManager == null)
            {
                gameModeManager = FindAnyObjectByType<GameModeManager>();
            }
        }

        private void ResolveTokenMapManager()
        {
            if (tokenMapManager == null)
            {
                tokenMapManager = FindAnyObjectByType<TokenMapManager>();
            }
        }

        private void ResolvePlacementSelector()
        {
            if (placementSelector == null)
            {
                placementSelector = FindAnyObjectByType<TokenMapPlacementSelector>();
            }
        }

        private void ResolvePatternCardManager()
        {
            if (patternCardManager == null)
            {
                patternCardManager = FindAnyObjectByType<PatternCardManager>();
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

            if (networkStartInProgress)
            {
                Debug.LogWarning("[Network] Network start is already in progress.", this);
                return false;
            }

            if (networkManager.IsListening)
            {
                Debug.LogWarning("[Network] NetworkManager is already running.", this);
                return false;
            }

            return true;
        }

        private void ConfigureTransportForHost(ushort targetPort)
        {
            ConfigureTransportProtocol();
            unityTransport.SetConnectionData(LoopbackAddress, targetPort, GetHostListenAddress());
        }

        private void ConfigureTransportForClient(string address, ushort targetPort)
        {
            ConfigureTransportProtocol();
            unityTransport.SetConnectionData(address, targetPort);
        }

        private void ConfigureTransportForRelay(RelayServerData relayServerData)
        {
            ConfigureTransportProtocol();
            unityTransport.UseWebSockets = true;
            unityTransport.SetRelayServerData(relayServerData);
        }

        private void ConfigureTransportProtocol()
        {
            if (unityTransport == null)
            {
                return;
            }

            unityTransport.UseWebSockets = useWebSocketTransport || IsWebGlRuntime();
        }

        private string GetRelayConnectionType()
        {
            return string.IsNullOrWhiteSpace(relayConnectionType)
                ? DefaultRelayConnectionType
                : relayConnectionType.Trim().ToLowerInvariant();
        }

        private static async Task EnsureUnityServicesSignedInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        private static string NormalizeRelayJoinCode(string joinCode)
        {
            return string.IsNullOrWhiteSpace(joinCode)
                ? string.Empty
                : joinCode.Trim().Replace(" ", string.Empty).ToUpperInvariant();
        }

        private bool TryPrepareHostPort()
        {
            ushort requestedPort = port;
            int firstPort = requestedPort;
            int maxPort = ushort.MaxValue;

            for (int i = 0; i < MaxHostPortSearchAttempts && firstPort + i <= maxPort; i++)
            {
                ushort candidatePort = (ushort)(firstPort + i);
                if (!IsHostPortAvailable(candidatePort))
                {
                    continue;
                }

                if (candidatePort != requestedPort)
                {
                    Debug.LogWarning("[Network] Host port " + requestedPort
                        + " is already in use. Falling back to " + candidatePort + ".", this);
                    port = candidatePort;
                }

                return true;
            }

            Debug.LogError("[Network] No available host port found from " + requestedPort
                + " to " + Mathf.Min(maxPort, firstPort + MaxHostPortSearchAttempts - 1) + ".", this);
            return false;
        }

        private bool IsHostPortAvailable(ushort targetPort)
        {
            return unityTransport != null && unityTransport.UseWebSockets
                ? IsTcpPortAvailable(targetPort)
                : IsUdpPortAvailable(targetPort);
        }

        private static bool IsTcpPortAvailable(ushort targetPort)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Any, targetPort);
                listener.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                if (listener != null)
                {
                    listener.Stop();
                }
            }
        }

        private static bool IsUdpPortAvailable(ushort targetPort)
        {
            try
            {
                IPEndPoint[] listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners();
                for (int i = 0; i < listeners.Length; i++)
                {
                    if (listeners[i].Port == targetPort)
                    {
                        return false;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Network] Could not inspect active UDP listeners: " + exception.Message, null);
            }

            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.ExclusiveAddressUse = true;
                    socket.Bind(new IPEndPoint(IPAddress.Any, targetPort));
                }

                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private string GetHostListenAddress()
        {
            if (forceHostListenOnAllInterfaces)
            {
                return AnyListenAddress;
            }

            return string.IsNullOrWhiteSpace(hostListenAddress) ? AnyListenAddress : hostListenAddress.Trim();
        }

        private static string GetShareableHostAddresses()
        {
            if (IsWebGlRuntime())
            {
                return string.Empty;
            }

            List<string> addresses = new List<string>();

            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < interfaces.Length; i++)
                {
                    NetworkInterface networkInterface = interfaces[i];
                    if (networkInterface.OperationalStatus != OperationalStatus.Up
                        || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    IPInterfaceProperties properties = networkInterface.GetIPProperties();
                    UnicastIPAddressInformationCollection unicastAddresses = properties.UnicastAddresses;
                    foreach (UnicastIPAddressInformation unicastAddress in unicastAddresses)
                    {
                        IPAddress address = unicastAddress.Address;
                        if (address.AddressFamily != AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        string value = address.ToString();
                        if (IsNonShareableIpv4(value) || addresses.Contains(value))
                        {
                            continue;
                        }

                        addresses.Add(value);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Network] Failed to resolve adapter IPs for lobby display: " + exception.Message, null);
            }

            if (addresses.Count == 0)
            {
                AddDnsHostAddresses(addresses);
            }

            return string.Join(", ", addresses);
        }

        private static void AddDnsHostAddresses(List<string> addresses)
        {
            try
            {
                IPAddress[] hostAddresses = Dns.GetHostAddresses(Dns.GetHostName());
                for (int i = 0; i < hostAddresses.Length; i++)
                {
                    IPAddress address = hostAddresses[i];
                    if (address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    string value = address.ToString();
                    if (!IsNonShareableIpv4(value) && !addresses.Contains(value))
                    {
                        addresses.Add(value);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Network] Failed to resolve DNS host IPs for lobby display: " + exception.Message, null);
            }
        }

        private static bool IsNonShareableIpv4(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                || value.StartsWith("127.", StringComparison.Ordinal)
                || value.StartsWith("169.254.", StringComparison.Ordinal)
                || string.Equals(value, AnyListenAddress, StringComparison.Ordinal);
        }

        private static string GetShareableHostAddress()
        {
            string addresses = GetShareableHostAddresses();
            int separatorIndex = addresses.IndexOf(",", StringComparison.Ordinal);
            return separatorIndex >= 0 ? addresses.Substring(0, separatorIndex) : addresses;
        }

        private static bool IsWebGlRuntime()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        private void CleanupFailedNetworkStart()
        {
            UnregisterNetworkMessageHandlers();

            if (networkManager != null)
            {
                try
                {
                    networkManager.Shutdown();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Network] Failed-start cleanup hit an exception: " + exception.Message, this);
                }
            }

            SetLocalPlayerRole(FlickDomPlayerId.None);
            networkGameStarted = false;
            localGameStartedFromNetwork = false;
            lobbyPlayerCount = 0;
            relayJoinCode = string.Empty;
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
            bool canJoin = connectedClients < maxPlayers && !networkGameStarted;

            response.Approved = canJoin;
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = canJoin
                ? string.Empty
                : networkGameStarted ? "Game already started." : "Room is full.";
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
                networkStatusMessage = string.Empty;
            }

            if (networkManager.IsHost && clientId != networkManager.LocalClientId)
            {
                networkStatusMessage = string.Empty;
            }

            Debug.Log("[Network] Client connected: " + clientId + ".", this);
            RegisterNetworkMessageHandlersIfReady();
            BroadcastLobbyState();

            if (networkManager.IsHost && networkGameStarted)
            {
                SendStartGameMessageToClient(clientId);
                BroadcastGameState();
                BroadcastPlacementCandidates();
                BroadcastBoardState();
                BroadcastScoreState();
                BroadcastCardState();
                SendPieceOrderStateToClient(clientId);
                SendAllPieceTransformsToClient(clientId);
                SendAllMonkeyPosesToClient(clientId);
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            Debug.Log("[Network] Client disconnected: " + clientId + ".", this);

            if (networkManager != null && clientId == networkManager.LocalClientId)
            {
                SetLocalPlayerRole(FlickDomPlayerId.None);
                ReturnLocalGameToMenu();
                lobbyPlayerCount = 0;
                Debug.Log("[Network] Local client returned to lobby/menu state after disconnect.", this);
                return;
            }

            if (networkManager != null && networkManager.IsHost && networkGameStarted)
            {
                ReturnNetworkMatchToLobbyAsHost();
                Debug.Log("[Network] Match returned to lobby because a remote client disconnected.", this);
                return;
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
            if (!string.IsNullOrEmpty(networkStatusMessage))
            {
                if (!string.IsNullOrEmpty(relayJoinCode) && IsRunning && networkManager != null && networkManager.IsHost)
                {
                    return networkStatusMessage + "\nShare this code: " + relayJoinCode;
                }

                return networkStatusMessage;
            }

            if (!IsRunning)
            {
                return localSinglePlayerModeActive
                    ? "Single-player match is running."
                    : useUnityRelay
                        ? "Create a Relay room, or enter a join code."
                        : "Create a room, join a room, or start Single Mode.";
            }

            if (networkGameStarted)
            {
                return "Game started.";
            }

            if (networkManager != null && networkManager.IsHost)
            {
                string hostHint = canStartGame ? "Two players connected. Start Game is ready." : "Waiting for Player 2.";
                return !string.IsNullOrEmpty(relayJoinCode)
                    ? hostHint + "\nJoin Code: " + relayJoinCode
                    : hostHint;
            }

            return "Connected. Waiting for Host to start.";
        }

        public bool TryStartNetworkGameFromMenu()
        {
            StartNetworkGame();
            return networkGameStarted;
        }

        public bool TryStartSinglePlayerModeFromMenu()
        {
            StartSinglePlayerMode();
            return localSinglePlayerModeActive;
        }

        public void StartNetworkGame()
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
            localSinglePlayerModeActive = false;
            BroadcastLobbyState();
            StartGameLocally();
            SendStartGameMessageToClients();
            BroadcastGameState();
            BroadcastPlacementCandidates();
            BroadcastBoardState();
            BroadcastScoreState();
            BroadcastCardState();
            BroadcastPieceOrderState();
            SendAllPieceTransformsToClients();
            SendAllMonkeyPosesToClients();
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
            SubscribePatternCardEvents(true);
            BroadcastGameState();
            BroadcastPlacementCandidates();
            BroadcastBoardState();
            BroadcastScoreState();
            BroadcastCardState();
            BroadcastPieceOrderState();
            Debug.Log("[Network] Local GameModeManager started.", this);
        }

        public void StartSinglePlayerMode()
        {
            if (IsRunning)
            {
                Debug.LogWarning("[Network] Single-player mode cannot start while networking is running.", this);
                return;
            }

            ResolveGameModeManager();
            if (gameModeManager == null)
            {
                Debug.LogWarning("[Network] Cannot start single-player mode because GameModeManager was not found.", this);
                return;
            }

            localSinglePlayerModeActive = true;
            localGameStartedFromNetwork = true;
            SetLocalPlayerRole(FlickDomPlayerId.None);
            gameModeManager.StartLocalGame();
            SubscribeGameModeEvents(true);
            SubscribePatternCardEvents(true);
            Debug.Log("[Network] Single-player mode started from lobby.", this);
        }

        private string GetCurrentNetworkModeText()
        {
            if (networkManager == null)
            {
                return "Offline";
            }

            if (networkManager.IsHost)
            {
                return "Host";
            }

            if (networkManager.IsClient)
            {
                return "Client";
            }

            if (networkManager.IsServer)
            {
                return "Server";
            }

            return "Offline";
        }

        private void RestartNetworkMatchAsHost()
        {
            if (networkManager == null || !networkManager.IsHost)
            {
                return;
            }

            networkGameStarted = true;
            RestartLocalGameOnly();
            SendEmptyMessageToClients(RestartMatchMessageName);
            BroadcastLobbyState();
            BroadcastGameState();
            BroadcastPlacementCandidates();
            BroadcastBoardState();
            BroadcastScoreState();
            BroadcastCardState();
            BroadcastPieceOrderState();
            SendAllPieceTransformsToClients();
            SendAllMonkeyPosesToClients();
            Debug.Log("[Network] Host restarted network match.", this);
        }

        private void ReturnNetworkMatchToLobbyAsHost()
        {
            if (networkManager == null || !networkManager.IsHost)
            {
                return;
            }

            ReturnLocalGameToMenu();
            SendEmptyMessageToClients(ReturnToLobbyMessageName);
            BroadcastLobbyState();
            BroadcastGameState();
            BroadcastBoardState();
            BroadcastScoreState();
            BroadcastCardState();
            SendAllPieceTransformsToClients();
            SendAllMonkeyPosesToClients();
            Debug.Log("[Network] Host returned network match to lobby.", this);
        }

        private void RestartLocalGameOnly()
        {
            ResolveGameModeManager();
            if (gameModeManager == null)
            {
                Debug.LogWarning("[Network] Cannot restart match because GameModeManager was not found.", this);
                return;
            }

            networkGameStarted = IsRunning ? networkGameStarted : false;
            localGameStartedFromNetwork = true;
            gameModeManager.StartLocalGame();
            SubscribeGameModeEvents(true);
            SubscribePatternCardEvents(true);
            Debug.Log("[Network] Local match restarted.", this);
        }

        private void ReturnLocalGameToMenu()
        {
            ResolveGameModeManager();
            if (gameModeManager != null)
            {
                gameModeManager.ResetToNotStarted();
            }

            networkGameStarted = false;
            localGameStartedFromNetwork = false;
            localSinglePlayerModeActive = false;
            Debug.Log("[Network] Local match returned to lobby/menu state.", this);
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

        private void SendEmptyMessageToClients(string messageName)
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

            using (FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp))
            {
                networkManager.CustomMessagingManager.SendNamedMessage(messageName, clients, writer);
            }
        }

        private void SendEmptyMessageToHost(string messageName)
        {
            if (networkManager == null
                || !networkManager.IsClient
                || networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            using (FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp))
            {
                networkManager.CustomMessagingManager.SendNamedMessage(messageName, NetworkManager.ServerClientId, writer);
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

            if (!monkeyInputMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(MonkeyInputMessageName, HandleMonkeyInputMessage);
                monkeyInputMessageHandlerRegistered = true;
                Debug.Log("[Network] Monkey input message handler registered.", this);
            }

            if (!monkeyPoseMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(MonkeyPoseMessageName, HandleMonkeyPoseMessage);
                monkeyPoseMessageHandlerRegistered = true;
                Debug.Log("[Network] Monkey pose message handler registered.", this);
            }

            if (!pieceOrderSelectionMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PieceOrderSelectionMessageName, HandlePieceOrderSelectionMessage);
                pieceOrderSelectionMessageHandlerRegistered = true;
                Debug.Log("[Network] Piece order selection message handler registered.", this);
            }

            if (!pieceOrderStateMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PieceOrderStateMessageName, HandlePieceOrderStateMessage);
                pieceOrderStateMessageHandlerRegistered = true;
                Debug.Log("[Network] Piece order state message handler registered.", this);
            }

            if (!pieceTransformMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PieceTransformMessageName, HandlePieceTransformMessage);
                pieceTransformMessageHandlerRegistered = true;
                Debug.Log("[Network] Piece transform message handler registered.", this);
            }

            if (!placementRequestMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PlacementRequestMessageName, HandlePlacementRequestMessage);
                placementRequestMessageHandlerRegistered = true;
                Debug.Log("[Network] Placement request message handler registered.", this);
            }

            if (!placementAcceptedMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PlacementAcceptedMessageName, HandlePlacementAcceptedMessage);
                placementAcceptedMessageHandlerRegistered = true;
                Debug.Log("[Network] Placement accepted message handler registered.", this);
            }

            if (!placementCandidatesMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PlacementCandidatesMessageName, HandlePlacementCandidatesMessage);
                placementCandidatesMessageHandlerRegistered = true;
                Debug.Log("[Network] Placement candidates message handler registered.", this);
            }

            if (!boardStateMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(BoardStateMessageName, HandleBoardStateMessage);
                boardStateMessageHandlerRegistered = true;
                Debug.Log("[Network] Board state message handler registered.", this);
            }

            if (!scoreStateMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ScoreStateMessageName, HandleScoreStateMessage);
                scoreStateMessageHandlerRegistered = true;
                Debug.Log("[Network] Score state message handler registered.", this);
            }

            if (!cardStateMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(CardStateMessageName, HandleCardStateMessage);
                cardStateMessageHandlerRegistered = true;
                Debug.Log("[Network] Card state message handler registered.", this);
            }

            if (!restartRequestMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(RestartRequestMessageName, HandleRestartRequestMessage);
                restartRequestMessageHandlerRegistered = true;
                Debug.Log("[Network] Restart request message handler registered.", this);
            }

            if (!restartMatchMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(RestartMatchMessageName, HandleRestartMatchMessage);
                restartMatchMessageHandlerRegistered = true;
                Debug.Log("[Network] Restart match message handler registered.", this);
            }

            if (!returnToLobbyRequestMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ReturnToLobbyRequestMessageName, HandleReturnToLobbyRequestMessage);
                returnToLobbyRequestMessageHandlerRegistered = true;
                Debug.Log("[Network] Return to lobby request message handler registered.", this);
            }

            if (!returnToLobbyMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ReturnToLobbyMessageName, HandleReturnToLobbyMessage);
                returnToLobbyMessageHandlerRegistered = true;
                Debug.Log("[Network] Return to lobby message handler registered.", this);
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

            if (monkeyInputMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MonkeyInputMessageName);
                monkeyInputMessageHandlerRegistered = false;
            }

            if (monkeyPoseMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MonkeyPoseMessageName);
                monkeyPoseMessageHandlerRegistered = false;
            }

            if (pieceOrderSelectionMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(PieceOrderSelectionMessageName);
                pieceOrderSelectionMessageHandlerRegistered = false;
            }

            if (pieceOrderStateMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(PieceOrderStateMessageName);
                pieceOrderStateMessageHandlerRegistered = false;
            }

            if (pieceTransformMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(PieceTransformMessageName);
                pieceTransformMessageHandlerRegistered = false;
            }

            if (placementRequestMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(PlacementRequestMessageName);
                placementRequestMessageHandlerRegistered = false;
            }

            if (placementAcceptedMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(PlacementAcceptedMessageName);
                placementAcceptedMessageHandlerRegistered = false;
            }

            if (placementCandidatesMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(PlacementCandidatesMessageName);
                placementCandidatesMessageHandlerRegistered = false;
            }

            if (boardStateMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(BoardStateMessageName);
                boardStateMessageHandlerRegistered = false;
            }

            if (scoreStateMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ScoreStateMessageName);
                scoreStateMessageHandlerRegistered = false;
            }

            if (cardStateMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(CardStateMessageName);
                cardStateMessageHandlerRegistered = false;
            }

            if (restartRequestMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(RestartRequestMessageName);
                restartRequestMessageHandlerRegistered = false;
            }

            if (restartMatchMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(RestartMatchMessageName);
                restartMatchMessageHandlerRegistered = false;
            }

            if (returnToLobbyRequestMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ReturnToLobbyRequestMessageName);
                returnToLobbyRequestMessageHandlerRegistered = false;
            }

            if (returnToLobbyMessageHandlerRegistered)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ReturnToLobbyMessageName);
                returnToLobbyMessageHandlerRegistered = false;
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

        private void HandleRestartRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager == null || !networkManager.IsHost)
            {
                return;
            }

            Debug.Log("[Network] Restart request received from client " + senderClientId + ".", this);
            RestartNetworkMatchAsHost();
        }

        private void HandleRestartMatchMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager != null && networkManager.IsHost)
            {
                return;
            }

            networkGameStarted = true;
            RestartLocalGameOnly();
            Debug.Log("[Network] Restart match received from Host.", this);
        }

        private void HandleReturnToLobbyRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager == null || !networkManager.IsHost)
            {
                return;
            }

            Debug.Log("[Network] Return to lobby request received from client " + senderClientId + ".", this);
            ReturnNetworkMatchToLobbyAsHost();
        }

        private void HandleReturnToLobbyMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager != null && networkManager.IsHost)
            {
                return;
            }

            ReturnLocalGameToMenu();
            Debug.Log("[Network] Return to lobby received from Host.", this);
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

            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) * 4, Allocator.Temp))
            {
                writer.WriteValueSafe((int)gameModeManager.CurrentState);
                writer.WriteValueSafe((int)gameModeManager.ActivePlayer);
                writer.WriteValueSafe(gameModeManager.RoundNumber);
                writer.WriteValueSafe(gameModeManager.CurrentTurnIndex);
                networkManager.CustomMessagingManager.SendNamedMessageToAll(GameStateMessageName, writer);
            }

            Debug.Log("[Network] Game state broadcast. State: " + gameModeManager.CurrentState + ", Active: " + gameModeManager.ActivePlayer + ", Round: " + gameModeManager.RoundNumber + ", TurnIndex: " + gameModeManager.CurrentTurnIndex + ".", this);
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
            reader.ReadValueSafe(out int turnIndex);

            ResolveGameModeManager();
            if (gameModeManager == null)
            {
                return;
            }

            FlickDomGameState state = (FlickDomGameState)stateValue;
            FlickDomPlayerId activePlayer = (FlickDomPlayerId)activePlayerValue;
            gameModeManager.ApplyNetworkStateSnapshot(state, activePlayer, roundNumber, turnIndex);
            Debug.Log("[Network] Game state received from client " + senderClientId + ". State: " + state + ", Active: " + activePlayer + ", Round: " + roundNumber + ", TurnIndex: " + turnIndex + ".", this);
        }

        private bool IsAllowedRemotePlayerRequest(ulong senderClientId, FlickDomPlayerId owner)
        {
            if (networkManager == null || senderClientId == networkManager.LocalClientId)
            {
                return true;
            }

            return owner == FlickDomPlayerId.Player2;
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
            reader.ReadValueSafe(out Vector3 launchPosition);

            FlickDomPlayerId owner = (FlickDomPlayerId)ownerValue;
            string pieceId = fixedPieceId.ToString();
            impulse = ClampNetworkFlickImpulse(impulse);

            if (!IsAllowedRemotePlayerRequest(senderClientId, owner))
            {
                Debug.LogWarning("[Network] Rejected flick request from client " + senderClientId + " for non-local player " + owner + ".", this);
                return;
            }

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

            if (piece.TryQueueAuthoritativeFlick(impulse, launchPosition))
            {
                SendFlickAcceptedToClients(owner, pieceId);
                BroadcastPieceOrderState();
                Debug.Log("[Network] Host accepted flick request from client " + senderClientId + ". Piece: " + pieceId + ", Impulse: " + impulse + ", LaunchPosition: " + launchPosition + ".", this);
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

        private void HandleMonkeyInputMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager == null || !networkManager.IsHost)
            {
                return;
            }

            reader.ReadValueSafe(out int ownerValue);
            reader.ReadValueSafe(out Vector3 moveDirection);
            reader.ReadValueSafe(out bool sprint);

            FlickDomPlayerId owner = (FlickDomPlayerId)ownerValue;
            moveDirection = ClampNetworkMoveDirection(moveDirection);

            if (!IsAllowedRemotePlayerRequest(senderClientId, owner))
            {
                Debug.LogWarning("[Network] Rejected monkey input from client " + senderClientId + " for non-local player " + owner + ".", this);
                return;
            }

            ResolveGameModeManager();
            if (!networkGameStarted
                || gameModeManager == null
                || gameModeManager.CurrentState == FlickDomGameState.PhysicsProcessing
                || gameModeManager.CurrentState == FlickDomGameState.CardMatch
                || gameModeManager.CurrentState == FlickDomGameState.RoundEnd)
            {
                return;
            }

            MonkeyThirdPersonController monkey = FindMonkey(owner);
            if (monkey != null)
            {
                monkey.ApplyNetworkMovementInput(moveDirection, sprint);
            }
        }

        private void HandleMonkeyPoseMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager != null && networkManager.IsHost)
            {
                return;
            }

            reader.ReadValueSafe(out int ownerValue);
            reader.ReadValueSafe(out Vector3 position);
            reader.ReadValueSafe(out Quaternion rotation);

            MonkeyThirdPersonController monkey = FindMonkey((FlickDomPlayerId)ownerValue);
            if (monkey != null)
            {
                monkey.ApplyNetworkPose(position, rotation);
            }
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

            if (!IsAllowedRemotePlayerRequest(senderClientId, owner))
            {
                Debug.LogWarning("[Network] Rejected piece order selection from client " + senderClientId + " for non-local player " + owner + ".", this);
                return;
            }

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
            BroadcastPieceOrderState();
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

        private void BroadcastPieceOrderState()
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

            SendPieceOrderStateToClients(clients);
        }

        private void SendPieceOrderStateToClient(ulong clientId)
        {
            if (networkManager == null
                || !networkManager.IsHost
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            SendPieceOrderStateToClients(new List<ulong>(1) { clientId });
        }

        private void SendPieceOrderStateToClients(IReadOnlyList<ulong> clients)
        {
            if (clients == null || clients.Count <= 0)
            {
                return;
            }

            LocalFlickTurnTestRig turnRig = FindAnyObjectByType<LocalFlickTurnTestRig>();
            if (turnRig == null)
            {
                return;
            }

            List<string> player1Order = new List<string>(3);
            List<string> player2Order = new List<string>(3);
            turnRig.GetPieceOrderSnapshot(FlickDomPlayerId.Player1, player1Order);
            turnRig.GetPieceOrderSnapshot(FlickDomPlayerId.Player2, player2Order);

            FastBufferWriter writer = new FastBufferWriter(CalculatePieceOrderStateCapacity(player1Order, player2Order), Allocator.Temp);
            try
            {
                WritePieceOrderState(ref writer, player1Order, turnRig.GetNextOrderIndexSnapshot(FlickDomPlayerId.Player1));
                WritePieceOrderState(ref writer, player2Order, turnRig.GetNextOrderIndexSnapshot(FlickDomPlayerId.Player2));
                networkManager.CustomMessagingManager.SendNamedMessage(PieceOrderStateMessageName, clients, writer);
            }
            finally
            {
                writer.Dispose();
            }

            Debug.Log("[Network] Piece order state broadcast. P1: " + BuildPieceOrderLog(player1Order) + ", P2: " + BuildPieceOrderLog(player2Order) + ".", this);
        }

        private void HandlePieceOrderStateMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager != null && networkManager.IsHost)
            {
                return;
            }

            List<string> player1Order = ReadPieceOrderState(ref reader, out int player1NextIndex);
            List<string> player2Order = ReadPieceOrderState(ref reader, out int player2NextIndex);
            LocalFlickTurnTestRig turnRig = FindAnyObjectByType<LocalFlickTurnTestRig>();
            if (turnRig != null)
            {
                turnRig.ApplyNetworkPieceOrderSnapshot(player1Order, player1NextIndex, player2Order, player2NextIndex);
            }

            Debug.Log("[Network] Piece order state received from Host. P1: " + BuildPieceOrderLog(player1Order) + ", P2: " + BuildPieceOrderLog(player2Order) + ".", this);
        }

        private static int CalculatePieceOrderStateCapacity(IReadOnlyList<string> player1Order, IReadOnlyList<string> player2Order)
        {
            int player1Count = player1Order != null ? player1Order.Count : 0;
            int player2Count = player2Order != null ? player2Order.Count : 0;
            return sizeof(int) * 4 + 64 * (player1Count + player2Count);
        }

        private static void WritePieceOrderState(ref FastBufferWriter writer, IReadOnlyList<string> pieceIds, int nextIndex)
        {
            int count = pieceIds != null ? pieceIds.Count : 0;
            writer.WriteValueSafe(nextIndex);
            writer.WriteValueSafe(count);
            for (int i = 0; i < count; i++)
            {
                writer.WriteValueSafe(new FixedString64Bytes(pieceIds[i] ?? string.Empty));
            }
        }

        private static List<string> ReadPieceOrderState(ref FastBufferReader reader, out int nextIndex)
        {
            reader.ReadValueSafe(out nextIndex);
            reader.ReadValueSafe(out int count);
            List<string> pieceIds = new List<string>(Mathf.Max(0, count));
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out FixedString64Bytes fixedPieceId);
                pieceIds.Add(fixedPieceId.ToString());
            }

            return pieceIds;
        }

        private static string BuildPieceOrderLog(IReadOnlyList<string> pieceIds)
        {
            if (pieceIds == null || pieceIds.Count <= 0)
            {
                return "[]";
            }

            return "[" + string.Join(",", pieceIds) + "]";
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

            nextTransformBroadcastTime = Time.unscaledTime + Mathf.Max(0.01f, transformBroadcastInterval);
            SendAllPieceTransformsToClients();
            SendAllMonkeyPosesToClients();
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
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) + 64 + sizeof(float) * 7 + sizeof(bool), Allocator.Temp))
            {
                writer.WriteValueSafe((int)piece.Owner);
                writer.WriteValueSafe(fixedPieceId);
                writer.WriteValueSafe(piece.transform.position);
                writer.WriteValueSafe(piece.transform.rotation);
                writer.WriteValueSafe(piece.IsDead);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    PieceTransformMessageName,
                    clients,
                    writer,
                    NetworkDelivery.UnreliableSequenced);
            }
        }

        private void SendPieceTransformToClient(TurnBasedFlickPiece piece, ulong clientId)
        {
            List<ulong> clients = new List<ulong>(1) { clientId };
            SendPieceTransformToClients(piece, clients);
        }

        private void SendAllMonkeyPosesToClients()
        {
            List<ulong> clients = GetRemoteClientIds();
            if (clients.Count <= 0)
            {
                return;
            }

            List<MonkeyThirdPersonController> monkeys = CollectUniqueMonkeys();
            for (int i = 0; i < monkeys.Count; i++)
            {
                SendMonkeyPoseToClients(monkeys[i], clients);
            }
        }

        private void SendAllMonkeyPosesToClient(ulong clientId)
        {
            List<MonkeyThirdPersonController> monkeys = CollectUniqueMonkeys();
            List<ulong> clients = new List<ulong>(1) { clientId };
            for (int i = 0; i < monkeys.Count; i++)
            {
                SendMonkeyPoseToClients(monkeys[i], clients);
            }
        }

        private void SendMonkeyPoseToClients(MonkeyThirdPersonController monkey, IReadOnlyList<ulong> clients)
        {
            if (monkey == null
                || networkManager == null
                || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) + sizeof(float) * 7, Allocator.Temp))
            {
                writer.WriteValueSafe((int)monkey.Owner);
                writer.WriteValueSafe(monkey.transform.position);
                writer.WriteValueSafe(monkey.transform.rotation);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    MonkeyPoseMessageName,
                    clients,
                    writer,
                    NetworkDelivery.UnreliableSequenced);
            }
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
            reader.ReadValueSafe(out bool isDead);

            TurnBasedFlickPiece piece = FindFlickPiece((FlickDomPlayerId)ownerValue, fixedPieceId.ToString());
            if (piece != null)
            {
                piece.ApplyNetworkPose(position, rotation);
                piece.ApplyNetworkState(isDead);
            }
        }

        private void HandlePlacementRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager == null || !networkManager.IsHost)
            {
                return;
            }

            reader.ReadValueSafe(out int ownerValue);
            reader.ReadValueSafe(out FixedString64Bytes fixedPieceId);
            reader.ReadValueSafe(out int destinationX);
            reader.ReadValueSafe(out int destinationY);
            reader.ReadValueSafe(out bool hasRelocationSource);
            reader.ReadValueSafe(out int sourceX);
            reader.ReadValueSafe(out int sourceY);

            FlickDomPlayerId owner = (FlickDomPlayerId)ownerValue;
            string pieceId = fixedPieceId.ToString();
            Vector2Int destination = new Vector2Int(destinationX, destinationY);
            Vector2Int? relocationSource = hasRelocationSource
                ? new Vector2Int(sourceX, sourceY)
                : (Vector2Int?)null;

            if (senderClientId != networkManager.LocalClientId && owner != FlickDomPlayerId.Player2)
            {
                Debug.LogWarning("[Network] Rejected placement request from client " + senderClientId + ". Client may only request Player2 placement.", this);
                return;
            }

            ResolvePlacementSelector();
            if (placementSelector == null
                || !placementSelector.TryApplyNetworkPlacementRequest(owner, pieceId, destination, relocationSource, out TokenPlacementResult result))
            {
                Debug.LogWarning("[Network] Rejected placement request from client " + senderClientId + ". Player: " + owner + ", Piece: " + pieceId + ", Destination: " + destination + ".", this);
                return;
            }

            SendPlacementAcceptedToClients(owner, pieceId, result.Destination, result.RelocationSource);
            BroadcastBoardState();
            BroadcastScoreState();
            BroadcastGameState();
            Debug.Log("[Network] Host accepted placement request from client " + senderClientId + ". Player: " + owner + ", Piece: " + pieceId + ", Destination: " + destination + ".", this);
        }

        private void SendPlacementAcceptedToClients(FlickDomPlayerId owner, string pieceId, Vector2Int destination, Vector2Int? relocationSource)
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
            bool hasRelocationSource = relocationSource.HasValue;
            Vector2Int source = relocationSource.GetValueOrDefault();
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) * 6 + sizeof(bool) + 64, Allocator.Temp))
            {
                writer.WriteValueSafe((int)owner);
                writer.WriteValueSafe(fixedPieceId);
                writer.WriteValueSafe(destination.x);
                writer.WriteValueSafe(destination.y);
                writer.WriteValueSafe(hasRelocationSource);
                writer.WriteValueSafe(source.x);
                writer.WriteValueSafe(source.y);
                networkManager.CustomMessagingManager.SendNamedMessage(PlacementAcceptedMessageName, clients, writer);
            }
        }

        private void HandlePlacementAcceptedMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager != null && networkManager.IsHost)
            {
                return;
            }

            reader.ReadValueSafe(out int ownerValue);
            reader.ReadValueSafe(out FixedString64Bytes fixedPieceId);
            reader.ReadValueSafe(out int destinationX);
            reader.ReadValueSafe(out int destinationY);
            reader.ReadValueSafe(out bool hasRelocationSource);
            reader.ReadValueSafe(out int sourceX);
            reader.ReadValueSafe(out int sourceY);

            FlickDomPlayerId owner = (FlickDomPlayerId)ownerValue;
            string pieceId = fixedPieceId.ToString();
            Vector2Int destination = new Vector2Int(destinationX, destinationY);
            Vector2Int? relocationSource = hasRelocationSource
                ? new Vector2Int(sourceX, sourceY)
                : (Vector2Int?)null;

            ResolvePlacementSelector();
            if (placementSelector != null)
            {
                placementSelector.ApplyNetworkPlacementAccepted(owner, pieceId, destination, relocationSource);
            }

            Debug.Log("[Network] Placement accepted received from Host. Player: " + owner + ", Piece: " + pieceId + ", Destination: " + destination + ".", this);
        }

        private void BroadcastPlacementCandidates()
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

            ResolveGameModeManager();
            if (gameModeManager == null)
            {
                return;
            }

            IReadOnlyList<PiecePlacementCandidate> candidates = gameModeManager.PendingPlacementCandidates;
            int candidateCount = candidates != null ? candidates.Count : 0;
            FastBufferWriter writer = new FastBufferWriter(CalculatePlacementCandidatesCapacity(candidates), Allocator.Temp);
            try
            {
                writer.WriteValueSafe(candidateCount);
                for (int i = 0; i < candidateCount; i++)
                {
                    WritePlacementCandidate(ref writer, candidates[i]);
                }

                networkManager.CustomMessagingManager.SendNamedMessage(PlacementCandidatesMessageName, clients, writer);
            }
            finally
            {
                writer.Dispose();
            }

            Debug.Log("[Network] Placement candidates broadcast. Count: " + candidateCount + ".", this);
        }

        private void HandlePlacementCandidatesMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager != null && networkManager.IsHost)
            {
                return;
            }

            reader.ReadValueSafe(out int candidateCount);
            List<PiecePlacementCandidate> candidates = new List<PiecePlacementCandidate>(Mathf.Max(0, candidateCount));
            for (int i = 0; i < candidateCount; i++)
            {
                candidates.Add(ReadPlacementCandidate(ref reader));
            }

            ResolveGameModeManager();
            if (gameModeManager != null)
            {
                gameModeManager.ApplyNetworkPlacementCandidates(candidates);
            }

            ResolvePlacementSelector();
            if (placementSelector != null && gameModeManager != null && gameModeManager.CurrentState == FlickDomGameState.PlacementSelection)
            {
                placementSelector.RefreshNetworkPlacementCandidates();
            }

            Debug.Log("[Network] Placement candidates received from Host. Count: " + candidates.Count + ".", this);
        }

        private void BroadcastBoardState()
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

            ResolveTokenMapManager();
            if (tokenMapManager == null)
            {
                return;
            }

            List<Vector2Int> player1Cells = tokenMapManager.GetOwnedCells(FlickDomPlayerId.Player1);
            List<Vector2Int> player2Cells = tokenMapManager.GetOwnedCells(FlickDomPlayerId.Player2);
            int capacity = sizeof(int) * (3 + (player1Cells.Count + player2Cells.Count) * 2);
            FastBufferWriter writer = new FastBufferWriter(capacity, Allocator.Temp);
            try
            {
                writer.WriteValueSafe(tokenMapManager.BoardSize);
                WriteOwnedCells(ref writer, player1Cells);
                WriteOwnedCells(ref writer, player2Cells);
                networkManager.CustomMessagingManager.SendNamedMessage(BoardStateMessageName, clients, writer);
            }
            finally
            {
                writer.Dispose();
            }

            Debug.Log("[Network] Board state broadcast. P1 cells: " + player1Cells.Count + ", P2 cells: " + player2Cells.Count + ".", this);
        }

        private void HandleBoardStateMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager != null && networkManager.IsHost)
            {
                return;
            }

            reader.ReadValueSafe(out int boardSize);
            List<Vector2Int> player1Cells = ReadOwnedCells(ref reader);
            List<Vector2Int> player2Cells = ReadOwnedCells(ref reader);

            ResolveTokenMapManager();
            if (tokenMapManager != null)
            {
                tokenMapManager.ApplyNetworkOwnerGrid(boardSize, player1Cells, player2Cells);
                TokenMapGridView gridView = FindAnyObjectByType<TokenMapGridView>();
                if (gridView != null)
                {
                    gridView.ClearCandidateHighlights();
                    gridView.RefreshOwnerCells(tokenMapManager);
                }
            }

            ResolveGameModeManager();
            ResolvePlacementSelector();
            if (placementSelector != null
                && gameModeManager != null
                && gameModeManager.CurrentState == FlickDomGameState.PlacementSelection)
            {
                placementSelector.RefreshNetworkPlacementCandidates();
            }

            Debug.Log("[Network] Board state received from Host. P1 cells: " + player1Cells.Count + ", P2 cells: " + player2Cells.Count + ".", this);
        }

        private void BroadcastScoreState()
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

            ResolvePatternCardManager();
            if (patternCardManager == null)
            {
                return;
            }

            bool[] claimedCards = patternCardManager.GetClaimedCardSnapshot();
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) * (6 + claimedCards.Length), Allocator.Temp))
            {
                writer.WriteValueSafe(patternCardManager.Player1Score);
                writer.WriteValueSafe(patternCardManager.Player2Score);
                writer.WriteValueSafe((int)patternCardManager.Winner);
                writer.WriteValueSafe(patternCardManager.CurrentFallbackDeckIndex);
                writer.WriteValueSafe(patternCardManager.CardDrawSeed);
                writer.WriteValueSafe(claimedCards.Length);
                for (int i = 0; i < claimedCards.Length; i++)
                {
                    writer.WriteValueSafe(claimedCards[i]);
                }

                networkManager.CustomMessagingManager.SendNamedMessage(ScoreStateMessageName, clients, writer);
            }

            Debug.Log("[Network] Score state broadcast. P1: " + patternCardManager.Player1Score + ", P2: " + patternCardManager.Player2Score + ", Winner: " + patternCardManager.Winner + ", Stage: " + patternCardManager.CurrentStageNumber + ", DrawSeed: " + patternCardManager.CardDrawSeed + ", Claimed: " + BuildClaimedCardsLog(claimedCards) + ".", this);
        }

        private void HandleScoreStateMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager != null && networkManager.IsHost)
            {
                return;
            }

            reader.ReadValueSafe(out int player1Score);
            reader.ReadValueSafe(out int player2Score);
            reader.ReadValueSafe(out int winnerValue);
            reader.ReadValueSafe(out int deckIndex);
            reader.ReadValueSafe(out int cardDrawSeed);
            reader.ReadValueSafe(out int claimedCount);
            List<bool> claimedCards = new List<bool>(Mathf.Max(0, claimedCount));
            for (int i = 0; i < claimedCount; i++)
            {
                reader.ReadValueSafe(out bool isClaimed);
                claimedCards.Add(isClaimed);
            }

            ResolvePatternCardManager();
            if (patternCardManager != null)
            {
                patternCardManager.ApplyNetworkScoreSnapshot(player1Score, player2Score, (FlickDomPlayerId)winnerValue);
                patternCardManager.ApplyNetworkCardStateSnapshot(deckIndex, cardDrawSeed, claimedCards);
            }

            Debug.Log("[Network] Score state received from Host. P1: " + player1Score + ", P2: " + player2Score + ", Winner: " + (FlickDomPlayerId)winnerValue + ", StageIndex: " + deckIndex + ", DrawSeed: " + cardDrawSeed + ", Claimed: " + BuildClaimedCardsLog(claimedCards) + ".", this);
        }

        private void BroadcastCardState()
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

            ResolvePatternCardManager();
            if (patternCardManager == null)
            {
                return;
            }

            bool[] claimedCards = patternCardManager.GetClaimedCardSnapshot();
            using (FastBufferWriter writer = new FastBufferWriter(sizeof(int) * (3 + claimedCards.Length), Allocator.Temp))
            {
                writer.WriteValueSafe(patternCardManager.CurrentFallbackDeckIndex);
                writer.WriteValueSafe(patternCardManager.CardDrawSeed);
                writer.WriteValueSafe(claimedCards.Length);
                for (int i = 0; i < claimedCards.Length; i++)
                {
                    writer.WriteValueSafe(claimedCards[i]);
                }

                networkManager.CustomMessagingManager.SendNamedMessage(CardStateMessageName, clients, writer);
            }

            Debug.Log("[Network] Card state broadcast. Stage: " + patternCardManager.CurrentStageNumber + ", DrawSeed: " + patternCardManager.CardDrawSeed + ", Claimed: " + BuildClaimedCardsLog(claimedCards) + ".", this);
        }

        private void HandleCardStateMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager != null && networkManager.IsHost)
            {
                return;
            }

            reader.ReadValueSafe(out int deckIndex);
            reader.ReadValueSafe(out int cardDrawSeed);
            reader.ReadValueSafe(out int claimedCount);
            List<bool> claimedCards = new List<bool>(Mathf.Max(0, claimedCount));
            for (int i = 0; i < claimedCount; i++)
            {
                reader.ReadValueSafe(out bool isClaimed);
                claimedCards.Add(isClaimed);
            }

            ResolvePatternCardManager();
            if (patternCardManager != null)
            {
                patternCardManager.ApplyNetworkCardStateSnapshot(deckIndex, cardDrawSeed, claimedCards);
            }

            Debug.Log("[Network] Card state received from Host. StageIndex: " + deckIndex + ", DrawSeed: " + cardDrawSeed + ", Claimed: " + BuildClaimedCardsLog(claimedCards) + ".", this);
        }

        private static string BuildClaimedCardsLog(IReadOnlyList<bool> claimedCards)
        {
            if (claimedCards == null || claimedCards.Count <= 0)
            {
                return "[]";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder("[");
            for (int i = 0; i < claimedCards.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }

                builder.Append(claimedCards[i] ? "1" : "0");
            }

            builder.Append("]");
            return builder.ToString();
        }

        private static void WriteOwnedCells(ref FastBufferWriter writer, IReadOnlyList<Vector2Int> cells)
        {
            int count = cells != null ? cells.Count : 0;
            writer.WriteValueSafe(count);
            for (int i = 0; i < count; i++)
            {
                writer.WriteValueSafe(cells[i].x);
                writer.WriteValueSafe(cells[i].y);
            }
        }

        private static List<Vector2Int> ReadOwnedCells(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out int count);
            List<Vector2Int> cells = new List<Vector2Int>(Mathf.Max(0, count));
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out int x);
                reader.ReadValueSafe(out int y);
                cells.Add(new Vector2Int(x, y));
            }

            return cells;
        }

        private static int CalculatePlacementCandidatesCapacity(IReadOnlyList<PiecePlacementCandidate> candidates)
        {
            int capacity = sizeof(int);
            if (candidates == null)
            {
                return capacity;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                PiecePlacementCandidate candidate = candidates[i];
                int cellCount = candidate != null ? candidate.CandidateCells.Count : 0;
                capacity += sizeof(int) + 64 + (sizeof(float) * 4) + sizeof(int) + (cellCount * sizeof(int) * 2);
            }

            return capacity;
        }

        private static void WritePlacementCandidate(ref FastBufferWriter writer, PiecePlacementCandidate candidate)
        {
            if (candidate == null)
            {
                writer.WriteValueSafe((int)FlickDomPlayerId.None);
                writer.WriteValueSafe(new FixedString64Bytes(string.Empty));
                writer.WriteValueSafe(Vector3.zero);
                writer.WriteValueSafe(0f);
                writer.WriteValueSafe(0);
                return;
            }

            FixedString64Bytes fixedPieceId = new FixedString64Bytes(candidate.PieceId ?? string.Empty);
            writer.WriteValueSafe((int)candidate.Owner);
            writer.WriteValueSafe(fixedPieceId);
            writer.WriteValueSafe(candidate.WorldPosition);
            writer.WriteValueSafe(candidate.TokenRadius);

            IReadOnlyList<Vector2Int> cells = candidate.CandidateCells;
            writer.WriteValueSafe(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                writer.WriteValueSafe(cells[i].x);
                writer.WriteValueSafe(cells[i].y);
            }
        }

        private static PiecePlacementCandidate ReadPlacementCandidate(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out int ownerValue);
            reader.ReadValueSafe(out FixedString64Bytes fixedPieceId);
            reader.ReadValueSafe(out Vector3 worldPosition);
            reader.ReadValueSafe(out float tokenRadius);
            reader.ReadValueSafe(out int cellCount);

            List<Vector2Int> cells = new List<Vector2Int>(Mathf.Max(0, cellCount));
            for (int i = 0; i < cellCount; i++)
            {
                reader.ReadValueSafe(out int x);
                reader.ReadValueSafe(out int y);
                cells.Add(new Vector2Int(x, y));
            }

            return new PiecePlacementCandidate(
                fixedPieceId.ToString(),
                (FlickDomPlayerId)ownerValue,
                worldPosition,
                tokenRadius,
                cells);
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

        private static MonkeyThirdPersonController FindMonkey(FlickDomPlayerId owner)
        {
            EnsureNamedMonkeyController(owner);

            MonkeyThirdPersonController[] monkeys =
                FindObjectsByType<MonkeyThirdPersonController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < monkeys.Length; i++)
            {
                MonkeyThirdPersonController monkey = monkeys[i];
                if (monkey != null && monkey.Owner == owner)
                {
                    return monkey;
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

        private static List<MonkeyThirdPersonController> CollectUniqueMonkeys()
        {
            EnsureNamedMonkeyController(FlickDomPlayerId.Player1);
            EnsureNamedMonkeyController(FlickDomPlayerId.Player2);

            MonkeyThirdPersonController[] monkeys =
                FindObjectsByType<MonkeyThirdPersonController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            List<MonkeyThirdPersonController> uniqueMonkeys = new List<MonkeyThirdPersonController>(monkeys.Length);
            HashSet<FlickDomPlayerId> seenOwners = new HashSet<FlickDomPlayerId>();

            for (int i = 0; i < monkeys.Length; i++)
            {
                MonkeyThirdPersonController monkey = monkeys[i];
                if (monkey == null || monkey.Owner == FlickDomPlayerId.None)
                {
                    continue;
                }

                if (!seenOwners.Add(monkey.Owner))
                {
                    Debug.LogWarning("[Network] Duplicate monkey ignored for transform sync. Owner: " + monkey.Owner + ", Object: " + monkey.name + ".", monkey);
                    continue;
                }

                uniqueMonkeys.Add(monkey);
            }

            return uniqueMonkeys;
        }

        private static void EnsureSceneMonkeyControllers()
        {
            EnsureNamedMonkeyController(FlickDomPlayerId.Player1);
            EnsureNamedMonkeyController(FlickDomPlayerId.Player2);
        }

        private static MonkeyThirdPersonController EnsureNamedMonkeyController(FlickDomPlayerId owner)
        {
            string objectName = GetExpectedMonkeyObjectName(owner);
            if (string.IsNullOrEmpty(objectName))
            {
                return null;
            }

            GameObject monkeyObject = FindSceneGameObjectByName(objectName);
            if (monkeyObject == null)
            {
                return null;
            }

            MonkeyThirdPersonController controller =
                monkeyObject.GetComponent<MonkeyThirdPersonController>();
            if (controller == null)
            {
                controller = monkeyObject.AddComponent<MonkeyThirdPersonController>();
                Debug.Log("[Network] Added MonkeyThirdPersonController to " + objectName + ".", monkeyObject);
            }

            controller.SetOwner(owner);
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                controller.SetCameraTransform(mainCamera.transform);
            }

            return controller;
        }

        private static GameObject FindSceneGameObjectByName(string objectName)
        {
            Transform[] transforms =
                FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate != null && candidate.gameObject.scene.IsValid() && candidate.name == objectName)
                {
                    return candidate.gameObject;
                }
            }

            return null;
        }

        private static string GetExpectedMonkeyObjectName(FlickDomPlayerId owner)
        {
            switch (owner)
            {
                case FlickDomPlayerId.Player1:
                    return Player1MonkeyObjectName;
                case FlickDomPlayerId.Player2:
                    return Player2MonkeyObjectName;
                default:
                    return string.Empty;
            }
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

        private void SubscribePatternCardEvents(bool subscribe)
        {
            ResolvePatternCardManager();
            if (patternCardManager == null)
            {
                return;
            }

            if (subscribe)
            {
                if (patternCardEventsSubscribed)
                {
                    return;
                }

                patternCardManager.ScoreChanged += HandlePatternScoreChanged;
                patternCardManager.MatchWon += HandlePatternMatchWon;
                patternCardManager.ActiveCardChanged += HandlePatternActiveCardChanged;
                patternCardManager.CardCompleted += HandlePatternCardCompleted;
                patternCardManager.CardsExhausted += HandlePatternCardsExhausted;
                patternCardEventsSubscribed = true;
                return;
            }

            if (!patternCardEventsSubscribed)
            {
                return;
            }

            patternCardManager.ScoreChanged -= HandlePatternScoreChanged;
            patternCardManager.MatchWon -= HandlePatternMatchWon;
            patternCardManager.ActiveCardChanged -= HandlePatternActiveCardChanged;
            patternCardManager.CardCompleted -= HandlePatternCardCompleted;
            patternCardManager.CardsExhausted -= HandlePatternCardsExhausted;
            patternCardEventsSubscribed = false;
        }

        private void HandlePatternScoreChanged(FlickDomPlayerId player, int gainedScore, int player1Score, int player2Score)
        {
            BroadcastScoreState();
            BroadcastCardState();
        }

        private void HandlePatternMatchWon(FlickDomPlayerId winner, int player1Score, int player2Score)
        {
            BroadcastScoreState();
            BroadcastCardState();
        }

        private void HandlePatternActiveCardChanged(PatternCardData card)
        {
            BroadcastCardState();
        }

        private void HandlePatternCardCompleted(PatternCardData card, FlickDomPlayerId player, int score, Vector2Int matchOrigin)
        {
            BroadcastCardState();
        }

        private void HandlePatternCardsExhausted()
        {
            BroadcastCardState();
        }

        private void HandleGameModeStateChanged(FlickDomGameState previousState, FlickDomGameState nextState)
        {
            BroadcastGameState();
            if (nextState == FlickDomGameState.PlacementSelection)
            {
                BroadcastPlacementCandidates();
            }
        }

        private void HandleGameModeActivePlayerChanged(FlickDomPlayerId activePlayer)
        {
            BroadcastGameState();
        }

        private void HandleGameModeRoundStarted(int roundNumber, System.Collections.Generic.IReadOnlyList<FlickDomPlayerId> turnOrder)
        {
            BroadcastGameState();
        }

        private Vector3 ClampNetworkFlickImpulse(Vector3 impulse)
        {
            float maxMagnitude = Mathf.Max(0f, maxNetworkFlickImpulseMagnitude);
            if (maxMagnitude <= 0f)
            {
                return Vector3.zero;
            }

            if (float.IsNaN(impulse.x)
                || float.IsNaN(impulse.y)
                || float.IsNaN(impulse.z)
                || float.IsInfinity(impulse.x)
                || float.IsInfinity(impulse.y)
                || float.IsInfinity(impulse.z))
            {
                return Vector3.zero;
            }

            return Vector3.ClampMagnitude(impulse, maxMagnitude);
        }

        private static Vector3 ClampNetworkMoveDirection(Vector3 moveDirection)
        {
            if (float.IsNaN(moveDirection.x)
                || float.IsNaN(moveDirection.y)
                || float.IsNaN(moveDirection.z)
                || float.IsInfinity(moveDirection.x)
                || float.IsInfinity(moveDirection.y)
                || float.IsInfinity(moveDirection.z))
            {
                return Vector3.zero;
            }

            moveDirection.y = 0f;
            if (moveDirection.sqrMagnitude > 1f)
            {
                moveDirection.Normalize();
            }

            return moveDirection;
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
