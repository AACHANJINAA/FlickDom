using FlickDom.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FlickDom.Gameplay
{
    public sealed class FlickDomMainMenuHud : MonoBehaviour
    {
        private const string TargetSceneName = "good_Scene";

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

        [Header("Text")]
        [SerializeField] private string titleText = "FlickDom";
        [SerializeField] private string singleModeText = "싱글모드";
        [SerializeField] private string multiplayerText = "멀티플레이";
        [SerializeField] private string gameRulesText = "게임룰";
        [SerializeField] private string createRoomText = "방 생성하기";
        [SerializeField] private string joinRoomText = "조인 룸";
        [SerializeField] private string startGameText = "게임 시작";
        [SerializeField] private string backText = "뒤로가기";
        [SerializeField] private string addressLabelText = "IP 주소";
        [SerializeField] private string portLabelText = "포트";
        [SerializeField] private string emptyRulesText = "";
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
        private Text multiplayerStatusText;
        private Button createRoomButton;
        private Button joinRoomButton;
        private Button startGameButton;
        private Font resolvedFont;
        private FlickDomNetworkBootstrap bootstrap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureMainMenuInScene()
        {
            if (!IsTargetScene(SceneManager.GetActiveScene().name))
            {
                return;
            }

            if (FindAnyObjectByType<FlickDomMainMenuHud>() != null)
            {
                return;
            }

            GameObject hudObject = new GameObject("FlickDom Main Menu HUD");
            hudObject.AddComponent<FlickDomMainMenuHud>();
        }

        private void Awake()
        {
            if (!IsTargetScene(SceneManager.GetActiveScene().name))
            {
                enabled = false;
                return;
            }

            EnsureBootstrap();
            BuildHud();
            ShowMainMenu();
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
            canvas.sortingOrder = 80;

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

            Text title = CreateText("Title", titleText, panel.transform, titleFontSize, lightTextColor, TextAnchor.MiddleCenter);
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 86f;

            AddFlexibleSpace(panel.transform, 8f);
            CreateMenuButton("Single Mode Button", singleModeText, panel.transform, HandleSingleModeClicked);
            CreateMenuButton("Multiplayer Button", multiplayerText, panel.transform, ShowMultiplayerMenu);
            CreateMenuButton("Game Rules Button", gameRulesText, panel.transform, ShowRulesMenu);

            layout.childForceExpandHeight = false;
            return panel;
        }

        private GameObject CreateMultiplayerPanel(Transform parent)
        {
            GameObject panel = CreateMenuPanel("Multiplayer Menu Panel", parent);
            ConfigureVerticalLayout(panel, 22, 22, 12f);

            Text title = CreateText("Multiplayer Title", multiplayerText, panel.transform, 34, lightTextColor, TextAnchor.MiddleCenter);
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 46f;

            CreateText("Address Label", addressLabelText, panel.transform, bodyFontSize, lightTextColor, TextAnchor.LowerLeft);
            addressInput = CreateInputField("Address Input", panel.transform, bootstrap != null ? bootstrap.CurrentConnectAddress : "127.0.0.1");

            CreateText("Port Label", portLabelText, panel.transform, bodyFontSize, lightTextColor, TextAnchor.LowerLeft);
            portInput = CreateInputField("Port Input", panel.transform, bootstrap != null ? bootstrap.CurrentPort.ToString() : "7777");

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
            rowElement.preferredHeight = 52f;

            createRoomButton = CreateMenuButton("Create Room Button", createRoomText, row.transform, HandleCreateRoomClicked);
            joinRoomButton = CreateMenuButton("Join Room Button", joinRoomText, row.transform, HandleJoinRoomClicked);

            startGameButton = CreateMenuButton("Start Game Button", startGameText, panel.transform, HandleStartGameClicked);
            multiplayerStatusText = CreateText("Multiplayer Status", string.Empty, panel.transform, 18, lightTextColor, TextAnchor.UpperLeft);
            LayoutElement statusLayout = multiplayerStatusText.gameObject.AddComponent<LayoutElement>();
            statusLayout.preferredHeight = 74f;

            CreateMenuButton("Multiplayer Back Button", backText, panel.transform, ShowMainMenu);
            return panel;
        }

        private GameObject CreateRulesPanel(Transform parent)
        {
            GameObject panel = CreateMenuPanel("Game Rules Panel", parent);
            ConfigureVerticalLayout(panel, 24, 24, 18f);

            Text title = CreateText("Rules Title", gameRulesText, panel.transform, 34, lightTextColor, TextAnchor.MiddleCenter);
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 58f;

            Text body = CreateText("Rules Body", emptyRulesText, panel.transform, bodyFontSize, lightTextColor, TextAnchor.UpperLeft);
            LayoutElement bodyLayout = body.gameObject.AddComponent<LayoutElement>();
            bodyLayout.flexibleHeight = 1f;

            CreateMenuButton("Rules Back Button", backText, panel.transform, ShowMainMenu);
            return panel;
        }

        private GameObject CreateMenuPanel(string objectName, Transform parent)
        {
            GameObject panel = new GameObject(objectName);
            panel.transform.SetParent(parent, false);

            RectTransform rectTransform = panel.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 0.5f);
            rectTransform.anchorMax = new Vector2(0f, 0.5f);
            rectTransform.pivot = new Vector2(0f, 0.5f);
            rectTransform.anchoredPosition = menuPanelOffset;
            rectTransform.sizeDelta = menuPanelSize;

            Image image = panel.AddComponent<Image>();
            image.color = panelColor;
            image.raycastTarget = true;
            return panel;
        }

        private VerticalLayoutGroup ConfigureVerticalLayout(GameObject panel, int horizontalPadding, int verticalPadding, float spacing)
        {
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(horizontalPadding, horizontalPadding, verticalPadding, verticalPadding);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        private Button CreateMenuButton(string objectName, string text, Transform parent, UnityEngine.Events.UnityAction onClick)
        {
            GameObject buttonObject = new GameObject(objectName);
            buttonObject.transform.SetParent(parent, false);

            RectTransform rectTransform = buttonObject.AddComponent<RectTransform>();
            rectTransform.sizeDelta = buttonSize;

            LayoutElement layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = buttonSize.x;
            layoutElement.preferredHeight = buttonSize.y;

            Image image = buttonObject.AddComponent<Image>();
            image.color = buttonColor;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            ColorBlock colors = button.colors;
            colors.normalColor = buttonColor;
            colors.highlightedColor = buttonHighlightedColor;
            colors.pressedColor = buttonPressedColor;
            colors.selectedColor = buttonHighlightedColor;
            colors.disabledColor = disabledButtonColor;
            button.colors = colors;

            Text buttonText = CreateText("Text", text, buttonObject.transform, buttonFontSize, textColor, TextAnchor.MiddleCenter);
            RectTransform textRect = buttonText.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 3f);
            textRect.offsetMax = new Vector2(-10f, -3f);

            return button;
        }

        private InputField CreateInputField(string objectName, Transform parent, string value)
        {
            GameObject inputObject = new GameObject(objectName);
            inputObject.transform.SetParent(parent, false);

            RectTransform rectTransform = inputObject.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(buttonSize.x, 42f);

            LayoutElement layoutElement = inputObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = buttonSize.x;
            layoutElement.preferredHeight = 42f;

            Image image = inputObject.AddComponent<Image>();
            image.color = inputColor;

            InputField inputField = inputObject.AddComponent<InputField>();
            Text text = CreateText("Text", value, inputObject.transform, bodyFontSize, textColor, TextAnchor.MiddleLeft);
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 3f);
            textRect.offsetMax = new Vector2(-12f, -3f);

            inputField.textComponent = text;
            inputField.text = value;
            inputField.lineType = InputField.LineType.SingleLine;
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

        private void ApplyConnectionInput()
        {
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
            bool canEditConnection = hasBootstrap && !bootstrap.IsRunning;
            bool canStartGame = hasBootstrap && bootstrap.CanStartNetworkGame;

            if (addressInput != null)
            {
                addressInput.interactable = canEditConnection;
            }

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

            if (startGameButton != null)
            {
                startGameButton.interactable = canStartGame;
            }

            if (multiplayerStatusText != null)
            {
                multiplayerStatusText.text = hasBootstrap
                    ? "상태: " + bootstrap.CurrentNetworkModeText
                        + "\n플레이어: " + bootstrap.VisiblePlayerCount + " / " + bootstrap.MaxPlayers
                        + "\n" + bootstrap.LobbyStatusText
                    : "상태: 초기화 중";
            }
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

            Font dynamicFont = CreateDynamicFont(fallbackFontNames, fontSize);
            if (CanRenderText(dynamicFont, sampleText, fontSize))
            {
                resolvedFont = dynamicFont;
                return resolvedFont;
            }

            resolvedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            if (FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystemObject = new GameObject("Generated Main Menu EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
