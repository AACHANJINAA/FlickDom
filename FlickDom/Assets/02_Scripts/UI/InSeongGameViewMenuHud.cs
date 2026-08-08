using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.UI;

namespace FlickDom.Gameplay
{
    public sealed class InSeongGameViewMenuHud : MonoBehaviour
    {
        private const string DefaultBundledFontResourcePath = "Fonts/NotoSansKR-VF";

        [Header("References")]
        [SerializeField] private PatternCardManager cardManager;
        [SerializeField] private PlacementCameraController placementCameraController;
        [SerializeField] private Font font;
        [SerializeField] private string bundledFontResourcePath = DefaultBundledFontResourcePath;
        [SerializeField] private string[] fallbackFontNames =
        {
            "Malgun Gothic",
            "Segoe UI",
            "Arial Unicode MS",
            "Arial"
        };

        [Header("Cards")]
        [SerializeField] private PatternCardTextureBinding[] cardTextureBindings;
        [SerializeField] private Texture2D[] cardTextures;
        [SerializeField] private Vector2 cardPanelSize = new Vector2(680f, 464f);
        [SerializeField] private Vector2 cardImageSize = new Vector2(260f, 337f);
        [SerializeField] private float cardImageSpacing = 28f;

        [Header("Layout")]
        [SerializeField] private Vector2 menuButtonSize = new Vector2(116f, 38f);
        [SerializeField] private Vector2 menuButtonOffset = new Vector2(0f, -10f);
        [SerializeField] private Vector2 menuPanelSize = new Vector2(380f, 260f);
        [SerializeField] private Vector2 menuPanelOffset = new Vector2(0f, -82f);
        [SerializeField] private Vector2 returnButtonSize = new Vector2(120f, 57f);
        [SerializeField] private Vector2 returnButtonOffset = new Vector2(0f, -22f);
        [SerializeField] private int buttonFontSize = 24;
        [SerializeField] private int emptyCardFontSize = 28;
        [SerializeField] private Vector2 roundChangePanelSize = new Vector2(420f, 95f);
        [SerializeField] private int roundChangeFontSize = 42;
        [SerializeField] private float roundChangeMessageSeconds = 1.6f;

        [Header("Text")]
        [SerializeField] private string menuText = "Menu";
        [SerializeField] private string cardViewText = "Cards";
        [SerializeField] private string placementViewText = "Board";
        [SerializeField] private string returnText = "Back";
        [SerializeField] private string emptyCardText = "No Card";
        [SerializeField] private string roundChangeText = "Round Change";

        [Header("Colors")]
        [SerializeField] private Color panelColor = new Color(0.08f, 0.09f, 0.1f, 0.86f);
        [SerializeField] private Color buttonColor = new Color(0.93f, 0.95f, 0.96f, 0.96f);
        [SerializeField] private Color buttonHighlightedColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color buttonPressedColor = new Color(0.78f, 0.84f, 0.9f, 1f);
        [SerializeField] private Color buttonTextColor = new Color(0.08f, 0.1f, 0.12f, 1f);
        [SerializeField] private Color cardBackgroundColor = new Color(1f, 1f, 1f, 0.97f);
        [SerializeField] private Color emptyCardTextColor = new Color(0.94f, 0.96f, 0.98f, 1f);
        [SerializeField] private Color roundChangePanelColor = new Color(0.05f, 0.06f, 0.07f, 0.9f);
        [SerializeField] private Color roundChangeTextColor = new Color(1f, 1f, 1f, 1f);


        // 아트 스킨 슬롯. 전부 선택 사항이며, 비워두면 기존 단색 UI 그대로 동작한다.
        // (황인성, 2026-08-08)
        [Header("Art Skin (optional)")]
        [SerializeField] private Sprite panelSprite;
        [SerializeField] private Sprite buttonSprite;
        [SerializeField] private Sprite cardFrameSprite;
        [SerializeField] private Sprite ribbonSprite;

        private static void ApplyFixedSkin(Image image, Sprite sprite)
        {
            if (image == null || sprite == null)
            {
                return;
            }

            image.sprite = sprite;
            image.color = Color.white;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
        }

        private static Vector2 ResolveFixedSpriteSize(Vector2 requestedSize, Sprite sprite)
        {
            if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f)
            {
                return requestedSize;
            }

