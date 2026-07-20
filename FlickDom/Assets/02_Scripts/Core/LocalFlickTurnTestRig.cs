using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlickDom.Gameplay
{
    public sealed class LocalFlickTurnTestRig : MonoBehaviour
    {
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private TokenMapGridView tokenMapGridView;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private TurnBasedFlickPiece[] player1Pieces;
        [SerializeField] private TurnBasedFlickPiece[] player2Pieces;
        [SerializeField] private bool startGameOnPlay = true;
        [SerializeField] private bool autoCreateMissingPieces = true;
        [SerializeField] private int targetPiecesPerPlayer = 3;
        [SerializeField] private float generatedPieceSpacing = 1.1f;
        [SerializeField] private float pieceSelectionRaycastDistance = 1000f;
        [SerializeField] private bool logStateChanges = true;

        private readonly StringBuilder logBuilder = new StringBuilder(256);
        private readonly List<TurnBasedFlickPiece> player1PieceOrder = new List<TurnBasedFlickPiece>(3);
        private readonly List<TurnBasedFlickPiece> player2PieceOrder = new List<TurnBasedFlickPiece>(3);
        private int player1NextOrderIndex;
        private int player2NextOrderIndex;

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

            if (autoCreateMissingPieces)
            {
                player1Pieces = EnsurePieceCount(player1Pieces, "Player1");
                player2Pieces = EnsurePieceCount(player2Pieces, "Player2");
            }

            ConfigurePieces(player1Pieces, FlickDomPlayerId.Player1, "P1");
            ConfigurePieces(player2Pieces, FlickDomPlayerId.Player2, "P2");
        }

        private void OnValidate()
        {
            targetPiecesPerPlayer = Mathf.Max(1, targetPiecesPerPlayer);
            generatedPieceSpacing = Mathf.Max(0.1f, generatedPieceSpacing);
            pieceSelectionRaycastDistance = Mathf.Max(1f, pieceSelectionRaycastDistance);
        }

        private void OnEnable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.StateChanged += HandleStateChanged;
                gameModeManager.ActivePlayerChanged += HandleActivePlayerChanged;
                gameModeManager.RoundStarted += HandleRoundStarted;
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
            }

            SubscribePieces(player1Pieces, false);
            SubscribePieces(player2Pieces, false);
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

        private void HandlePieceOrderSelectionInput()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || inputCamera == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            FlickDomPlayerId activePlayer = gameModeManager.ActivePlayer;
            if (activePlayer == FlickDomPlayerId.None)
            {
                return;
            }

            if (TryFindSelectablePieceUnderPointer(activePlayer, out TurnBasedFlickPiece piece))
            {
                SelectPieceForCurrentOrder(activePlayer, piece);
            }
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
            BlockFlickInputUntilPointerReleased();

            if (logStateChanges)
            {
                Debug.Log("[TurnTest] " + player + " selected " + piece.PieceId + " as flick order " + order.Count + ".", this);
            }

            RefreshPieceHighlights();

            if (order.Count >= CountPieces(GetPiecesForPlayer(player)))
            {
                gameModeManager.CompleteCurrentPlayerPieceOrderSelection();
                RefreshPieceHighlights();
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

        private TurnBasedFlickPiece[] EnsurePieceCount(TurnBasedFlickPiece[] pieces, string objectNamePrefix)
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

            ArrangePieceStarts(result);
            return result;
        }

        private void ArrangePieceStarts(TurnBasedFlickPiece[] pieces)
        {
            TurnBasedFlickPiece template = FindFirstPiece(pieces);
            if (template == null)
            {
                return;
            }

            Vector3 centerPosition = template.transform.position;
            Quaternion rotation = template.transform.rotation;
            float centerIndex = (pieces.Length - 1) * 0.5f;

            for (int i = 0; i < pieces.Length; i++)
            {
                TurnBasedFlickPiece piece = pieces[i];
                if (piece == null)
                {
                    continue;
                }

                Vector3 position = centerPosition + (Vector3.forward * ((i - centerIndex) * generatedPieceSpacing));
                piece.SetRoundStartPose(position, rotation);
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
            gameModeManager.CompleteCurrentPlayerFlicking();
            RefreshPieceHighlights();
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

            gameModeManager.CompleteCurrentPlayerPhysics();
        }

        private void HandlePieceSettled(TurnBasedFlickPiece piece)
        {
            if (gameModeManager == null
                || gameModeManager.CurrentState != FlickDomGameState.PhysicsProcessing)
            {
                return;
            }

            PiecePlacementCandidate candidate = gameModeManager.RegisterStoppedPieceCandidate(
                piece.Owner,
                piece.PieceId,
                piece.transform.position,
                piece.TokenRadius);

            if (logStateChanges && candidate != null)
            {
                Debug.Log(BuildCandidateLog(candidate), this);
            }

            if (tokenMapGridView != null)
            {
                tokenMapGridView.ShowCandidateCells(candidate);
            }

            gameModeManager.CompleteCurrentPlayerPhysics();
        }

        private void HandleStateChanged(FlickDomGameState previousState, FlickDomGameState nextState)
        {
            if (logStateChanges)
            {
                Debug.Log("[TurnTest] State: " + previousState + " -> " + nextState, this);
            }

            if (nextState == FlickDomGameState.PieceOrderSelection)
            {
                CompleteOrderSelectionIfNoPieces();
            }
            else if (nextState == FlickDomGameState.PlayerFlicking)
            {
                EnsureDefaultOrderForPlayer(gameModeManager.ActivePlayer);
            }

            RefreshPieceHighlights();
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
        }

        private void HandleRoundStarted(int roundNumber, IReadOnlyList<FlickDomPlayerId> turnOrder)
        {
            if (tokenMapGridView != null)
            {
                tokenMapGridView.ClearCandidateHighlights();
            }

            ResetPiecesForRound(player1Pieces);
            ResetPiecesForRound(player2Pieces);
            ResetPieceOrderRuntimeData();

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
                    piece.SetOrderSelectionHighlight(isSelectingPlayerPiece, IsPieceAlreadyOrdered(activePlayer, piece));
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
                    pieces[i].SetFlickTurnHighlight(false, false);
                }
            }
        }

        private void ResetPieceOrderRuntimeData()
        {
            player1PieceOrder.Clear();
            player2PieceOrder.Clear();
            player1NextOrderIndex = 0;
            player2NextOrderIndex = 0;
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

        private bool IsPieceAlreadyOrdered(FlickDomPlayerId player, TurnBasedFlickPiece piece)
        {
            List<TurnBasedFlickPiece> order = GetOrderForPlayer(player);
            return order != null && piece != null && order.Contains(piece);
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

        private string BuildCandidateLog(PiecePlacementCandidate candidate)
        {
            logBuilder.Clear();
            logBuilder.Append("[TurnTest] Candidate cells for ")
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
