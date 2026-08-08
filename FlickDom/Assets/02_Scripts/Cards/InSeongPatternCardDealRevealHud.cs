using System.Collections;
using System.Collections.Generic;
using FlickDom.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.UI;

namespace FlickDom.Gameplay
{
    public sealed class InSeongPatternCardDealRevealHud : MonoBehaviour
    {
        private const string DefaultBundledFontResourcePath = "Fonts/NotoSansKR-VF";
        private const string CardSwapSoundResourcePath = "Audio/Card_Swap";
        private const string CardSwapAudioObjectName = "Pattern Card Swap Audio";

        [Header("References")]
        [SerializeField] private PatternCardManager cardManager;
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private FlickDomNetworkBootstrap gameStartGate;
        [SerializeField] private Transform deckTransform;
        [SerializeField] private Camera worldCamera;

        [Header("Cards")]
        [SerializeField] private PatternCardTextureBinding[] cardTextureBindings;
        [SerializeField] private Texture2D[] fallbackCardTextures;
        [SerializeField] private int maxDisplayedCards = 3;
        [Tooltip("이미지 연결 여부와 관계없이 카드 데이터로 동일한 리본과 격자 UI를 생성합니다.")]
        [SerializeField] private bool useGeneratedCardVisuals = true;

        [Header("Animation")]
        [InspectorName("Show On Game Start")]
        [SerializeField] private bool showOnStart = true;
        [SerializeField] private bool replayOnCardSetChanged = true;
        [SerializeField] private int canvasSortingOrder = 130;
        [SerializeField] private float startRevealDelay = 0.35f;
        [SerializeField] private float dealDuration = 0.42f;
        [SerializeField] private float dealStagger = 0.08f;
        [SerializeField] private float fadeOutSeconds = 0.28f;
        [SerializeField] private Vector2 revealCardSize = new Vector2(200f, 259f);
        [SerializeField] private float targetSpacing = 30f;
        [SerializeField] private float targetYOffset = 20f;
        [SerializeField] private float startScale = 0.18f;
        [SerializeField] private Vector2 cardContentPadding = new Vector2(22f, 24f);
        [SerializeField] private float difficultyStripHeight = 30f;
        [SerializeField] private float difficultyStripGap = 8f;

        [Header("Confirm Button")]
        [SerializeField] private string stageTextFormat = "Stage {0}";
        [SerializeField] private Vector2 stageLabelSize = new Vector2(300f, 68f);
        [SerializeField] private float stageLabelYOffset = 220f;
        [SerializeField] private int stageLabelFontSize = 32;
        [SerializeField] private string confirmButtonText = "Confirm";
        [SerializeField] private Vector2 confirmButtonSize = new Vector2(144f, 90f);
        [SerializeField] private float confirmButtonYOffset = -207f;
        [SerializeField] private int confirmButtonFontSize = 22;
        [SerializeField] private Font bundledFont;
        [SerializeField] private string bundledFontResourcePath = DefaultBundledFontResourcePath;
        [SerializeField] private string[] fallbackFontNames =
        {
            "Malgun Gothic",
            "Segoe UI",
            "Arial Unicode MS",
            "Arial"
        };

        [Header("Colors")]
        [SerializeField] private Color dimmerColor = new Color(0.02f, 0.07f, 0.12f, 0.32f);
        [SerializeField] private Color frameColor = new Color(1f, 1f, 1f, 0.98f);
        [SerializeField] private Color stageTextColor = new Color(1f, 0.93f, 0.55f, 1f);
        [SerializeField] private Color confirmButtonColor = new Color(1f, 0.88f, 0.28f, 0.98f);
        [SerializeField] private Color confirmButtonHighlightedColor = new Color(1f, 0.96f, 0.55f, 1f);
        [SerializeField] private Color confirmButtonPressedColor = new Color(0.94f, 0.7f, 0.12f, 1f);
        [SerializeField] private Color confirmButtonTextColor = new Color(0.08f, 0.1f, 0.12f, 1f);
        [SerializeField] private Color fallbackBaseColor = new Color(0.96f, 0.94f, 0.88f, 1f);
        [SerializeField] private Color easyColor = new Color(0.42f, 0.68f, 0.12f, 1f);
        [SerializeField] private Color normalColor = new Color(0.95f, 0.72f, 0.12f, 1f);
        [SerializeField] private Color hardColor = new Color(0.82f, 0.16f, 0.16f, 1f);
        [SerializeField] private Color emptyCellColor = new Color(0.8f, 0.78f, 0.7f, 1f);