            float width = Mathf.Max(1f, requestedSize.x);
            return new Vector2(width, width * (sprite.rect.height / sprite.rect.width));
        }

        private Canvas canvas;
        private GameObject screenBlocker;
        private GameObject menuButtonObject;
        private GameObject menuPanel;
        private GameObject cardPanel;
        private GameObject placementReturnPanel;
        private GameObject roundChangePanel;
        private RectTransform cardContent;
        private Font resolvedFont;
        private bool placementPreviewOpen;
        private Coroutine roundChangeRoutine;

        private void Awake()
        {
            ApplyWebSafeText();

            if (cardManager == null)
            {
                cardManager = GetComponent<PatternCardManager>();
            }

            if (placementCameraController == null)
            {
                placementCameraController = GetComponent<PlacementCameraController>();
            }

            BuildHud();
            ShowIdleState();
        }

        private void ApplyWebSafeText()
        {
            menuText = "Menu";
            cardViewText = "Cards";
            placementViewText = "Board";
            returnText = "Back";
            emptyCardText = "No Card";
        }

        private void OnEnable()
        {
            if (cardManager == null)
            {
                return;
            }

            cardManager.ActiveCardChanged += HandleActiveCardChanged;
            cardManager.CardCompleted += HandleCardCompleted;
            cardManager.CardsExhausted += HandleCardsExhausted;
        }

        private void OnDisable()
        {
            if (cardManager != null)
            {
                cardManager.ActiveCardChanged -= HandleActiveCardChanged;
                cardManager.CardCompleted -= HandleCardCompleted;
                cardManager.CardsExhausted -= HandleCardsExhausted;
            }

            StopRoundChangeRoutine();
            ClosePlacementPreview();
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
            cardPanelSize.x = Mathf.Max(260f, cardPanelSize.x);
            cardPanelSize.y = Mathf.Max(220f, cardPanelSize.y);
            cardImageSize.x = Mathf.Max(80f, cardImageSize.x);
            cardImageSize.y = Mathf.Max(120f, cardImageSize.y);
            cardImageSpacing = Mathf.Max(0f, cardImageSpacing);
            menuButtonSize.x = Mathf.Max(80f, menuButtonSize.x);
            menuButtonSize.y = Mathf.Max(32f, menuButtonSize.y);
            menuPanelSize.x = Mathf.Max(180f, menuPanelSize.x);
            menuPanelSize.y = Mathf.Max(150f, menuPanelSize.y);
            returnButtonSize.x = Mathf.Max(100f, returnButtonSize.x);
            returnButtonSize.y = Mathf.Max(36f, returnButtonSize.y);
            buttonFontSize = Mathf.Max(10, buttonFontSize);
            emptyCardFontSize = Mathf.Max(10, emptyCardFontSize);
            roundChangePanelSize.x = Mathf.Max(180f, roundChangePanelSize.x);
            roundChangePanelSize.y = Mathf.Max(60f, roundChangePanelSize.y);
            roundChangeFontSize = Mathf.Max(10, roundChangeFontSize);
            roundChangeMessageSeconds = Mathf.Max(0f, roundChangeMessageSeconds);
        }

        private void HandleActiveCardChanged(PatternCardData card)
        {
            if (cardPanel != null && cardPanel.activeSelf)
            {
                RebuildCardContent();
            }
        }

        private void HandleCardsExhausted()
        {
            ShowRoundChangeMessage();
        }

        private void HandleCardCompleted(
            PatternCardData card,
            FlickDomPlayerId player,
            int score,
            Vector2Int matchOrigin)
        {
            if (cardPanel != null && cardPanel.activeSelf)
            {
                RebuildCardContent();
            }
        }

        private void BuildHud()
        {
            EnsureEventSystem();

            GameObject canvasObject = new GameObject("Generated Game View Menu HUD");
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            screenBlocker = CreateScreenBlocker();
            menuButtonObject = CreateTopButton("Menu Button", menuText, menuButtonSize, menuButtonOffset, ShowMenuPanel);
            menuPanel = CreateMenuPanel();
            cardPanel = CreateCardPanel();
            placementReturnPanel = CreatePlacementReturnPanel();
            roundChangePanel = CreateRoundChangePanel();
        }

