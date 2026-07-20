using System;
using UnityEngine;

namespace FlickDom.Gameplay
{
    public sealed class PatternCardManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private TokenMapManager tokenMapManager;

        [Header("Active Card")]
        [SerializeField] private PatternCardData activeCard;
        [SerializeField] private bool autoCreateEasyFallbackCard = true;
        [SerializeField] private bool matchAnywhereOnBoard = true;
        [SerializeField] private bool resetScoresWhenMapCleared = true;
        [SerializeField] private bool logCardClaims = true;

        private PatternCardData runtimeFallbackCard;
        private FlickDomPlayerId lastChangedOwner = FlickDomPlayerId.None;
        private bool activeCardClaimed;
        private int player1Score;
        private int player2Score;

        public event Action<PatternCardData> ActiveCardChanged;
        public event Action<FlickDomPlayerId, int, int, int> ScoreChanged;
        public event Action<PatternCardData, FlickDomPlayerId, int, Vector2Int> CardCompleted;

        public PatternCardData ActiveCard
        {
            get { return activeCard != null ? activeCard : runtimeFallbackCard; }
        }

        public bool IsActiveCardClaimed
        {
            get { return activeCardClaimed; }
        }

        public int Player1Score
        {
            get { return player1Score; }
        }

        public int Player2Score
        {
            get { return player2Score; }
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

            EnsureRuntimeFallbackCard();
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
            activeCardClaimed = false;
            ActiveCardChanged?.Invoke(ActiveCard);
            EvaluateAllPlayers();
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

        private void HandleCellOwnerChanged(Vector2Int cell, FlickDomPlayerId previousOwner, FlickDomPlayerId nextOwner)
        {
            if (nextOwner != FlickDomPlayerId.None)
            {
                lastChangedOwner = nextOwner;
            }
        }

        private void HandleMapChanged()
        {
            if (!CanScoreNow())
            {
                return;
            }

            if (lastChangedOwner != FlickDomPlayerId.None && TryClaimActiveCard(lastChangedOwner))
            {
                lastChangedOwner = FlickDomPlayerId.None;
                return;
            }

            lastChangedOwner = FlickDomPlayerId.None;
            EvaluateAllPlayers();
        }

        private void HandleMapCleared()
        {
            activeCardClaimed = false;
            lastChangedOwner = FlickDomPlayerId.None;

            if (resetScoresWhenMapCleared)
            {
                player1Score = 0;
                player2Score = 0;
                ScoreChanged?.Invoke(FlickDomPlayerId.None, 0, player1Score, player2Score);
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
            if (!CanScoreNow() || activeCardClaimed)
            {
                return;
            }

            if (lastChangedOwner != FlickDomPlayerId.None && TryClaimActiveCard(lastChangedOwner))
            {
                lastChangedOwner = FlickDomPlayerId.None;
                return;
            }

            if (TryClaimActiveCard(FlickDomPlayerId.Player1))
            {
                return;
            }

            TryClaimActiveCard(FlickDomPlayerId.Player2);
        }

        private bool TryClaimActiveCard(FlickDomPlayerId player)
        {
            PatternCardData card = ActiveCard;
            if (activeCardClaimed || card == null)
            {
                return false;
            }

            if (!PatternCardMatcher.TryFindMatch(
                    tokenMapManager,
                    card,
                    player,
                    matchAnywhereOnBoard,
                    out Vector2Int matchOrigin))
            {
                return false;
            }

            activeCardClaimed = true;
            int gainedScore = card.ScoreValue;
            AddScore(player, gainedScore);
            CardCompleted?.Invoke(card, player, gainedScore, matchOrigin);
            ActiveCardChanged?.Invoke(card);

            if (logCardClaims)
            {
                Debug.Log("[PatternCard] " + player + " completed " + card.CardId + " and gained " + gainedScore + " point(s).", this);
            }

            return true;
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
        }

        private bool CanScoreNow()
        {
            if (gameModeManager == null)
            {
                return true;
            }

            return gameModeManager.CurrentState == FlickDomGameState.PlacementSelection
                || gameModeManager.CurrentState == FlickDomGameState.CardMatch;
        }

        private void EnsureRuntimeFallbackCard()
        {
            if (activeCard != null || !autoCreateEasyFallbackCard)
            {
                return;
            }

            if (runtimeFallbackCard == null)
            {
                runtimeFallbackCard = PatternCardData.CreateRuntimeEasyCard();
            }
        }
    }
}
