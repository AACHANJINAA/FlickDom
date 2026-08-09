using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using FlickDom.Networking;

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
        [SerializeField] private Vector2 orderLabelSize = new Vector2(260f, 42f);
        [SerializeField] private Vector2 winLabelSize = new Vector2(520f, 120f);
        [SerializeField] private Vector2 restartButtonSize = new Vector2(220f, 56f);
        [SerializeField] private Vector2 returnToMenuButtonSize = new Vector2(220f, 56f);
        [SerializeField] private Vector2 player1Offset = new Vector2(28f, -24f);
        [SerializeField] private Vector2 player2Offset = new Vector2(-28f, -24f);
        [SerializeField] private Vector2 turnOffset = new Vector2(0f, -34f);
        [SerializeField] private Vector2 orderOffset = new Vector2(0f, -72f);
        [SerializeField] private Vector2 restartButtonOffset = new Vector2(0f, -78f);
        [SerializeField] private Vector2 returnToMenuButtonOffset = new Vector2(0f, -142f);

        [Header("Text")]
        [SerializeField] private string player1Prefix = "P1";
        [SerializeField] private string player2Prefix = "P2";
        [SerializeField] private string yourTurnText = "Your Turn";
        [SerializeField] private string orderSeparatorText = "  ";
        [SerializeField] private string player1WinText = "P1 WIN!!";
        [SerializeField] private string player2WinText = "P2 WIN!!";
        [SerializeField] private string restartButtonText = "RESTART";
        [SerializeField] private string returnToMenuButtonText = "MENU";
        [SerializeField] private Color player1Color = new Color(0.18f, 0.42f, 1f, 1f);
        [SerializeField] private Color player2Color = new Color(1f, 0.22f, 0.18f, 1f);
        [SerializeField] private Color firstOrderColor = new Color(1f, 0.68f, 0.05f, 1f);
        [SerializeField] private Color secondOrderColor = new Color(0.18f, 0.82f, 0.48f, 1f);
        [SerializeField] private Color thirdOrderColor = new Color(0.15f, 0.68f, 1f, 1f);
        [SerializeField] private Color outlineColor = new Color(0f, 0f, 0f, 0.8f);
        [SerializeField] private Vector2 outlineDistance = new Vector2(2f, -2f);
        [SerializeField] private Color restartButtonColor = new Color(0.93f, 0.95f, 0.96f, 0.96f);
        [SerializeField] private Color restartButtonHighlightedColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color restartButtonPressedColor = new Color(0.78f, 0.84f, 0.9f, 1f);
        [SerializeField] private Color restartButtonTextColor = new Color(0.08f, 0.1f, 0.12f, 1f);

        private const string DefaultOrderMarkup =
            "<color=#FFAD0D>1</color>  <color=#2ED17A>2</color>  <color=#26ADFF>3</color>";
        private const string GetPointSoundResourcePath = "Audio/GetPoint";
        private const string GetPointAudioObjectName = "Player Score Get Point Audio";
        private const string YouWinSoundResourcePath = "Audio/YouWin";
        private const string YouWinAudioObjectName = "Player Score You Win Audio";
        private static readonly Vector2 RuntimeRestartButtonOffset = new Vector2(0f, -78f);
        private static readonly Vector2 RuntimeReturnToMenuButtonOffset = new Vector2(0f, -142f);

        private Canvas canvas;
        private Text player1Text;
        private Text player2Text;
        private Text player1TurnText;
        private Text player2TurnText;
        private Text player1OrderText;
        private Text player2OrderText;
        private Text winText;
        private Button restartButton;
        private Button returnToMenuButton;
        private ScoreCardCollectAnimator scoreCardCollectAnimator;
        private LocalFlickTurnTestRig turnTestRig;
        private int lastPlayer1Score;
        private int lastPlayer2Score;
        private bool hasScoreSnapshot;
        private static AudioSource getPointAudioSource;
        private static AudioClip getPointSoundClip;
        private static AudioSource youWinAudioSource;
        private static AudioClip youWinSoundClip;

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

            turnTestRig = GetComponent<LocalFlickTurnTestRig>();
            BuildHud();
            PreloadGetPointSound();
            PreloadYouWinSound();
        }

        private void OnEnable()
        {
            if (cardManager != null)
            {
                cardManager.ScoreChanged += HandleScoreChanged;
                cardManager.CardCompleted += HandleCardCompleted;
                cardManager.MatchWon += HandleMatchWon;
            }

            if (gameModeManager != null)
            {
                gameModeManager.ActivePlayerChanged += HandleActivePlayerChanged;
                gameModeManager.StateChanged += HandleStateChanged;
            }

            if (turnTestRig != null)
            {
                turnTestRig.PieceOrderChanged += HandlePieceOrderChanged;
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
                cardManager.CardCompleted -= HandleCardCompleted;
                cardManager.MatchWon -= HandleMatchWon;
            }

            if (gameModeManager != null)
            {
                gameModeManager.ActivePlayerChanged -= HandleActivePlayerChanged;
                gameModeManager.StateChanged -= HandleStateChanged;
            }

            if (turnTestRig != null)
            {
                turnTestRig.PieceOrderChanged -= HandlePieceOrderChanged;
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
            orderLabelSize.x = Mathf.Max(80f, orderLabelSize.x);
            orderLabelSize.y = Mathf.Max(20f, orderLabelSize.y);
            winLabelSize.x = Mathf.Max(180f, winLabelSize.x);
            winLabelSize.y = Mathf.Max(48f, winLabelSize.y);
            restartButtonSize.x = Mathf.Max(120f, restartButtonSize.x);
            restartButtonSize.y = Mathf.Max(36f, restartButtonSize.y);
            returnToMenuButtonSize.x = Mathf.Max(120f, returnToMenuButtonSize.x);
            returnToMenuButtonSize.y = Mathf.Max(36f, returnToMenuButtonSize.y);
        }

        private void HandleScoreChanged(
            FlickDomPlayerId player,
            int gainedScore,
            int player1Score,
            int player2Score)
        {
            PlayGetPointSoundIfScoreIncreased(player1Score, player2Score);
            SetScoreText(player1Text, player1Prefix, player1Score);
            SetScoreText(player2Text, player2Prefix, player2Score);
            StoreScoreSnapshot(player1Score, player2Score);

            if (cardManager == null || cardManager.Winner == FlickDomPlayerId.None)
            {
                HideVictoryControls();
            }
        }

        private void HandleCardCompleted(
            PatternCardData card,
            FlickDomPlayerId player,
            int gainedScore,
            Vector2Int matchOrigin)
        {
            if (scoreCardCollectAnimator == null)
            {
                return;
            }

            scoreCardCollectAnimator.Play(
                card,
                player,
                player1Text != null ? player1Text.rectTransform : null,
                player2Text != null ? player2Text.rectTransform : null);
        }

        private void RefreshScores()
        {
            int p1 = cardManager != null ? cardManager.Player1Score : 0;
            int p2 = cardManager != null ? cardManager.Player2Score : 0;
            SetScoreText(player1Text, player1Prefix, p1);
            SetScoreText(player2Text, player2Prefix, p2);
            StoreScoreSnapshot(p1, p2);
        }

        private void HandleMatchWon(FlickDomPlayerId winner, int player1Score, int player2Score)
        {
            PlayYouWinSound();
            SetScoreText(player1Text, player1Prefix, player1Score);
            SetScoreText(player2Text, player2Prefix, player2Score);
            StoreScoreSnapshot(player1Score, player2Score);
            RefreshTurnIndicator();
            ShowWinText(winner);
            SetRestartButtonVisible(true);
        }

        private void HandleActivePlayerChanged(FlickDomPlayerId activePlayer)
        {
            RefreshTurnIndicator();
        }

        private void HandleStateChanged(FlickDomGameState previousState, FlickDomGameState nextState)
        {
            RefreshTurnIndicator();
            if ((cardManager == null || cardManager.Winner == FlickDomPlayerId.None)
                && nextState != FlickDomGameState.CardMatch
                && nextState != FlickDomGameState.PlacementSelection)
            {
                HideVictoryControls();
            }
        }

        private void HandlePieceOrderChanged(FlickDomPlayerId player)
        {
            RefreshTurnIndicator();
        }

        private void RefreshTurnIndicator()
        {
            SetTurnText(player1TurnText, gameModeManager != null && gameModeManager.ActivePlayer == FlickDomPlayerId.Player1, yourTurnText);
            SetTurnText(player2TurnText, gameModeManager != null && gameModeManager.ActivePlayer == FlickDomPlayerId.Player2, yourTurnText);
            RefreshOrderIndicator();
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
            scoreCardCollectAnimator = canvasObject.AddComponent<ScoreCardCollectAnimator>();
            scoreCardCollectAnimator.Initialize(canvas);

            player1Text = CreateScoreText("Player 1 Score", TextAnchor.UpperLeft, player1Color, player1Offset);
            player2Text = CreateScoreText("Player 2 Score", TextAnchor.UpperRight, player2Color, player2Offset);
            player1TurnText = CreateTurnText("Player 1 Turn", TextAnchor.UpperLeft, player1Color, player1Offset + turnOffset);
            player2TurnText = CreateTurnText("Player 2 Turn", TextAnchor.UpperRight, player2Color, player2Offset + turnOffset);
            player1OrderText = CreateOrderText("Player 1 Order", TextAnchor.UpperLeft, player1Color, player1Offset + orderOffset);
            player2OrderText = CreateOrderText("Player 2 Order", TextAnchor.UpperRight, player2Color, player2Offset + orderOffset);
            winText = CreateWinText("Winner Text");
            restartButton = CreateVictoryButton("Restart Button", restartButtonText, RuntimeRestartButtonOffset, restartButtonSize, RestartCurrentScene);
            returnToMenuButton = CreateVictoryButton("Return To Menu Button", returnToMenuButtonText, RuntimeReturnToMenuButtonOffset, returnToMenuButtonSize, ReturnToMenu);
            SetVictoryButtonsVisible(false);
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

        private Text CreateOrderText(
            string objectName,
            TextAnchor alignment,
            Color textColor,
            Vector2 anchoredPosition)
        {
            GameObject textObject = new GameObject(objectName);
            textObject.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = textObject.AddComponent<RectTransform>();
            rectTransform.sizeDelta = orderLabelSize;
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
            text.fontSize = Mathf.Max(18, fontSize - 6);
            text.alignment = alignment;
            text.color = textColor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = true;
            text.text = string.Empty;

            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = outlineDistance;

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

        private Text CreateWinText(string objectName)
        {
            GameObject textObject = new GameObject(objectName);
            textObject.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = textObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = winLabelSize;

            Text text = textObject.AddComponent<Text>();
            text.font = ResolveFont();
            text.fontSize = fontSize + 18;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = string.Empty;

            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new Vector2(3f, -3f);

            return text;
        }

        private void ShowWinText(FlickDomPlayerId winner)
        {
            if (winText == null)
            {
                return;
            }

            if (winner == FlickDomPlayerId.Player2)
            {
                winText.text = player2WinText;
                winText.color = player2Color;
                return;
            }

            winText.text = player1WinText;
            winText.color = player1Color;
        }

        private Button CreateVictoryButton(
            string objectName,
            string buttonLabel,
            Vector2 anchoredPosition,
            Vector2 size,
            UnityEngine.Events.UnityAction onClick)
        {
            GameObject buttonObject = new GameObject(objectName);
            buttonObject.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = buttonObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = size;

            Image image = buttonObject.AddComponent<Image>();
            image.color = restartButtonColor;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            UiButtonClickSound.Attach(button);
            button.onClick.AddListener(onClick);

            ColorBlock colors = button.colors;
            colors.normalColor = restartButtonColor;
            colors.highlightedColor = restartButtonHighlightedColor;
            colors.pressedColor = restartButtonPressedColor;
            colors.selectedColor = restartButtonHighlightedColor;
            button.colors = colors;

            GameObject textObject = new GameObject("Text");
            textObject.transform.SetParent(buttonObject.transform, false);

            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 2f);
            textRect.offsetMax = new Vector2(-8f, -2f);

            Text buttonText = textObject.AddComponent<Text>();
            buttonText.font = ResolveFont();
            buttonText.fontSize = Mathf.Max(18, fontSize - 4);
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.color = restartButtonTextColor;
            buttonText.horizontalOverflow = HorizontalWrapMode.Overflow;
            buttonText.verticalOverflow = VerticalWrapMode.Overflow;
            buttonText.raycastTarget = false;
            buttonText.text = buttonLabel;

            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = outlineDistance;

            return button;
        }

        private void RestartCurrentScene()
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            if (bootstrap != null)
            {
                bootstrap.RestartMatchFromUi();
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
        }

        private void ReturnToMenu()
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            if (bootstrap != null)
            {
                bootstrap.ReturnToLobbyFromUi();
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
        }

        private void SetRestartButtonVisible(bool visible)
        {
            SetVictoryButtonsVisible(visible);
        }

        private void SetVictoryButtonsVisible(bool visible)
        {
            if (restartButton == null)
            {
                return;
            }

            restartButton.gameObject.SetActive(visible);
            if (returnToMenuButton != null)
            {
                returnToMenuButton.gameObject.SetActive(visible);
            }
        }

        private void HideVictoryControls()
        {
            if (winText != null)
            {
                winText.text = string.Empty;
            }

            SetVictoryButtonsVisible(false);
        }

        private static void SetScoreText(Text text, string prefix, int score)
        {
            if (text == null)
            {
                return;
            }

            text.text = prefix + "  " + score;
        }

        private void PlayGetPointSoundIfScoreIncreased(int player1Score, int player2Score)
        {
            if (!hasScoreSnapshot)
            {
                return;
            }

            if (player1Score > lastPlayer1Score || player2Score > lastPlayer2Score)
            {
                PlayGetPointSound();
            }
        }

        private void StoreScoreSnapshot(int player1Score, int player2Score)
        {
            lastPlayer1Score = player1Score;
            lastPlayer2Score = player2Score;
            hasScoreSnapshot = true;
        }

        private static void PlayGetPointSound()
        {
            EnsureGetPointAudioSource();
            EnsureGetPointSoundClip();
            if (getPointAudioSource == null || getPointSoundClip == null)
            {
                return;
            }

            getPointAudioSource.PlayOneShot(getPointSoundClip);
        }

        private static void PreloadGetPointSound()
        {
            EnsureGetPointAudioSource();
            EnsureGetPointSoundClip();
        }

        private static void EnsureGetPointAudioSource()
        {
            if (getPointAudioSource != null)
            {
                return;
            }

            GameObject audioObject = GameObject.Find(GetPointAudioObjectName);
            if (audioObject == null)
            {
                audioObject = new GameObject(GetPointAudioObjectName);
                DontDestroyOnLoad(audioObject);
            }

            if (!audioObject.TryGetComponent(out getPointAudioSource))
            {
                getPointAudioSource = audioObject.AddComponent<AudioSource>();
            }

            getPointAudioSource.playOnAwake = false;
            getPointAudioSource.loop = false;
            getPointAudioSource.spatialBlend = 0f;
        }

        private static void EnsureGetPointSoundClip()
        {
            if (getPointSoundClip != null)
            {
                return;
            }

            getPointSoundClip = Resources.Load<AudioClip>(GetPointSoundResourcePath);
            if (getPointSoundClip == null)
            {
                Debug.LogWarning("[GetPoint Audio] Could not load sound at Resources/" + GetPointSoundResourcePath + ".", null);
            }
        }

        private static void PlayYouWinSound()
        {
            EnsureYouWinAudioSource();
            EnsureYouWinSoundClip();
            if (youWinAudioSource == null || youWinSoundClip == null)
            {
                return;
            }

            youWinAudioSource.PlayOneShot(youWinSoundClip);
        }

        private static void PreloadYouWinSound()
        {
            EnsureYouWinAudioSource();
            EnsureYouWinSoundClip();
        }

        private static void EnsureYouWinAudioSource()
        {
            if (youWinAudioSource != null)
            {
                return;
            }

            GameObject audioObject = GameObject.Find(YouWinAudioObjectName);
            if (audioObject == null)
            {
                audioObject = new GameObject(YouWinAudioObjectName);
                DontDestroyOnLoad(audioObject);
            }

            if (!audioObject.TryGetComponent(out youWinAudioSource))
            {
                youWinAudioSource = audioObject.AddComponent<AudioSource>();
            }

            youWinAudioSource.playOnAwake = false;
            youWinAudioSource.loop = false;
            youWinAudioSource.spatialBlend = 0f;
        }

        private static void EnsureYouWinSoundClip()
        {
            if (youWinSoundClip != null)
            {
                return;
            }

            youWinSoundClip = Resources.Load<AudioClip>(YouWinSoundResourcePath);
            if (youWinSoundClip == null)
            {
                Debug.LogWarning("[YouWin Audio] Could not load sound at Resources/" + YouWinSoundResourcePath + ".", null);
            }
        }

        private static void SetTurnText(Text text, bool isActive, string turnText)
        {
            if (text == null)
            {
                return;
            }

            text.text = isActive ? turnText : string.Empty;
        }

        private void RefreshOrderIndicator()
        {
            SetOrderText(player1OrderText, string.Empty);
            SetOrderText(player2OrderText, string.Empty);
        }

        private bool ShouldShowOrderForPlayer(FlickDomPlayerId player)
        {
            if (gameModeManager == null || turnTestRig == null)
            {
                return false;
            }

            if (gameModeManager.ActivePlayer != player)
            {
                return false;
            }

            if (gameModeManager.CurrentState != FlickDomGameState.PieceOrderSelection
                && gameModeManager.CurrentState != FlickDomGameState.PlayerFlicking
                && gameModeManager.CurrentState != FlickDomGameState.PhysicsProcessing)
            {
                return false;
            }

            return turnTestRig.GetSelectedOrderCount(player) > 0;
        }

        private string BuildOrderMarkup(FlickDomPlayerId player)
        {
            int selectedCount = turnTestRig != null ? turnTestRig.GetSelectedOrderCount(player) : 0;
            if (selectedCount <= 0)
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(DefaultOrderMarkup.Length);
            for (int i = 1; i <= selectedCount; i++)
            {
                if (builder.Length > 0)
                {
                    builder.Append(orderSeparatorText);
                }

                builder.Append("<color=");
                builder.Append(ColorUtility.ToHtmlStringRGB(GetOrderColor(i)));
                builder.Append(">");
                builder.Append(i);
                builder.Append("</color>");
            }

            return builder.ToString();
        }

        private Color GetOrderColor(int orderNumber)
        {
            switch (orderNumber)
            {
                case 1:
                    return firstOrderColor;
                case 2:
                    return secondOrderColor;
                case 3:
                    return thirdOrderColor;
                default:
                    return Color.white;
            }
        }

        private static void SetOrderText(Text text, string value)
        {
            if (text == null)
            {
                return;
            }

            text.text = value ?? string.Empty;
        }
    }
}
