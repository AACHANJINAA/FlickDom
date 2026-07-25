using System;
using FlickDom.Gameplay;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlickDom.Networking
{
    public sealed class FlickDomNetworkBootstrap : MonoBehaviour
    {
        [Header("Network")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private UnityTransport unityTransport;
        [SerializeField] private string connectAddress = "127.0.0.1";
        [SerializeField] private ushort port = 7777;
        [SerializeField] private int maxPlayers = 2;
        [SerializeField] private bool persistAcrossScenes = true;

        [Header("Local Test Controls")]
        [SerializeField] private bool enableKeyboardShortcuts = true;
        [SerializeField] private bool showRuntimeStatus = true;
        [SerializeField] private bool enableCommandLineAutoStart = true;
        [SerializeField] private Key startHostKey = Key.S;
        [SerializeField] private Key startClientKey = Key.C;
        [SerializeField] private Key shutdownKey = Key.X;

        public event Action<FlickDomPlayerId> LocalPlayerRoleChanged;

        public NetworkManager NetworkManager
        {
            get { return networkManager; }
        }

        public FlickDomPlayerId LocalPlayerId { get; private set; } = FlickDomPlayerId.None;

        public bool IsRunning
        {
            get { return networkManager != null && networkManager.IsListening; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrapInScene()
        {
            if (FindAnyObjectByType<FlickDomNetworkBootstrap>() != null)
            {
                return;
            }

            GameObject bootstrapObject = new GameObject("FlickDom Network Bootstrap");
            bootstrapObject.AddComponent<FlickDomNetworkBootstrap>();
        }

        private void Awake()
        {
            ResolveNetworkManager();
            ConfigureNetworkManager();

            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
                DontDestroyOnLoad(networkManager.gameObject);
            }
        }

        private void Start()
        {
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
        }

        private void Update()
        {
            if (!enableKeyboardShortcuts || Keyboard.current == null)
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
            if (!showRuntimeStatus)
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

            SetLocalPlayerRole(FlickDomPlayerId.Player1);
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

            SetLocalPlayerRole(FlickDomPlayerId.Player2);
            Debug.Log("[Network] Client start requested. Local role is Player2.", this);
        }

        [ContextMenu("Shutdown")]
        public void Shutdown()
        {
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
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            Debug.Log("[Network] Client disconnected: " + clientId + ".", this);

            if (networkManager != null && clientId == networkManager.LocalClientId)
            {
                SetLocalPlayerRole(FlickDomPlayerId.None);
            }
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

        private static bool WasPressedThisFrame(Key key)
        {
            return Keyboard.current[key].wasPressedThisFrame;
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
    }
}
