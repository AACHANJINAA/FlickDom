using UnityEngine;
using UnityEngine.UI;

namespace FlickDom.Gameplay
{
    public sealed class PlayerScoreHud : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PatternCardManager cardManager;
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private Font font;

        [Header("Layout")]
        [SerializeField] private int fontSize = 34;
        [SerializeField] private Vector2 labelSize = new Vector2(260f, 72f);
        [SerializeField] private Vector2 turnLabelSize = new Vector2(260f, 42f);
        [SerializeField] private Vector2 player1Offset = new Vector2(28f, -24f);
        [SerializeField] private Vector2 player2Offset = new Vector2(-28f, -24f);
        [SerializeField] private Vector2 turnOffset = new Vector2(0f, -34f);

        [Header("Text")]
        [SerializeField] private string player1Prefix = "P1";
        [SerializeField] private string player2Prefix = "P2";
        [SerializeField] private string yourTurnText = "Your Turn";
        [SerializeField] private Color player1Color = new Color(0.18f, 0.42f, 1f, 1f);
        [SerializeField] private Color player2Color = new Color(1f, 0.22f, 0.18f, 1f);
        [SerializeField] private Color outlineColor = new Color(0f, 0f, 0f, 0.8f);
        [SerializeField] private Vector2 outlineDistance = new Vector2(2f, -2f);

        private Canvas canvas;
        private Text player1Text;
        private Text player2Text;
        private Text player1TurnText;
        private Text player2TurnText;

        private void Awake()
        {
            if (cardManager == null)
            {
                cardManager = GetComponent<PatternCardManager>();
            }

            if (gameModeManager == null)
            {
                gameModeManager = GetComponent<GameModeManager>();
            }

            BuildHud();
        }

        private void OnEnable()
        {
            if (cardManager != null)
            {
                cardManager.ScoreChanged += HandleScoreChanged;
            }

            if (gameModeManager != null)
            {
                gameModeManager.ActivePlayerChanged += HandleActivePlayerChanged;
            }
        }

        private void Start()
        {
            RefreshScores();
            RefreshTurnIndicator();
        }

        private void OnDisable()
        {
            if (cardManager != null)
            {
                cardManager.ScoreChanged -= HandleScoreChanged;
            }

            if (gameModeManager != null)
            {
                gameModeManager.ActivePlayerChanged -= HandleActivePlayerChanged;
            }
        }

        private void OnDestroy()
        {
            if (canvas != null)
            {
                Destroy(canvas.gameObject);
                canvas = null;
            }
        }

        private void OnValidate()
        {
            fontSize = Mathf.Max(8, fontSize);
            labelSize.x = Mathf.Max(80f, labelSize.x);
            labelSize.y = Mathf.Max(24f, labelSize.y);
            turnLabelSize.x = Mathf.Max(80f, turnLabelSize.x);
            turnLabelSize.y = Mathf.Max(20f, turnLabelSize.y);
        }

        private void HandleScoreChanged(
            FlickDomPlayerId player,
            int gainedScore,
            int player1Score,
            int player2Score)
        {
            SetScoreText(player1Text, player1Prefix, player1Score);
            SetScoreText(player2Text, player2Prefix, player2Score);
        }

        private void RefreshScores()
        {
            int p1 = cardManager != null ? cardManager.Player1Score : 0;
            int p2 = cardManager != null ? cardManager.Player2Score : 0;
            SetScoreText(player1Text, player1Prefix, p1);
            SetScoreText(player2Text, player2Prefix, p2);
        }

        private void HandleActivePlayerChanged(FlickDomPlayerId activePlayer)
        {
            RefreshTurnIndicator();
        }

        private void RefreshTurnIndicator()
        {
            SetTurnText(player1TurnText, gameModeManager != null && gameModeManager.ActivePlayer == FlickDomPlayerId.Player1, yourTurnText);
            SetTurnText(player2TurnText, gameModeManager != null && gameModeManager.ActivePlayer == FlickDomPlayerId.Player2, yourTurnText);
        }

        private void BuildHud()
        {
            GameObject canvasObject = new GameObject("Generated Player Score HUD");
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            player1Text = CreateScoreText("Player 1 Score", TextAnchor.UpperLeft, player1Color, player1Offset);
            player2Text = CreateScoreText("Player 2 Score", TextAnchor.UpperRight, player2Color, player2Offset);
            player1TurnText = CreateTurnText("Player 1 Turn", TextAnchor.UpperLeft, player1Color, player1Offset + turnOffset);
            player2TurnText = CreateTurnText("Player 2 Turn", TextAnchor.UpperRight, player2Color, player2Offset + turnOffset);
        }

        private Text CreateScoreText(
            string objectName,
            TextAnchor alignment,
            Color textColor,
            Vector2 anchoredPosition)
        {
            GameObject textObject = new GameObject(objectName);
            textObject.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = textObject.AddComponent<RectTransform>();
            rectTransform.sizeDelta = labelSize;
            rectTransform.anchoredPosition = anchoredPosition;

            if (alignment == TextAnchor.UpperLeft)
            {
                rectTransform.anchorMin = new Vector2(0f, 1f);
                rectTransform.anchorMax = new Vector2(0f, 1f);
                rectTransform.pivot = new Vector2(0f, 1f);
            }
            else
            {
                rectTransform.anchorMin = new Vector2(1f, 1f);
                rectTransform.anchorMax = new Vector2(1f, 1f);
                rectTransform.pivot = new Vector2(1f, 1f);
            }

            Text text = textObject.AddComponent<Text>();
            text.font = ResolveFont();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = textColor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = outlineDistance;

            return text;
        }

        private Text CreateTurnText(
            string objectName,
            TextAnchor alignment,
            Color textColor,
            Vector2 anchoredPosition)
        {
            GameObject textObject = new GameObject(objectName);
            textObject.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = textObject.AddComponent<RectTransform>();
            rectTransform.sizeDelta = turnLabelSize;
            rectTransform.anchoredPosition = anchoredPosition;

            if (alignment == TextAnchor.UpperLeft)
            {
                rectTransform.anchorMin = new Vector2(0f, 1f);
                rectTransform.anchorMax = new Vector2(0f, 1f);
                rectTransform.pivot = new Vector2(0f, 1f);
            }
            else
            {
                rectTransform.anchorMin = new Vector2(1f, 1f);
                rectTransform.anchorMax = new Vector2(1f, 1f);
                rectTransform.pivot = new Vector2(1f, 1f);
            }

            Text text = textObject.AddComponent<Text>();
            text.font = ResolveFont();
            text.fontSize = Mathf.Max(16, fontSize - 8);
            text.alignment = alignment;
            text.color = textColor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = outlineDistance;

            text.text = string.Empty;
            return text;
        }

        private Font ResolveFont()
        {
            if (font != null)
            {
                return font;
            }

            Font dynamicFont = Font.CreateDynamicFontFromOSFont("Arial", fontSize);
            if (dynamicFont != null)
            {
                return dynamicFont;
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static void SetScoreText(Text text, string prefix, int score)
        {
            if (text == null)
            {
                return;
            }

            text.text = prefix + "  " + score;
        }

        private static void SetTurnText(Text text, bool isActive, string turnText)
        {
            if (text == null)
            {
                return;
            }

            text.text = isActive ? turnText : string.Empty;
        }
    }
}