        // 아트 스킨 슬롯. 전부 선택 사항이며, 비워두면 기존 단색 UI 그대로 동작한다.
        // (황인성, 2026-08-08)
        [Header("Art Skin (optional)")]
        [SerializeField] private Sprite cardFrameSprite;
        [SerializeField] private Sprite confirmButtonSprite;
        [SerializeField] private Sprite stripSprite;

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

        private Canvas canvas;
        private RectTransform canvasRect;
        private GameObject dimmerObject;
        private GameObject stageLabelObject;
        private Text stageLabelText;
        private CanvasGroup stageLabelCanvasGroup;
        private GameObject confirmButtonObject;
        private CanvasGroup confirmButtonCanvasGroup;
        private Font resolvedFont;
        private Coroutine revealRoutine;
        private Coroutine pendingGameStartRevealRoutine;
        private readonly List<RevealCardView> activeCardViews = new List<RevealCardView>(3);
        private bool hasShownCardSet;
        private bool confirmRequested;
        private int lastShownDeckIndex = int.MinValue;
        private int lastShownDrawSeed = int.MinValue;
        private static AudioSource cardSwapAudioSource;
        private static AudioClip cardSwapSoundClip;

        private sealed class RevealCardView
        {
            public RectTransform RectTransform;
            public CanvasGroup CanvasGroup;
        }

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

            ResolveGameStartGate();

            if (deckTransform == null)
            {
                PatternCardSlot cardSlot = FindAnyObjectByType<PatternCardSlot>();
                if (cardSlot != null)
                {
                    deckTransform = cardSlot.transform;
                }
            }

            BuildCanvas();
            PreloadCardSwapSound();
        }

        private void OnEnable()
        {
            if (cardManager == null)
            {
                return;
            }

            cardManager.ActiveCardChanged += HandleActiveCardChanged;
            cardManager.CardsExhausted += HandleCardsExhausted;
        }

        private void Start()
        {
            if (showOnStart)
            {
                TryPlayCurrentCardSetAfterGameStart(false);
            }
        }

        private void OnDisable()
        {
            if (cardManager != null)
            {
                cardManager.ActiveCardChanged -= HandleActiveCardChanged;
                cardManager.CardsExhausted -= HandleCardsExhausted;
            }

            StopRevealRoutine();
            StopPendingGameStartRevealRoutine();
            ClearActiveCards();
            SetDimmerVisible(false);
            SetStageLabelVisible(false);
            SetConfirmButtonVisible(false);
        }

        private void OnDestroy()
        {
            if (canvas != null)
            {
                Destroy(canvas.gameObject);
                canvas = null;
                canvasRect = null;
            }
        }

        private void OnValidate()
        {
            maxDisplayedCards = Mathf.Max(1, maxDisplayedCards);
            canvasSortingOrder = Mathf.Max(0, canvasSortingOrder);
            startRevealDelay = Mathf.Max(0f, startRevealDelay);
            dealDuration = Mathf.Max(0.01f, dealDuration);
            dealStagger = Mathf.Max(0f, dealStagger);
            fadeOutSeconds = Mathf.Max(0.01f, fadeOutSeconds);
            revealCardSize.x = Mathf.Max(64f, revealCardSize.x);
            revealCardSize.y = Mathf.Max(96f, revealCardSize.y);
            targetSpacing = Mathf.Max(0f, targetSpacing);
            startScale = Mathf.Clamp(startScale, 0.01f, 1f);
            cardContentPadding.x = Mathf.Max(0f, cardContentPadding.x);
            cardContentPadding.y = Mathf.Max(0f, cardContentPadding.y);
            difficultyStripHeight = Mathf.Max(12f, difficultyStripHeight);
            difficultyStripGap = Mathf.Max(0f, difficultyStripGap);
            stageLabelSize.x = Mathf.Max(160f, stageLabelSize.x);
            stageLabelSize.y = Mathf.Max(36f, stageLabelSize.y);
            stageLabelFontSize = Mathf.Max(10, stageLabelFontSize);
            confirmButtonSize.x = Mathf.Max(80f, confirmButtonSize.x);
            confirmButtonSize.y = Mathf.Max(32f, confirmButtonSize.y);
            confirmButtonFontSize = Mathf.Max(10, confirmButtonFontSize);
        }

