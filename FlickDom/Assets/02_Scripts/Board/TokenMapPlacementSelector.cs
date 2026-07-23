using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FlickDom.Gameplay
{
    public sealed class TokenMapPlacementSelector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private TokenMapManager tokenMapManager;
        [SerializeField] private TokenMapGridView tokenMapGridView;
        [SerializeField] private Camera inputCamera;

        [Header("Selection")]
        [SerializeField] private LayerMask selectionMask = ~0;
        [SerializeField] private float raycastDistance = 100f;
        [SerializeField] private bool autoCompleteWhenAllCandidatesPlaced = true;
        [SerializeField] private bool autoStartNextRoundAfterPlacement = true;
        [SerializeField] private bool showOnlyCurrentCandidate = true;
        [SerializeField] private bool logSelections = true;

        [Header("Selection UI")]
        [SerializeField] private bool showSelectionMessages = true;
        [SerializeField] private Font messageFont;
        [SerializeField] private int messageFontSize = 28;
        [SerializeField] private Vector2 messagePanelSize = new Vector2(560f, 72f);
        [SerializeField] private Vector2 messagePanelOffset = new Vector2(0f, -92f);
        [SerializeField] private Color messagePanelColor = new Color(0.05f, 0.06f, 0.07f, 0.82f);
        [SerializeField] private Color messageTextColor = Color.white;
        [SerializeField] private string claimPromptText = "점령할 칸을 선택하세요";
        [SerializeField] private string relocationPromptText = "점령칸이 5개입니다. 지울 내 점령칸을 선택하세요";
        [SerializeField] private string invalidClaimText = "선택할 수 없는 점령칸입니다";
        [SerializeField] private string invalidRelocationText = "내 점령칸만 지울 수 있습니다";
        [SerializeField] private string claimCompleteText = "점령 완료";
        [SerializeField] private string relocationCompleteText = "점령칸 교체 완료";

        private readonly HashSet<PiecePlacementCandidate> resolvedCandidates = new HashSet<PiecePlacementCandidate>();
        private readonly RaycastHit[] raycastHits = new RaycastHit[8];
        private PiecePlacementCandidate activeCandidate;
        private PiecePlacementCandidate pendingRelocationCandidate;
        private Vector2Int pendingRelocationDestination;
        private bool selectionActive;
        private bool waitingForRelocationSource;
        private Canvas messageCanvas;
        private Text messageText;
        private Font resolvedMessageFont;

        private void Awake()
        {
            if (gameModeManager == null)
            {
                gameModeManager = GetComponent<GameModeManager>();
            }

            if (tokenMapManager == null)
            {
                tokenMapManager = GetComponent<TokenMapManager>();
            }

            if (tokenMapGridView == null)
            {
                tokenMapGridView = GetComponent<TokenMapGridView>();
            }

            if (inputCamera == null)
            {
                inputCamera = Camera.main;
            }

            BuildSelectionMessageUi();
        }

        private void OnValidate()
        {
            raycastDistance = Mathf.Max(1f, raycastDistance);
            messageFontSize = Mathf.Max(8, messageFontSize);
            messagePanelSize.x = Mathf.Max(120f, messagePanelSize.x);
            messagePanelSize.y = Mathf.Max(32f, messagePanelSize.y);
        }

        private void OnEnable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.StateChanged += HandleStateChanged;
                selectionActive = gameModeManager.CurrentState == FlickDomGameState.PlacementSelection;
                if (selectionActive)
                {
                    RefreshActiveCandidateHighlight();
                }
            }
        }

        private void OnDisable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.StateChanged -= HandleStateChanged;
            }
        }

        private void OnDestroy()
        {
            if (messageCanvas != null)
            {
                Destroy(messageCanvas.gameObject);
                messageCanvas = null;
            }
        }

        private void Update()
        {
            if (!selectionActive || gameModeManager == null || tokenMapGridView == null || inputCamera == null)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            Ray ray = inputCamera.ScreenPointToRay(mouse.position.ReadValue());
            int hitCount = Physics.RaycastNonAlloc(ray, raycastHits, raycastDistance, selectionMask);
            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = raycastHits[i].collider;
                if (tokenMapGridView.TryGetCell(hitCollider, out Vector2Int cell))
                {
                    if (waitingForRelocationSource)
                    {
                        TrySelectRelocationSource(cell);
                    }
                    else
                    {
                        TrySelectCell(cell);
                    }

                    return;
                }
            }
        }

        private void HandleStateChanged(FlickDomGameState previousState, FlickDomGameState nextState)
        {
            selectionActive = nextState == FlickDomGameState.PlacementSelection;
            if (selectionActive)
            {
                resolvedCandidates.Clear();
                ClearPendingRelocation();
                RefreshActiveCandidateHighlight();
            }
            else if (previousState == FlickDomGameState.PlacementSelection)
            {
                activeCandidate = null;
                ClearPendingRelocation();
                HideSelectionMessage();
                if (tokenMapGridView != null)
                {
                    tokenMapGridView.ClearCandidateHighlights();
                }
            }
        }

        private void TrySelectCell(Vector2Int cell)
        {
            PiecePlacementCandidate candidate = FindCandidateForCell(cell);
            if (candidate == null)
            {
                if (logSelections)
                {
                    Debug.Log("[PlacementSelect] No unresolved candidate can claim " + cell + ".", this);
                }

                SetSelectionMessage(invalidClaimText);
                return;
            }

            bool placed = gameModeManager.TryApplyCandidatePlacement(
                candidate,
                cell,
                null,
                out TokenPlacementResult result);

            if (!placed && result.Status == TokenPlacementStatus.NeedsRelocationSource)
            {
                BeginRelocationSourceSelection(candidate, cell);
                return;
            }

            if (!placed || !result.IsSuccess)
            {
                if (logSelections)
                {
                    Debug.Log("[PlacementSelect] Failed to claim " + cell + ": " + result.Status, this);
                }

                SetSelectionMessage(invalidClaimText);
                return;
            }

            CompleteCandidatePlacement(candidate, result.RelocatedOwnToken ? relocationCompleteText : claimCompleteText);

            if (logSelections)
            {
                Debug.Log("[PlacementSelect] " + candidate.Owner + " claimed " + cell + " from " + candidate.PieceId + ".", this);
            }
        }

        private void TrySelectRelocationSource(Vector2Int cell)
        {
            if (pendingRelocationCandidate == null || tokenMapManager == null)
            {
                ClearPendingRelocation();
                RefreshActiveCandidateHighlight();
                return;
            }

            if (tokenMapManager.GetOwner(cell) != pendingRelocationCandidate.Owner)
            {
                if (logSelections)
                {
                    Debug.Log("[PlacementSelect] Invalid relocation source " + cell + ". Choose an owned cell.", this);
                }

                SetSelectionMessage(invalidRelocationText);
                return;
            }

            bool placed = gameModeManager.TryApplyCandidatePlacement(
                pendingRelocationCandidate,
                pendingRelocationDestination,
                cell,
                out TokenPlacementResult result);

            if (!placed || !result.IsSuccess)
            {
                if (logSelections)
                {
                    Debug.Log("[PlacementSelect] Failed to relocate from " + cell + ": " + result.Status, this);
                }

                SetSelectionMessage(invalidRelocationText);
                return;
            }

            PiecePlacementCandidate completedCandidate = pendingRelocationCandidate;
            ClearPendingRelocation();
            CompleteCandidatePlacement(completedCandidate, relocationCompleteText);
        }

        private PiecePlacementCandidate FindCandidateForCell(Vector2Int cell)
        {
            if (showOnlyCurrentCandidate && activeCandidate != null)
            {
                return !resolvedCandidates.Contains(activeCandidate) && activeCandidate.ContainsCell(cell)
                    ? activeCandidate
                    : null;
            }

            IReadOnlyList<PiecePlacementCandidate> candidates = gameModeManager.PendingPlacementCandidates;
            for (int i = 0; i < candidates.Count; i++)
            {
                PiecePlacementCandidate candidate = candidates[i];
                if (candidate != null && !resolvedCandidates.Contains(candidate) && candidate.ContainsCell(cell))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void RefreshActiveCandidateHighlight()
        {
            if (!selectionActive || gameModeManager == null || tokenMapGridView == null)
            {
                return;
            }

            activeCandidate = FindNextUnresolvedCandidate();
            tokenMapGridView.ClearCandidateHighlights();

            if (!showOnlyCurrentCandidate)
            {
                ShowAllUnresolvedCandidateHighlights();
                SetSelectionMessage(claimPromptText);
                return;
            }

            if (activeCandidate == null)
            {
                SetSelectionMessage(string.Empty);
                return;
            }

            tokenMapGridView.ShowCandidateCells(activeCandidate);
            SetSelectionMessage(claimPromptText);

            if (logSelections)
            {
                Debug.Log("[PlacementSelect] Waiting for " + activeCandidate.Owner + " to choose a cell from " + activeCandidate.PieceId + ".", this);
            }
        }

        private PiecePlacementCandidate FindNextUnresolvedCandidate()
        {
            if (gameModeManager == null)
            {
                return null;
            }

            IReadOnlyList<PiecePlacementCandidate> candidates = gameModeManager.PendingPlacementCandidates;
            for (int i = 0; i < candidates.Count; i++)
            {
                PiecePlacementCandidate candidate = candidates[i];
                if (candidate != null && !resolvedCandidates.Contains(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void ShowAllUnresolvedCandidateHighlights()
        {
            if (gameModeManager == null || tokenMapGridView == null)
            {
                return;
            }

            IReadOnlyList<PiecePlacementCandidate> candidates = gameModeManager.PendingPlacementCandidates;
            for (int i = 0; i < candidates.Count; i++)
            {
                PiecePlacementCandidate candidate = candidates[i];
                if (candidate != null && !resolvedCandidates.Contains(candidate))
                {
                    tokenMapGridView.ShowCandidateCells(candidate);
                }
            }
        }

        private void BeginRelocationSourceSelection(PiecePlacementCandidate candidate, Vector2Int destination)
        {
            pendingRelocationCandidate = candidate;
            pendingRelocationDestination = destination;
            waitingForRelocationSource = true;

            tokenMapGridView.ClearCandidateHighlights();
            if (tokenMapManager != null)
            {
                tokenMapGridView.ShowCandidateCells(candidate.Owner, tokenMapManager.GetOwnedCells(candidate.Owner));
            }

            SetSelectionMessage(relocationPromptText);

            if (logSelections)
            {
                Debug.Log("[PlacementSelect] " + candidate.Owner + " must choose an owned cell to remove before claiming " + destination + ".", this);
            }
        }

        private void CompleteCandidatePlacement(PiecePlacementCandidate candidate, string message)
        {
            resolvedCandidates.Add(candidate);
            SetSelectionMessage(message);
            RefreshActiveCandidateHighlight();
            CompleteSelectionIfReady();
        }

        private void ClearPendingRelocation()
        {
            pendingRelocationCandidate = null;
            pendingRelocationDestination = default(Vector2Int);
            waitingForRelocationSource = false;
        }

        private void CompleteSelectionIfReady()
        {
            if (!autoCompleteWhenAllCandidatesPlaced)
            {
                return;
            }

            IReadOnlyList<PiecePlacementCandidate> candidates = gameModeManager.PendingPlacementCandidates;
            if (candidates.Count > 0 && resolvedCandidates.Count >= candidates.Count)
            {
                bool completedPlacement = gameModeManager.CompletePlacementSelection();
                if (completedPlacement && autoStartNextRoundAfterPlacement)
                {
                    AdvanceToNextRoundForLocalTest();
                }
            }
        }

        private void AdvanceToNextRoundForLocalTest()
        {
            if (gameModeManager.CurrentState == FlickDomGameState.CardMatch)
            {
                gameModeManager.CompleteCardMatch();
            }

            if (gameModeManager.CurrentState == FlickDomGameState.RoundEnd)
            {
                gameModeManager.FinishRoundAndStartNext();
            }
        }

        private void BuildSelectionMessageUi()
        {
            if (!showSelectionMessages || messageCanvas != null)
            {
                return;
            }

            GameObject canvasObject = new GameObject("Generated Placement Selection Message");
            canvasObject.transform.SetParent(transform, false);

            messageCanvas = canvasObject.AddComponent<Canvas>();
            messageCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            messageCanvas.sortingOrder = 115;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panelObject = new GameObject("Message Panel");
            panelObject.transform.SetParent(messageCanvas.transform, false);
            RectTransform panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.sizeDelta = messagePanelSize;
            panelRect.anchoredPosition = messagePanelOffset;

            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = messagePanelColor;
            panelImage.raycastTarget = false;

            GameObject textObject = new GameObject("Message Text");
            textObject.transform.SetParent(panelObject.transform, false);
            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 6f);
            textRect.offsetMax = new Vector2(-16f, -6f);

            messageText = textObject.AddComponent<Text>();
            messageText.font = ResolveMessageFont(claimPromptText);
            messageText.fontSize = messageFontSize;
            messageText.alignment = TextAnchor.MiddleCenter;
            messageText.color = messageTextColor;
            messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
            messageText.verticalOverflow = VerticalWrapMode.Truncate;
            messageText.raycastTarget = false;
            messageText.text = string.Empty;

            canvasObject.SetActive(false);
        }

        private void SetSelectionMessage(string message)
        {
            if (!showSelectionMessages)
            {
                return;
            }

            BuildSelectionMessageUi();
            if (messageCanvas == null || messageText == null)
            {
                return;
            }

            bool hasMessage = !string.IsNullOrEmpty(message);
            messageCanvas.gameObject.SetActive(hasMessage);
            messageText.text = message;
        }

        private void HideSelectionMessage()
        {
            if (messageCanvas != null)
            {
                messageCanvas.gameObject.SetActive(false);
            }
        }

        private Font ResolveMessageFont(string sampleText)
        {
            if (CanRenderText(resolvedMessageFont, sampleText))
            {
                return resolvedMessageFont;
            }

            if (CanRenderText(messageFont, sampleText))
            {
                resolvedMessageFont = messageFont;
                return messageFont;
            }

            Font dynamicFont = Font.CreateDynamicFontFromOSFont("Malgun Gothic", messageFontSize);
            if (CanRenderText(dynamicFont, sampleText))
            {
                resolvedMessageFont = dynamicFont;
                return dynamicFont;
            }

            dynamicFont = Font.CreateDynamicFontFromOSFont("Arial", messageFontSize);
            if (dynamicFont != null)
            {
                resolvedMessageFont = dynamicFont;
                return dynamicFont;
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private bool CanRenderText(Font candidate, string sampleText)
        {
            if (candidate == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(sampleText))
            {
                return true;
            }

            candidate.RequestCharactersInTexture(sampleText, messageFontSize, FontStyle.Normal);
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
    }
}