        private GameObject CreateScreenBlocker()
        {
            GameObject blocker = new GameObject("Menu Screen Blocker");
            blocker.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = blocker.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            Image image = blocker.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.001f);
            image.raycastTarget = true;
            return blocker;
        }

        private GameObject CreateMenuPanel()
        {
            GameObject panel = CreatePanel("Menu Panel", menuPanelSize, menuPanelOffset, new Vector2(0.5f, 1f), panelColor, panelSprite);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 18, 18);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            CreatePanelButton("Card View Button", cardViewText, panel.transform, ShowCardPanel);
            CreatePanelButton("Placement View Button", placementViewText, panel.transform, ShowPlacementPreview);
            CreatePanelButton("Return Button", returnText, panel.transform, ReturnToGameView);
            return panel;
        }

        private GameObject CreateCardPanel()
        {
            GameObject panel = CreatePanel("Card Panel", cardPanelSize, Vector2.zero, new Vector2(0.5f, 0.5f), panelColor, panelSprite);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            GameObject contentObject = new GameObject("Card Content");
            contentObject.transform.SetParent(panel.transform, false);
            cardContent = contentObject.AddComponent<RectTransform>();
            LayoutElement contentLayout = contentObject.AddComponent<LayoutElement>();
            contentLayout.flexibleWidth = 1f;
            contentLayout.flexibleHeight = 1f;
            contentLayout.minHeight = ResolveFixedSpriteSize(cardImageSize, cardFrameSprite).y;
            HorizontalLayoutGroup contentGroup = contentObject.AddComponent<HorizontalLayoutGroup>();
            contentGroup.spacing = cardImageSpacing;
            contentGroup.childAlignment = TextAnchor.MiddleCenter;
            contentGroup.childControlWidth = false;
            contentGroup.childControlHeight = false;
            contentGroup.childForceExpandWidth = false;
            contentGroup.childForceExpandHeight = false;

            Button returnButton = CreatePanelButton("Card Return Button", returnText, panel.transform, ReturnToGameView);
            LayoutElement returnLayout = returnButton.GetComponent<LayoutElement>();
            Vector2 resolvedReturnSize = ResolveFixedSpriteSize(returnButtonSize, buttonSprite);
            returnLayout.minWidth = resolvedReturnSize.x;
            returnLayout.minHeight = resolvedReturnSize.y;
            returnLayout.preferredWidth = resolvedReturnSize.x;
            returnLayout.preferredHeight = resolvedReturnSize.y;
            return panel;
        }

        private GameObject CreatePlacementReturnPanel()
        {
            GameObject panel = new GameObject("Placement Preview Return Panel");
            panel.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = panel.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 1f);
            rectTransform.anchorMax = new Vector2(0.5f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = returnButtonOffset;
            rectTransform.sizeDelta = ResolveFixedSpriteSize(returnButtonSize, buttonSprite);

            Button returnButton = CreateButton("Placement Preview Return Button", returnText, panel.transform, ReturnToGameView);
            RectTransform buttonRect = returnButton.GetComponent<RectTransform>();
            buttonRect.anchorMin = Vector2.zero;
            buttonRect.anchorMax = Vector2.one;
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;
            return panel;
        }

        private GameObject CreateRoundChangePanel()
        {
            GameObject panel = CreatePanel(
                "Round Change Panel",
                roundChangePanelSize,
                Vector2.zero,
                new Vector2(0.5f, 0.5f),
                roundChangePanelColor,
                ribbonSprite);
            panel.GetComponent<Image>().raycastTarget = false;

            GameObject textObject = new GameObject("Round Change Text");
            textObject.transform.SetParent(panel.transform, false);
            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 4f);
            textRect.offsetMax = new Vector2(-12f, -4f);

            Text text = textObject.AddComponent<Text>();
            text.font = ResolveFont(roundChangeText, roundChangeFontSize);
            text.fontSize = roundChangeFontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = roundChangeTextColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = roundChangeText;
            return panel;
        }

        private GameObject CreatePanel(
            string objectName,
            Vector2 size,
            Vector2 anchoredPosition,
            Vector2 anchor,
            Color color,
            Sprite sprite)
        {
            GameObject panel = new GameObject(objectName);
            panel.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = panel.AddComponent<RectTransform>();
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.pivot = anchor;
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = ResolveFixedSpriteSize(size, sprite);

            Image image = panel.AddComponent<Image>();
            image.color = color;
            ApplyFixedSkin(image, sprite);
            image.raycastTarget = true;
            return panel;
        }

        private GameObject CreateTopButton(
            string objectName,
            string text,
            Vector2 size,
            Vector2 anchoredPosition,
            UnityEngine.Events.UnityAction onClick)
        {
            GameObject buttonObject = new GameObject(objectName);
            buttonObject.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = buttonObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 1f);
            rectTransform.anchorMax = new Vector2(0.5f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = ResolveFixedSpriteSize(size, buttonSprite);

            CreateButtonComponents(buttonObject, text, onClick);
            Text buttonText = buttonObject.GetComponentInChildren<Text>();
            if (buttonText != null)
            {
                buttonText.fontSize = Mathf.Max(16, buttonFontSize - 4);
            }
            return buttonObject;
        }

        private Button CreatePanelButton(
            string objectName,
            string text,
            Transform parent,
            UnityEngine.Events.UnityAction onClick)
        {
            Button button = CreateButton(objectName, text, parent, onClick);
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            Vector2 fixedSize = ResolveFixedSpriteSize(new Vector2(135f, 64f), buttonSprite);
            layout.minWidth = fixedSize.x;
            layout.minHeight = fixedSize.y;
            layout.preferredWidth = fixedSize.x;
            layout.preferredHeight = fixedSize.y;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;
            return button;
        }

        private Button CreateButton(
            string objectName,
            string text,
            Transform parent,
            UnityEngine.Events.UnityAction onClick)
        {
            GameObject buttonObject = new GameObject(objectName);
            buttonObject.transform.SetParent(parent, false);
            buttonObject.AddComponent<RectTransform>();
            return CreateButtonComponents(buttonObject, text, onClick);
        }

        private Button CreateButtonComponents(
            GameObject buttonObject,
            string text,
            UnityEngine.Events.UnityAction onClick)
        {
            Image image = buttonObject.AddComponent<Image>();
            image.color = buttonColor;
            ApplyFixedSkin(image, buttonSprite);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            UiButtonClickSound.Attach(button);
            button.onClick.AddListener(onClick);

            ColorBlock colors = button.colors;
            if (buttonSprite != null)
            {
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 0.97f, 0.86f, 1f);
                colors.pressedColor = new Color(0.82f, 0.76f, 0.66f, 1f);
                colors.selectedColor = Color.white;
                colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            }
            else
            {
                colors.normalColor = buttonColor;
                colors.highlightedColor = buttonHighlightedColor;
                colors.pressedColor = buttonPressedColor;
                colors.selectedColor = buttonHighlightedColor;
                colors.disabledColor = new Color(buttonColor.r, buttonColor.g, buttonColor.b, 0.35f);
            }
            button.colors = colors;

            GameObject textObject = new GameObject("Text");
            textObject.transform.SetParent(buttonObject.transform, false);
            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 3f);
            textRect.offsetMax = new Vector2(-8f, -1f);

            Text buttonText = textObject.AddComponent<Text>();
            buttonText.font = ResolveFont(text, buttonFontSize);
            buttonText.fontSize = buttonFontSize;
            buttonText.fontStyle = FontStyle.Bold;
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.color = buttonTextColor;
            buttonText.horizontalOverflow = HorizontalWrapMode.Wrap;
            buttonText.verticalOverflow = VerticalWrapMode.Truncate;
            buttonText.raycastTarget = false;
            buttonText.text = text;
            return button;
        }

        private void ShowMenuPanel()
        {
            ClosePlacementPreview();
            screenBlocker.SetActive(true);
            menuButtonObject.SetActive(false);
            menuPanel.SetActive(true);
            cardPanel.SetActive(false);
            placementReturnPanel.SetActive(false);
        }

        private void ShowCardPanel()
        {
            ClosePlacementPreview();
            screenBlocker.SetActive(true);
            menuButtonObject.SetActive(false);
            menuPanel.SetActive(false);
            placementReturnPanel.SetActive(false);
            cardPanel.SetActive(true);
            RebuildCardContent();
        }

        private void ShowPlacementPreview()
        {
            screenBlocker.SetActive(true);
            menuButtonObject.SetActive(false);
            menuPanel.SetActive(false);
            cardPanel.SetActive(false);
            placementReturnPanel.SetActive(true);
            placementPreviewOpen = true;

            if (placementCameraController != null)
            {
                placementCameraController.ShowPlacementBoardPreview();
            }
        }

        private void ReturnToGameView()
        {
            ClosePlacementPreview();
            ShowIdleState();
        }

        private void ShowIdleState()
        {
            if (menuButtonObject != null)
            {
                menuButtonObject.SetActive(true);
            }

            if (screenBlocker != null)
            {
                screenBlocker.SetActive(false);
            }

            if (menuPanel != null)
            {
                menuPanel.SetActive(false);
            }

            if (cardPanel != null)
            {
                cardPanel.SetActive(false);
            }

            if (placementReturnPanel != null)
            {
                placementReturnPanel.SetActive(false);
            }

            if (roundChangePanel != null)
            {
                roundChangePanel.SetActive(false);
            }
        }

        private void ShowRoundChangeMessage()
        {
            if (roundChangePanel == null)
            {
                return;
            }

            roundChangePanel.SetActive(true);
            StopRoundChangeRoutine();

            if (roundChangeMessageSeconds > 0f)
            {
                roundChangeRoutine = StartCoroutine(HideRoundChangeAfterDelay());
            }
        }

        private IEnumerator HideRoundChangeAfterDelay()
        {
            yield return new WaitForSeconds(roundChangeMessageSeconds);

            if (roundChangePanel != null)
            {
                roundChangePanel.SetActive(false);
            }

            roundChangeRoutine = null;
        }

        private void StopRoundChangeRoutine()
        {
            if (roundChangeRoutine == null)
            {
                return;
            }

            StopCoroutine(roundChangeRoutine);
            roundChangeRoutine = null;
        }

        private void ClosePlacementPreview()
        {
            if (!placementPreviewOpen)
            {
                return;
            }

            placementPreviewOpen = false;
            if (placementCameraController != null)
            {
                placementCameraController.ReturnFromPlacementBoardPreview();
            }
        }

        private void RebuildCardContent()
        {
            ClearChildren(cardContent);

            List<Texture2D> textures = ResolveVisibleCardTextures();
            if (textures.Count <= 0)
            {
                CreateEmptyCardText();
                return;
            }

            for (int i = 0; i < textures.Count; i++)
            {
                CreateCardImage(textures[i]);
            }
        }

        private List<Texture2D> ResolveVisibleCardTextures()
        {
            List<Texture2D> textures = new List<Texture2D>();
            if (cardManager != null)
            {
                for (int i = 0; i < cardManager.RemainingCardCount; i++)
                {
                    PatternCardData card = cardManager.GetRemainingCard(i);
                    Texture2D texture = ResolveTextureForCard(card, i);
                    if (texture != null)
                    {
                        textures.Add(texture);
                    }
                }

                return textures;
            }

            if (cardTextures != null)
            {
                for (int i = 0; i < cardTextures.Length; i++)
                {
                    if (cardTextures[i] != null)
                    {
                        textures.Add(cardTextures[i]);
                    }
                }
            }

            return textures;
        }

        private Texture2D ResolveTextureForCard(PatternCardData card, int fallbackIndex)
        {
            Texture2D boundTexture = PatternCardTextureBinding.Resolve(cardTextureBindings, card);
            if (boundTexture != null)
            {
                return boundTexture;
            }

            if (cardTextures != null && fallbackIndex >= 0 && fallbackIndex < cardTextures.Length)
            {
                return cardTextures[fallbackIndex];
            }

            if (card == null || string.IsNullOrEmpty(card.ResourcesImagePath))
            {
                return null;
            }

            return Resources.Load<Texture2D>(card.ResourcesImagePath);
        }

        private void CreateCardImage(Texture2D texture)
        {
            GameObject frame = new GameObject("Card Image Frame");
            frame.transform.SetParent(cardContent, false);
            RectTransform frameRect = frame.AddComponent<RectTransform>();
            Vector2 fixedCardSize = ResolveFixedSpriteSize(cardImageSize, cardFrameSprite);
            frameRect.sizeDelta = fixedCardSize;

            LayoutElement layout = frame.AddComponent<LayoutElement>();
            layout.preferredWidth = fixedCardSize.x;
            layout.preferredHeight = fixedCardSize.y;

            Image frameImage = frame.AddComponent<Image>();
            frameImage.color = cardBackgroundColor;
            ApplyFixedSkin(frameImage, cardFrameSprite);

            GameObject imageObject = new GameObject("Card Image");
            imageObject.transform.SetParent(frame.transform, false);
            RectTransform imageRect = imageObject.AddComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = new Vector2(10f, 10f);
            imageRect.offsetMax = new Vector2(-10f, -10f);

            RawImage rawImage = imageObject.AddComponent<RawImage>();
            rawImage.texture = texture;
            rawImage.color = Color.white;
            rawImage.raycastTarget = false;

            AspectRatioFitter fitter = imageObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = texture.width / (float)Mathf.Max(1, texture.height);
        }

        private void CreateEmptyCardText()
        {
            GameObject textObject = new GameObject("Empty Card Text");
            textObject.transform.SetParent(cardContent, false);

            RectTransform rectTransform = textObject.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(cardPanelSize.x - 80f, 80f);

            LayoutElement layout = textObject.AddComponent<LayoutElement>();
            layout.preferredWidth = cardPanelSize.x - 80f;
            layout.preferredHeight = 80f;

            Text text = textObject.AddComponent<Text>();
            text.font = ResolveFont(emptyCardText, emptyCardFontSize);
            text.fontSize = emptyCardFontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = emptyCardTextColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = emptyCardText;
        }

        private Font ResolveFont(string sampleText, int fontSize)
        {
            if (CanRenderText(resolvedFont, sampleText, fontSize))
            {
                return resolvedFont;
            }

            if (CanRenderText(font, sampleText, fontSize))
            {
                resolvedFont = font;
                return font;
            }

            if (!string.IsNullOrEmpty(bundledFontResourcePath))
            {
                Font resourceFont = Resources.Load<Font>(bundledFontResourcePath);
                if (CanRenderText(resourceFont, sampleText, fontSize))
                {
                    resolvedFont = resourceFont;
                    return resourceFont;
                }
            }

            Font dynamicFont = CreateFallbackFont(sampleText, fontSize);
            if (dynamicFont != null)
            {
                resolvedFont = dynamicFont;
                return dynamicFont;
            }

            if (font != null)
            {
                return font;
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private Font CreateFallbackFont(string sampleText, int fontSize)
        {
            Font dynamicFont = CreateDynamicFont(fallbackFontNames, fontSize);
            if (CanRenderText(dynamicFont, sampleText, fontSize))
            {
                return dynamicFont;
            }

            dynamicFont = Font.CreateDynamicFontFromOSFont("Malgun Gothic", fontSize);
            if (CanRenderText(dynamicFont, sampleText, fontSize))
            {
                return dynamicFont;
            }

            dynamicFont = Font.CreateDynamicFontFromOSFont("Arial", fontSize);
            if (dynamicFont != null)
            {
                return dynamicFont;
            }

            return null;
        }

        private static Font CreateDynamicFont(string[] fontNames, int fontSize)
        {
            if (fontNames == null || fontNames.Length <= 0)
            {
                return null;
            }

            List<string> validNames = new List<string>();
            for (int i = 0; i < fontNames.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(fontNames[i]))
                {
                    validNames.Add(fontNames[i]);
                }
            }

            return validNames.Count > 0
                ? Font.CreateDynamicFontFromOSFont(validNames.ToArray(), fontSize)
                : null;
        }

        private static bool CanRenderText(Font candidate, string sampleText, int fontSize)
        {
            if (candidate == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(sampleText))
            {
                return true;
            }

            candidate.RequestCharactersInTexture(sampleText, fontSize, FontStyle.Normal);
            for (int i = 0; i < sampleText.Length; i++)
            {
                char character = sampleText[i];
                if (!char.IsWhiteSpace(character) && !candidate.HasCharacter(character))
                {
                    return false;
                }
            }

            return true;
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystemObject = new GameObject("Generated UI EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