        [ContextMenu("Play Current Card Reveal")]
        public void PlayCurrentCardReveal()
        {
            TryPlayCurrentCardSet(true);
        }

        private void HandleActiveCardChanged(PatternCardData card)
        {
            if (!IsRevealGateOpen())
            {
                if (showOnStart && !hasShownCardSet)
                {
                    TryPlayCurrentCardSetAfterGameStart(false);
                }

                return;
            }

            if (showOnStart && !hasShownCardSet)
            {
                TryPlayCurrentCardSet(false);
                return;
            }

            if (replayOnCardSetChanged)
            {
                TryPlayCurrentCardSet(false);
            }
        }

        private void HandleCardsExhausted()
        {
            StopRevealRoutine();
            ClearActiveCards();
            SetDimmerVisible(false);
            SetStageLabelVisible(false);
            SetConfirmButtonVisible(false);
        }

        private void TryPlayCurrentCardSetAfterGameStart(bool force)
        {
            if (IsRevealGateOpen())
            {
                StopPendingGameStartRevealRoutine();
                pendingGameStartRevealRoutine = StartCoroutine(PlayCurrentCardSetNextFrame(force));
                return;
            }

            if (pendingGameStartRevealRoutine == null)
            {
                pendingGameStartRevealRoutine = StartCoroutine(WaitForGameStartAndPlayCurrentCardSet(force));
            }
        }

        private IEnumerator WaitForGameStartAndPlayCurrentCardSet(bool force)
        {
            while (!IsRevealGateOpen())
            {
                yield return null;
            }

            yield return null;
            pendingGameStartRevealRoutine = null;
            TryPlayCurrentCardSet(force);
        }

        private IEnumerator PlayCurrentCardSetNextFrame(bool force)
        {
            yield return null;
            pendingGameStartRevealRoutine = null;
            TryPlayCurrentCardSet(force);
        }

        private void StopPendingGameStartRevealRoutine()
        {
            if (pendingGameStartRevealRoutine == null)
            {
                return;
            }

            StopCoroutine(pendingGameStartRevealRoutine);
            pendingGameStartRevealRoutine = null;
        }

        private bool IsRevealGateOpen()
        {
            FlickDomNetworkBootstrap gate = ResolveGameStartGate();
            if (gate != null)
            {
                return gate.IsGameActive;
            }

            return gameModeManager == null
                || gameModeManager.CurrentState != FlickDomGameState.NotStarted;
        }

        private FlickDomNetworkBootstrap ResolveGameStartGate()
        {
            if (gameStartGate != null && gameStartGate.isActiveAndEnabled)
            {
                return gameStartGate;
            }

            FlickDomNetworkBootstrap activeGate = FlickDomNetworkBootstrap.Active;
            if (activeGate != null && activeGate.isActiveAndEnabled)
            {
                gameStartGate = activeGate;
                return gameStartGate;
            }

            gameStartGate = FindAnyObjectByType<FlickDomNetworkBootstrap>();
            if (gameStartGate != null && !gameStartGate.isActiveAndEnabled)
            {
                gameStartGate = null;
            }

            return gameStartGate;
        }

        private void TryPlayCurrentCardSet(bool force)
        {
            if (cardManager == null || cardManager.RemainingCardCount <= 0)
            {
                return;
            }

            int currentDeckIndex = cardManager.CurrentFallbackDeckIndex;
            int currentDrawSeed = cardManager.CardDrawSeed;
            if (!force
                && hasShownCardSet
                && currentDeckIndex == lastShownDeckIndex
                && currentDrawSeed == lastShownDrawSeed)
            {
                return;
            }

            List<PatternCardData> cards = ResolveVisibleCards();
            if (cards.Count <= 0)
            {
                return;
            }

            hasShownCardSet = true;
            lastShownDeckIndex = currentDeckIndex;
            lastShownDrawSeed = currentDrawSeed;

            StopRevealRoutine();
            revealRoutine = StartCoroutine(PlayRevealRoutine(cards));
        }

        private List<PatternCardData> ResolveVisibleCards()
        {
            int count = Mathf.Min(maxDisplayedCards, cardManager.RemainingCardCount);
            List<PatternCardData> cards = new List<PatternCardData>(count);
            for (int i = 0; i < count; i++)
            {
                PatternCardData card = cardManager.GetRemainingCard(i);
                if (card != null)
                {
                    cards.Add(card);
                }
            }

            return cards;
        }

