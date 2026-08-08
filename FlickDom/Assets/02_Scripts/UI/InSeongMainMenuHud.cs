using FlickDom.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FlickDom.Gameplay
{
    public sealed class InSeongMainMenuHud : MonoBehaviour
    {
        private const string TargetSceneName = "good_Scene";
        private const int MenuCanvasSortingOrder = 300;
        private const string DefaultBundledFontResourcePath = "Fonts/NotoSansKR-VF";
        private const int MaxAddressLength = 64;
        private const int MaxJoinCodeLength = 16;
        private const int MaxPortLength = 5;

        [Header("Background")]
        [SerializeField] private Texture2D backgroundTexture;
        [SerializeField] private string editorBackgroundAssetPath = "Assets/04_Arts/UI/FlickDom_main.png";
        [SerializeField] private string resourcesBackgroundPath = "UI/FlickDom_main";

        [Header("Layout")]
        [SerializeField] private Vector2 menuPanelSize = new Vector2(360f, 430f);
        [SerializeField] private Vector2 menuPanelOffset = new Vector2(86f, 0f);
        [SerializeField] private Vector2 buttonSize = new Vector2(270f, 56f);
        [SerializeField] private int titleFontSize = 52;
        [SerializeField] private int buttonFontSize = 25;
        [SerializeField] private int bodyFontSize = 21;

        [Header("Colors")]
        [SerializeField] private Color dimColor = new Color(0f, 0f, 0f, 0.16f);
        [SerializeField] private Color panelColor = new Color(0.06f, 0.08f, 0.09f, 0.58f);
        [SerializeField] private Color buttonColor = new Color(1f, 0.86f, 0.28f, 0.94f);
        [SerializeField] private Color buttonHighlightedColor = new Color(1f, 0.94f, 0.46f, 1f);
        [SerializeField] private Color buttonPressedColor = new Color(0.88f, 0.63f, 0.16f, 1f);
        [SerializeField] private Color disabledButtonColor = new Color(0.55f, 0.55f, 0.55f, 0.62f);
        [SerializeField] private Color textColor = new Color(0.08f, 0.07f, 0.04f, 1f);
        [SerializeField] private Color lightTextColor = new Color(1f, 0.98f, 0.9f, 1f);
        [SerializeField] private Color inputColor = new Color(1f, 1f, 1f, 0.9f);
        [SerializeField] private Color focusedInputColor = new Color(1f, 0.95f, 0.58f, 0.98f);

        // 아트 스킨 슬롯. 전부 선택 사항이며, 비워두면 기존 단색 UI 그대로 동작한다.
        // 채우면 해당 요소만 스프라이트로 바뀐다. (황인성, 2026-08-08)
        [Header("Art Skin (optional)")]
        [SerializeField] private Sprite panelSprite;
        [SerializeField] private Sprite dialogPanelSprite;
        [SerializeField] private Vector2 dialogPanelSize = new Vector2(680f, 464f);
        [SerializeField] private Vector2 rulesPanelSize = new Vector2(880f, 601f);
        [SerializeField] private Color dialogPanelColor = new Color(0.35f, 0.20f, 0.09f, 0.85f);
        // 9-slice 테두리는 원본 픽셀 크기 그대로 그려진다. 1보다 크게 하면 화면상 테두리가 얇아진다.
        [SerializeField] private Sprite buttonSprite;
        [SerializeField] private Sprite singleModeButtonSprite;
        [SerializeField] private Sprite multiplayerButtonSprite;
        [SerializeField] private Sprite gameRulesButtonSprite;
        [SerializeField] private Sprite inputSlotSprite;
        [SerializeField] private Sprite logoSprite;
        [SerializeField] private Vector2 logoSize = new Vector2(560f, 166f);
        [SerializeField] private Sprite rulesImageSprite;
        [SerializeField] private Vector2 rulesImageSize = new Vector2(660f, 372f);

        private static void ApplyFixedSkin(Image image, Sprite sprite)
        {
            if (image == null || sprite == null)
            {
                return;
            }

            image.sprite = sprite;
            image.color = Color.white;

            if (sprite.border == Vector4.zero)
            {
                // 9-slice 가 없는 그림은 늘리면 그대로 찌그러진다. 비율을 지킨다.
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
                return;
            }

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

        private static Vector2 FitFixedSpriteInside(Vector2 bounds, Sprite sprite)
        {
            if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f)
            {
                return bounds;
            }

            float scale = Mathf.Min(
                Mathf.Max(1f, bounds.x) / sprite.rect.width,
                Mathf.Max(1f, bounds.y) / sprite.rect.height);
            return new Vector2(sprite.rect.width * scale, sprite.rect.height * scale);
        }

        private void ApplyButtonSkin(Button button, Sprite sprite)
        {
            if (button == null || sprite == null)
            {
                return;
            }

            ApplyFixedSkin(button.GetComponent<Image>(), sprite);
            Vector2 fixedSize = FitFixedSpriteInside(buttonSize, sprite);
            button.GetComponent<RectTransform>().sizeDelta = fixedSize;
            LayoutElement layout = button.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minWidth = fixedSize.x;
                layout.minHeight = fixedSize.y;
                layout.preferredWidth = fixedSize.x;
                layout.preferredHeight = fixedSize.y;
            }

            // 글자가 구워진 스프라이트라 라벨을 겹쳐 그리면 두 번 보인다.
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.gameObject.SetActive(false);
            }
        }

        [Header("Text")]
        [SerializeField] private string titleText = "FlickDom";
        [SerializeField] private string singleModeText = "Single Play";
        [SerializeField] private string multiplayerText = "Multiplayer";
        [SerializeField] private string gameRulesText = "Rules";
        [SerializeField] private string createRoomText = "Create Room";
        [SerializeField] private string joinRoomText = "Join Room";
        [SerializeField] private string copyCodeText = "Copy Code";
        [SerializeField] private string startGameText = "Start Game";
        [SerializeField] private string backText = "Back";
        [SerializeField] private string addressLabelText = "IP Address";
        [SerializeField] private string portLabelText = "Port";
        [SerializeField] private string emptyRulesText = "";
        [SerializeField] private Font bundledFont;
        [SerializeField] private string bundledFontResourcePath = DefaultBundledFontResourcePath;
        [SerializeField] private string[] fallbackFontNames =
        {
            "Malgun Gothic",
            "Segoe UI",
            "Arial Unicode MS",
            "Arial"
        };

        private Canvas canvas;
        private GameObject mainPanel;
        private GameObject multiplayerPanel;
        private GameObject rulesPanel;
        private InputField addressInput;
        private InputField portInput;
        private Text portLabelTextComponent;
        private Text multiplayerStatusText;
        private Button singleModeButton;
        private Button multiplayerButton;
        private Button gameRulesButton;
        private Button createRoomButton;
        private Button joinRoomButton;
        private Button copyCodeButton;
        private Button startGameButton;
        private Button multiplayerBackButton;
        private Button rulesBackButton;
        private Font resolvedFont;
        private FlickDomNetworkBootstrap bootstrap;
        private InputField focusedConnectionInput;
        private bool replaceFocusedInputOnNextCharacter;


        private void Start()
        {
            // 원본 FlickDomMainMenuHud 는 AfterSceneLoad 에 자기를 자동 생성한다.
            // 그 파일은 팀 소유라 건드리지 않고, 생성된 뒤 여기서 걷어낸다.
            // 실행 순서상 Start 시점에는 이미 만들어져 있다.
            FlickDomMainMenuHud[] originals = FindObjectsByType<FlickDomMainMenuHud>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < originals.Length; i++)
            {
                if (originals[i] != null)
                {
                    Destroy(originals[i].gameObject);
                }
            }
        }

        private void Awake()
        {
            if (!IsTargetScene(SceneManager.GetActiveScene().name))
            {
                enabled = false;
                return;
            }

            ApplyWebSafeText();
            EnsureBootstrap();
            BuildHud();
            ShowMainMenu();
        }

        private void ApplyWebSafeText()
        {
            singleModeText = "Single Play";
            multiplayerText = "Multiplayer";
            gameRulesText = "Rules";
            createRoomText = "Create Room";
            joinRoomText = "Join Room";
            copyCodeText = "Copy Code";
            startGameText = "Start Game";
            backText = "Back";
            addressLabelText = "Join Code";
            portLabelText = "Port";
        }

        private void OnDisable()
        {
            ClearFocusedConnectionInput();
        }

        private void Update()
        {
            EnsureBootstrap();

            if (bootstrap != null && bootstrap.IsGameActive)
            {
                if (canvas != null && canvas.gameObject.activeSelf)
                {
                    canvas.gameObject.SetActive(false);
                }

                return;
            }

            if (canvas != null && !canvas.gameObject.activeSelf)
            {
                canvas.gameObject.SetActive(true);
                ShowMainMenu();
            }

            HandleFocusedConnectionInputKeys();
            HandleFallbackMenuInput();
            RefreshMultiplayerPanel();
        }

        private void OnDestroy()
        {
            if (canvas != null)
            {
                Destroy(canvas.gameObject);
                canvas = null;
            }
        }

        private void BuildHud()
        {
            EnsureEventSystem();

            GameObject canvasObject = new GameObject("Generated FlickDom Main Menu Canvas");
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = MenuCanvasSortingOrder;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();

            CreateBackground(canvasObject.transform);
            mainPanel = CreateMainPanel(canvasObject.transform);
            multiplayerPanel = CreateMultiplayerPanel(canvasObject.transform);
            rulesPanel = CreateRulesPanel(canvasObject.transform);
        }

        private void CreateBackground(Transform parent)
        {
            GameObject backgroundObject = new GameObject("Main Menu Background");
            backgroundObject.transform.SetParent(parent, false);

            RectTransform rectTransform = backgroundObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            RawImage rawImage = backgroundObject.AddComponent<RawImage>();
            rawImage.texture = ResolveBackgroundTexture();
            rawImage.color = Color.white;
            rawImage.raycastTarget = false;

            Texture texture = rawImage.texture;
            if (texture != null)
            {
                AspectRatioFitter fitter = backgroundObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = texture.width / (float)Mathf.Max(1, texture.height);
            }

            GameObject dimObject = new GameObject("Main Menu Dim");
            dimObject.transform.SetParent(parent, false);

            RectTransform dimRect = dimObject.AddComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;

            Image dimImage = dimObject.AddComponent<Image>();
            dimImage.color = dimColor;
            dimImage.raycastTarget = false;
        }

        private GameObject CreateMainPanel(Transform parent)
        {
            GameObject panel = CreateMenuPanel("Main Menu Panel", parent);
            VerticalLayoutGroup layout = ConfigureVerticalLayout(panel, 24, 24, 22f);

            if (logoSprite != null)
            {
                // 로고 이미지가 있으면 제목 텍스트 대신 그림을 쓴다.
                GameObject logoObject = new GameObject("Logo");
                logoObject.transform.SetParent(panel.transform, false);
                logoObject.AddComponent<RectTransform>().sizeDelta = logoSize;
                Image logoImage = logoObject.AddComponent<Image>();
                logoImage.sprite = logoSprite;
                logoImage.preserveAspect = true;
                logoImage.raycastTarget = false;
                LayoutElement logoLayout = logoObject.AddComponent<LayoutElement>();
                logoLayout.preferredWidth = logoSize.x;
                logoLayout.preferredHeight = logoSize.y;
            }
            else
            {
                Text title = CreateText("Title", titleText, panel.transform, titleFontSize, lightTextColor, TextAnchor.MiddleCenter);
                title.fontStyle = FontStyle.Bold;
                title.verticalOverflow = VerticalWrapMode.Overflow;
                LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
                titleLayout.minWidth = 300f;
                titleLayout.preferredWidth = 300f;
                titleLayout.preferredHeight = 86f;
            }

            AddFlexibleSpace(panel.transform, 8f);
            singleModeButton = CreateMenuButton("Single Mode Button", singleModeText, panel.transform, HandleSingleModeClicked);
            multiplayerButton = CreateMenuButton("Multiplayer Button", multiplayerText, panel.transform, ShowMultiplayerMenu);
            gameRulesButton = CreateMenuButton("Game Rules Button", gameRulesText, panel.transform, ShowRulesMenu);

            // 버튼별 전용 스프라이트. 글자가 구워진 이미지를 쓸 때는 라벨 텍스트를 숨긴다.
            ApplyButtonSkin(singleModeButton, singleModeButtonSprite);
            ApplyButtonSkin(multiplayerButton, multiplayerButtonSprite);
            ApplyButtonSkin(gameRulesButton, gameRulesButtonSprite);

            layout.childForceExpandHeight = false;
            return panel;
        }

        private GameObject CreateMultiplayerPanel(Transform parent)
        {
            GameObject panel = CreateMenuPanel("Multiplayer Menu Panel", parent);
            ConfigureVerticalLayout(panel, 22, 18, 8f);

            Text title = CreateText("Multiplayer Title", multiplayerText, panel.transform, 34, lightTextColor, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.minWidth = 300f;
            titleLayout.preferredWidth = 300f;
            titleLayout.minHeight = 40f;
            titleLayout.preferredHeight = 40f;

            bool useRelay = bootstrap == null || bootstrap.UsesUnityRelay;
            Text addressLabel = CreateText("Address Label", useRelay ? "Join Code" : addressLabelText, panel.transform, 18, lightTextColor, TextAnchor.LowerLeft);
            LayoutElement addressLabelLayout = addressLabel.gameObject.AddComponent<LayoutElement>();
            addressLabelLayout.minHeight = 22f;
            addressLabelLayout.preferredHeight = 22f;
            addressInput = CreateInputField("Address Input", panel.transform, useRelay ? string.Empty : bootstrap != null ? bootstrap.CurrentConnectAddress : "127.0.0.1");

            portLabelTextComponent = CreateText("Port Label", portLabelText, panel.transform, 18, lightTextColor, TextAnchor.LowerLeft);
            LayoutElement portLabelLayout = portLabelTextComponent.gameObject.AddComponent<LayoutElement>();
            portLabelLayout.minHeight = 22f;
            portLabelLayout.preferredHeight = 22f;
            portInput = CreateInputField("Port Input", panel.transform, bootstrap != null ? bootstrap.CurrentPort.ToString() : "7777");
            SetPortInputVisible(!useRelay);

            copyCodeButton = CreateMenuButton("Copy Code Button", copyCodeText, panel.transform, HandleCopyCodeClicked);
            SetButtonPreferredHeight(copyCodeButton, 32f);
            SetCopyCodeButtonVisible(useRelay);

            GameObject row = new GameObject("Room Button Row");
            row.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 10f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;
            LayoutElement rowElement = row.AddComponent<LayoutElement>();
            rowElement.minHeight = 58f;
            rowElement.preferredHeight = 58f;

            createRoomButton = CreateMenuButton("Create Room Button", createRoomText, row.transform, HandleCreateRoomClicked);
            joinRoomButton = CreateMenuButton("Join Room Button", joinRoomText, row.transform, HandleJoinRoomClicked);

            startGameButton = CreateMenuButton("Start Game Button", startGameText, panel.transform, HandleStartGameClicked);
            multiplayerStatusText = CreateText("Multiplayer Status", string.Empty, panel.transform, 18, lightTextColor, TextAnchor.UpperLeft);
            LayoutElement statusLayout = multiplayerStatusText.gameObject.AddComponent<LayoutElement>();
            statusLayout.minHeight = 50f;
            statusLayout.preferredHeight = 50f;

            multiplayerBackButton = CreateMenuButton("Multiplayer Back Button", backText, panel.transform, ShowMainMenu);
            return panel;
        }

        private GameObject CreateRulesPanel(Transform parent)
        {
            GameObject panel = CreateMenuPanel("Game Rules Panel", parent);
            ConfigureVerticalLayout(panel, 24, 24, 18f);

            Text title = CreateText("Rules Title", gameRulesText, panel.transform, 34, lightTextColor, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.minWidth = 300f;
            titleLayout.preferredWidth = 300f;
            titleLayout.minHeight = 58f;
            titleLayout.preferredHeight = 58f;

            if (rulesImageSprite != null)
            {
                // 규칙 설명 그림. 글자는 이미지에 안 굽고 필요하면 위에 Text 로 얹는다.
                GameObject rulesImageObject = new GameObject("Rules Image");
                rulesImageObject.transform.SetParent(panel.transform, false);
                rulesImageObject.AddComponent<RectTransform>();
                Image rulesImage = rulesImageObject.AddComponent<Image>();
                rulesImage.sprite = rulesImageSprite;
                rulesImage.preserveAspect = true;
                rulesImage.raycastTarget = false;
                LayoutElement rulesLayout = rulesImageObject.AddComponent<LayoutElement>();
                // flexibleWidth 를 주면 형제(뒤로 버튼)까지 패널 폭으로 늘어나 납작해진다.
                rulesLayout.flexibleWidth = 0f;
                rulesLayout.flexibleHeight = 1f;
                rulesLayout.preferredWidth = rulesImageSize.x;
                rulesLayout.preferredHeight = rulesImageSize.y;
                rulesLayout.minHeight = rulesImageSize.y * 0.5f;
            }
            else
            {
                Text body = CreateText("Rules Body", emptyRulesText, panel.transform, bodyFontSize, lightTextColor, TextAnchor.UpperLeft);
                LayoutElement bodyLayout = body.gameObject.AddComponent<LayoutElement>();
                bodyLayout.flexibleHeight = 1f;
            }

            rulesBackButton = CreateMenuButton("Rules Back Button", backText, panel.transform, ShowMainMenu);
            LayoutElement rulesBackLayout = rulesBackButton.GetComponent<LayoutElement>();
            if (rulesBackLayout != null)
            {
                rulesBackLayout.minWidth = buttonSize.x;
                rulesBackLayout.minHeight = buttonSize.y;
                rulesBackLayout.flexibleWidth = 0f;
                rulesBackLayout.flexibleHeight = 0f;
            }
            return panel;
        }

        private GameObject CreateMenuPanel(string objectName, Transform parent)
        {
            GameObject panel = new GameObject(objectName);
            panel.transform.SetParent(parent, false);

            // 메인 패널은 배경 없이 왼쪽에 붙고, 나머지(멀티·룰)는 나무 프레임을 두르고 가운데에 뜬다.
            // 원래는 셋이 같은 크기·색을 공유해서 한쪽을 맞추면 다른 쪽이 깨졌다.
            bool isMain = objectName == "Main Menu Panel";
            bool isRules = objectName == "Game Rules Panel";

            RectTransform rectTransform = panel.AddComponent<RectTransform>();
            float anchor = isMain ? 0f : 0.5f;
            rectTransform.anchorMin = new Vector2(anchor, 0.5f);
            rectTransform.anchorMax = new Vector2(anchor, 0.5f);
            rectTransform.pivot = new Vector2(anchor, 0.5f);
            rectTransform.anchoredPosition = isMain ? menuPanelOffset : Vector2.zero;
            Sprite panelArt = isMain ? panelSprite : dialogPanelSprite;
            Vector2 requestedPanelSize = isMain
                ? menuPanelSize
                : isRules ? rulesPanelSize : dialogPanelSize;
            rectTransform.sizeDelta = ResolveFixedSpriteSize(requestedPanelSize, panelArt);

            Image image = panel.AddComponent<Image>();
            image.color = isMain ? panelColor : dialogPanelColor;
            ApplyFixedSkin(image, panelArt);
            image.raycastTarget = true;
            return panel;
        }

        private VerticalLayoutGroup ConfigureVerticalLayout(GameObject panel, int horizontalPadding, int verticalPadding, float spacing)
        {
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(horizontalPadding, horizontalPadding, verticalPadding, verticalPadding);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            // ForceExpandWidth 를 켜면 자식이 전부 패널 폭으로 늘어난다. 버튼 스프라이트가
            // 가로로 찌그러지는 원인이었다. ControlWidth 는 켜둔 채 ForceExpand 만 끄면
            // LayoutElement.preferredWidth 가 그대로 쓰여 버튼이 제 크기를 지킨다.
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return layout;
        }

        private Button CreateMenuButton(string objectName, string text, Transform parent, UnityEngine.Events.UnityAction onClick)
        {
            GameObject buttonObject = new GameObject(objectName);
            buttonObject.transform.SetParent(parent, false);

            RectTransform rectTransform = buttonObject.AddComponent<RectTransform>();
            Vector2 fixedSize = FitFixedSpriteInside(buttonSize, buttonSprite);
            rectTransform.sizeDelta = fixedSize;

            LayoutElement layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = fixedSize.x;
            layoutElement.preferredHeight = fixedSize.y;
            layoutElement.minWidth = fixedSize.x;
            layoutElement.minHeight = fixedSize.y;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;

            Image image = buttonObject.AddComponent<Image>();
            image.color = buttonColor;
            ApplyFixedSkin(image, buttonSprite);
            bool skinned = image.sprite != null;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            UiButtonClickSound.Attach(button);
            button.onClick.AddListener(onClick);

            ColorBlock colors = button.colors;
            if (skinned)
            {
                // 스프라이트가 붙으면 색은 상태 표현에만 쓴다. 곱수를 1.3 으로 올려
                // Normal 을 기준점(1.0)에 맞추고 Hover 가 그보다 밝아질 수 있게 한다.
                colors.colorMultiplier = 1.3f;
                colors.normalColor = new Color(0.769f, 0.769f, 0.769f, 1f);
                colors.highlightedColor = new Color(0.877f, 0.989f, 0.989f, 1f);
                colors.pressedColor = new Color(0.708f, 0.665f, 0.665f, 1f);
                colors.selectedColor = new Color(0.769f, 0.769f, 0.769f, 1f);
                colors.disabledColor = new Color(0.477f, 0.477f, 0.477f, 0.6f);
            }
            else
            {
                colors.normalColor = buttonColor;
                colors.highlightedColor = buttonHighlightedColor;
                colors.pressedColor = buttonPressedColor;
                colors.selectedColor = buttonHighlightedColor;
                colors.disabledColor = disabledButtonColor;
            }
            button.colors = colors;

            Text buttonText = CreateText("Text", text, buttonObject.transform, buttonFontSize, textColor, TextAnchor.MiddleCenter);
            buttonText.fontStyle = FontStyle.Bold;
            RectTransform textRect = buttonText.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 3f);
            textRect.offsetMax = new Vector2(-10f, -3f);

            return button;
        }

        private void SetButtonPreferredHeight(Button button, float height)
        {
            if (button == null)
            {
                return;
            }

            Image image = button.GetComponent<Image>();
            Vector2 fixedSize = FitFixedSpriteInside(
                new Vector2(buttonSize.x, height),
                image != null ? image.sprite : buttonSprite);
            button.GetComponent<RectTransform>().sizeDelta = fixedSize;

            LayoutElement layoutElement = button.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.minWidth = fixedSize.x;
                layoutElement.minHeight = fixedSize.y;
                layoutElement.preferredWidth = fixedSize.x;
                layoutElement.preferredHeight = fixedSize.y;
            }
        }

        private InputField CreateInputField(string objectName, Transform parent, string value)
        {
            GameObject inputObject = new GameObject(objectName);
            inputObject.transform.SetParent(parent, false);

            RectTransform rectTransform = inputObject.AddComponent<RectTransform>();
            Vector2 inputSize = FitFixedSpriteInside(new Vector2(buttonSize.x, 54f), inputSlotSprite);
            rectTransform.sizeDelta = inputSize;

            LayoutElement layoutElement = inputObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = inputSize.x;
            layoutElement.preferredHeight = inputSize.y;
            layoutElement.minWidth = inputSize.x;
            layoutElement.minHeight = inputSize.y;

            Image image = inputObject.AddComponent<Image>();
            image.color = inputColor;
            ApplyFixedSkin(image, inputSlotSprite);

            InputField inputField = inputObject.AddComponent<InputField>();
            inputField.targetGraphic = image;
            inputField.readOnly = true;
            Text text = CreateText("Text", value, inputObject.transform, bodyFontSize, textColor, TextAnchor.MiddleLeft);
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 2f);
            textRect.offsetMax = new Vector2(-12f, -2f);

            inputField.textComponent = text;
            inputField.text = value;
            inputField.lineType = InputField.LineType.SingleLine;
            inputField.caretColor = textColor;
            inputField.selectionColor = new Color(0.25f, 0.48f, 1f, 0.35f);
            inputField.ForceLabelUpdate();
            return inputField;
        }

        private Text CreateText(string objectName, string text, Transform parent, int fontSize, Color color, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(objectName);
            textObject.transform.SetParent(parent, false);
            textObject.AddComponent<RectTransform>();

            Text textComponent = textObject.AddComponent<Text>();
            textComponent.font = ResolveFont(text, fontSize);
            textComponent.fontSize = fontSize;
            textComponent.alignment = alignment;
            textComponent.color = color;
            textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            textComponent.raycastTarget = false;
            textComponent.text = text;
            return textComponent;
        }

        private void AddFlexibleSpace(Transform parent, float minHeight)
        {
            GameObject spacer = new GameObject("Spacer");
            spacer.transform.SetParent(parent, false);
            spacer.AddComponent<RectTransform>();

            LayoutElement layoutElement = spacer.AddComponent<LayoutElement>();
            layoutElement.minHeight = minHeight;
            layoutElement.flexibleHeight = 1f;
        }

        private void ShowMainMenu()
        {
            ClearFocusedConnectionInput();
            SetPanelActive(mainPanel, true);
            SetPanelActive(multiplayerPanel, false);
            SetPanelActive(rulesPanel, false);
        }

        private void ShowMultiplayerMenu()
        {
            SetPanelActive(mainPanel, false);
            SetPanelActive(multiplayerPanel, true);
            SetPanelActive(rulesPanel, false);
            RefreshMultiplayerPanel();
        }

        private void ShowRulesMenu()
        {
            ClearFocusedConnectionInput();
            SetPanelActive(mainPanel, false);
            SetPanelActive(multiplayerPanel, false);
            SetPanelActive(rulesPanel, true);
        }

        private void HandleSingleModeClicked()
        {
            EnsureBootstrap();
            if (bootstrap != null && bootstrap.TryStartSinglePlayerModeFromMenu())
            {
                canvas.gameObject.SetActive(false);
            }
        }

        private void HandleCreateRoomClicked()
        {
            EnsureBootstrap();
            if (bootstrap == null)
            {
                return;
            }

            ApplyConnectionInput();
            bootstrap.StartHost();
            RefreshMultiplayerPanel();
        }

        private void HandleJoinRoomClicked()
        {
            EnsureBootstrap();
            if (bootstrap == null)
            {
                return;
            }

            ApplyConnectionInput();
            bootstrap.StartClient();
            RefreshMultiplayerPanel();
        }

        private void HandleStartGameClicked()
        {
            EnsureBootstrap();
            if (bootstrap != null && bootstrap.TryStartNetworkGameFromMenu())
            {
                canvas.gameObject.SetActive(false);
            }
        }

        private void HandleCopyCodeClicked()
        {
            EnsureBootstrap();

            string joinCode = bootstrap != null && !string.IsNullOrEmpty(bootstrap.RelayJoinCode)
                ? bootstrap.RelayJoinCode
                : addressInput != null
                    ? addressInput.text
                    : string.Empty;

            if (string.IsNullOrWhiteSpace(joinCode))
            {
                return;
            }

            GUIUtility.systemCopyBuffer = joinCode.Trim();
            Debug.Log("[Network] Relay join code copied to clipboard.", this);
        }

        private void ApplyConnectionInput()
        {
            if (bootstrap != null && bootstrap.UsesUnityRelay)
            {
                string joinCode = addressInput != null ? addressInput.text : string.Empty;
                bootstrap.SetRelayJoinCodeInput(joinCode);
                if (addressInput != null)
                {
                    addressInput.text = bootstrap.RelayJoinCodeInput;
                    ForceInputLabelUpdate(addressInput);
                }

                return;
            }

            string address = addressInput != null && !string.IsNullOrWhiteSpace(addressInput.text)
                ? addressInput.text.Trim()
                : "127.0.0.1";

            ushort targetPort = 7777;
            if (portInput != null && !ushort.TryParse(portInput.text, out targetPort))
            {
                targetPort = 7777;
                portInput.text = targetPort.ToString();
            }

            bootstrap.SetConnectionTarget(address, targetPort);
        }

        private void RefreshMultiplayerPanel()
        {
            if (multiplayerPanel == null || !multiplayerPanel.activeSelf)
            {
                return;
            }

            bool hasBootstrap = bootstrap != null;
            bool useRelay = hasBootstrap && bootstrap.UsesUnityRelay;
            bool canEditConnection = hasBootstrap && !bootstrap.IsRunning && !bootstrap.IsNetworkStartInProgress;
            bool canStartGame = hasBootstrap && bootstrap.CanStartNetworkGame;
            bool hasRelayJoinCode = useRelay && !string.IsNullOrEmpty(bootstrap.RelayJoinCode);

            if (addressInput != null)
            {
                addressInput.interactable = canEditConnection;
                if (useRelay && !addressInput.isFocused)
                {
                    string relayCodeText = !string.IsNullOrEmpty(bootstrap.RelayJoinCode)
                        ? bootstrap.RelayJoinCode
                        : bootstrap.RelayJoinCodeInput;
                    if (!string.Equals(addressInput.text, relayCodeText, System.StringComparison.Ordinal))
                    {
                        addressInput.text = relayCodeText;
                        ForceInputLabelUpdate(addressInput);
                    }
                }
            }

            SetPortInputVisible(!useRelay);
            SetCopyCodeButtonVisible(useRelay);
            if (portInput != null)
            {
                portInput.interactable = canEditConnection;
                string currentPortText = bootstrap != null ? bootstrap.CurrentPort.ToString() : "7777";
                bool shouldSyncActualHostPort = hasBootstrap
                    && bootstrap.IsRunning
                    && bootstrap.LocalPlayerId == FlickDomPlayerId.Player1;
                if (shouldSyncActualHostPort
                    && !portInput.isFocused
                    && !string.Equals(portInput.text, currentPortText, System.StringComparison.Ordinal))
                {
                    portInput.text = currentPortText;
                }
            }

            if (createRoomButton != null)
            {
                createRoomButton.interactable = canEditConnection;
            }

            if (joinRoomButton != null)
            {
                joinRoomButton.interactable = canEditConnection;
            }

            if (copyCodeButton != null)
            {
                copyCodeButton.interactable = hasRelayJoinCode;
            }

            if (startGameButton != null)
            {
                startGameButton.interactable = canStartGame;
            }

            if (multiplayerStatusText != null)
            {
                multiplayerStatusText.text = hasBootstrap
                    ? "Status: " + bootstrap.CurrentNetworkModeText
                        + "\nPlayers: " + bootstrap.VisiblePlayerCount + " / " + bootstrap.MaxPlayers
                        + "\n" + bootstrap.LobbyStatusText
                    : "Status: Initializing";
            }
        }

        private void HandleFallbackMenuInput()
        {
            if (canvas == null || !canvas.gameObject.activeInHierarchy)
            {
                return;
            }

            if (!IsConnectionInputFocused())
            {
                HandleFallbackKeyboardInput();
            }

            if (!TryGetPointerDownPosition(out Vector2 screenPosition))
            {
                return;
            }

            if (mainPanel != null && mainPanel.activeSelf)
            {
                if (TryInvokeButtonAt(singleModeButton, screenPosition)
                    || TryInvokeButtonAt(multiplayerButton, screenPosition)
                    || TryInvokeButtonAt(gameRulesButton, screenPosition))
                {
                    return;
                }
            }

            if (multiplayerPanel != null && multiplayerPanel.activeSelf)
            {
                if (TryFocusInputFieldAt(addressInput, screenPosition)
                    || TryFocusInputFieldAt(portInput, screenPosition))
                {
                    return;
                }

                ClearFocusedConnectionInput();
                TryInvokeButtonAt(createRoomButton, screenPosition);
                TryInvokeButtonAt(joinRoomButton, screenPosition);
                TryInvokeButtonAt(copyCodeButton, screenPosition);
                TryInvokeButtonAt(startGameButton, screenPosition);
                TryInvokeButtonAt(multiplayerBackButton, screenPosition);
                return;
            }

            if (rulesPanel != null && rulesPanel.activeSelf)
            {
                ClearFocusedConnectionInput();
                TryInvokeButtonAt(rulesBackButton, screenPosition);
            }
        }

        private bool HandleFocusedConnectionInputKeys()
        {
            if (!IsConnectionInputFocused())
            {
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            if (IsCtrlHeld(keyboard) && keyboard.aKey.wasPressedThisFrame)
            {
                replaceFocusedInputOnNextCharacter = true;
                return true;
            }

            if (IsCtrlHeld(keyboard) && keyboard.vKey.wasPressedThisFrame)
            {
                PasteClipboardIntoFocusedInput();
                return true;
            }

            if (keyboard.backspaceKey.wasPressedThisFrame)
            {
                replaceFocusedInputOnNextCharacter = false;
                RemoveLastCharacterFromFocusedInput();
                return true;
            }

            if (keyboard.deleteKey.wasPressedThisFrame)
            {
                replaceFocusedInputOnNextCharacter = false;
                SetFocusedInputText(string.Empty);
                return true;
            }

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                InputField nextInput = focusedConnectionInput == addressInput && IsInputAvailable(portInput)
                    ? portInput
                    : addressInput;
                FocusConnectionInput(nextInput);
                return true;
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                ClearFocusedConnectionInput();
                return true;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                ClearFocusedConnectionInput();
                return true;
            }

            if (TryGetConnectionInputCharacter(keyboard, out char character))
            {
                AppendCharacterToFocusedInput(character);
                return true;
            }
#endif

            return false;
        }

