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
    public sealed class FlickDomMainMenuHud : MonoBehaviour
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

            ApplyWebSafeText();
            FlickDomBgmPlayer.PlayStartBgm();
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

            Text title = CreateText("Title", titleText, panel.transform, titleFontSize, lightTextColor, TextAnchor.MiddleCenter);
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 86f;

            AddFlexibleSpace(panel.transform, 8f);
            singleModeButton = CreateMenuButton("Single Mode Button", singleModeText, panel.transform, HandleSingleModeClicked);
            multiplayerButton = CreateMenuButton("Multiplayer Button", multiplayerText, panel.transform, ShowMultiplayerMenu);
            gameRulesButton = CreateMenuButton("Game Rules Button", gameRulesText, panel.transform, ShowRulesMenu);

            layout.childForceExpandHeight = false;
            return panel;
        }

        private GameObject CreateMultiplayerPanel(Transform parent)
        {
            GameObject panel = CreateMenuPanel("Multiplayer Menu Panel", parent);
            ConfigureVerticalLayout(panel, 22, 18, 8f);

            Text title = CreateText("Multiplayer Title", multiplayerText, panel.transform, 34, lightTextColor, TextAnchor.MiddleCenter);
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 40f;

            bool useRelay = bootstrap == null || bootstrap.UsesUnityRelay;
            Text addressLabel = CreateText("Address Label", useRelay ? "Join Code" : addressLabelText, panel.transform, 18, lightTextColor, TextAnchor.LowerLeft);
            LayoutElement addressLabelLayout = addressLabel.gameObject.AddComponent<LayoutElement>();
            addressLabelLayout.preferredHeight = 22f;
            addressInput = CreateInputField("Address Input", panel.transform, useRelay ? string.Empty : bootstrap != null ? bootstrap.CurrentConnectAddress : "127.0.0.1");

            portLabelTextComponent = CreateText("Port Label", portLabelText, panel.transform, 18, lightTextColor, TextAnchor.LowerLeft);
            LayoutElement portLabelLayout = portLabelTextComponent.gameObject.AddComponent<LayoutElement>();
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
            rowElement.preferredHeight = 44f;

            createRoomButton = CreateMenuButton("Create Room Button", createRoomText, row.transform, HandleCreateRoomClicked);
            joinRoomButton = CreateMenuButton("Join Room Button", joinRoomText, row.transform, HandleJoinRoomClicked);

            startGameButton = CreateMenuButton("Start Game Button", startGameText, panel.transform, HandleStartGameClicked);
            multiplayerStatusText = CreateText("Multiplayer Status", string.Empty, panel.transform, 18, lightTextColor, TextAnchor.UpperLeft);
            LayoutElement statusLayout = multiplayerStatusText.gameObject.AddComponent<LayoutElement>();
            statusLayout.preferredHeight = 50f;

            multiplayerBackButton = CreateMenuButton("Multiplayer Back Button", backText, panel.transform, ShowMainMenu);
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

            rulesBackButton = CreateMenuButton("Rules Back Button", backText, panel.transform, ShowMainMenu);
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
            UiButtonClickSound.Attach(button);
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

        private static void SetButtonPreferredHeight(Button button, float height)
        {
            if (button == null)
            {
                return;
            }

            LayoutElement layoutElement = button.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.preferredHeight = height;
            }
        }

        private InputField CreateInputField(string objectName, Transform parent, string value)
        {
            GameObject inputObject = new GameObject(objectName);
            inputObject.transform.SetParent(parent, false);

            RectTransform rectTransform = inputObject.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(buttonSize.x, 36f);

            LayoutElement layoutElement = inputObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = buttonSize.x;
            layoutElement.preferredHeight = 36f;

            Image image = inputObject.AddComponent<Image>();
            image.color = inputColor;

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
                FlickDomBgmPlayer.PlayInGameBgm();
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
                FlickDomBgmPlayer.PlayInGameBgm();
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