        private IEnumerator PlayRevealRoutine(IReadOnlyList<PatternCardData> cards)
        {
            if (startRevealDelay > 0f)
            {
                yield return WaitUnscaled(startRevealDelay);
            }

            ClearActiveCards();
            SetDimmerVisible(true);
            RefreshStageLabelText();
            SetStageLabelVisible(true);
            SetConfirmButtonVisible(false);
            confirmRequested = false;

            Vector2 startPosition = ResolveDeckScreenPosition();
            Vector2[] targetPositions = BuildTargetPositions(cards.Count);

            for (int i = 0; i < cards.Count; i++)
            {
                activeCardViews.Add(CreateRevealCard(cards[i], startPosition));
            }

            float totalDealSeconds = dealDuration + (dealStagger * Mathf.Max(0, activeCardViews.Count - 1));
            float elapsed = 0f;
            bool[] playedCardSwapSounds = new bool[activeCardViews.Count];
            while (elapsed < totalDealSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                PlayCardSwapSoundsForElapsedCards(elapsed, playedCardSwapSounds);
                ApplyDealFrame(elapsed, startPosition, targetPositions);
                yield return null;
            }

            PlayCardSwapSoundsForElapsedCards(totalDealSeconds, playedCardSwapSounds);
            ApplyDealFrame(totalDealSeconds, startPosition, targetPositions);
            SetConfirmButtonVisible(true);

            while (!confirmRequested)
            {
                yield return null;
            }

            SetConfirmButtonVisible(false);
            elapsed = 0f;
            while (elapsed < fadeOutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float fade = Mathf.Clamp01(elapsed / fadeOutSeconds);
                float alpha = 1f - Smooth(fade);
                for (int i = 0; i < activeCardViews.Count; i++)
                {
                    RevealCardView view = activeCardViews[i];
                    view.CanvasGroup.alpha = alpha;
                    view.RectTransform.anchoredPosition = targetPositions[i] + new Vector2(0f, 24f * Smooth(fade));
                }

                SetDimmerAlpha(dimmerColor.a * alpha);
                yield return null;
            }

            ClearActiveCards();
            SetDimmerVisible(false);
            SetStageLabelVisible(false);
            revealRoutine = null;
        }

