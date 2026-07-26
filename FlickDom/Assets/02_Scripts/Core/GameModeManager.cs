using System;
using System.Collections.Generic;
using UnityEngine;


namespace FlickDom.Gameplay
{
    public sealed class GameModeManager : MonoBehaviour
    {
        [Header("Scene References")]
        // 토큰맵을 담당하는 매니저
        [SerializeField] private TokenMapManager tokenMapManager;
        // 알이 멈춰있는 float 월드 좌표를 보고, 그 알이 걸쳐 있는 점령맵 후보 칸들을 계산
        [SerializeField] private GridCellCandidateResolver cellCandidateResolver;

        [Header("Local Two Player Flow")]
         // 게임 시작 시 자동으로 로컬 게임을 시작할지 여부 
        [SerializeField] private bool startLocalGameOnStart = true;
        [SerializeField] private bool autoEnterFlickingAfterReady = true;
        [SerializeField] private FlickDomPlayerId firstPlayer = FlickDomPlayerId.Player1;
        [SerializeField] private int flicksPerPlayerPerRound = 3;
        [SerializeField] private bool selectPieceOrderBeforeFlicking = true;
        // 임시로 라운드가 끝날 때마다 선공을 번갈아 바꿀지
        [SerializeField] private bool alternateFirstPlayerEachRound;

        private readonly List<FlickDomPlayerId> roundTurnOrder = new List<FlickDomPlayerId>(6);
        // 누가 알까기 조작 끝났는지
        private readonly HashSet<FlickDomPlayerId> playersCompletedFlicking = new HashSet<FlickDomPlayerId>();
        // 누가 알까기 물리 시뮬레이션 끝났는지
        private readonly HashSet<FlickDomPlayerId> playersCompletedPhysics = new HashSet<FlickDomPlayerId>();
        private readonly List<PiecePlacementCandidate> pendingPlacementCandidates = new List<PiecePlacementCandidate>();

        private FlickDomPlayerId currentFirstPlayer;
        private int activeTurnIndex;

        public event Action<FlickDomGameState, FlickDomGameState> StateChanged;
        public event Action<FlickDomPlayerId> ActivePlayerChanged;
        public event Action<int, IReadOnlyList<FlickDomPlayerId>> RoundStarted;
        public event Action BeforePlacementSelectionStarted;

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

        public void SetStartLocalGameOnStart(bool enabled)
        {
            startLocalGameOnStart = enabled;
        }

        public void SetSelectPieceOrderBeforeFlicking(bool enabled)
        {
            selectPieceOrderBeforeFlicking = enabled;
        }

