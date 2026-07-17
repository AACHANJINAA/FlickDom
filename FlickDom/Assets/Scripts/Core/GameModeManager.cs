using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlickDom.Gameplay
{
    public sealed class GameModeManager : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private TokenMapManager tokenMapManager;
        [SerializeField] private GridCellCandidateResolver cellCandidateResolver;

        [Header("Local Two Player Flow")]
        [SerializeField] private bool startLocalGameOnStart = true;
        [SerializeField] private bool autoEnterFlickingAfterReady = true;
        [SerializeField] private FlickDomPlayerId firstPlayer = FlickDomPlayerId.Player1;
        [SerializeField] private bool alternateFirstPlayerEachRound;

        private readonly List<FlickDomPlayerId> roundTurnOrder = new List<FlickDomPlayerId>(2);
        private readonly HashSet<FlickDomPlayerId> playersCompletedFlicking = new HashSet<FlickDomPlayerId>();
        private readonly HashSet<FlickDomPlayerId> playersCompletedPhysics = new HashSet<FlickDomPlayerId>();
        private readonly List<PiecePlacementCandidate> pendingPlacementCandidates = new List<PiecePlacementCandidate>();

        private FlickDomPlayerId currentFirstPlayer;
        private int activeTurnIndex;

        public event Action<FlickDomGameState, FlickDomGameState> StateChanged;
        public event Action<FlickDomPlayerId> ActivePlayerChanged;
        public event Action<int, IReadOnlyList<FlickDomPlayerId>> RoundStarted;

        public FlickDomGameState CurrentState { get; private set; } = FlickDomGameState.NotStarted;
        public FlickDomPlayerId ActivePlayer { get; private set; } = FlickDomPlayerId.None;
        public int RoundNumber { get; private set; }

        public IReadOnlyList<FlickDomPlayerId> RoundTurnOrder
        {
            get { return roundTurnOrder; }
        }

        public IReadOnlyList<PiecePlacementCandidate> PendingPlacementCandidates
        {
            get { return pendingPlacementCandidates; }
        }

        private void Awake()
        {
            if (tokenMapManager == null)
            {
                tokenMapManager = FindAnyObjectByType<TokenMapManager>();
            }

            if (cellCandidateResolver == null)
            {
                cellCandidateResolver = FindAnyObjectByType<GridCellCandidateResolver>();
            }
        }

        private void Start()
        {
            if (startLocalGameOnStart && CurrentState == FlickDomGameState.NotStarted)
            {
                StartLocalGame();
            }
        }

        public void StartLocalGame()
        {
            RoundNumber = 0;
            currentFirstPlayer = NormalizePlayer(firstPlayer);
            if (tokenMapManager != null)
            {
                tokenMapManager.ClearMap();
            }

            BeginNextRound();
        }

        public void BeginNextRound()
        {
            RoundNumber++;
            ClearRoundRuntimeData();
            BuildRoundTurnOrder(currentFirstPlayer);

            SetActivePlayer(FlickDomPlayerId.None);
            SetState(FlickDomGameState.Ready);
            RoundStarted?.Invoke(RoundNumber, roundTurnOrder.AsReadOnly());

            if (autoEnterFlickingAfterReady)
            {
                BeginCurrentPlayerFlicking();
            }
        }

        public bool BeginCurrentPlayerFlicking()
        {
            if (CurrentState != FlickDomGameState.Ready && CurrentState != FlickDomGameState.PhysicsProcessing)
            {
                return false;
            }

            if (activeTurnIndex < 0 || activeTurnIndex >= roundTurnOrder.Count)
            {
                return false;
            }

            SetActivePlayer(roundTurnOrder[activeTurnIndex]);
            SetState(FlickDomGameState.PlayerFlicking);
            return true;
        }

        public bool CompleteCurrentPlayerFlicking()
        {
            if (CurrentState != FlickDomGameState.PlayerFlicking || ActivePlayer == FlickDomPlayerId.None)
            {
                return false;
            }

            playersCompletedFlicking.Add(ActivePlayer);
            SetState(FlickDomGameState.PhysicsProcessing);
            return true;
        }

        public bool CompleteCurrentPlayerPhysics()
        {
            if (CurrentState != FlickDomGameState.PhysicsProcessing || ActivePlayer == FlickDomPlayerId.None)
            {
                return false;
            }

            playersCompletedPhysics.Add(ActivePlayer);

            if (activeTurnIndex + 1 < roundTurnOrder.Count)
            {
                activeTurnIndex++;
                return BeginCurrentPlayerFlicking();
            }

            SetActivePlayer(FlickDomPlayerId.None);
            SetState(FlickDomGameState.PlacementSelection);
            return true;
        }

        public bool CompletePlacementSelection()
        {
            if (CurrentState != FlickDomGameState.PlacementSelection)
            {
                return false;
            }

            SetState(FlickDomGameState.CardMatch);
            return true;
        }

        public bool CompleteCardMatch()
        {
            if (CurrentState != FlickDomGameState.CardMatch)
            {
                return false;
            }

            SetState(FlickDomGameState.RoundEnd);
            return true;
        }

        public bool FinishRoundAndStartNext()
        {
            if (CurrentState != FlickDomGameState.RoundEnd)
            {
                return false;
            }

            if (alternateFirstPlayerEachRound)
            {
                currentFirstPlayer = GetOtherPlayer(currentFirstPlayer);
            }

            BeginNextRound();
            return true;
        }

        public void SetFirstPlayerForNextRound(FlickDomPlayerId player)
        {
            currentFirstPlayer = NormalizePlayer(player);
        }

        public PiecePlacementCandidate RegisterStoppedPieceCandidate(
            FlickDomPlayerId player,
            string pieceId,
            Vector3 worldPosition,
            float tokenRadius)
        {
            if (cellCandidateResolver == null)
            {
                Debug.LogError("GridCellCandidateResolver is required before registering stopped pieces.", this);
                return null;
            }

            PiecePlacementCandidate candidate = cellCandidateResolver.ResolveCandidate(
                NormalizePlayer(player),
                pieceId,
                worldPosition,
                tokenRadius);

            pendingPlacementCandidates.Add(candidate);
            return candidate;
        }

        public PiecePlacementCandidate RegisterStoppedPieceCandidate(
            FlickDomPlayerId player,
            Transform pieceTransform,
            float tokenRadius)
        {
            if (pieceTransform == null)
            {
                return null;
            }

            return RegisterStoppedPieceCandidate(player, pieceTransform.name, pieceTransform.position, tokenRadius);
        }

        public List<PiecePlacementCandidate> GetPlacementCandidatesForPlayer(FlickDomPlayerId player)
        {
            List<PiecePlacementCandidate> result = new List<PiecePlacementCandidate>();
            for (int i = 0; i < pendingPlacementCandidates.Count; i++)
            {
                if (pendingPlacementCandidates[i].Owner == player)
                {
                    result.Add(pendingPlacementCandidates[i]);
                }
            }

            return result;
        }

        public bool TryApplyCandidatePlacement(
            PiecePlacementCandidate candidate,
            Vector2Int chosenCell,
            Vector2Int? relocationSource,
            out TokenPlacementResult result)
        {
            if (candidate == null)
            {
                result = new TokenPlacementResult(TokenPlacementStatus.InvalidCell, FlickDomPlayerId.None, chosenCell);
                return false;
            }

            if (CurrentState != FlickDomGameState.PlacementSelection)
            {
                result = new TokenPlacementResult(TokenPlacementStatus.InvalidCell, candidate.Owner, chosenCell);
                return false;
            }

            if (!candidate.ContainsCell(chosenCell))
            {
                result = new TokenPlacementResult(TokenPlacementStatus.InvalidCell, candidate.Owner, chosenCell);
                return false;
            }

            if (tokenMapManager == null)
            {
                Debug.LogError("TokenMapManager is required before applying placement choices.", this);
                result = new TokenPlacementResult(TokenPlacementStatus.InvalidCell, candidate.Owner, chosenCell);
                return false;
            }

            return tokenMapManager.TryClaimCell(candidate.Owner, chosenCell, relocationSource, out result);
        }

        private void ClearRoundRuntimeData()
        {
            activeTurnIndex = 0;
            playersCompletedFlicking.Clear();
            playersCompletedPhysics.Clear();
            pendingPlacementCandidates.Clear();
        }

        private void BuildRoundTurnOrder(FlickDomPlayerId startingPlayer)
        {
            FlickDomPlayerId normalizedStarter = NormalizePlayer(startingPlayer);
            roundTurnOrder.Clear();
            roundTurnOrder.Add(normalizedStarter);
            roundTurnOrder.Add(GetOtherPlayer(normalizedStarter));
        }

        private void SetState(FlickDomGameState nextState)
        {
            if (CurrentState == nextState)
            {
                return;
            }

            FlickDomGameState previousState = CurrentState;
            CurrentState = nextState;
            StateChanged?.Invoke(previousState, nextState);
        }

        private void SetActivePlayer(FlickDomPlayerId player)
        {
            FlickDomPlayerId normalizedPlayer = player == FlickDomPlayerId.None ? FlickDomPlayerId.None : NormalizePlayer(player);
            if (ActivePlayer == normalizedPlayer)
            {
                return;
            }

            ActivePlayer = normalizedPlayer;
            ActivePlayerChanged?.Invoke(ActivePlayer);
        }

        private static FlickDomPlayerId NormalizePlayer(FlickDomPlayerId player)
        {
            return player == FlickDomPlayerId.Player2 ? FlickDomPlayerId.Player2 : FlickDomPlayerId.Player1;
        }

        private static FlickDomPlayerId GetOtherPlayer(FlickDomPlayerId player)
        {
            return NormalizePlayer(player) == FlickDomPlayerId.Player1
                ? FlickDomPlayerId.Player2
                : FlickDomPlayerId.Player1;
        }
    }
}