#if ENABLE_INPUT_SYSTEM
        private bool TryGetConnectionInputCharacter(Keyboard keyboard, out char character)
        {
            if (keyboard == null)
            {
                character = default;
                return false;
            }

            if (TryGetDigitCharacter(keyboard, out character))
            {
                return true;
            }

            if (focusedConnectionInput == portInput)
            {
                character = default;
                return false;
            }

            if (keyboard.periodKey.wasPressedThisFrame || keyboard.numpadPeriodKey.wasPressedThisFrame)
            {
                character = '.';
                return true;
            }

            if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
            {
                character = '-';
                return true;
            }

            if (keyboard.semicolonKey.wasPressedThisFrame && IsShiftHeld(keyboard))
            {
                character = ':';
                return true;
            }

            if (keyboard.leftBracketKey.wasPressedThisFrame)
            {
                character = '[';
                return true;
            }

            if (keyboard.rightBracketKey.wasPressedThisFrame)
            {
                character = ']';
                return true;
            }

            if (TryGetLetterCharacter(keyboard, out character))
            {
                return true;
            }

            character = default;
            return false;
        }

        private static bool TryGetDigitCharacter(Keyboard keyboard, out char character)
        {
            KeyControl[] digitKeys =
            {
                keyboard.digit0Key,
                keyboard.digit1Key,
                keyboard.digit2Key,
                keyboard.digit3Key,
                keyboard.digit4Key,
                keyboard.digit5Key,
                keyboard.digit6Key,
                keyboard.digit7Key,
                keyboard.digit8Key,
                keyboard.digit9Key
            };

            KeyControl[] numpadKeys =
            {
                keyboard.numpad0Key,
                keyboard.numpad1Key,
                keyboard.numpad2Key,
                keyboard.numpad3Key,
                keyboard.numpad4Key,
                keyboard.numpad5Key,
                keyboard.numpad6Key,
                keyboard.numpad7Key,
                keyboard.numpad8Key,
                keyboard.numpad9Key
            };

            for (int i = 0; i < digitKeys.Length; i++)
            {
                if ((digitKeys[i] != null && digitKeys[i].wasPressedThisFrame)
                    || (numpadKeys[i] != null && numpadKeys[i].wasPressedThisFrame))
                {
                    character = (char)('0' + i);
                    return true;
                }
            }

            character = default;
            return false;
        }

        private static bool TryGetLetterCharacter(Keyboard keyboard, out char character)
        {
            KeyControl[] letterKeys =
            {
                keyboard.aKey, keyboard.bKey, keyboard.cKey, keyboard.dKey, keyboard.eKey, keyboard.fKey,
                keyboard.gKey, keyboard.hKey, keyboard.iKey, keyboard.jKey, keyboard.kKey, keyboard.lKey,
                keyboard.mKey, keyboard.nKey, keyboard.oKey, keyboard.pKey, keyboard.qKey, keyboard.rKey,
                keyboard.sKey, keyboard.tKey, keyboard.uKey, keyboard.vKey, keyboard.wKey, keyboard.xKey,
                keyboard.yKey, keyboard.zKey
            };

            for (int i = 0; i < letterKeys.Length; i++)
            {
                if (letterKeys[i] != null && letterKeys[i].wasPressedThisFrame)
                {
                    char lower = (char)('a' + i);
                    character = IsShiftHeld(keyboard) ? char.ToUpperInvariant(lower) : lower;
                    return true;
                }
            }

            character = default;
            return false;
        }

        private static bool IsShiftHeld(Keyboard keyboard)
        {
            return keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }

        private static bool IsCtrlHeld(Keyboard keyboard)
        {
            return keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        }
