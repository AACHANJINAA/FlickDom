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
            get { return scoreValue; }
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
            scoreValue = Mathf.Max(0, scoreValue);

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
            card.scoreValue = 1;
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
            card.scoreValue = 1;
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
    }
}
