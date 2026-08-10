using System;
using System.Collections.Generic;
using FlickDom.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlickDom.Gameplay
{
    public sealed class PatternCardManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private TokenMapManager tokenMapManager;

        private const int StageCount = 3;
        private const int CardsPerStage = 3;
        private const float NetworkCardPresentationDuplicateWindowSeconds = 1.5f;
        private const float PendingNetworkCardSnapshotWindowSeconds = 3f;

        [Header("Stage Cards")]
        [SerializeField] private PatternCardData activeCard;
        [SerializeField] private PatternCardData[] cardDeck;
        [InspectorName("Auto Create Random Stage Cards")]
        [SerializeField] private bool autoCreateEasyFallbackCard = true;
        [InspectorName("Advance Stage On Cards Exhausted")]
        [SerializeField] private bool advanceFallbackDeckOnExhaustion = true;
        [InspectorName("Clear Token Map On Stage Advance")]
        [SerializeField] private bool clearTokenMapOnFallbackDeckAdvance = true;
        [SerializeField] private bool finishRoundOnCardsExhausted = true;
        [SerializeField] private bool allowRotatedMatches = true;
        [SerializeField] private bool matchAnywhereOnBoard = true;
        [SerializeField] private bool resetScoresWhenMapCleared = true;
        [SerializeField] private bool logCardClaims = true;
        [SerializeField] private int winningScore = 10;

        private PatternCardData[][] runtimeFallbackDecks = new PatternCardData[0][];
        private int currentFallbackDeckIndex;
        private int cardDrawSeed;
        private PatternCardData[] runtimeCards = new PatternCardData[0];
        private bool[] claimedCards = new bool[0];
        private FlickDomPlayerId lastChangedOwner = FlickDomPlayerId.None;
        private int player1Score;
        private int player2Score;
        private bool isClearingMapForCardRoundChange;
        private FlickDomPlayerId winner = FlickDomPlayerId.None;
        private FlickDomPlayerId pendingNetworkScorePlayer = FlickDomPlayerId.None;
        private int pendingNetworkScoreGain;
        private PatternCardData pendingNetworkClaimedCard;
        private float pendingNetworkClaimedCardAt = -999f;
        private string lastNetworkPresentedCardId = string.Empty;
        private FlickDomPlayerId lastNetworkPresentedPlayer = FlickDomPlayerId.None;
        private int lastNetworkPresentedDeckIndex = -1;
        private int lastNetworkPresentedDrawSeed;
        private float lastNetworkPresentedAt = -999f;

        public event Action<PatternCardData> ActiveCardChanged;
        public event Action<FlickDomPlayerId, int, int, int> ScoreChanged;
        public event Action<PatternCardData, FlickDomPlayerId, int, Vector2Int> CardCompleted;
        public event Action CardsExhausted;
        public event Action<FlickDomPlayerId, int, int> MatchWon;

        public PatternCardData ActiveCard
        {
            get { return GetFirstRemainingCard(); }
        }

        public bool IsActiveCardClaimed
        {
            get { return ActiveCard == null; }
        }

        public int RemainingCardCount
        {
            get { return CountRemainingCards(); }
        }

        public int Player1Score
        {
            get { return player1Score; }
        }

        public int Player2Score
        {
            get { return player2Score; }
        }

        public int WinningScore
        {
            get { return winningScore; }
        }

        public FlickDomPlayerId Winner
        {
            get { return winner; }
        }

        public int CurrentFallbackDeckIndex
        {
            get { return currentFallbackDeckIndex; }
        }

        public int CurrentStageNumber
        {
            get { return currentFallbackDeckIndex + 1; }
        }

        public int CardDrawSeed
        {
            get { return cardDrawSeed; }
        }

        private void Awake()
        {
            if (gameModeManager == null)
            {
                gameModeManager = GetComponent<GameModeManager>();
            }

            if (tokenMapManager == null)
            {
                tokenMapManager = GetComponent<TokenMapManager>();
            }

            EnsureRuntimeFallbackDecks();
            RefreshRuntimeCards();
        }

        private void OnValidate()
        {
            winningScore = Mathf.Max(1, winningScore);
        }

        private void OnEnable()
        {
            if (tokenMapManager != null)
            {
                tokenMapManager.CellOwnerChanged += HandleCellOwnerChanged;
                tokenMapManager.MapChanged += HandleMapChanged;
                tokenMapManager.MapCleared += HandleMapCleared;
            }

            if (gameModeManager != null)
            {
                gameModeManager.StateChanged += HandleStateChanged;
            }
        }

        private void Start()
        {
            ActiveCardChanged?.Invoke(ActiveCard);
            EvaluateAllPlayers();
        }

        private void OnDisable()
        {
            if (tokenMapManager != null)
            {
                tokenMapManager.CellOwnerChanged -= HandleCellOwnerChanged;
                tokenMapManager.MapChanged -= HandleMapChanged;
                tokenMapManager.MapCleared -= HandleMapCleared;
            }

            if (gameModeManager != null)
            {
                gameModeManager.StateChanged -= HandleStateChanged;
            }
        }

        private void OnDestroy()
        {
            ReleaseRuntimeFallbackDecks();
        }

        public void SetActiveCard(PatternCardData nextCard)
        {
            activeCard = nextCard;
            cardDeck = null;
            RefreshRuntimeCards();
            ActiveCardChanged?.Invoke(ActiveCard);
            EvaluateAllPlayers();
        }

        public PatternCardData GetRemainingCard(int index)
        {
            if (index < 0)
            {
                return null;
            }

            int remainingIndex = 0;
            for (int i = 0; i < runtimeCards.Length; i++)
            {
                if (!IsCardAvailable(i))
                {
                    continue;
                }

                if (remainingIndex == index)
                {
                    return runtimeCards[i];
                }

                remainingIndex++;
            }

            return null;
        }

        public bool IsCardClaimed(PatternCardData card)
        {
            if (card == null || runtimeCards == null || claimedCards == null)
            {
                return false;
            }

            for (int i = 0; i < runtimeCards.Length && i < claimedCards.Length; i++)
            {
                PatternCardData runtimeCard = runtimeCards[i];
                if (runtimeCard == null)
                {
                    continue;
                }

                bool sameCard = ReferenceEquals(runtimeCard, card)
                    || string.Equals(runtimeCard.CardId, card.CardId, StringComparison.Ordinal);
                if (sameCard)
                {
                    return claimedCards[i];
                }
            }

            return false;
        }

        public int GetScore(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                return player1Score;
            }

            if (player == FlickDomPlayerId.Player2)
            {
                return player2Score;
            }

            return 0;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !CanControlScoreState())
            {
                return;
            }

            if (keyboard.f10Key.wasPressedThisFrame)
            {
                ForceDebugWinForPlayer1();
            }
        }

        private void HandleCellOwnerChanged(Vector2Int cell, FlickDomPlayerId previousOwner, FlickDomPlayerId nextOwner)
        {
            if (nextOwner != FlickDomPlayerId.None)
            {
                lastChangedOwner = nextOwner;
            }
        }

        private void HandleMapChanged()
        {
            if (isClearingMapForCardRoundChange)
            {
                return;
            }

            if (winner != FlickDomPlayerId.None)
            {
                return;
            }

            if (!CanScoreNow())
            {
                return;
            }

            if (lastChangedOwner != FlickDomPlayerId.None && TryClaimMatchingCards(lastChangedOwner))
            {
                lastChangedOwner = FlickDomPlayerId.None;
                return;
            }

            lastChangedOwner = FlickDomPlayerId.None;
            EvaluateAllPlayers();
        }

        private void HandleMapCleared()
        {
            lastChangedOwner = FlickDomPlayerId.None;

            if (isClearingMapForCardRoundChange)
            {
                return;
            }

            if (resetScoresWhenMapCleared)
            {
                ResetCardProgress();
                player1Score = 0;
                player2Score = 0;
                winner = FlickDomPlayerId.None;
                ScoreChanged?.Invoke(FlickDomPlayerId.None, 0, player1Score, player2Score);
            }
            else
            {
                ResetClaimedCards();
            }

            ActiveCardChanged?.Invoke(ActiveCard);
        }

        private void HandleStateChanged(FlickDomGameState previousState, FlickDomGameState nextState)
        {
            if (nextState == FlickDomGameState.PlacementSelection
                || nextState == FlickDomGameState.CardMatch)
            {
                EvaluateAllPlayers();
            }
        }

        private void EvaluateAllPlayers()
        {
            if (!CanScoreNow() || RemainingCardCount <= 0)
            {
                return;
            }

            if (winner != FlickDomPlayerId.None)
            {
                return;
            }

            if (lastChangedOwner != FlickDomPlayerId.None && TryClaimMatchingCards(lastChangedOwner))
            {
                lastChangedOwner = FlickDomPlayerId.None;
                return;
            }

            if (TryClaimMatchingCards(FlickDomPlayerId.Player1))
            {
                return;
            }

            TryClaimMatchingCards(FlickDomPlayerId.Player2);
        }

        private bool TryClaimMatchingCards(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.None || winner != FlickDomPlayerId.None)
            {
                return false;
            }

            bool claimedAnyCard = false;
            for (int i = 0; i < runtimeCards.Length; i++)
            {
                PatternCardData card = runtimeCards[i];
                if (!IsCardAvailable(i))
                {
                    continue;
                }

                if (!PatternCardMatcher.TryFindMatch(
                        tokenMapManager,
                        card,
                        player,
                        allowRotatedMatches,
                        matchAnywhereOnBoard,
                        out Vector2Int matchOrigin))
                {
                    continue;
                }

                ClaimCard(i, card, player, matchOrigin);
                claimedAnyCard = true;
            }

            return claimedAnyCard;
        }

        private void ClaimCard(int cardIndex, PatternCardData card, FlickDomPlayerId player, Vector2Int matchOrigin)
        {
            claimedCards[cardIndex] = true;
            int gainedScore = card.ScoreValue;
            AddScore(player, gainedScore);
            CardCompleted?.Invoke(card, player, gainedScore, matchOrigin);
            ActiveCardChanged?.Invoke(ActiveCard);

            if (logCardClaims)
            {
                Debug.Log("[PatternCard] " + player + " completed " + card.CardId + " and gained " + gainedScore + " point(s).", this);
            }

            if (RemainingCardCount <= 0)
            {
                CardsExhausted?.Invoke();

                bool advancedDeck = TryAdvanceToNextFallbackDeck();
                if (advancedDeck)
                {
                    ClearTokenMapForCardRoundChange();
                }

                FinishRoundForCardsExhausted();

                if (advancedDeck)
                {
                    ActiveCardChanged?.Invoke(ActiveCard);
                }
            }
        }

        private void AddScore(FlickDomPlayerId player, int score)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                player1Score += score;
            }
            else if (player == FlickDomPlayerId.Player2)
            {
                player2Score += score;
            }

            ScoreChanged?.Invoke(player, score, player1Score, player2Score);

            if (winner == FlickDomPlayerId.None)
            {
                int currentScore = GetScore(player);
                if (currentScore >= winningScore)
                {
                    winner = player;
                    MatchWon?.Invoke(winner, player1Score, player2Score);

                    if (logCardClaims)
                    {
                        Debug.Log("[PatternCard] " + winner + " won the match by reaching " + currentScore + " points.", this);
                    }
                }
            }
        }

        private bool CanScoreNow()
        {
            if (!CanControlScoreState())
            {
                return false;
            }

            if (gameModeManager == null)
            {
                return true;
            }

            return gameModeManager.CurrentState == FlickDomGameState.PlacementSelection
                || gameModeManager.CurrentState == FlickDomGameState.CardMatch;
        }

        public void ApplyNetworkScoreSnapshot(int nextPlayer1Score, int nextPlayer2Score, FlickDomPlayerId nextWinner)
        {
            int previousPlayer1Score = player1Score;
            int previousPlayer2Score = player2Score;

            player1Score = Mathf.Max(0, nextPlayer1Score);
            player2Score = Mathf.Max(0, nextPlayer2Score);
            winner = nextWinner;
            CapturePendingNetworkScoreGain(previousPlayer1Score, previousPlayer2Score);
            ScoreChanged?.Invoke(FlickDomPlayerId.None, 0, player1Score, player2Score);
            PresentPendingNetworkCardCompletion(null);

            if (winner != FlickDomPlayerId.None)
            {
                MatchWon?.Invoke(winner, player1Score, player2Score);
            }
        }

        public bool[] GetClaimedCardSnapshot()
        {
            bool[] snapshot = new bool[claimedCards != null ? claimedCards.Length : 0];
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = claimedCards[i];
            }

            return snapshot;
        }

        public void ApplyNetworkCardStateSnapshot(
            int nextFallbackDeckIndex,
            int nextCardDrawSeed,
            IReadOnlyList<bool> nextClaimedCards)
        {
            if (!CanApplyNetworkCardStateSnapshot(nextFallbackDeckIndex, nextCardDrawSeed, nextClaimedCards))
            {
                Debug.Log("[PatternCard] Ignored stale network card snapshot. Current Stage: " + CurrentStageNumber + ", Incoming StageIndex: " + nextFallbackDeckIndex + ", DrawSeed: " + nextCardDrawSeed + ".", this);
                return;
            }

            ApplyNetworkCardDeckSnapshot(nextFallbackDeckIndex, nextCardDrawSeed);
            PatternCardData newlyClaimedCard = FindFirstNewlyClaimedCard(nextClaimedCards);
            StorePendingNetworkClaimedCard(newlyClaimedCard);

            int count = Mathf.Min(claimedCards.Length, nextClaimedCards != null ? nextClaimedCards.Count : 0);
            for (int i = 0; i < claimedCards.Length; i++)
            {
                claimedCards[i] = i < count && nextClaimedCards[i];
            }

            PresentPendingNetworkCardCompletion(newlyClaimedCard);
            ActiveCardChanged?.Invoke(ActiveCard);
            Debug.Log("[PatternCard] Network card snapshot applied. Stage: " + CurrentStageNumber + ", DrawSeed: " + cardDrawSeed + ", Remaining: " + RemainingCardCount + ".", this);
        }

        public bool CanApplyNetworkCardStateSnapshot(
            int nextFallbackDeckIndex,
            int nextCardDrawSeed,
            IReadOnlyList<bool> nextClaimedCards)
        {
            return !IsStaleNetworkCardStateSnapshot(nextFallbackDeckIndex, nextCardDrawSeed, nextClaimedCards);
        }

        public void ApplyNetworkCardCompletedPresentation(
            int nextFallbackDeckIndex,
            int nextCardDrawSeed,
            string cardId,
            FlickDomPlayerId player,
            int gainedScore,
            Vector2Int matchOrigin)
        {
            if (IsStaleNetworkCardDeckSnapshot(nextFallbackDeckIndex, nextCardDrawSeed))
            {
                PatternCardData staleCard = FindRuntimeCardById(Mathf.Max(0, nextFallbackDeckIndex), cardId);
                if (staleCard != null)
                {
                    PresentNetworkCardCompletion(staleCard, player, Mathf.Max(0, gainedScore), matchOrigin);
                    Debug.Log("[PatternCard] Applied stale network card completion presentation without reverting stage. Current Stage: " + CurrentStageNumber + ", Incoming StageIndex: " + nextFallbackDeckIndex + ", DrawSeed: " + nextCardDrawSeed + ".", this);
                    return;
                }

                Debug.Log("[PatternCard] Ignored stale network card completion for unknown card " + cardId + ". Current Stage: " + CurrentStageNumber + ", Incoming StageIndex: " + nextFallbackDeckIndex + ", DrawSeed: " + nextCardDrawSeed + ".", this);
                return;
            }

            ApplyNetworkCardDeckSnapshot(nextFallbackDeckIndex, nextCardDrawSeed);
            PatternCardData card = FindRuntimeCardById(cardId);
            if (card == null)
            {
                Debug.LogWarning("[PatternCard] Ignored network card completion for unknown card " + cardId + ".", this);
                return;
            }

            PresentNetworkCardCompletion(card, player, Mathf.Max(0, gainedScore), matchOrigin);
        }

        private void ApplyNetworkCardDeckSnapshot(int nextFallbackDeckIndex, int nextCardDrawSeed)
        {
            int clampedDeckIndex = Mathf.Max(0, nextFallbackDeckIndex);
            bool drawChanged = cardDrawSeed != nextCardDrawSeed;
            bool deckChanged = drawChanged || clampedDeckIndex != currentFallbackDeckIndex;
            if (deckChanged)
            {
                ClearPendingNetworkClaimedCard();
            }

            if (drawChanged)
            {
                cardDrawSeed = nextCardDrawSeed;
                RebuildRuntimeFallbackDecks();
            }

            if (deckChanged)
            {
                EnsureRuntimeFallbackDecks();
                if (runtimeFallbackDecks != null && clampedDeckIndex < runtimeFallbackDecks.Length)
                {
                    currentFallbackDeckIndex = clampedDeckIndex;
                    RefreshRuntimeCards();
                }
            }

            if (claimedCards == null || claimedCards.Length != runtimeCards.Length)
            {
                RefreshRuntimeCards();
            }
        }

        private PatternCardData FindRuntimeCardById(string cardId)
        {
            if (string.IsNullOrEmpty(cardId) || runtimeCards == null)
            {
                return null;
            }

            for (int i = 0; i < runtimeCards.Length; i++)
            {
                PatternCardData card = runtimeCards[i];
                if (card != null && string.Equals(card.CardId, cardId, StringComparison.Ordinal))
                {
                    return card;
                }
            }

            return null;
        }

        private PatternCardData FindRuntimeCardById(int fallbackDeckIndex, string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                return null;
            }

            if (fallbackDeckIndex == currentFallbackDeckIndex)
            {
                return FindRuntimeCardById(cardId);
            }

            if (runtimeFallbackDecks == null
                || fallbackDeckIndex < 0
                || fallbackDeckIndex >= runtimeFallbackDecks.Length)
            {
                return null;
            }

            PatternCardData[] cards = runtimeFallbackDecks[fallbackDeckIndex];
            if (cards == null)
            {
                return null;
            }

            for (int i = 0; i < cards.Length; i++)
            {
                PatternCardData card = cards[i];
                if (card != null && string.Equals(card.CardId, cardId, StringComparison.Ordinal))
                {
                    return card;
                }
            }

            return null;
        }

        private PatternCardData FindFirstNewlyClaimedCard(IReadOnlyList<bool> nextClaimedCards)
        {
            if (runtimeCards == null || claimedCards == null || nextClaimedCards == null)
            {
                return null;
            }

            int count = Mathf.Min(runtimeCards.Length, Mathf.Min(claimedCards.Length, nextClaimedCards.Count));
            for (int i = 0; i < count; i++)
            {
                if (!claimedCards[i] && nextClaimedCards[i] && runtimeCards[i] != null)
                {
                    return runtimeCards[i];
                }
            }

            return null;
        }

        private bool IsStaleNetworkCardStateSnapshot(
            int nextFallbackDeckIndex,
            int nextCardDrawSeed,
            IReadOnlyList<bool> nextClaimedCards)
        {
            if (cardDrawSeed != nextCardDrawSeed)
            {
                return false;
            }

            if (IsStaleNetworkCardDeckSnapshot(nextFallbackDeckIndex, nextCardDrawSeed))
            {
                return true;
            }

            int clampedDeckIndex = Mathf.Max(0, nextFallbackDeckIndex);
            if (clampedDeckIndex > currentFallbackDeckIndex)
            {
                return false;
            }

            if (claimedCards == null || nextClaimedCards == null)
            {
                return false;
            }

            for (int i = 0; i < claimedCards.Length; i++)
            {
                if (claimedCards[i] && (i >= nextClaimedCards.Count || !nextClaimedCards[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsStaleNetworkCardDeckSnapshot(int nextFallbackDeckIndex, int nextCardDrawSeed)
        {
            return cardDrawSeed == nextCardDrawSeed
                && Mathf.Max(0, nextFallbackDeckIndex) < currentFallbackDeckIndex;
        }

        private void CapturePendingNetworkScoreGain(int previousPlayer1Score, int previousPlayer2Score)
        {
            ClearPendingNetworkScoreGain();

            int player1Gain = player1Score - previousPlayer1Score;
            int player2Gain = player2Score - previousPlayer2Score;
            if (player1Gain > 0 && player2Gain <= 0)
            {
                pendingNetworkScorePlayer = FlickDomPlayerId.Player1;
                pendingNetworkScoreGain = player1Gain;
            }
            else if (player2Gain > 0 && player1Gain <= 0)
            {
                pendingNetworkScorePlayer = FlickDomPlayerId.Player2;
                pendingNetworkScoreGain = player2Gain;
            }
        }

        private void StorePendingNetworkClaimedCard(PatternCardData card)
        {
            if (card == null)
            {
                return;
            }

            pendingNetworkClaimedCard = card;
            pendingNetworkClaimedCardAt = Time.unscaledTime;
        }

        private void PresentPendingNetworkCardCompletion(PatternCardData card)
        {
            FlickDomPlayerId player = pendingNetworkScorePlayer;
            int gainedScore = pendingNetworkScoreGain;
            if (player == FlickDomPlayerId.None || gainedScore <= 0)
            {
                return;
            }

            PatternCardData completionCard = card != null ? card : ConsumePendingNetworkClaimedCard();
            if (completionCard == null)
            {
                return;
            }

            ClearPendingNetworkScoreGain();
            ClearPendingNetworkClaimedCard();
            PresentNetworkCardCompletion(completionCard, player, gainedScore, new Vector2Int(-1, -1));
        }

        private void PresentNetworkCardCompletion(
            PatternCardData card,
            FlickDomPlayerId player,
            int gainedScore,
            Vector2Int matchOrigin)
        {
            if (card == null || player == FlickDomPlayerId.None)
            {
                return;
            }

            if (IsDuplicateNetworkCardPresentation(card, player))
            {
                return;
            }

            lastNetworkPresentedCardId = card.CardId ?? string.Empty;
            lastNetworkPresentedPlayer = player;
            lastNetworkPresentedDeckIndex = currentFallbackDeckIndex;
            lastNetworkPresentedDrawSeed = cardDrawSeed;
            lastNetworkPresentedAt = Time.unscaledTime;
            CardCompleted?.Invoke(card, player, Mathf.Max(0, gainedScore), matchOrigin);
        }

        private bool IsDuplicateNetworkCardPresentation(PatternCardData card, FlickDomPlayerId player)
        {
            if (card == null || string.IsNullOrEmpty(lastNetworkPresentedCardId))
            {
                return false;
            }

            float elapsed = Time.unscaledTime - lastNetworkPresentedAt;
            return player == lastNetworkPresentedPlayer
                && currentFallbackDeckIndex == lastNetworkPresentedDeckIndex
                && cardDrawSeed == lastNetworkPresentedDrawSeed
                && elapsed >= 0f
                && elapsed <= NetworkCardPresentationDuplicateWindowSeconds
                && string.Equals(card.CardId, lastNetworkPresentedCardId, StringComparison.Ordinal);
        }

        private PatternCardData ConsumePendingNetworkClaimedCard()
        {
            PatternCardData card = pendingNetworkClaimedCard;
            if (card == null)
            {
                return null;
            }

            float elapsed = Time.unscaledTime - pendingNetworkClaimedCardAt;
            if (elapsed < 0f || elapsed > PendingNetworkCardSnapshotWindowSeconds)
            {
                ClearPendingNetworkClaimedCard();
                return null;
            }

            ClearPendingNetworkClaimedCard();
            return card;
        }

        private void ClearPendingNetworkScoreGain()
        {
            pendingNetworkScorePlayer = FlickDomPlayerId.None;
            pendingNetworkScoreGain = 0;
        }

        private void ClearPendingNetworkClaimedCard()
        {
            pendingNetworkClaimedCard = null;
            pendingNetworkClaimedCardAt = -999f;
        }

        private static bool CanControlScoreState()
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            return bootstrap == null || bootstrap.AllowsLocalStateControl();
        }

        private void EnsureRuntimeFallbackDecks()
        {
            if (activeCard != null || HasConfiguredDeck() || !autoCreateEasyFallbackCard)
            {
                return;
            }

            if (runtimeFallbackDecks == null || runtimeFallbackDecks.Length <= 0)
            {
                EnsureCardDrawSeed();
                RebuildRuntimeFallbackDecks();
                currentFallbackDeckIndex = 0;
            }
        }

        private void RefreshRuntimeCards()
        {
            runtimeCards = ResolveRuntimeCards();
            claimedCards = new bool[runtimeCards.Length];
        }

        private PatternCardData[] ResolveRuntimeCards()
        {
            if (HasConfiguredDeck())
            {
                return cardDeck;
            }

            if (activeCard != null)
            {
                return new[] { activeCard };
            }

            if (runtimeFallbackDecks == null
                || runtimeFallbackDecks.Length <= 0
                || currentFallbackDeckIndex < 0
                || currentFallbackDeckIndex >= runtimeFallbackDecks.Length)
            {
                return new PatternCardData[0];
            }

            return runtimeFallbackDecks[currentFallbackDeckIndex] ?? new PatternCardData[0];
        }

        private void ResetCardProgress()
        {
            currentFallbackDeckIndex = 0;
            ClearPendingNetworkScoreGain();
            ClearPendingNetworkClaimedCard();
            if (activeCard == null && !HasConfiguredDeck() && autoCreateEasyFallbackCard)
            {
                cardDrawSeed = CreateCardDrawSeed();
                RebuildRuntimeFallbackDecks();
            }
            else
            {
                EnsureRuntimeFallbackDecks();
            }

            RefreshRuntimeCards();
        }

        private bool TryAdvanceToNextFallbackDeck()
        {
            if (!advanceFallbackDeckOnExhaustion
                || activeCard != null
                || HasConfiguredDeck()
                || !autoCreateEasyFallbackCard)
            {
                return false;
            }

            EnsureRuntimeFallbackDecks();
            int nextDeckIndex = currentFallbackDeckIndex + 1;
            if (runtimeFallbackDecks == null || nextDeckIndex >= runtimeFallbackDecks.Length)
            {
                return false;
            }

            currentFallbackDeckIndex = nextDeckIndex;
            RefreshRuntimeCards();

            if (logCardClaims && ActiveCard != null)
            {
                Debug.Log("[PatternCard] Advanced to Stage " + CurrentStageNumber + ".", this);
            }

            return true;
        }

        private void ClearTokenMapForCardRoundChange()
        {
            if (!clearTokenMapOnFallbackDeckAdvance || tokenMapManager == null)
            {
                return;
            }

            isClearingMapForCardRoundChange = true;
            try
            {
                tokenMapManager.ClearMap();
            }
            finally
            {
                isClearingMapForCardRoundChange = false;
            }
        }

        private void FinishRoundForCardsExhausted()
        {
            if (!finishRoundOnCardsExhausted || gameModeManager == null)
            {
                return;
            }

            gameModeManager.ForceFinishRoundAndStartNext();
        }

        private bool HasConfiguredDeck()
        {
            if (cardDeck == null)
            {
                return false;
            }

            for (int i = 0; i < cardDeck.Length; i++)
            {
                if (cardDeck[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureCardDrawSeed()
        {
            if (cardDrawSeed == 0)
            {
                cardDrawSeed = CreateCardDrawSeed();
            }
        }

        private void RebuildRuntimeFallbackDecks()
        {
            ReleaseRuntimeFallbackDecks();
            runtimeFallbackDecks = PatternCardData.CreateRuntimeStageDecks(
                cardDrawSeed,
                StageCount,
                CardsPerStage);
        }

        private void ReleaseRuntimeFallbackDecks()
        {
            if (runtimeFallbackDecks == null)
            {
                return;
            }

            for (int stageIndex = 0; stageIndex < runtimeFallbackDecks.Length; stageIndex++)
            {
                PatternCardData[] stageCards = runtimeFallbackDecks[stageIndex];
                if (stageCards == null)
                {
                    continue;
                }

                for (int cardIndex = 0; cardIndex < stageCards.Length; cardIndex++)
                {
                    if (stageCards[cardIndex] != null)
                    {
                        Destroy(stageCards[cardIndex]);
                    }
                }
            }

            runtimeFallbackDecks = new PatternCardData[0][];
        }

        private static int CreateCardDrawSeed()
        {
            int seed = Guid.NewGuid().GetHashCode();
            return seed == 0 ? 1 : seed;
        }

        private void ResetClaimedCards()
        {
            ClearPendingNetworkClaimedCard();
            if (runtimeCards == null || runtimeCards.Length <= 0)
            {
                RefreshRuntimeCards();
                return;
            }

            for (int i = 0; i < claimedCards.Length; i++)
            {
                claimedCards[i] = false;
            }
        }

        private PatternCardData GetFirstRemainingCard()
        {
            for (int i = 0; i < runtimeCards.Length; i++)
            {
                if (IsCardAvailable(i))
                {
                    return runtimeCards[i];
                }
            }

            return null;
        }

        private int CountRemainingCards()
        {
            int count = 0;
            for (int i = 0; i < runtimeCards.Length; i++)
            {
                if (IsCardAvailable(i))
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsCardAvailable(int index)
        {
            return index >= 0
                && index < runtimeCards.Length
                && index < claimedCards.Length
                && runtimeCards[index] != null
                && !claimedCards[index];
        }

        private void ForceDebugWinForPlayer1()
        {
            if (winner != FlickDomPlayerId.None)
            {
                return;
            }

            int delta = Mathf.Max(0, winningScore - player1Score);
            if (delta <= 0)
            {
                delta = winningScore;
                player1Score = 0;
            }

            AddScore(FlickDomPlayerId.Player1, delta);
            FlickDomNetworkBootstrap.Active?.NotifyHostScoreStateChanged("F10 debug win");
        }
    }
}