#endif

        private bool TryFocusInputFieldAt(InputField inputField, Vector2 screenPosition)
        {
            if (inputField == null || !inputField.isActiveAndEnabled || !inputField.interactable)
            {
                return false;
            }

            RectTransform rectTransform = inputField.GetComponent<RectTransform>();
            if (rectTransform == null
                || !RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPosition, null))
            {
                return false;
            }

            FocusConnectionInput(inputField);
            return true;
        }

        private void FocusConnectionInput(InputField inputField)
        {
            if (inputField == null || !inputField.interactable)
            {
                return;
            }

            focusedConnectionInput = inputField;
            replaceFocusedInputOnNextCharacter = true;
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.SetSelectedGameObject(inputField.gameObject);
            }

            inputField.Select();
            inputField.ActivateInputField();
            inputField.MoveTextEnd(true);
            ForceInputLabelUpdate(inputField);
            RefreshInputFocusVisuals();
        }

        private void ClearFocusedConnectionInput()
        {
            if (focusedConnectionInput != null)
            {
                focusedConnectionInput.DeactivateInputField();
            }

            focusedConnectionInput = null;
            replaceFocusedInputOnNextCharacter = false;
            RefreshInputFocusVisuals();
        }

        private bool IsConnectionInputFocused()
        {
            return focusedConnectionInput != null
                && focusedConnectionInput.isActiveAndEnabled
                && focusedConnectionInput.interactable;
        }

        private void RefreshInputFocusVisuals()
        {
            SetInputFieldColor(addressInput, focusedConnectionInput == addressInput ? focusedInputColor : inputColor);
            SetInputFieldColor(portInput, focusedConnectionInput == portInput ? focusedInputColor : inputColor);
        }

        private static void SetInputFieldColor(InputField inputField, Color color)
        {
            if (inputField != null && inputField.targetGraphic != null)
            {
                inputField.targetGraphic.color = color;
            }
        }

        private static void ForceInputLabelUpdate(InputField inputField)
        {
            if (inputField == null)
            {
                return;
            }

            inputField.ForceLabelUpdate();
            if (inputField.textComponent != null)
            {
                inputField.textComponent.SetAllDirty();
            }
        }

        private void RemoveLastCharacterFromFocusedInput()
        {
            if (!IsConnectionInputFocused())
            {
                return;
            }

            string currentText = focusedConnectionInput.text ?? string.Empty;
            if (currentText.Length <= 0)
            {
                return;
            }

            SetFocusedInputText(currentText.Substring(0, currentText.Length - 1));
        }

        private void AppendCharacterToFocusedInput(char character)
        {
            if (!IsConnectionInputFocused())
            {
                return;
            }

            string currentText = focusedConnectionInput.text ?? string.Empty;
            if (replaceFocusedInputOnNextCharacter)
            {
                currentText = string.Empty;
                replaceFocusedInputOnNextCharacter = false;
            }

            bool relayJoinCodeInput = IsRelayJoinCodeInput(focusedConnectionInput);
            if (focusedConnectionInput == portInput)
            {
                if (!char.IsDigit(character) || currentText.Length >= MaxPortLength)
                {
                    return;
                }
            }
            else if (relayJoinCodeInput)
            {
                if (!char.IsLetterOrDigit(character) || currentText.Length >= MaxJoinCodeLength)
                {
                    return;
                }

                character = char.ToUpperInvariant(character);
            }
            else if (!IsAllowedAddressCharacter(character) || currentText.Length >= MaxAddressLength)
            {
                return;
            }

            SetFocusedInputText(currentText + character);
        }

        private void PasteClipboardIntoFocusedInput()
        {
            if (!IsConnectionInputFocused())
            {
                return;
            }

            string clipboardText = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(clipboardText))
            {
                return;
            }

            string currentText = replaceFocusedInputOnNextCharacter
                ? string.Empty
                : focusedConnectionInput.text ?? string.Empty;
            replaceFocusedInputOnNextCharacter = false;

            bool relayJoinCodeInput = IsRelayJoinCodeInput(focusedConnectionInput);
            int maxLength = focusedConnectionInput == portInput
                ? MaxPortLength
                : relayJoinCodeInput
                    ? MaxJoinCodeLength
                    : MaxAddressLength;
            System.Text.StringBuilder builder = new System.Text.StringBuilder(currentText, maxLength);
            for (int i = 0; i < clipboardText.Length && builder.Length < maxLength; i++)
            {
                char character = clipboardText[i];
                if (focusedConnectionInput == portInput)
                {
                    if (char.IsDigit(character))
                    {
                        builder.Append(character);
                    }

                    continue;
                }

                if (relayJoinCodeInput)
                {
                    if (char.IsLetterOrDigit(character))
                    {
                        builder.Append(char.ToUpperInvariant(character));
                    }

                    continue;
                }

                if (IsAllowedAddressCharacter(character))
                {
                    builder.Append(character);
                }
            }

            SetFocusedInputText(builder.ToString());
        }

        private void SetFocusedInputText(string text)
        {
            if (!IsConnectionInputFocused())
            {
                return;
            }

            focusedConnectionInput.text = text ?? string.Empty;
            focusedConnectionInput.MoveTextEnd(false);
            ForceInputLabelUpdate(focusedConnectionInput);
        }

        private void SetPortInputVisible(bool visible)
        {
            if (portLabelTextComponent != null)
            {
                portLabelTextComponent.gameObject.SetActive(visible);
            }

            if (portInput != null)
            {
                portInput.gameObject.SetActive(visible);
                if (!visible && focusedConnectionInput == portInput)
                {
                    ClearFocusedConnectionInput();
                }
            }
        }

        private void SetCopyCodeButtonVisible(bool visible)
        {
            if (copyCodeButton != null)
            {
                copyCodeButton.gameObject.SetActive(visible);
            }
        }

        private bool IsRelayJoinCodeInput(InputField inputField)
        {
            return inputField == addressInput
                && bootstrap != null
                && bootstrap.UsesUnityRelay;
        }

        private static bool IsInputAvailable(InputField inputField)
        {
            return inputField != null
                && inputField.isActiveAndEnabled
                && inputField.interactable;
        }

        private static bool IsAllowedAddressCharacter(char character)
        {
            return char.IsLetterOrDigit(character)
                || character == '.'
                || character == ':'
                || character == '-'
                || character == '_'
                || character == '['
                || character == ']';
        }

        private void HandleFallbackKeyboardInput()
        {
            if (mainPanel != null && mainPanel.activeSelf)
            {
                if (WasKeyPressedThisFrame(KeyCode.Alpha1) || WasKeyPressedThisFrame(KeyCode.Keypad1))
                {
                    HandleSingleModeClicked();
                }
                else if (WasKeyPressedThisFrame(KeyCode.Alpha2) || WasKeyPressedThisFrame(KeyCode.Keypad2))
                {
                    ShowMultiplayerMenu();
                }
                else if (WasKeyPressedThisFrame(KeyCode.Alpha3) || WasKeyPressedThisFrame(KeyCode.Keypad3))
                {
                    ShowRulesMenu();
                }
            }
            else if ((multiplayerPanel != null && multiplayerPanel.activeSelf)
                || (rulesPanel != null && rulesPanel.activeSelf))
            {
                if (WasKeyPressedThisFrame(KeyCode.Escape))
                {
                    ShowMainMenu();
                }
            }
        }

        private static bool TryInvokeButtonAt(Button button, Vector2 screenPosition)
        {
            if (button == null || !button.isActiveAndEnabled || !button.interactable)
            {
                return false;
            }

            RectTransform rectTransform = button.GetComponent<RectTransform>();
            if (rectTransform == null
                || !RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPosition, null))
            {
                return false;
            }

            button.onClick.Invoke();
            return true;
        }

        private static bool TryGetPointerDownPosition(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPosition = mouse.position.ReadValue();
                return true;
            }
