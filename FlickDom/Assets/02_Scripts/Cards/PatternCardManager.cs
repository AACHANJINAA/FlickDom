using System;
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

        [Header("Active Card")]
        [SerializeField] private PatternCardData activeCard;
        [SerializeField] private PatternCardData[] cardDeck;
        [SerializeField] private bool autoCreateEasyFallbackCard = true;
        [SerializeField] private bool advanceFallbackDeckOnExhaustion = true;
        [SerializeField] private bool clearTokenMapOnFallbackDeckAdvance = true;
        [SerializeField] private bool finishRoundOnCardsExhausted = true;
        [SerializeField] private bool allowRotatedMatches = true;
        [SerializeField] private bool matchAnywhereOnBoard = true;
        [SerializeField] private bool resetScoresWhenMapCleared = true;
        [SerializeField] private bool logCardClaims = true;
        [SerializeField] private int winningScore = 10;

        private PatternCardData[][] runtimeFallbackDecks = new PatternCardData[0][];
        private int currentFallbackDeckIndex;
        private PatternCardData[] runtimeCards = new PatternCardData[0];
        private bool[] claimedCards = new bool[0];
        private FlickDomPlayerId lastChangedOwner = FlickDomPlayerId.None;
        private int player1Score;
        private int player2Score;
        private bool isClearingMapForCardRoundChange;
        private FlickDomPlayerId winner = FlickDomPlayerId.None;

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
            player1Score = Mathf.Max(0, nextPlayer1Score);
            player2Score = Mathf.Max(0, nextPlayer2Score);
            winner = nextWinner;
            ScoreChanged?.Invoke(FlickDomPlayerId.None, 0, player1Score, player2Score);

            if (winner != FlickDomPlayerId.None)
            {
                MatchWon?.Invoke(winner, player1Score, player2Score);
            }
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
                runtimeFallbackDecks = PatternCardData.CreateRuntimeProgressionDecks();
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
            EnsureRuntimeFallbackDecks();
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
                Debug.Log("[PatternCard] Card round changed to " + ActiveCard.Difficulty + ".", this);
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

        private void ResetClaimedCards()
        {
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
