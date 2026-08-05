using UnityEngine;

namespace FlickDom.Gameplay
{
    [CreateAssetMenu(menuName = "FlickDom/Pattern Card", fileName = "PatternCard")]
    public sealed class PatternCardData : ScriptableObject
    {
        [SerializeField] private string cardId = "EasyCard_1";
        [SerializeField] private PatternCardDifficulty difficulty = PatternCardDifficulty.Easy;
        [SerializeField] private int scoreValue = 1;
        [SerializeField] private int width = 4;
        [SerializeField] private int height = 5;
        [SerializeField] private string resourcesImagePath = "Cards/EasyCard_1";
        [SerializeField] private Vector2Int[] filledCells =
        {
            new Vector2Int(1, 3),
            new Vector2Int(2, 3)
        };

        public string CardId
        {
            get { return string.IsNullOrEmpty(cardId) ? name : cardId; }
        }

        public PatternCardDifficulty Difficulty
        {
            get { return difficulty; }
        }

        public int ScoreValue
        {
            get { return GetScoreValueForDifficulty(difficulty); }
        }

        public int Width
        {
            get { return width; }
        }

        public int Height
        {
            get { return height; }
        }

        public string ResourcesImagePath
        {
            get { return resourcesImagePath; }
        }

        public Vector2Int[] FilledCells
        {
            get { return filledCells; }
        }

        private void OnValidate()
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            scoreValue = GetScoreValueForDifficulty(difficulty);

            if (filledCells == null)
            {
                filledCells = new Vector2Int[0];
                return;
            }

            for (int i = 0; i < filledCells.Length; i++)
            {
                filledCells[i] = new Vector2Int(
                    Mathf.Clamp(filledCells[i].x, 0, width - 1),
                    Mathf.Clamp(filledCells[i].y, 0, height - 1));
            }
        }

        public static PatternCardData CreateRuntimeEasyCard()
        {
            return CreateRuntimeEasyCard1();
        }

        public static PatternCardData CreateRuntimeEasyCard1()
        {
            PatternCardData card = CreateInstance<PatternCardData>();
            card.cardId = "EasyCard_1";
            card.difficulty = PatternCardDifficulty.Easy;
            card.scoreValue = GetScoreValueForDifficulty(card.difficulty);
            card.width = 4;
            card.height = 5;
            card.resourcesImagePath = "Cards/EasyCard_1";
            card.filledCells = new[]
            {
                new Vector2Int(1, 3),
                new Vector2Int(2, 3)
            };
            return card;
        }

        public static PatternCardData CreateRuntimeEasyCard2()
        {
            PatternCardData card = CreateInstance<PatternCardData>();
            card.cardId = "EasyCard_2";
            card.difficulty = PatternCardDifficulty.Easy;
            card.scoreValue = GetScoreValueForDifficulty(card.difficulty);
            card.width = 4;
            card.height = 5;
            card.resourcesImagePath = "Cards/EasyCard_2";
            card.filledCells = new[]
            {
                new Vector2Int(1, 3),
                new Vector2Int(2, 2)
            };
            return card;
        }

        public static PatternCardData[] CreateRuntimeEasyDeck()
        {
            return new[]
            {
                CreateRuntimeEasyCard1(),
                CreateRuntimeEasyCard2()
            };
        }

        public static PatternCardData CreateRuntimeNormalCard1()
        {
            PatternCardData card = CreateInstance<PatternCardData>();
            card.cardId = "NormalCard_1";
            card.difficulty = PatternCardDifficulty.Normal;
            card.scoreValue = GetScoreValueForDifficulty(card.difficulty);
            card.width = 4;
            card.height = 5;
            card.resourcesImagePath = "Cards/NormalCard_1";
            card.filledCells = new[]
            {
                new Vector2Int(1, 3),
                new Vector2Int(1, 2),
                new Vector2Int(2, 2)
            };
            return card;
        }

        public static PatternCardData CreateRuntimeNormalCard2()
        {
            PatternCardData card = CreateInstance<PatternCardData>();
            card.cardId = "NormalCard_2";
            card.difficulty = PatternCardDifficulty.Normal;
            card.scoreValue = GetScoreValueForDifficulty(card.difficulty);
            card.width = 4;
            card.height = 5;
            card.resourcesImagePath = "Cards/NormalCard_2";
            card.filledCells = new[]
            {
                new Vector2Int(2, 4),
                new Vector2Int(1, 3),
                new Vector2Int(1, 2)
            };
            return card;
        }

        public static PatternCardData CreateRuntimeNormalCard3()
        {
            PatternCardData card = CreateInstance<PatternCardData>();
            card.cardId = "NormalCard_3";
            card.difficulty = PatternCardDifficulty.Normal;
            card.scoreValue = GetScoreValueForDifficulty(card.difficulty);
            card.width = 4;
            card.height = 5;
            card.resourcesImagePath = "Cards/NormalCard_3";
            card.filledCells = new[]
            {
                new Vector2Int(0, 4),
                new Vector2Int(1, 3),
                new Vector2Int(2, 2)
            };
            return card;
        }

        public static PatternCardData[] CreateRuntimeNormalDeck()
        {
            return new[]
            {
                CreateRuntimeNormalCard1(),
                CreateRuntimeNormalCard2(),
                CreateRuntimeNormalCard3()
            };
        }

