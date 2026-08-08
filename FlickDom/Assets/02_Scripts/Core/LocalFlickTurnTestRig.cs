using System.Collections;
using System.Collections.Generic;
using System.Text;
using FlickDom.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FlickDom.Gameplay
{
    public sealed class LocalFlickTurnTestRig : MonoBehaviour
    {
        public event System.Action<FlickDomPlayerId> PieceOrderChanged;

        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private TokenMapGridView tokenMapGridView;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private TurnBasedFlickPiece[] player1Pieces;
        [SerializeField] private TurnBasedFlickPiece[] player2Pieces;
        [Header("Scene Authored Piece Layout")]
        [Tooltip("Models placed directly in the scene. Array order becomes the default piece order and piece ID order.")]
        [SerializeField] private Transform[] player1PieceObjects;
        [SerializeField] private Transform[] player2PieceObjects;
        [Tooltip("Adds the gameplay components required by a plain art model at runtime. Existing components are preserved.")]
        [SerializeField] private bool configureAuthoredPieceComponents = true;
        [Header("Token Data")]
        [SerializeField] private TokenData[] player1TokenDataSequence;
        [SerializeField] private TokenData[] player2TokenDataSequence;
        [Header("Piece Visual Overrides")]
        [SerializeField] private Material player1PieceMaterialOverride;
        [SerializeField] private Material player2PieceMaterialOverride;
        [Header("Start Tray Visuals")]
        [SerializeField] private GameObject startTrayPrefab;
        [SerializeField] private GridCellCandidateResolver startTrayBoardResolver;
        [SerializeField] private bool showStartTrays = true;
        [SerializeField] private bool alignStartTraysToBoardCells = true;
        [SerializeField] private bool alignGeneratedPieceStartsToBoardCells = true;
        [SerializeField] private float startTrayCellSizeRatio = 0.95f;
        [SerializeField] private float startTraySideGap = 0.16f;
        [SerializeField] private float startTrayHeight = 0.04f;
        [SerializeField] private Vector3 startTrayScale = Vector3.one;
        [SerializeField] private float startTrayWorldY = 0.025f;
        [Header("Startup")]
        [SerializeField] private bool startGameOnPlay = true;
        [Tooltip("Legacy fallback that clones a configured piece. Keep disabled when using scene-authored piece objects.")]
        [SerializeField] private bool autoCreateMissingPieces = true;
        [SerializeField] private int targetPiecesPerPlayer = 3;
        [SerializeField] private float generatedPieceSpacing = 1.1f;
        [SerializeField] private float pieceSelectionRaycastDistance = 1000f;
        [SerializeField] private bool logStateChanges = true;
        [SerializeField] private bool autoStartNextRoundWhenNoPlacementCandidates = true;
        [Header("Order UI")]
        [SerializeField] private Font orderLabelFont;
        [SerializeField] private Vector2 orderLabelSize = new Vector2(72f, 72f);
        [SerializeField] private Vector3 orderLabelWorldOffset = new Vector3(0f, 0.8f, 0f);
        [SerializeField] private int orderLabelFontSize = 42;
        [SerializeField] private Color player1OrderColor = new Color(0.18f, 0.42f, 1f, 1f);
        [SerializeField] private Color player2OrderColor = new Color(1f, 0.22f, 0.18f, 1f);
        [SerializeField] private Color orderOutlineColor = new Color(0f, 0f, 0f, 0.8f);
        [SerializeField] private Vector2 orderOutlineDistance = new Vector2(2f, -2f);

        private readonly StringBuilder logBuilder = new StringBuilder(256);
        private readonly List<TurnBasedFlickPiece> player1PieceOrder = new List<TurnBasedFlickPiece>(3);
        private readonly List<TurnBasedFlickPiece> player2PieceOrder = new List<TurnBasedFlickPiece>(3);
        private readonly WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();
        private int player1NextOrderIndex;
        private int player2NextOrderIndex;
        private Coroutine physicsCompletionRoutine;
        private Coroutine noPlacementAdvanceRoutine;
        private Canvas orderLabelCanvas;
        private readonly List<Text> orderLabels = new List<Text>(3);

        private const string SelectSoundResourcePath = "Audio/Select";
        private const string SelectAudioObjectName = "Flick Selection Audio";
        private const float SelectSoundVolumeScale = 0.45f;

        private static AudioSource selectAudioSource;
        private static AudioClip selectSoundClip;

        private void Awake()
        {
            if (gameModeManager == null)
            {
                gameModeManager = GetComponent<GameModeManager>();
            }

            if (tokenMapGridView == null)
            {
                tokenMapGridView = GetComponent<TokenMapGridView>();
            }

            if (inputCamera == null)
            {
                inputCamera = Camera.main;
            }

            if (startTrayBoardResolver == null)
            {
                startTrayBoardResolver = GetComponent<GridCellCandidateResolver>();
            }

            player1Pieces = ResolveSceneAuthoredPieces(player1Pieces, player1PieceObjects);
            player2Pieces = ResolveSceneAuthoredPieces(player2Pieces, player2PieceObjects);

            if (autoCreateMissingPieces && !HasSceneAuthoredPieces())
            {
                player1Pieces = EnsurePieceCount(player1Pieces, "Player1", FlickDomPlayerId.Player1);
                player2Pieces = EnsurePieceCount(player2Pieces, "Player2", FlickDomPlayerId.Player2);
            }

            RemoveDuplicatePieceComponents(player1Pieces);
            RemoveDuplicatePieceComponents(player2Pieces);

            ApplyTokenDataSequence(player1Pieces, player1TokenDataSequence);
            ApplyTokenDataSequence(player2Pieces, player2TokenDataSequence);
            ApplyPieceMaterialOverride(player1Pieces, player1PieceMaterialOverride);
            ApplyPieceMaterialOverride(player2Pieces, player2PieceMaterialOverride);

            ConfigurePieces(player1Pieces, FlickDomPlayerId.Player1, "P1");
            ConfigurePieces(player2Pieces, FlickDomPlayerId.Player2, "P2");
            BuildStartTrayVisuals();
            EnsureOrderLabelUi();
            HideAllOrderLabels();
            PreloadSelectSound();
        }

        private void OnValidate()
        {
            targetPiecesPerPlayer = Mathf.Max(1, targetPiecesPerPlayer);
            generatedPieceSpacing = Mathf.Max(0.1f, generatedPieceSpacing);
            pieceSelectionRaycastDistance = Mathf.Max(1f, pieceSelectionRaycastDistance);
            startTrayCellSizeRatio = Mathf.Clamp(startTrayCellSizeRatio, 0.1f, 2f);
            startTraySideGap = Mathf.Max(0f, startTraySideGap);
            startTrayHeight = Mathf.Max(0.01f, startTrayHeight);
            startTrayScale.x = Mathf.Max(0.01f, startTrayScale.x);
            startTrayScale.y = Mathf.Max(0.01f, startTrayScale.y);
            startTrayScale.z = Mathf.Max(0.01f, startTrayScale.z);
        }

        private void OnEnable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.StateChanged += HandleStateChanged;
                gameModeManager.ActivePlayerChanged += HandleActivePlayerChanged;
                gameModeManager.RoundStarted += HandleRoundStarted;
                gameModeManager.BeforePlacementSelectionStarted += HandleBeforePlacementSelectionStarted;
            }

            SubscribePieces(player1Pieces, true);
            SubscribePieces(player2Pieces, true);
        }

        private void OnDisable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.StateChanged -= HandleStateChanged;
                gameModeManager.ActivePlayerChanged -= HandleActivePlayerChanged;
                gameModeManager.RoundStarted -= HandleRoundStarted;
                gameModeManager.BeforePlacementSelectionStarted -= HandleBeforePlacementSelectionStarted;
            }

            StopPendingPhysicsCompletion();
            StopPendingNoPlacementAdvance();
            SubscribePieces(player1Pieces, false);
            SubscribePieces(player2Pieces, false);
            HideAllOrderLabels();
        }

        private void LateUpdate()
        {
            RefreshOrderLabels();
        }

        private void OnDestroy()
        {
            if (orderLabelCanvas != null)
            {
                Destroy(orderLabelCanvas.gameObject);
                orderLabelCanvas = null;
            }
        }

        private void Start()
        {
            RefreshPieceHighlights();

            if (startGameOnPlay
                && gameModeManager != null
                && gameModeManager.CurrentState == FlickDomGameState.NotStarted)
            {
                gameModeManager.StartLocalGame();
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || gameModeManager == null)
            {
                return;
            }

            if (gameModeManager.CurrentState == FlickDomGameState.PieceOrderSelection)
            {
                HandlePieceOrderSelectionInput();
                return;
            }

            if (!CanControlLocalGameState())
            {
                return;
            }

            if (keyboard.cKey.wasPressedThisFrame)
            {
                gameModeManager.CompletePlacementSelection();
            }

            if (keyboard.vKey.wasPressedThisFrame)
            {
                gameModeManager.CompleteCardMatch();
            }

            if (keyboard.bKey.wasPressedThisFrame)
            {
                gameModeManager.FinishRoundAndStartNext();
            }
        }

        private void ConfigurePieces(TurnBasedFlickPiece[] pieces, FlickDomPlayerId owner, string prefix)
        {
            if (pieces == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece == null)
                {
                    continue;
                }

                piece.Configure(owner, prefix + "_" + (i + 1), gameModeManager);
            }
        }

        private void BuildStartTrayVisuals()
        {
            if (!showStartTrays || startTrayPrefab == null)
            {
                return;
            }

            Transform root = new GameObject("Generated Start Trays").transform;
            root.SetParent(transform, false);
            CreateStartTraysForPieces(player1Pieces, root, FlickDomPlayerId.Player1, "P1");
            CreateStartTraysForPieces(player2Pieces, root, FlickDomPlayerId.Player2, "P2");
        }

        private void CreateStartTraysForPieces(
            TurnBasedFlickPiece[] pieces,
            Transform parent,
            FlickDomPlayerId owner,
            string prefix)
        {
            if (pieces == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece == null)
                {
                    continue;
                }

                GameObject trayObject = InstantiateVisualObject(startTrayPrefab, parent);
                if (trayObject == null)
                {
                    continue;
                }

                Vector3 piecePosition = piece.transform.position;
                trayObject.name = prefix + " Start Tray " + (i + 1);
                trayObject.transform.position = GetStartTrayPosition(owner, piecePosition, i, pieces.Length);
                trayObject.transform.rotation = Quaternion.identity;
                trayObject.transform.localScale = Vector3.one;
                RemoveVisualColliders(trayObject);
                FitVisualToSize(trayObject, GetStartTrayTargetSize());
                trayObject.transform.localScale = Vector3.Scale(trayObject.transform.localScale, startTrayScale);
            }
        }

        private Vector3 GetStartTrayPosition(
            FlickDomPlayerId owner,
            Vector3 piecePosition,
            int pieceIndex,
            int pieceCount)
        {
            if (!alignStartTraysToBoardCells || startTrayBoardResolver == null)
            {
                return new Vector3(piecePosition.x, startTrayWorldY, piecePosition.z);
            }

            float traySize = GetStartTrayCellSize();
            Vector3 boardOrigin = startTrayBoardResolver.BoardOrigin;
            Vector3 boardMax = startTrayBoardResolver.BoardMax;
            float z = GetBoardAlignedStartLaneZ(pieceIndex, pieceCount);
            float x = owner == FlickDomPlayerId.Player1
                ? boardOrigin.x - startTraySideGap - traySize * 0.5f
                : boardMax.x + startTraySideGap + traySize * 0.5f;

            return new Vector3(x, startTrayWorldY, z);
        }

        private Vector3 GetStartTrayTargetSize()
        {
            float traySize = GetStartTrayCellSize();
            return new Vector3(traySize, startTrayHeight, traySize);
        }

        private float GetStartTrayCellSize()
        {
            float baseSize = startTrayBoardResolver != null ? startTrayBoardResolver.CellSize : 1f;
            return Mathf.Max(0.01f, baseSize * startTrayCellSizeRatio);
        }

        private void HandlePieceOrderSelectionInput()
        {
            FlickDomPlayerId activePlayer = gameModeManager.ActivePlayer;
            if (activePlayer == FlickDomPlayerId.None)
            {
                return;
            }

            if (!CanProvideInputFor(activePlayer))
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || inputCamera == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (TryFindSelectablePieceUnderPointer(activePlayer, out TurnBasedFlickPiece piece))
            {
                if (TrySubmitNetworkPieceOrderSelection(activePlayer, piece))
                {
                    return;
                }

                SelectPieceForCurrentOrder(activePlayer, piece);
            }
        }

        public bool TrySelectPieceForNetwork(FlickDomPlayerId player, string pieceId)
        {
            if (gameModeManager == null
                || gameModeManager.CurrentState != FlickDomGameState.PieceOrderSelection
                || gameModeManager.ActivePlayer != player
                || string.IsNullOrEmpty(pieceId))
            {
                return false;
            }

            TurnBasedFlickPiece piece = FindPieceById(player, pieceId);
            if (piece == null || IsPieceAlreadyOrdered(player, piece))
            {
                return false;
            }

            SelectPieceForCurrentOrder(player, piece);
            return true;
        }

        public void GetPieceOrderSnapshot(FlickDomPlayerId player, List<string> pieceIds)
        {
            if (pieceIds == null)
            {
                return;
            }

            pieceIds.Clear();
            List<TurnBasedFlickPiece> order = GetOrderForPlayer(player);
            if (order == null)
            {
                return;
            }

            for (int i = 0; i < order.Count; i++)
            {
                TurnBasedFlickPiece piece = order[i];
                if (piece != null && !string.IsNullOrEmpty(piece.PieceId))
                {
                    pieceIds.Add(piece.PieceId);
                }
            }
        }

        public int GetNextOrderIndexSnapshot(FlickDomPlayerId player)
        {
            return Mathf.Max(0, GetNextOrderIndex(player));
        }

        public void ApplyNetworkPieceOrderSnapshot(
            IReadOnlyList<string> player1PieceIds,
            int player1NextIndex,
            IReadOnlyList<string> player2PieceIds,
            int player2NextIndex)
        {
            ApplyNetworkPieceOrderSnapshot(FlickDomPlayerId.Player1, player1PieceIds, player1NextIndex);
            ApplyNetworkPieceOrderSnapshot(FlickDomPlayerId.Player2, player2PieceIds, player2NextIndex);
            NotifyPieceOrderChanged(FlickDomPlayerId.Player1);
            NotifyPieceOrderChanged(FlickDomPlayerId.Player2);
            RefreshPieceHighlights();
            RefreshOrderLabels();
        }

        public bool TryMarkFlickAcceptedFromNetwork(FlickDomPlayerId player, string pieceId)
        {
            if (gameModeManager == null
                || string.IsNullOrEmpty(pieceId))
            {
                return false;
            }

            EnsureDefaultOrderForPlayer(player);
            List<TurnBasedFlickPiece> order = GetOrderForPlayer(player);
            int acceptedIndex = FindOrderIndexByPieceId(order, pieceId);
            if (acceptedIndex < 0)
            {
                TurnBasedFlickPiece currentTarget = GetCurrentFlickTarget(player);
                Debug.LogWarning("[TurnTest] Ignored network flick acceptance for out-of-order piece " + pieceId + ". Current target is " + (currentTarget != null ? currentTarget.PieceId : "none") + ".", this);
                return false;
            }

            int nextIndex = GetNextOrderIndex(player);
            if (acceptedIndex < nextIndex)
            {
                return true;
            }

            SetNextOrderIndex(player, acceptedIndex + 1);
            RefreshPieceHighlights();
            RefreshOrderLabels();
            return true;
        }

        private static bool CanControlLocalGameState()
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            return bootstrap == null || bootstrap.AllowsLocalStateControl();
        }

        private static bool CanProvideInputFor(FlickDomPlayerId player)
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            return bootstrap == null || bootstrap.AllowsLocalInputFor(player);
        }

        private static bool TrySubmitNetworkPieceOrderSelection(FlickDomPlayerId player, TurnBasedFlickPiece piece)
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            if (bootstrap == null || !bootstrap.IsClientOnly || piece == null)
            {
                return false;
            }

            bootstrap.SubmitPieceOrderSelectionToHost(player, piece.PieceId);
            return true;
        }

        private static void NotifyNetworkPieceOrderSelected(FlickDomPlayerId player, TurnBasedFlickPiece piece)
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            if (bootstrap == null || !bootstrap.IsHost || piece == null)
            {
                return;
            }

            bootstrap.NotifyHostPieceOrderSelected(player, piece.PieceId);
        }

        private bool TryFindSelectablePieceUnderPointer(FlickDomPlayerId player, out TurnBasedFlickPiece selectedPiece)
        {
            selectedPiece = null;
            TurnBasedFlickPiece[] pieces = GetPiecesForPlayer(player);
            if (pieces == null)
            {
                return false;
            }

            Ray ray = inputCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            float closestDistance = float.MaxValue;
            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece == null || IsPieceAlreadyOrdered(player, piece))
                {
                    continue;
                }

                if (piece.TryRaycast(ray, pieceSelectionRaycastDistance, out float distance)
                    && distance < closestDistance)
                {
                    closestDistance = distance;
                    selectedPiece = piece;
                }
            }

            return selectedPiece != null;
        }

        private void SelectPieceForCurrentOrder(FlickDomPlayerId player, TurnBasedFlickPiece piece)
        {
            List<TurnBasedFlickPiece> order = GetOrderForPlayer(player);
            if (order == null || piece == null || order.Contains(piece))
            {
                return;
            }

            order.Add(piece);
            PlaySelectSound();
            BlockFlickInputUntilPointerReleased();

            if (logStateChanges)
            {
                Debug.Log("[TurnTest] " + player + " selected " + piece.PieceId + " as flick order " + order.Count + ".", this);
            }

            RefreshPieceHighlights();
            NotifyPieceOrderChanged(player);
            NotifyNetworkPieceOrderSelected(player, piece);

            if (order.Count >= CountPieces(GetPiecesForPlayer(player)))
            {
                gameModeManager.CompleteCurrentPlayerPieceOrderSelection();
                RefreshPieceHighlights();
            }
        }

        private static void PlaySelectSound()
        {
            EnsureSelectAudioSource();
            EnsureSelectSoundClip();
            if (selectAudioSource == null || selectSoundClip == null)
            {
                return;
            }

            selectAudioSource.PlayOneShot(selectSoundClip, SelectSoundVolumeScale);
        }

        private static void PreloadSelectSound()
        {
            EnsureSelectAudioSource();
            EnsureSelectSoundClip();
        }

        private static void EnsureSelectAudioSource()
        {
            if (selectAudioSource != null)
            {
                return;
            }

            GameObject audioObject = GameObject.Find(SelectAudioObjectName);
            if (audioObject == null)
            {
                audioObject = new GameObject(SelectAudioObjectName);
                DontDestroyOnLoad(audioObject);
            }

            if (!audioObject.TryGetComponent(out selectAudioSource))
            {
                selectAudioSource = audioObject.AddComponent<AudioSource>();
            }

            selectAudioSource.playOnAwake = false;
            selectAudioSource.loop = false;
            selectAudioSource.spatialBlend = 0f;
        }

        private static void EnsureSelectSoundClip()
        {
            if (selectSoundClip != null)
            {
                return;
            }

            selectSoundClip = Resources.Load<AudioClip>(SelectSoundResourcePath);
            if (selectSoundClip == null)
            {
                Debug.LogWarning("[Select Audio] Could not load sound at Resources/" + SelectSoundResourcePath + ".", null);
            }
        }

        private void BlockFlickInputUntilPointerReleased()
        {
            BlockFlickInputUntilPointerReleased(player1Pieces);
            BlockFlickInputUntilPointerReleased(player2Pieces);
        }

        private static void BlockFlickInputUntilPointerReleased(TurnBasedFlickPiece[] pieces)
        {
            if (pieces == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i] != null)
                {
                    pieces[i].BlockInputUntilPointerReleased();
                }
            }
        }

        private TurnBasedFlickPiece[] EnsurePieceCount(
            TurnBasedFlickPiece[] pieces,
            string objectNamePrefix,
            FlickDomPlayerId owner)
        {
            TurnBasedFlickPiece template = FindFirstPiece(pieces);
            if (template == null)
            {
                return pieces;
            }

            int targetCount = Mathf.Max(1, targetPiecesPerPlayer);
            TurnBasedFlickPiece[] result = new TurnBasedFlickPiece[targetCount];
            int copyCount = pieces != null ? Mathf.Min(pieces.Length, targetCount) : 0;
            for (int i = 0; i < copyCount; i++)
            {
                result[i] = pieces[i];
            }

            for (int i = 0; i < targetCount; i++)
            {
                if (result[i] != null)
                {
                    continue;
                }

                TurnBasedFlickPiece clone = Instantiate(template, template.transform.parent);
                clone.name = objectNamePrefix + "_" + (i + 1);
                result[i] = clone;
            }

            ArrangePieceStarts(result, owner);
            return result;
        }

        private static void RemoveDuplicatePieceComponents(TurnBasedFlickPiece[] pieces)
        {
            if (pieces == null)
            {
                return;
            }

            HashSet<GameObject> scannedObjects = new HashSet<GameObject>();
            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece == null || !scannedObjects.Add(piece.gameObject))
                {
                    continue;
                }

                TurnBasedFlickPiece[] components = piece.GetComponents<TurnBasedFlickPiece>();
                if (components.Length <= 1)
                {
                    continue;
                }

                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    TurnBasedFlickPiece component = components[componentIndex];
                    if (component != null && component != piece)
                    {
                        Destroy(component);
                    }
                }

                Debug.LogWarning("[TurnTest] Removed duplicate TurnBasedFlickPiece components from " + piece.name + ".", piece);
            }
        }

        private void ArrangePieceStarts(TurnBasedFlickPiece[] pieces, FlickDomPlayerId owner)
        {
            TurnBasedFlickPiece template = FindFirstPiece(pieces);
            if (template == null)
            {
                return;
            }

            Vector3 centerPosition = template.transform.position;
            Quaternion rotation = template.transform.rotation;

            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece == null)
                {
                    continue;
                }

                Vector3 position = GetGeneratedPieceStartPosition(owner, centerPosition, i, pieces.Length);
                piece.SetRoundStartPose(position, rotation);
            }
        }

        private Vector3 GetGeneratedPieceStartPosition(
            FlickDomPlayerId owner,
            Vector3 fallbackCenterPosition,
            int pieceIndex,
            int pieceCount)
        {
            if (!alignGeneratedPieceStartsToBoardCells || startTrayBoardResolver == null)
            {
                float centerIndex = (pieceCount - 1) * 0.5f;
                return fallbackCenterPosition + (Vector3.forward * ((pieceIndex - centerIndex) * generatedPieceSpacing));
            }

            float x = owner == FlickDomPlayerId.Player1
                ? startTrayBoardResolver.BoardOrigin.x - startTraySideGap - GetStartTrayCellSize() * 0.5f
                : startTrayBoardResolver.BoardMax.x + startTraySideGap + GetStartTrayCellSize() * 0.5f;
            return new Vector3(x, fallbackCenterPosition.y, GetBoardAlignedStartLaneZ(pieceIndex, pieceCount));
        }

        private float GetBoardAlignedStartLaneZ(int pieceIndex, int pieceCount)
        {
            if (startTrayBoardResolver == null)
            {
                return 0f;
            }

            int count = Mathf.Max(1, pieceCount);
            int boardSize = Mathf.RoundToInt(
                (startTrayBoardResolver.BoardMax.z - startTrayBoardResolver.BoardOrigin.z)
                / startTrayBoardResolver.CellSize);
            int firstCell = Mathf.Clamp((boardSize - count) / 2, 0, Mathf.Max(0, boardSize - count));
            int cell = Mathf.Clamp(firstCell + pieceIndex, 0, Mathf.Max(0, boardSize - 1));
            return startTrayBoardResolver.BoardOrigin.z + ((cell + 0.5f) * startTrayBoardResolver.CellSize);
        }

        private static void ApplyTokenDataSequence(TurnBasedFlickPiece[] pieces, TokenData[] tokenDataSequence)
        {
            if (pieces == null || tokenDataSequence == null || tokenDataSequence.Length == 0)
            {
                return;
            }

            int sequenceLength = tokenDataSequence.Length;
            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece == null)
                {
                    continue;
                }

                TokenData tokenData = tokenDataSequence[i % sequenceLength];
                if (tokenData == null || !piece.TryGetComponent<TokenSetup>(out TokenSetup tokenSetup))
                {
                    continue;
                }

                tokenSetup.tokenData = tokenData;
                tokenSetup.ApplyTokenData();
            }
        }

        private static void ApplyPieceMaterialOverride(TurnBasedFlickPiece[] pieces, Material material)
        {
            if (pieces == null || material == null)
            {
                return;
            }

            HashSet<GameObject> updatedObjects = new HashSet<GameObject>();
            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece == null || !updatedObjects.Add(piece.gameObject))
                {
                    continue;
                }

                Renderer[] renderers = piece.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    if (renderers[rendererIndex] != null)
                    {
                        renderers[rendererIndex].sharedMaterial = material;
                    }
                }
            }
        }

        private static TurnBasedFlickPiece FindFirstPiece(TurnBasedFlickPiece[] pieces)
        {
            if (pieces == null)
            {
                return null;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i] != null)
                {
                    return pieces[i];
                }
            }

            return null;
        }

        private TurnBasedFlickPiece[] ResolveSceneAuthoredPieces(
            TurnBasedFlickPiece[] configuredPieces,
            Transform[] authoredPieceObjects)
        {
            if (!HasAnyTransform(authoredPieceObjects))
            {
                return configuredPieces;
            }

            TurnBasedFlickPiece[] resolvedPieces = new TurnBasedFlickPiece[authoredPieceObjects.Length];
            for (int i = 0; i < authoredPieceObjects.Length; i++)
            {
                Transform authoredTransform = authoredPieceObjects[i];
                if (authoredTransform == null)
                {
                    Debug.LogError("[TurnTest] A scene-authored piece reference is missing at index " + i + ".", this);
                    continue;
                }

                resolvedPieces[i] = ResolveSceneAuthoredPiece(authoredTransform);
            }

            return resolvedPieces;
        }

        private TurnBasedFlickPiece ResolveSceneAuthoredPiece(Transform authoredTransform)
        {
            if (authoredTransform.TryGetComponent(out TurnBasedFlickPiece existingPiece))
            {
                return existingPiece;
            }

            if (!configureAuthoredPieceComponents)
            {
                Debug.LogError(
                    "[TurnTest] " + authoredTransform.name
                    + " has no TurnBasedFlickPiece. Enable authored-piece component setup or configure the object in the scene.",
                    authoredTransform);
                return null;
            }

            GameObject pieceObject = authoredTransform.gameObject;
            EnsureAuthoredPieceCollider(pieceObject);

            if (!pieceObject.TryGetComponent(out Rigidbody pieceRigidbody))
            {
                pieceRigidbody = pieceObject.AddComponent<Rigidbody>();
                pieceRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                pieceRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                pieceRigidbody.constraints = RigidbodyConstraints.FreezeRotationX
                    | RigidbodyConstraints.FreezeRotationZ;
            }

            if (!pieceObject.TryGetComponent(out TokenSetup _))
            {
                pieceObject.AddComponent<TokenSetup>();
            }

            if (!pieceObject.TryGetComponent(out FlickVisuals _))
            {
                pieceObject.AddComponent<FlickVisuals>();
            }

            return pieceObject.AddComponent<TurnBasedFlickPiece>();
        }

        private static void EnsureAuthoredPieceCollider(GameObject pieceObject)
        {
            if (pieceObject.TryGetComponent(out Collider _))
            {
                return;
            }

            MeshFilter meshFilter = pieceObject.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                MeshCollider meshCollider = pieceObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = true;
                return;
            }

            Renderer pieceRenderer = pieceObject.GetComponentInChildren<Renderer>();
            BoxCollider boxCollider = pieceObject.AddComponent<BoxCollider>();
            if (pieceRenderer == null)
            {
                return;
            }

            Transform pieceTransform = pieceObject.transform;
            boxCollider.center = pieceTransform.InverseTransformPoint(pieceRenderer.bounds.center);

            Vector3 lossyScale = pieceTransform.lossyScale;
            boxCollider.size = new Vector3(
                DivideByNonZeroScale(pieceRenderer.bounds.size.x, lossyScale.x),
                DivideByNonZeroScale(pieceRenderer.bounds.size.y, lossyScale.y),
                DivideByNonZeroScale(pieceRenderer.bounds.size.z, lossyScale.z));
        }

        private static float DivideByNonZeroScale(float value, float scale)
        {
            float absoluteScale = Mathf.Abs(scale);
            return absoluteScale > 0.0001f ? value / absoluteScale : value;
        }

        private bool HasSceneAuthoredPieces()
        {
            return HasAnyTransform(player1PieceObjects) || HasAnyTransform(player2PieceObjects);
        }

        private static bool HasAnyTransform(Transform[] transforms)
        {
            if (transforms == null)
            {
                return false;
            }

            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void SubscribePieces(TurnBasedFlickPiece[] pieces, bool subscribe)
        {
            if (pieces == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece == null)
                {
                    continue;
                }

                if (subscribe)
                {
                    piece.FlickStarted += HandlePieceFlickStarted;
                    piece.SettledAfterFlick += HandlePieceSettled;
                    piece.InvalidatedAfterFlick += HandlePieceInvalidated;
                }
                else
                {
                    piece.FlickStarted -= HandlePieceFlickStarted;
                    piece.SettledAfterFlick -= HandlePieceSettled;
                    piece.InvalidatedAfterFlick -= HandlePieceInvalidated;
                }
            }
        }

        private void HandlePieceFlickStarted(TurnBasedFlickPiece piece)
        {
            if (gameModeManager == null
                || gameModeManager.CurrentState != FlickDomGameState.PlayerFlicking
                || gameModeManager.ActivePlayer != piece.Owner)
            {
                return;
            }

            TurnBasedFlickPiece currentTarget = GetCurrentFlickTarget(piece.Owner);
            if (currentTarget != null && currentTarget != piece)
            {
                if (logStateChanges)
                {
                    Debug.Log("[TurnTest] Ignored out-of-order flick from " + piece.PieceId + ". Current target is " + currentTarget.PieceId + ".", this);
                }

                return;
            }

            if (logStateChanges)
            {
                Debug.Log("[TurnTest] Flick started: " + piece.PieceId, this);
            }

            AdvancePieceOrderIndex(piece.Owner);
            NotifyNetworkFlickAcceptedIfHostLocalPiece(piece);
            gameModeManager.CompleteCurrentPlayerFlicking();
            RefreshPieceHighlights();
        }

        private static void NotifyNetworkFlickAcceptedIfHostLocalPiece(TurnBasedFlickPiece piece)
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            if (bootstrap == null
                || !bootstrap.IsHost
                || piece == null
                || bootstrap.LocalPlayerId != piece.Owner)
            {
                return;
            }

            bootstrap.NotifyHostFlickAccepted(piece.Owner, piece.PieceId);
        }

        private void HandlePieceInvalidated(TurnBasedFlickPiece piece)
        {
            if (gameModeManager == null
                || gameModeManager.CurrentState != FlickDomGameState.PhysicsProcessing)
            {
                return;
            }

            if (logStateChanges)
            {
                Debug.Log("[TurnTest] Piece died: " + piece.PieceId + " left the playable board.", this);
            }

            gameModeManager.RemoveStoppedPieceCandidate(piece.Owner, piece.PieceId);
            BeginPhysicsCompletionAfterLaunchedPiecesSettle();
        }

        private void HandlePieceSettled(TurnBasedFlickPiece piece)
        {
            if (gameModeManager == null
                || gameModeManager.CurrentState != FlickDomGameState.PhysicsProcessing)
            {
                return;
            }

            if (!ShouldRegisterPlacementCandidate(piece))
            {
                gameModeManager.RemoveStoppedPieceCandidate(piece.Owner, piece.PieceId);
                piece.RemoveFromFieldAfterMissedContact();

                if (logStateChanges)
                {
                    Debug.Log("[TurnTest] Piece removed after missing every piece and wall: " + piece.PieceId + ".", this);
                }

                BeginPhysicsCompletionAfterLaunchedPiecesSettle();
                return;
            }

            PiecePlacementCandidate candidate = gameModeManager.RegisterStoppedPieceCandidate(
                piece.Owner,
                piece.PieceId,
                piece.transform.position,
                piece.TokenRadius);

            if (logStateChanges && candidate != null)
            {
                Debug.Log(BuildCandidateLog(candidate, false), this);
            }

            if (tokenMapGridView != null)
            {
                tokenMapGridView.ShowCandidateCells(candidate);
            }

            BeginPhysicsCompletionAfterLaunchedPiecesSettle();
        }

        private void BeginPhysicsCompletionAfterLaunchedPiecesSettle()
        {
            StopPendingPhysicsCompletion();
            physicsCompletionRoutine = StartCoroutine(CompletePhysicsWhenLaunchedPiecesSettle());
        }

        private IEnumerator CompletePhysicsWhenLaunchedPiecesSettle()
        {
            while (gameModeManager != null && gameModeManager.CurrentState == FlickDomGameState.PhysicsProcessing)
            {
                RemoveExitedLaunchedPieces(player1Pieces);
                RemoveExitedLaunchedPieces(player2Pieces);

                if (AreLaunchedPiecesSettled(player1Pieces) && AreLaunchedPiecesSettled(player2Pieces))
                {
                    break;
                }

                yield return waitForFixedUpdate;
            }

            physicsCompletionRoutine = null;
            if (gameModeManager != null && gameModeManager.CurrentState == FlickDomGameState.PhysicsProcessing)
            {
                gameModeManager.CompleteCurrentPlayerPhysics();
            }
        }

        private void StopPendingPhysicsCompletion()
        {
            if (physicsCompletionRoutine == null)
            {
                return;
            }

            StopCoroutine(physicsCompletionRoutine);
            physicsCompletionRoutine = null;
        }

        private void RemoveExitedLaunchedPieces(TurnBasedFlickPiece[] pieces)
        {
            if (pieces == null || gameModeManager == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece == null
                    || piece.IsDead
                    || !piece.HasLaunchedThisRound
                    || !piece.ShouldBeRemovedAfterLeavingPlayableBoard())
                {
                    continue;
                }

                piece.MarkDeadAfterExternalBoardExit();
                gameModeManager.RemoveStoppedPieceCandidate(piece.Owner, piece.PieceId);

                if (logStateChanges)
                {
                    Debug.Log("[TurnTest] Piece died after being pushed out: " + piece.PieceId + ".", this);
                }
            }
        }

        private static bool AreLaunchedPiecesSettled(TurnBasedFlickPiece[] pieces)
        {
            if (pieces == null)
            {
                return true;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece != null
                    && piece.HasLaunchedThisRound
                    && !piece.IsDead
                    && !piece.IsSettledForPlacement())
                {
                    return false;
                }
            }

            return true;
        }

        private void HandleStateChanged(FlickDomGameState previousState, FlickDomGameState nextState)
        {
            if (logStateChanges)
            {
                Debug.Log("[TurnTest] State: " + previousState + " -> " + nextState, this);
            }

            if (nextState == FlickDomGameState.PieceOrderSelection)
            {
                StopPendingNoPlacementAdvance();
                CompleteOrderSelectionIfNoPieces();
            }
            else if (nextState == FlickDomGameState.PlayerFlicking)
            {
                StopPendingNoPlacementAdvance();
                EnsureDefaultOrderForPlayer(gameModeManager.ActivePlayer);
            }
            else if (nextState == FlickDomGameState.CardMatch)
            {
                BeginNoPlacementAdvanceIfNeeded();
            }
            else if (nextState != FlickDomGameState.RoundEnd)
            {
                StopPendingNoPlacementAdvance();
            }

            RefreshPieceHighlights();
            RefreshOrderLabels();
        }

        private void BeginNoPlacementAdvanceIfNeeded()
        {
            if (!autoStartNextRoundWhenNoPlacementCandidates
                || gameModeManager == null
                || gameModeManager.PendingPlacementCandidates.Count > 0
                || !CanControlLocalGameState())
            {
                return;
            }

            StopPendingNoPlacementAdvance();
            noPlacementAdvanceRoutine = StartCoroutine(AdvanceRoundAfterNoPlacementCandidates());
        }

        private IEnumerator AdvanceRoundAfterNoPlacementCandidates()
        {
            yield return null;

            noPlacementAdvanceRoutine = null;
            if (gameModeManager == null
                || gameModeManager.CurrentState != FlickDomGameState.CardMatch
                || gameModeManager.PendingPlacementCandidates.Count > 0
                || !CanControlLocalGameState())
            {
                yield break;
            }

            if (logStateChanges)
            {
                Debug.Log("[TurnTest] No placement candidates remain. Skipping placement and starting next round.", this);
            }

            gameModeManager.CompleteCardMatch();
            if (gameModeManager.CurrentState == FlickDomGameState.RoundEnd)
            {
                gameModeManager.FinishRoundAndStartNext();
            }
        }

        private void StopPendingNoPlacementAdvance()
        {
            if (noPlacementAdvanceRoutine == null)
            {
                return;
            }

            StopCoroutine(noPlacementAdvanceRoutine);
            noPlacementAdvanceRoutine = null;
        }

        private void HandleBeforePlacementSelectionStarted()
        {
            RebuildPendingPlacementCandidatesFromFinalPiecePositions();
        }

        private void HandleActivePlayerChanged(FlickDomPlayerId activePlayer)
        {
            if (logStateChanges)
            {
                Debug.Log("[TurnTest] Active player: " + activePlayer, this);
            }

            if (gameModeManager != null && gameModeManager.CurrentState == FlickDomGameState.PieceOrderSelection)
            {
                CompleteOrderSelectionIfNoPieces();
            }

            RefreshPieceHighlights();
            RefreshOrderLabels();
        }

        private void HandleRoundStarted(int roundNumber, IReadOnlyList<FlickDomPlayerId> turnOrder)
        {
            StopPendingPhysicsCompletion();

            if (tokenMapGridView != null)
            {
                tokenMapGridView.ClearCandidateHighlights();
            }

            ResetPiecesForRound(player1Pieces);
            ResetPiecesForRound(player2Pieces);
            ResetPieceOrderRuntimeData();
            RefreshOrderLabels();

            if (!logStateChanges)
            {
                return;
            }

            logBuilder.Clear();
            logBuilder.Append("[TurnTest] Round ").Append(roundNumber).Append(" order: ");
            for (int i = 0; i < turnOrder.Count; i++)
            {
                if (i > 0)
                {
                    logBuilder.Append(" -> ");
                }

                logBuilder.Append(turnOrder[i]);
            }

            Debug.Log(logBuilder.ToString(), this);
        }

        private void RebuildPendingPlacementCandidatesFromFinalPiecePositions()
        {
            if (gameModeManager == null)
            {
                return;
            }

            gameModeManager.ClearPendingPlacementCandidates();
            if (tokenMapGridView != null)
            {
                tokenMapGridView.ClearCandidateHighlights();
            }

            RegisterFinalPlacementCandidatesInTurnOrder();
        }

        private void RegisterFinalPlacementCandidatesInTurnOrder()
        {
            IReadOnlyList<FlickDomPlayerId> turnOrder = gameModeManager.RoundTurnOrder;
            int player1OrderIndex = 0;
            int player2OrderIndex = 0;

            for (int i = 0; i < turnOrder.Count; i++)
            {
                FlickDomPlayerId owner = turnOrder[i];
                int orderIndex = owner == FlickDomPlayerId.Player2 ? player2OrderIndex++ : player1OrderIndex++;
                RegisterFinalPlacementCandidate(GetOrderedPieceForPlacement(owner, orderIndex));
            }
        }

        private TurnBasedFlickPiece GetOrderedPieceForPlacement(FlickDomPlayerId owner, int orderIndex)
        {
            EnsureDefaultOrderForPlayer(owner);

            List<TurnBasedFlickPiece> order = GetOrderForPlayer(owner);
            if (order != null && orderIndex >= 0 && orderIndex < order.Count)
            {
                return order[orderIndex];
            }

            TurnBasedFlickPiece[] pieces = GetPiecesForPlayer(owner);
            if (pieces == null || orderIndex < 0 || orderIndex >= pieces.Length)
            {
                return null;
            }

            return pieces[orderIndex];
        }

        private void RegisterFinalPlacementCandidate(TurnBasedFlickPiece piece)
        {
            if (piece == null || gameModeManager == null || piece.IsDead || !piece.HasLaunchedThisRound)
            {
                return;
            }

            if (piece.ShouldBeRemovedAfterLeavingPlayableBoard())
            {
                piece.MarkDeadAfterExternalBoardExit();
                return;
            }

            if (!ShouldRegisterPlacementCandidate(piece))
            {
                gameModeManager.RemoveStoppedPieceCandidate(piece.Owner, piece.PieceId);
                piece.RemoveFromFieldAfterMissedContact();
                return;
            }

            PiecePlacementCandidate candidate = gameModeManager.RegisterStoppedPieceCandidate(
                piece.Owner,
                piece.PieceId,
                piece.transform.position,
                piece.TokenRadius);

            if (logStateChanges && candidate != null)
            {
                Debug.Log(BuildCandidateLog(candidate, true), this);
            }
        }

        private bool ShouldRegisterPlacementCandidate(TurnBasedFlickPiece piece)
        {
            if (piece == null)
            {
                return false;
            }

            if (piece.HasRequiredContactForPlacement)
            {
                return true;
            }

            if (logStateChanges)
            {
                Debug.Log("[TurnTest] Ignored placement candidate for " + piece.PieceId + " because it did not touch another piece or wall.", this);
            }

            return false;
        }

        private void RefreshPieceHighlights()
        {
            if (gameModeManager == null)
            {
                SetNeutralHighlights(player1Pieces);
                SetNeutralHighlights(player2Pieces);
                return;
            }

            FlickDomPlayerId activePlayer = gameModeManager.ActivePlayer;
            if (gameModeManager.CurrentState == FlickDomGameState.PieceOrderSelection)
            {
                SetOrderSelectionHighlights(player1Pieces, activePlayer);
                SetOrderSelectionHighlights(player2Pieces, activePlayer);
                return;
            }

            if (gameModeManager.CurrentState == FlickDomGameState.PlayerFlicking
                || gameModeManager.CurrentState == FlickDomGameState.PhysicsProcessing)
            {
                TurnBasedFlickPiece targetPiece = gameModeManager.CurrentState == FlickDomGameState.PlayerFlicking
                    ? GetCurrentFlickTarget(activePlayer)
                    : null;

                SetFlickHighlights(player1Pieces, activePlayer, targetPiece);
                SetFlickHighlights(player2Pieces, activePlayer, targetPiece);
                return;
            }

            SetNeutralHighlights(player1Pieces);
            SetNeutralHighlights(player2Pieces);
        }

        private static void ResetPiecesForRound(TurnBasedFlickPiece[] pieces)
        {
            if (pieces == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i] != null)
                {
                    pieces[i].ResetRoundUse();
                }
            }
        }

        private void SetOrderSelectionHighlights(TurnBasedFlickPiece[] pieces, FlickDomPlayerId activePlayer)
        {
            if (pieces == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece != null)
                {
                    bool isSelectingPlayerPiece = activePlayer != FlickDomPlayerId.None && piece.Owner == activePlayer;
                    piece.SetOrderSelectionHighlight(isSelectingPlayerPiece, GetSelectionOrderNumber(piece.Owner, piece));
                }
            }
        }

        private static void SetFlickHighlights(
            TurnBasedFlickPiece[] pieces,
            FlickDomPlayerId activePlayer,
            TurnBasedFlickPiece targetPiece)
        {
            if (pieces == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece != null)
                {
                    bool isActivePlayerPiece = activePlayer != FlickDomPlayerId.None && piece.Owner == activePlayer;
                    piece.SetFlickTurnHighlight(isActivePlayerPiece, piece == targetPiece);
                }
            }
        }

        private static void SetNeutralHighlights(TurnBasedFlickPiece[] pieces)
        {
            if (pieces == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i] != null)
                {
                    pieces[i].SetTurnHighlight(false);
                }
            }
        }

        private void ResetPieceOrderRuntimeData()
        {
            player1PieceOrder.Clear();
            player2PieceOrder.Clear();
            player1NextOrderIndex = 0;
            player2NextOrderIndex = 0;
            NotifyPieceOrderChanged(FlickDomPlayerId.Player1);
            NotifyPieceOrderChanged(FlickDomPlayerId.Player2);
        }

        private void EnsureDefaultOrderForPlayer(FlickDomPlayerId player)
        {
            List<TurnBasedFlickPiece> order = GetOrderForPlayer(player);
            if (order == null || order.Count > 0)
            {
                return;
            }

            TurnBasedFlickPiece[] pieces = GetPiecesForPlayer(player);
            if (pieces == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i] != null)
                {
                    order.Add(pieces[i]);
                }
            }
        }

        private void CompleteOrderSelectionIfNoPieces()
        {
            if (gameModeManager == null
                || gameModeManager.CurrentState != FlickDomGameState.PieceOrderSelection
                || gameModeManager.ActivePlayer == FlickDomPlayerId.None)
            {
                return;
            }

            if (CountPieces(GetPiecesForPlayer(gameModeManager.ActivePlayer)) <= 0)
            {
                gameModeManager.CompleteCurrentPlayerPieceOrderSelection();
            }
        }

        private TurnBasedFlickPiece GetCurrentFlickTarget(FlickDomPlayerId player)
        {
            EnsureDefaultOrderForPlayer(player);

            List<TurnBasedFlickPiece> order = GetOrderForPlayer(player);
            int orderIndex = GetNextOrderIndex(player);
            if (order == null || orderIndex < 0 || orderIndex >= order.Count)
            {
                return null;
            }

            return order[orderIndex];
        }

        private void AdvancePieceOrderIndex(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                player1NextOrderIndex++;
            }
            else if (player == FlickDomPlayerId.Player2)
            {
                player2NextOrderIndex++;
            }
        }

        private void SetNextOrderIndex(FlickDomPlayerId player, int nextIndex)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                player1NextOrderIndex = Mathf.Max(0, nextIndex);
            }
            else if (player == FlickDomPlayerId.Player2)
            {
                player2NextOrderIndex = Mathf.Max(0, nextIndex);
            }
        }

        private int GetNextOrderIndex(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                return player1NextOrderIndex;
            }

            if (player == FlickDomPlayerId.Player2)
            {
                return player2NextOrderIndex;
            }

            return -1;
        }

        private TurnBasedFlickPiece[] GetPiecesForPlayer(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                return player1Pieces;
            }

            if (player == FlickDomPlayerId.Player2)
            {
                return player2Pieces;
            }

            return null;
        }

        private List<TurnBasedFlickPiece> GetOrderForPlayer(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                return player1PieceOrder;
            }

            if (player == FlickDomPlayerId.Player2)
            {
                return player2PieceOrder;
            }

            return null;
        }

        private void ApplyNetworkPieceOrderSnapshot(FlickDomPlayerId player, IReadOnlyList<string> pieceIds, int nextIndex)
        {
            List<TurnBasedFlickPiece> order = GetOrderForPlayer(player);
            if (order == null)
            {
                return;
            }

            order.Clear();
            if (pieceIds != null)
            {
                for (int i = 0; i < pieceIds.Count; i++)
                {
                    TurnBasedFlickPiece piece = FindPieceById(player, pieceIds[i]);
                    if (piece != null && !order.Contains(piece))
                    {
                        order.Add(piece);
                    }
                }
            }

            SetNextOrderIndex(player, Mathf.Clamp(nextIndex, 0, order.Count));
        }

        private bool IsPieceAlreadyOrdered(FlickDomPlayerId player, TurnBasedFlickPiece piece)
        {
            List<TurnBasedFlickPiece> order = GetOrderForPlayer(player);
            return order != null && piece != null && order.Contains(piece);
        }

        private static int FindOrderIndexByPieceId(List<TurnBasedFlickPiece> order, string pieceId)
        {
            if (order == null || string.IsNullOrEmpty(pieceId))
            {
                return -1;
            }

            for (int i = 0; i < order.Count; i++)
            {
                TurnBasedFlickPiece piece = order[i];
                if (piece != null && string.Equals(piece.PieceId, pieceId, System.StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private TurnBasedFlickPiece FindPieceById(FlickDomPlayerId player, string pieceId)
        {
            TurnBasedFlickPiece[] pieces = GetPiecesForPlayer(player);
            if (pieces == null)
            {
                return null;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece != null && string.Equals(piece.PieceId, pieceId, System.StringComparison.Ordinal))
                {
                    return piece;
                }
            }

            return null;
        }

        private int GetSelectionOrderNumber(FlickDomPlayerId player, TurnBasedFlickPiece piece)
        {
            List<TurnBasedFlickPiece> order = GetOrderForPlayer(player);
            if (order == null || piece == null)
            {
                return 0;
            }

            int index = order.IndexOf(piece);
            return index >= 0 ? index + 1 : 0;
        }

        public int GetSelectedOrderCount(FlickDomPlayerId player)
        {
            List<TurnBasedFlickPiece> order = GetOrderForPlayer(player);
            return order != null ? order.Count : 0;
        }

        private void NotifyPieceOrderChanged(FlickDomPlayerId player)
        {
            PieceOrderChanged?.Invoke(player);
            RefreshOrderLabels();
        }

        private void EnsureOrderLabelUi()
        {
            if (orderLabelCanvas != null)
            {
                return;
            }

            GameObject canvasObject = new GameObject("Generated Piece Order Labels");
            canvasObject.transform.SetParent(transform, false);

            orderLabelCanvas = canvasObject.AddComponent<Canvas>();
            orderLabelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            orderLabelCanvas.sortingOrder = 110;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>().enabled = false;

            for (int i = 0; i < 3; i++)
            {
                orderLabels.Add(CreateOrderLabel("Order Label " + (i + 1)));
            }
        }

        private Text CreateOrderLabel(string objectName)
        {
            GameObject textObject = new GameObject(objectName);
            textObject.transform.SetParent(orderLabelCanvas.transform, false);

            RectTransform rectTransform = textObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = orderLabelSize;

            Text text = textObject.AddComponent<Text>();
            text.font = ResolveOrderLabelFont();
            text.fontSize = orderLabelFontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = string.Empty;

            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = orderOutlineColor;
            outline.effectDistance = orderOutlineDistance;

            textObject.SetActive(false);
            return text;
        }

        private Font ResolveOrderLabelFont()
        {
            if (orderLabelFont != null)
            {
                return orderLabelFont;
            }

            Font dynamicFont = Font.CreateDynamicFontFromOSFont(new[]
            {
                "Malgun Gothic",
                "Segoe UI",
                "Arial Unicode MS",
                "Arial"
            }, orderLabelFontSize);

            return dynamicFont != null
                ? dynamicFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void RefreshOrderLabels()
        {
            if (orderLabelCanvas == null || inputCamera == null)
            {
                return;
            }

            List<TurnBasedFlickPiece> activeOrder = GetVisibleOrderForCurrentTurn();
            if (activeOrder == null || activeOrder.Count <= 0)
            {
                HideAllOrderLabels();
                return;
            }

            Color labelColor = gameModeManager != null && gameModeManager.ActivePlayer == FlickDomPlayerId.Player2
                ? player2OrderColor
                : player1OrderColor;

            int visibleCount = Mathf.Min(activeOrder.Count, orderLabels.Count);
            for (int i = 0; i < visibleCount; i++)
            {
                TurnBasedFlickPiece piece = activeOrder[i];
                Text label = orderLabels[i];
                if (piece == null || label == null)
                {
                    if (label != null)
                    {
                        label.gameObject.SetActive(false);
                    }

                    continue;
                }

                if (piece.IsDead || piece.ShouldBeRemovedAfterLeavingPlayableBoard())
                {
                    label.gameObject.SetActive(false);
                    continue;
                }

                Vector3 screenPoint = inputCamera.WorldToScreenPoint(piece.transform.position + orderLabelWorldOffset);
                if (screenPoint.z <= 0f)
                {
                    label.gameObject.SetActive(false);
                    continue;
                }

                label.gameObject.SetActive(true);
                label.text = (i + 1).ToString();
                label.color = labelColor;
                label.rectTransform.position = screenPoint;
            }

            for (int i = visibleCount; i < orderLabels.Count; i++)
            {
                if (orderLabels[i] != null)
                {
                    orderLabels[i].gameObject.SetActive(false);
                }
            }
        }

        private List<TurnBasedFlickPiece> GetVisibleOrderForCurrentTurn()
        {
            if (gameModeManager == null)
            {
                return null;
            }

            FlickDomPlayerId activePlayer = gameModeManager.ActivePlayer;
            if (activePlayer == FlickDomPlayerId.None)
            {
                return null;
            }

            if (gameModeManager.CurrentState != FlickDomGameState.PieceOrderSelection
                && gameModeManager.CurrentState != FlickDomGameState.PlayerFlicking
                && gameModeManager.CurrentState != FlickDomGameState.PhysicsProcessing)
            {
                return null;
            }

            return GetOrderForPlayer(activePlayer);
        }

        private void HideAllOrderLabels()
        {
            for (int i = 0; i < orderLabels.Count; i++)
            {
                if (orderLabels[i] != null)
                {
                    orderLabels[i].gameObject.SetActive(false);
                }
            }
        }

        private static int CountPieces(TurnBasedFlickPiece[] pieces)
        {
            if (pieces == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static void RemoveVisualColliders(GameObject rootObject)
        {
            Collider[] colliders = rootObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Destroy(colliders[i]);
            }
        }

        private static void FitVisualToSize(GameObject rootObject, Vector3 targetSize)
        {
            Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            Vector3 size = bounds.size;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                return;
            }

            rootObject.transform.localScale = new Vector3(
                targetSize.x / size.x,
                targetSize.y / size.y,
                targetSize.z / size.z);
        }

        private static GameObject InstantiateVisualObject(GameObject prefab, Transform parent)
        {
            Object instance = Instantiate((Object)prefab, parent);
            if (instance is GameObject gameObject)
            {
                return gameObject;
            }

            if (instance is Component component)
            {
                return component.gameObject;
            }

            if (instance != null)
            {
                Destroy(instance);
            }

            return null;
        }

        private string BuildCandidateLog(PiecePlacementCandidate candidate, bool isFinalCandidate)
        {
            logBuilder.Clear();
            logBuilder.Append(isFinalCandidate
                    ? "[TurnTest] Final candidate cells for "
                    : "[TurnTest] Candidate cells for ")
                .Append(candidate.PieceId)
                .Append(" (")
                .Append(candidate.Owner)
                .Append("): ");

            IReadOnlyList<Vector2Int> cells = candidate.CandidateCells;
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0)
                {
                    logBuilder.Append(", ");
                }

                logBuilder.Append(cells[i]);
            }

            return logBuilder.ToString();
        }
    }
}
