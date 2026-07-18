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
        [SerializeField] private TurnBasedFlickPiece[] player1Pieces;
        [SerializeField] private TurnBasedFlickPiece[] player2Pieces;
        [SerializeField] private bool startGameOnPlay = true;
        [SerializeField] private bool logStateChanges = true;

        private readonly StringBuilder logBuilder = new StringBuilder(256);

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

            ConfigurePieces(player1Pieces, FlickDomPlayerId.Player1, "P1");
            ConfigurePieces(player2Pieces, FlickDomPlayerId.Player2, "P2");
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
                }
                else
                {
                    piece.FlickStarted -= HandlePieceFlickStarted;
                    piece.SettledAfterFlick -= HandlePieceSettled;
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

            if (logStateChanges)
            {
                Debug.Log("[TurnTest] Flick started: " + piece.PieceId, this);
            }

            gameModeManager.CompleteCurrentPlayerFlicking();
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

            RefreshPieceHighlights();
        }

        private void HandleActivePlayerChanged(FlickDomPlayerId activePlayer)
        {
            if (logStateChanges)
            {
                Debug.Log("[TurnTest] Active player: " + activePlayer, this);
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
            FlickDomPlayerId activePlayer = gameModeManager != null
                ? gameModeManager.ActivePlayer
                : FlickDomPlayerId.None;

            SetHighlights(player1Pieces, activePlayer == FlickDomPlayerId.Player1);
            SetHighlights(player2Pieces, activePlayer == FlickDomPlayerId.Player2);
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

        private static void SetHighlights(TurnBasedFlickPiece[] pieces, bool isActive)
        {
            if (pieces == null)
            {
                return;
            }

            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i] != null)
                {
                    pieces[i].SetTurnHighlight(isActive);
                }
            }
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