        public static PatternCardData CreateRuntimeHardCard1()
        {
            PatternCardData card = CreateInstance<PatternCardData>();
            card.cardId = "HardCard_1";
            card.difficulty = PatternCardDifficulty.Hard;
            card.scoreValue = GetScoreValueForDifficulty(card.difficulty);
            card.width = 4;
            card.height = 4;
            card.resourcesImagePath = "Cards/HardCard_1";
            card.filledCells = new[]
            {
                new Vector2Int(1, 3),
                new Vector2Int(1, 2),
                new Vector2Int(1, 1),
                new Vector2Int(2, 1)
            };
            return card;
        }

        public static PatternCardData CreateRuntimeHardCard2()
        {
            PatternCardData card = CreateInstance<PatternCardData>();
            card.cardId = "HardCard_2";
            card.difficulty = PatternCardDifficulty.Hard;
            card.scoreValue = GetScoreValueForDifficulty(card.difficulty);
            card.width = 4;
            card.height = 4;
            card.resourcesImagePath = "Cards/HardCard_2";
            card.filledCells = new[]
            {
                new Vector2Int(1, 2),
                new Vector2Int(2, 2),
                new Vector2Int(1, 1),
                new Vector2Int(2, 1)
            };
            return card;
        }

        public static PatternCardData CreateRuntimeHardCard3()
        {
            PatternCardData card = CreateInstance<PatternCardData>();
            card.cardId = "HardCard_3";
            card.difficulty = PatternCardDifficulty.Hard;
            card.scoreValue = GetScoreValueForDifficulty(card.difficulty);
            card.width = 4;
            card.height = 4;
            card.resourcesImagePath = "Cards/HardCard_3";
            card.filledCells = new[]
            {
                new Vector2Int(0, 3),
                new Vector2Int(1, 2),
                new Vector2Int(2, 1),
                new Vector2Int(3, 0)
            };
            return card;
        }

        public static PatternCardData CreateRuntimeHardCard4()
        {
            PatternCardData card = CreateInstance<PatternCardData>();
            card.cardId = "HardCard_4";
            card.difficulty = PatternCardDifficulty.Hard;
            card.scoreValue = GetScoreValueForDifficulty(card.difficulty);
            card.width = 4;
            card.height = 4;
            card.resourcesImagePath = "Cards/HardCard_4";
            card.filledCells = new[]
            {
                new Vector2Int(1, 3),
                new Vector2Int(1, 2),
                new Vector2Int(2, 2),
                new Vector2Int(2, 1)
            };
            return card;
        }

        public static PatternCardData[] CreateRuntimeHardDeck()
        {
            return new[]
            {
                CreateRuntimeHardCard1(),
                CreateRuntimeHardCard2(),
                CreateRuntimeHardCard3(),
                CreateRuntimeHardCard4()
            };
        }

        public static PatternCardData[] CreateRuntimeCardPool()
        {
            PatternCardData[] easyCards = CreateRuntimeEasyDeck();
            PatternCardData[] normalCards = CreateRuntimeNormalDeck();
            PatternCardData[] hardCards = CreateRuntimeHardDeck();
            PatternCardData[] cardPool = new PatternCardData[
                easyCards.Length + normalCards.Length + hardCards.Length];

            int destinationIndex = 0;
            CopyCards(easyCards, cardPool, ref destinationIndex);
            CopyCards(normalCards, cardPool, ref destinationIndex);
            CopyCards(hardCards, cardPool, ref destinationIndex);
            return cardPool;
        }

        public static PatternCardData[][] CreateRuntimeStageDecks(
            int shuffleSeed,
            int stageCount = 3,
            int cardsPerStage = 3)
        {
            PatternCardData[] cardPool = CreateRuntimeCardPool();
            int normalizedStageCount = Mathf.Max(1, stageCount);
            int normalizedCardsPerStage = Mathf.Max(1, cardsPerStage);
            int requiredCardCount = normalizedStageCount * normalizedCardsPerStage;
            if (cardPool.Length < requiredCardCount)
            {
                Debug.LogError(
                    "[PatternCard] Not enough cards to create "
                    + normalizedStageCount
                    + " stages with "
                    + normalizedCardsPerStage
                    + " cards each.");
                return new PatternCardData[0][];
            }

            System.Random random = new System.Random(shuffleSeed);
            for (int i = cardPool.Length - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                (cardPool[i], cardPool[swapIndex]) =
                    (cardPool[swapIndex], cardPool[i]);
            }

            PatternCardData[][] stageDecks =
                new PatternCardData[normalizedStageCount][];
            int sourceIndex = 0;
            for (int stageIndex = 0; stageIndex < stageDecks.Length; stageIndex++)
            {
                PatternCardData[] stageCards =
                    new PatternCardData[normalizedCardsPerStage];
                for (int cardIndex = 0; cardIndex < stageCards.Length; cardIndex++)
                {
                    stageCards[cardIndex] = cardPool[sourceIndex];
                    sourceIndex++;
                }

                stageDecks[stageIndex] = stageCards;
            }

            return stageDecks;
        }

        private static void CopyCards(
            PatternCardData[] source,
            PatternCardData[] destination,
            ref int destinationIndex)
        {
            for (int i = 0; i < source.Length; i++)
            {
                destination[destinationIndex] = source[i];
                destinationIndex++;
            }
        }

        private static int GetScoreValueForDifficulty(PatternCardDifficulty difficulty)
        {
            if (difficulty == PatternCardDifficulty.Hard)
            {
                return 3;
            }

            if (difficulty == PatternCardDifficulty.Normal)
            {
                return 2;
            }

            return 1;
        }
    }
}