        public void ApplyNetworkStateSnapshot(
            FlickDomGameState state,
            FlickDomPlayerId activePlayer,
            int roundNumber)
        {
            RoundNumber = Mathf.Max(0, roundNumber);
            SetActivePlayer(activePlayer);
            SetState(state);
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

        private void OnValidate()
        {
            flicksPerPlayerPerRound = Mathf.Max(1, flicksPerPlayerPerRound);
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

        public void ResetToNotStarted()
        {
            RoundNumber = 0;
            currentFirstPlayer = NormalizePlayer(firstPlayer);
            roundTurnOrder.Clear();
            ClearRoundRuntimeData();
            SetActivePlayer(FlickDomPlayerId.None);
            SetState(FlickDomGameState.NotStarted);

            if (tokenMapManager != null)
            {
                tokenMapManager.ClearMap();
            }
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
                if (selectPieceOrderBeforeFlicking)
                {
                    BeginPieceOrderSelection();
                }
                else
                {
                    BeginCurrentPlayerFlicking();
                }
            }
        }

        public bool BeginPieceOrderSelection()
        {
            if (CurrentState != FlickDomGameState.Ready)
            {
                return false;
            }

            SetActivePlayer(currentFirstPlayer);
            SetState(FlickDomGameState.PieceOrderSelection);
            return true;
        }

        public bool CompleteCurrentPlayerPieceOrderSelection()
        {
            if (CurrentState != FlickDomGameState.PieceOrderSelection
                || ActivePlayer == FlickDomPlayerId.None)
            {
                return false;
            }

            if (ActivePlayer == currentFirstPlayer)
            {
                SetActivePlayer(GetOtherPlayer(currentFirstPlayer));
                return true;
            }

            SetActivePlayer(FlickDomPlayerId.None);
            SetState(FlickDomGameState.Ready);

            if (autoEnterFlickingAfterReady)
            {
                BeginCurrentPlayerFlicking();
            }

            return true;
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
            BeforePlacementSelectionStarted?.Invoke();
            SetState(pendingPlacementCandidates.Count > 0
                ? FlickDomGameState.PlacementSelection
                : FlickDomGameState.CardMatch);
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

        public bool ForceFinishRoundAndStartNext()
        {
            if (CurrentState == FlickDomGameState.NotStarted)
            {
                return false;
            }

            if (CurrentState != FlickDomGameState.RoundEnd)
            {
                SetActivePlayer(FlickDomPlayerId.None);
                SetState(FlickDomGameState.RoundEnd);
            }

            return FinishRoundAndStartNext();
        }

        public void SetFirstPlayerForNextRound(FlickDomPlayerId player)
        {
            currentFirstPlayer = NormalizePlayer(player);
        }

        public bool IsWorldPositionInsideFlickBoard(Vector3 worldPosition)
        {
            if (cellCandidateResolver == null)
            {
                return true;
            }

            return cellCandidateResolver.IsWorldPositionInsideBoard(worldPosition);
        }

        public PiecePlacementCandidate RegisterStoppedPieceCandidate(
            FlickDomPlayerId player,
            string pieceId,
            Vector3 worldPosition,
            float tokenRadius)
        {
            if (player == FlickDomPlayerId.None || string.IsNullOrEmpty(pieceId))
            {
                return null;
            }

            FlickDomPlayerId normalizedPlayer = NormalizePlayer(player);
            RemovePendingPlacementCandidate(normalizedPlayer, pieceId);

            if (cellCandidateResolver == null)
            {
                Debug.LogError("GridCellCandidateResolver is required before registering stopped pieces.", this);
                return null;
            }

            PiecePlacementCandidate candidate = cellCandidateResolver.ResolveCandidate(
                normalizedPlayer,
                pieceId,
                worldPosition,
                tokenRadius);

            if (candidate.CandidateCells.Count <= 0)
            {
                Debug.Log("[GameMode] Ignored stopped piece with no valid placement cells: " + pieceId, this);
                return null;
            }

            pendingPlacementCandidates.Add(candidate);
            return candidate;
        }

        public void ClearPendingPlacementCandidates()
        {
            pendingPlacementCandidates.Clear();
        }

        public void ApplyNetworkPlacementCandidates(IReadOnlyList<PiecePlacementCandidate> candidates)
        {
            pendingPlacementCandidates.Clear();
            if (candidates == null)
            {
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] != null)
                {
                    pendingPlacementCandidates.Add(candidates[i]);
                }
            }
        }

        public bool RemoveStoppedPieceCandidate(FlickDomPlayerId player, string pieceId)
        {
            if (player == FlickDomPlayerId.None || string.IsNullOrEmpty(pieceId))
            {
                return false;
            }

            return RemovePendingPlacementCandidate(NormalizePlayer(player), pieceId);
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

        private bool RemovePendingPlacementCandidate(FlickDomPlayerId player, string pieceId)
        {
            bool removed = false;
            for (int i = pendingPlacementCandidates.Count - 1; i >= 0; i--)
            {
                PiecePlacementCandidate candidate = pendingPlacementCandidates[i];
                if (candidate != null
                    && candidate.Owner == player
                    && string.Equals(candidate.PieceId, pieceId, StringComparison.Ordinal))
                {
                    pendingPlacementCandidates.RemoveAt(i);
                    removed = true;
                }
            }

            return removed;
        }

        private void BuildRoundTurnOrder(FlickDomPlayerId startingPlayer)
        {
            FlickDomPlayerId normalizedStarter = NormalizePlayer(startingPlayer);
            FlickDomPlayerId otherPlayer = GetOtherPlayer(normalizedStarter);
            roundTurnOrder.Clear();

            for (int i = 0; i < flicksPerPlayerPerRound; i++)
            {
                roundTurnOrder.Add(normalizedStarter);
                roundTurnOrder.Add(otherPlayer);
            }
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
