using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FlickDom.Gameplay
{
    public sealed class ScoreCardCollectAnimator : MonoBehaviour
    {
        [SerializeField] private Vector2 largeCardSize = new Vector2(250f, 330f);
        [SerializeField] private Vector2 collectedCardSize = new Vector2(58f, 76f);
        [SerializeField] private float popDuration = 0.18f;
        [SerializeField] private float holdDuration = 0.2f;
        [SerializeField] private float collectDuration = 0.55f;
        [SerializeField] private Color fallbackEasyColor = new Color(0.24f, 0.76f, 0.44f, 1f);
        [SerializeField] private Color fallbackNormalColor = new Color(0.18f, 0.55f, 1f, 1f);
        [SerializeField] private Color fallbackHardColor = new Color(1f, 0.36f, 0.26f, 1f);
        [SerializeField] private Color fallbackEmptyCellColor = new Color(1f, 1f, 1f, 0.24f);

        private Canvas canvas;
        private Coroutine runningAnimation;
        private GameObject activeAnimationObject;

        public void Initialize(Canvas targetCanvas)
        {
            canvas = targetCanvas;
        }

        public void Play(PatternCardData card, FlickDomPlayerId player, RectTransform player1Target, RectTransform player2Target)
        {
            if (canvas == null || card == null)
            {
                return;
            }

            RectTransform target = player == FlickDomPlayerId.Player2 ? player2Target : player1Target;
            if (target == null)
            {
                return;
            }

            if (runningAnimation != null)
            {
                StopCoroutine(runningAnimation);
                runningAnimation = null;
            }

            DestroyActiveAnimationObject();

            runningAnimation = StartCoroutine(AnimateCard(card, target));
        }

        private void OnDisable()
        {
            if (runningAnimation != null)
            {
                StopCoroutine(runningAnimation);
                runningAnimation = null;
            }

            DestroyActiveAnimationObject();
        }

        private IEnumerator AnimateCard(PatternCardData card, RectTransform target)
        {
            GameObject rootObject = new GameObject("Score Card Collect Animation");
            activeAnimationObject = rootObject;
            rootObject.transform.SetParent(canvas.transform, false);
            rootObject.transform.SetAsLastSibling();

            RectTransform root = rootObject.AddComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = largeCardSize;
            root.anchoredPosition = Vector2.zero;
            root.localScale = Vector3.one * 0.55f;

            CanvasGroup group = rootObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            Texture2D texture = ResolveTexture(card);
            if (texture != null)
            {
                RawImage image = rootObject.AddComponent<RawImage>();
                image.texture = texture;
                image.color = Color.white;
                image.raycastTarget = false;
            }
            else
            {
                BuildFallbackCard(root, card);
            }

            Vector2 endPosition = GetCanvasLocalPosition(target);
            float elapsed = 0f;
            while (elapsed < popDuration)
            {
                float t = EaseOutBack(elapsed / Mathf.Max(0.001f, popDuration));
                root.localScale = Vector3.one * Mathf.LerpUnclamped(0.55f, 1.12f, t);
                group.alpha = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, popDuration));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            root.localScale = Vector3.one;
            group.alpha = 1f;

            if (holdDuration > 0f)
            {
                yield return new WaitForSecondsRealtime(holdDuration);
            }

            Vector2 startSize = largeCardSize;
            Vector2 startPosition = root.anchoredPosition;
            elapsed = 0f;
            while (elapsed < collectDuration)
            {
                float normalized = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, collectDuration));
                float t = EaseInOutCubic(normalized);
                root.anchoredPosition = Vector2.LerpUnclamped(startPosition, endPosition, t);
                root.sizeDelta = Vector2.LerpUnclamped(startSize, collectedCardSize, t);
                root.localScale = Vector3.one * Mathf.LerpUnclamped(1f, 0.78f, t);
                group.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01((normalized - 0.68f) / 0.32f));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Destroy(rootObject);
            activeAnimationObject = null;
            runningAnimation = null;
        }

        private void DestroyActiveAnimationObject()
        {
            if (activeAnimationObject == null)
            {
                return;
            }

            Destroy(activeAnimationObject);
            activeAnimationObject = null;
        }

        private Vector2 GetCanvasLocalPosition(RectTransform target)
        {
            RectTransform canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null)
            {
                return Vector2.zero;
            }

            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, target.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPoint,
                canvas.worldCamera,
                out Vector2 localPoint);
            return localPoint;
        }

        private static Texture2D ResolveTexture(PatternCardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.ResourcesImagePath))
            {
                return null;
            }

            return Resources.Load<Texture2D>(card.ResourcesImagePath);
        }

        private void BuildFallbackCard(RectTransform parent, PatternCardData card)
        {
            Image background = parent.gameObject.AddComponent<Image>();
            background.color = new Color(0.08f, 0.08f, 0.09f, 0.96f);
            background.raycastTarget = false;

            GameObject gridObject = new GameObject("Fallback Pattern Grid");
            gridObject.transform.SetParent(parent, false);

            RectTransform gridRect = gridObject.AddComponent<RectTransform>();
            gridRect.anchorMin = new Vector2(0.5f, 0.5f);
            gridRect.anchorMax = new Vector2(0.5f, 0.5f);
            gridRect.pivot = new Vector2(0.5f, 0.5f);
            gridRect.sizeDelta = new Vector2(178f, 230f);
            gridRect.anchoredPosition = Vector2.zero;

            GridLayoutGroup grid = gridObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Max(1, card.Width);
            grid.spacing = new Vector2(6f, 6f);
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.cellSize = GetFallbackCellSize(gridRect.sizeDelta, card);

            Color filledColor = ResolveDifficultyColor(card.Difficulty);
            for (int y = card.Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < card.Width; x++)
                {
                    GameObject cellObject = new GameObject("Cell");
                    cellObject.transform.SetParent(gridObject.transform, false);
                    Image cell = cellObject.AddComponent<Image>();
                    cell.color = IsFilledCell(card, x, y) ? filledColor : fallbackEmptyCellColor;
                    cell.raycastTarget = false;
                }
            }
        }

        private static Vector2 GetFallbackCellSize(Vector2 gridSize, PatternCardData card)
        {
            int width = Mathf.Max(1, card.Width);
            int height = Mathf.Max(1, card.Height);
            float cellWidth = (gridSize.x - ((width - 1) * 6f)) / width;
            float cellHeight = (gridSize.y - ((height - 1) * 6f)) / height;
            float size = Mathf.Max(12f, Mathf.Min(cellWidth, cellHeight));
            return new Vector2(size, size);
        }

        private Color ResolveDifficultyColor(PatternCardDifficulty difficulty)
        {
            if (difficulty == PatternCardDifficulty.Hard)
            {
                return fallbackHardColor;
            }

            if (difficulty == PatternCardDifficulty.Normal)
            {
                return fallbackNormalColor;
            }

            return fallbackEasyColor;
        }

        private static bool IsFilledCell(PatternCardData card, int x, int y)
        {
            Vector2Int[] filledCells = card.FilledCells;
            if (filledCells == null)
            {
                return false;
            }

            for (int i = 0; i < filledCells.Length; i++)
            {
                Vector2Int cell = filledCells[i];
                if (cell.x == x && cell.y == y)
                {
                    return true;
                }
            }

            return false;
        }

        private static float EaseInOutCubic(float value)
        {
            float t = Mathf.Clamp01(value);
            return t < 0.5f
                ? 4f * t * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        private static float EaseOutBack(float value)
        {
            float t = Mathf.Clamp01(value);
            const float overshoot = 1.70158f;
            return 1f + (overshoot + 1f) * Mathf.Pow(t - 1f, 3f) + overshoot * Mathf.Pow(t - 1f, 2f);
        }
    }
}