        private void ApplyDealFrame(
            float elapsed,
            Vector2 startPosition,
            IReadOnlyList<Vector2> targetPositions)
        {
            for (int i = 0; i < activeCardViews.Count; i++)
            {
                float cardElapsed = elapsed - (dealStagger * i);
                float progress = Mathf.Clamp01(cardElapsed / dealDuration);
                float eased = Smooth(progress);

                RevealCardView view = activeCardViews[i];
                view.RectTransform.anchoredPosition = Vector2.Lerp(startPosition, targetPositions[i], eased);
                view.RectTransform.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, eased);
                view.RectTransform.localRotation = Quaternion.identity;
                view.CanvasGroup.alpha = eased;
            }
        }

        private void PlayCardSwapSoundsForElapsedCards(float elapsed, bool[] playedCardSwapSounds)
        {
            if (playedCardSwapSounds == null)
            {
                return;
            }

            for (int i = 0; i < playedCardSwapSounds.Length; i++)
            {
                if (playedCardSwapSounds[i] || elapsed < dealStagger * i)
                {
                    continue;
                }

                playedCardSwapSounds[i] = true;
                PlayCardSwapSound();
            }
        }

        private static void PlayCardSwapSound()
        {
            EnsureCardSwapAudioSource();
            EnsureCardSwapSoundClip();
            if (cardSwapAudioSource == null || cardSwapSoundClip == null)
            {
                return;
            }

            cardSwapAudioSource.PlayOneShot(cardSwapSoundClip);
        }

        private static void PreloadCardSwapSound()
        {
            EnsureCardSwapAudioSource();
            EnsureCardSwapSoundClip();
        }

        private static void EnsureCardSwapAudioSource()
        {
            if (cardSwapAudioSource != null)
            {
                return;
            }

            GameObject audioObject = GameObject.Find(CardSwapAudioObjectName);
            if (audioObject == null)
            {
                audioObject = new GameObject(CardSwapAudioObjectName);
                DontDestroyOnLoad(audioObject);
            }

            if (!audioObject.TryGetComponent(out cardSwapAudioSource))
            {
                cardSwapAudioSource = audioObject.AddComponent<AudioSource>();
            }

            cardSwapAudioSource.playOnAwake = false;
            cardSwapAudioSource.loop = false;
            cardSwapAudioSource.spatialBlend = 0f;
        }

        private static void EnsureCardSwapSoundClip()
        {
            if (cardSwapSoundClip != null)
            {
                return;
            }

            cardSwapSoundClip = Resources.Load<AudioClip>(CardSwapSoundResourcePath);
            if (cardSwapSoundClip == null)
            {
                Debug.LogWarning("[Card Swap Audio] Could not load sound at Resources/" + CardSwapSoundResourcePath + ".", null);
            }
        }

        private void BuildCanvas()
        {
            GameObject canvasObject = new GameObject("Generated Pattern Card Deal Reveal HUD");
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = canvasSortingOrder;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
            canvasRect = canvasObject.GetComponent<RectTransform>();
            dimmerObject = CreateDimmer();
            stageLabelObject = CreateStageLabel();
            confirmButtonObject = CreateConfirmButton();
            SetDimmerVisible(false);
            SetStageLabelVisible(false);
            SetConfirmButtonVisible(false);
            EnsureEventSystem();
        }

        private GameObject CreateDimmer()
        {
            GameObject dimmer = new GameObject("Card Reveal Dimmer");
            dimmer.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = dimmer.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            Image image = dimmer.AddComponent<Image>();
            image.color = dimmerColor;
            image.raycastTarget = true;
            return dimmer;
        }

        private GameObject CreateStageLabel()
        {
            GameObject labelObject = new GameObject("Card Reveal Stage Label");
            labelObject.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = labelObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0f, stageLabelYOffset);
            rectTransform.sizeDelta = ResolveFixedSpriteSize(stageLabelSize, stripSprite);

            Image background = labelObject.AddComponent<Image>();
            ApplyFixedSkin(background, stripSprite);
            background.raycastTarget = false;

            GameObject textObject = new GameObject("Text");
            textObject.transform.SetParent(labelObject.transform, false);
            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(30f, 16f);
            textRect.offsetMax = new Vector2(-30f, 0.0f);

            stageLabelText = textObject.AddComponent<Text>();
            stageLabelText.font = ResolveFont(stageTextFormat, stageLabelFontSize);
            stageLabelText.fontSize = stageLabelFontSize;
            stageLabelText.fontStyle = FontStyle.Bold;
            stageLabelText.alignment = TextAnchor.MiddleCenter;
            stageLabelText.color = stageTextColor;
            stageLabelText.horizontalOverflow = HorizontalWrapMode.Wrap;
            stageLabelText.verticalOverflow = VerticalWrapMode.Truncate;
            stageLabelText.raycastTarget = false;

            stageLabelCanvasGroup = labelObject.AddComponent<CanvasGroup>();
            return labelObject;
        }

        private GameObject CreateConfirmButton()
        {
            GameObject buttonObject = new GameObject("Card Reveal Confirm Button");
            buttonObject.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = buttonObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0f, confirmButtonYOffset);
            rectTransform.sizeDelta = ResolveFixedSpriteSize(confirmButtonSize, confirmButtonSprite);

            Image image = buttonObject.AddComponent<Image>();
            image.color = confirmButtonColor;
            ApplyFixedSkin(image, confirmButtonSprite);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            UiButtonClickSound.Attach(button);
            button.onClick.AddListener(ConfirmReveal);

            ColorBlock colors = button.colors;
            if (confirmButtonSprite != null)
            {
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 1f, 0.88f, 1f);
                colors.pressedColor = new Color(0.8f, 0.84f, 0.64f, 1f);
                colors.selectedColor = Color.white;
                colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            }
            else
            {
                colors.normalColor = confirmButtonColor;
                colors.highlightedColor = confirmButtonHighlightedColor;
                colors.pressedColor = confirmButtonPressedColor;
                colors.selectedColor = confirmButtonHighlightedColor;
                colors.disabledColor = new Color(confirmButtonColor.r, confirmButtonColor.g, confirmButtonColor.b, 0.35f);
            }
            button.colors = colors;

            GameObject textObject = new GameObject("Text");
            textObject.transform.SetParent(buttonObject.transform, false);
            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 3f);
            textRect.offsetMax = new Vector2(-8f, -1f);

            Text text = textObject.AddComponent<Text>();
            text.font = ResolveFont(confirmButtonText, confirmButtonFontSize);
            text.fontSize = confirmButtonFontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = confirmButtonTextColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = confirmButtonText;

            confirmButtonCanvasGroup = buttonObject.AddComponent<CanvasGroup>();
            return buttonObject;
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

        private void ConfirmReveal()
        {
            confirmRequested = true;
        }

        private void RefreshStageLabelText()
        {
            if (stageLabelText == null)
            {
                return;
            }

            int stageNumber = cardManager != null ? cardManager.CurrentStageNumber : 1;
            stageLabelText.text = FormatStageText(stageNumber);
        }

        private string FormatStageText(int stageNumber)
        {
            if (string.IsNullOrEmpty(stageTextFormat))
            {
                return "Stage " + stageNumber;
            }

            try
            {
                return string.Format(stageTextFormat, stageNumber);
            }
            catch (System.FormatException)
            {
                return stageTextFormat + " " + stageNumber;
            }
        }

        private RevealCardView CreateRevealCard(PatternCardData card, Vector2 startPosition)
        {
            GameObject frameObject = new GameObject(card.CardId + " Reveal Card");
            frameObject.transform.SetParent(canvas.transform, false);

            RectTransform frameRect = frameObject.AddComponent<RectTransform>();
            frameRect.sizeDelta = revealCardSize;
            frameRect.anchoredPosition = startPosition;
            frameRect.localScale = Vector3.one * startScale;

            Image frameImage = frameObject.AddComponent<Image>();
            frameImage.color = frameColor;
            ApplyFixedSkin(frameImage, cardFrameSprite);
            frameImage.raycastTarget = false;

            CanvasGroup canvasGroup = frameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            Texture2D texture = useGeneratedCardVisuals ? null : ResolveTexture(card);
            if (texture != null)
            {
                CreateTextureCard(frameRect, texture);
            }
            else
            {
                CreateFallbackCard(frameRect, card);
            }

            return new RevealCardView
            {
                RectTransform = frameRect,
                CanvasGroup = canvasGroup
            };
        }

        private void CreateTextureCard(RectTransform parent, Texture2D texture)
        {
            GameObject imageObject = new GameObject("Card Texture");
            imageObject.transform.SetParent(parent, false);

            RectTransform imageRect = imageObject.AddComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = cardContentPadding;
            imageRect.offsetMax = -cardContentPadding;

            RawImage rawImage = imageObject.AddComponent<RawImage>();
            rawImage.texture = texture;
            rawImage.color = Color.white;
            rawImage.raycastTarget = false;

            AspectRatioFitter fitter = imageObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = texture.width / (float)Mathf.Max(1, texture.height);
        }

        private void CreateFallbackCard(RectTransform parent, PatternCardData card)
        {
            Image parentImage = parent.GetComponent<Image>();
            if (parentImage != null && parentImage.sprite == null)
            {
                parentImage.color = fallbackBaseColor;
            }

            Color accent = ResolveDifficultyColor(card != null ? card.Difficulty : PatternCardDifficulty.Easy);
            CreateAccentStrip(parent, card, accent);
            CreateFallbackGrid(parent, card, accent);
        }

        private void CreateAccentStrip(RectTransform parent, PatternCardData card, Color accent)
        {
            GameObject stripObject = new GameObject("Difficulty Strip");
            stripObject.transform.SetParent(parent, false);

            RectTransform stripRect = stripObject.AddComponent<RectTransform>();
            stripRect.anchorMin = new Vector2(0.5f, 1f);
            stripRect.anchorMax = new Vector2(0.5f, 1f);
            stripRect.pivot = new Vector2(0.5f, 1f);
            stripRect.anchoredPosition = new Vector2(0f, -cardContentPadding.y);
            float stripWidth = Mathf.Max(40f, revealCardSize.x - (cardContentPadding.x * 2f));
            stripRect.sizeDelta = ResolveFixedSpriteSize(
                new Vector2(stripWidth, difficultyStripHeight),
                stripSprite);

            Image stripImage = stripObject.AddComponent<Image>();
            ApplyFixedSkin(stripImage, stripSprite);
            stripImage.raycastTarget = false;

            GameObject textObject = new GameObject("Difficulty Text");
            textObject.transform.SetParent(stripObject.transform, false);
            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 4f);
            textRect.offsetMax = new Vector2(-18f, -2f);

            Text text = textObject.AddComponent<Text>();
            text.font = ResolveFont("HARD", 14);
            text.fontSize = 14;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.Lerp(new Color(0.2f, 0.1f, 0.035f, 1f), accent, 0.28f);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = card != null ? card.Difficulty.ToString().ToUpperInvariant() : string.Empty;
        }

        private void CreateFallbackGrid(RectTransform parent, PatternCardData card, Color accent)
        {
            if (card == null || card.Width <= 0 || card.Height <= 0)
            {
                return;
            }

            GameObject gridObject = new GameObject("Fallback Pattern Grid");
            gridObject.transform.SetParent(parent, false);

            RectTransform gridRect = gridObject.AddComponent<RectTransform>();
            gridRect.anchorMin = new Vector2(0.5f, 0.5f);
            gridRect.anchorMax = new Vector2(0.5f, 0.5f);
            gridRect.pivot = new Vector2(0.5f, 0.5f);
            float difficultyVisualHeight = ResolveFixedSpriteSize(
                new Vector2(
                    Mathf.Max(40f, revealCardSize.x - (cardContentPadding.x * 2f)),
                    difficultyStripHeight),
                stripSprite).y;
            float gridTop = (revealCardSize.y * 0.5f)
                - cardContentPadding.y
                - difficultyVisualHeight
                - difficultyStripGap;
            float gridBottom = (-revealCardSize.y * 0.5f) + cardContentPadding.y;
            gridRect.sizeDelta = new Vector2(
                Mathf.Max(40f, revealCardSize.x - (cardContentPadding.x * 2f)),
                Mathf.Max(40f, gridTop - gridBottom));
            gridRect.anchoredPosition = new Vector2(0f, (gridTop + gridBottom) * 0.5f);
            gridObject.AddComponent<RectMask2D>();

            GridLayoutGroup grid = gridObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = card.Width;
            grid.spacing = new Vector2(4f, 4f);
            grid.childAlignment = TextAnchor.MiddleCenter;

            float cellWidth = (gridRect.sizeDelta.x - (grid.spacing.x * (card.Width - 1))) / card.Width;
            float cellHeight = (gridRect.sizeDelta.y - (grid.spacing.y * (card.Height - 1))) / card.Height;
            grid.cellSize = Vector2.one * Mathf.Max(1f, Mathf.Min(cellWidth, cellHeight));

            for (int y = card.Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < card.Width; x++)
                {
                    GameObject cellObject = new GameObject("Pattern Cell");
                    cellObject.transform.SetParent(gridObject.transform, false);
                    Image cellImage = cellObject.AddComponent<Image>();
                    cellImage.color = IsFilledCell(card, x, y) ? accent : emptyCellColor;
                    cellImage.raycastTarget = false;

                    Outline outline = cellObject.AddComponent<Outline>();
                    outline.effectColor = new Color(0.25f, 0.13f, 0.04f, 0.22f);
                    outline.effectDistance = new Vector2(1f, -1f);
                }
            }
        }

        private Texture2D ResolveTexture(PatternCardData card)
        {
            Texture2D boundTexture = PatternCardTextureBinding.Resolve(cardTextureBindings, card);
            if (boundTexture != null)
            {
                return boundTexture;
            }

            if (cardManager != null)
            {
                int remainingCount = Mathf.Min(cardManager.RemainingCardCount, fallbackCardTextures != null ? fallbackCardTextures.Length : 0);
                for (int i = 0; i < remainingCount; i++)
                {
                    PatternCardData remainingCard = cardManager.GetRemainingCard(i);
                    if (ReferenceEquals(remainingCard, card) && fallbackCardTextures[i] != null)
                    {
                        return fallbackCardTextures[i];
                    }
                }
            }

            if (card != null && !string.IsNullOrEmpty(card.ResourcesImagePath))
            {
                return Resources.Load<Texture2D>(card.ResourcesImagePath);
            }

            return null;
        }

        private Vector2 ResolveDeckScreenPosition()
        {
            if (canvasRect == null)
            {
                return Vector2.zero;
            }

            if (deckTransform == null)
            {
                return new Vector2(0f, -220f);
            }

            Camera cameraToUse = worldCamera != null ? worldCamera : Camera.main;
            if (cameraToUse == null)
            {
                return new Vector2(0f, -220f);
            }

            Vector3 screenPoint = cameraToUse.WorldToScreenPoint(deckTransform.position);
            if (screenPoint.z < 0f)
            {
                return new Vector2(0f, -220f);
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    screenPoint,
                    null,
                    out Vector2 localPoint))
            {
                return localPoint;
            }

            return new Vector2(0f, -220f);
        }

        private Vector2[] BuildTargetPositions(int count)
        {
            Vector2[] positions = new Vector2[count];
            float step = revealCardSize.x + targetSpacing;
            float center = (count - 1) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float offset = i - center;
                positions[i] = new Vector2(
                    offset * step,
                    targetYOffset);
            }

            return positions;
        }

        private void StopRevealRoutine()
        {
            if (revealRoutine == null)
            {
                return;
            }

            StopCoroutine(revealRoutine);
            revealRoutine = null;
            SetStageLabelVisible(false);
            SetConfirmButtonVisible(false);
        }

        private void ClearActiveCards()
        {
            for (int i = activeCardViews.Count - 1; i >= 0; i--)
            {
                if (activeCardViews[i].RectTransform != null)
                {
                    Destroy(activeCardViews[i].RectTransform.gameObject);
                }
            }

            activeCardViews.Clear();
        }

        private void SetDimmerVisible(bool visible)
        {
            if (dimmerObject == null)
            {
                return;
            }

            dimmerObject.SetActive(visible);
            if (visible)
            {
                SetDimmerAlpha(dimmerColor.a);
            }
        }

        private void SetStageLabelVisible(bool visible)
        {
            if (stageLabelObject == null)
            {
                return;
            }

            stageLabelObject.SetActive(visible);
            if (stageLabelCanvasGroup != null)
            {
                stageLabelCanvasGroup.alpha = visible ? 1f : 0f;
                stageLabelCanvasGroup.blocksRaycasts = false;
                stageLabelCanvasGroup.interactable = false;
            }
        }

        private void SetConfirmButtonVisible(bool visible)
        {
            if (confirmButtonObject == null)
            {
                return;
            }

            confirmButtonObject.SetActive(visible);
            if (confirmButtonCanvasGroup != null)
            {
                confirmButtonCanvasGroup.alpha = visible ? 1f : 0f;
                confirmButtonCanvasGroup.blocksRaycasts = visible;
                confirmButtonCanvasGroup.interactable = visible;
            }
        }

        private void SetDimmerAlpha(float alpha)
        {
            if (dimmerObject == null)
            {
                return;
            }

            Image image = dimmerObject.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(dimmerColor.r, dimmerColor.g, dimmerColor.b, alpha);
            }
        }

        private Color ResolveDifficultyColor(PatternCardDifficulty difficulty)
        {
            if (difficulty == PatternCardDifficulty.Hard)
            {
                return hardColor;
            }

            if (difficulty == PatternCardDifficulty.Normal)
            {
                return normalColor;
            }

            return easyColor;
        }

        private Font ResolveFont(string sampleText, int fontSize)
        {
            if (CanRenderText(resolvedFont, sampleText, fontSize))
            {
                return resolvedFont;
            }

            Font resourceFont = ResolveBundledFont(sampleText, fontSize);
            if (resourceFont != null)
            {
                return resourceFont;
            }

            Font dynamicFont = CreateDynamicFont(fallbackFontNames, fontSize);
            if (CanRenderText(dynamicFont, sampleText, fontSize))
            {
                resolvedFont = dynamicFont;
                return dynamicFont;
            }

            dynamicFont = Font.CreateDynamicFontFromOSFont("Malgun Gothic", fontSize);
            if (CanRenderText(dynamicFont, sampleText, fontSize))
            {
                resolvedFont = dynamicFont;
                return dynamicFont;
            }

            dynamicFont = Font.CreateDynamicFontFromOSFont("Arial", fontSize);
            if (dynamicFont != null)
            {
                resolvedFont = dynamicFont;
                return dynamicFont;
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private Font ResolveBundledFont(string sampleText, int fontSize)
        {
            if (CanRenderText(bundledFont, sampleText, fontSize))
            {
                resolvedFont = bundledFont;
                return resolvedFont;
            }

            if (string.IsNullOrEmpty(bundledFontResourcePath))
            {
                return null;
            }

            Font loadedFont = Resources.Load<Font>(bundledFontResourcePath);
            if (!CanRenderText(loadedFont, sampleText, fontSize))
            {
                return null;
            }

            bundledFont = loadedFont;
            resolvedFont = loadedFont;
            return resolvedFont;
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

        private static IEnumerator WaitUnscaled(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
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
                if (filledCells[i].x == x && filledCells[i].y == y)
                {
                    return true;
                }
            }

            return false;
        }

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - (2f * value));
        }
    }
}