#else
            if (Input.GetMouseButtonDown(0))
            {
                screenPosition = Input.mousePosition;
                return true;
            }
#endif

            screenPosition = default;
            return false;
        }

        private static bool WasKeyPressedThisFrame(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            Key inputKey = key switch
            {
                KeyCode.Alpha1 => Key.Digit1,
                KeyCode.Alpha2 => Key.Digit2,
                KeyCode.Alpha3 => Key.Digit3,
                KeyCode.Keypad1 => Key.Numpad1,
                KeyCode.Keypad2 => Key.Numpad2,
                KeyCode.Keypad3 => Key.Numpad3,
                KeyCode.Escape => Key.Escape,
                _ => Key.None
            };

            return inputKey != Key.None && keyboard[inputKey].wasPressedThisFrame;
#else
            return Input.GetKeyDown(key);
#endif
        }

        private void EnsureBootstrap()
        {
            if (bootstrap != null)
            {
                return;
            }

            bootstrap = FlickDomNetworkBootstrap.Active;
            if (bootstrap != null)
            {
                return;
            }

            bootstrap = FindAnyObjectByType<FlickDomNetworkBootstrap>();
            if (bootstrap != null)
            {
                return;
            }

            GameObject bootstrapObject = new GameObject("FlickDom Network Bootstrap");
            bootstrap = bootstrapObject.AddComponent<FlickDomNetworkBootstrap>();
        }

        private Texture2D ResolveBackgroundTexture()
        {
            if (backgroundTexture != null)
            {
                return backgroundTexture;
            }

#if UNITY_EDITOR
            backgroundTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(editorBackgroundAssetPath);
            if (backgroundTexture != null)
            {
                return backgroundTexture;
            }
#endif

            backgroundTexture = Resources.Load<Texture2D>(resourcesBackgroundPath);
            return backgroundTexture;
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
                return resolvedFont;
            }

            resolvedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return resolvedFont;
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

            return Font.CreateDynamicFontFromOSFont(fontNames, fontSize);
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

        private static void SetPanelActive(GameObject panel, bool active)
        {
            if (panel != null)
            {
                panel.SetActive(active);
            }
        }

        private static bool IsTargetScene(string sceneName)
        {
            return string.Equals(sceneName, TargetSceneName, System.StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                GameObject eventSystemObject = new GameObject("Generated Main Menu EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            EnsureSupportedInputModule(eventSystem.gameObject);
        }

        private static void EnsureSupportedInputModule(GameObject eventSystemObject)
        {
            if (eventSystemObject == null)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            StandaloneInputModule legacyModule = eventSystemObject.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
            {
                legacyModule.enabled = false;
            }

            InputSystemUIInputModule inputModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            }

            inputModule.enabled = true;
            inputModule.AssignDefaultActions();
#else
            if (eventSystemObject.GetComponent<StandaloneInputModule>() == null)
            {
                eventSystemObject.AddComponent<StandaloneInputModule>();
            }
#endif
        }
    }
}
