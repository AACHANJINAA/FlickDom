using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [SerializeField] private bool autoRelocateOldestTokenForLocalTest = true;
        [SerializeField] private bool logSelections = true;

        private readonly HashSet<PiecePlacementCandidate> resolvedCandidates = new HashSet<PiecePlacementCandidate>();
        private readonly RaycastHit[] raycastHits = new RaycastHit[8];
        private bool selectionActive;

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
        }

        private void OnEnable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.StateChanged += HandleStateChanged;
                selectionActive = gameModeManager.CurrentState == FlickDomGameState.PlacementSelection;
            }
        }

        private void OnDisable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.StateChanged -= HandleStateChanged;
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
                    TrySelectCell(cell);
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

                return;
            }

            bool placed = gameModeManager.TryApplyCandidatePlacement(
                candidate,
                cell,
                null,
                out TokenPlacementResult result);

            if (!placed
                && autoRelocateOldestTokenForLocalTest
                && result.Status == TokenPlacementStatus.NeedsRelocationSource
                && TryGetAutoRelocationSource(candidate.Owner, out Vector2Int relocationSource))
            {
                placed = gameModeManager.TryApplyCandidatePlacement(
                    candidate,
                    cell,
                    relocationSource,
                    out result);
            }

            if (!placed || !result.IsSuccess)
            {
                if (logSelections)
                {
                    Debug.Log("[PlacementSelect] Failed to claim " + cell + ": " + result.Status, this);
                }

                return;
            }

            resolvedCandidates.Add(candidate);
            tokenMapGridView.ClearCandidateHighlights(candidate);

            if (logSelections)
            {
                Debug.Log("[PlacementSelect] " + candidate.Owner + " claimed " + cell + " from " + candidate.PieceId + ".", this);
            }

            CompleteSelectionIfReady();
        }

        private bool TryGetAutoRelocationSource(FlickDomPlayerId player, out Vector2Int relocationSource)
        {
            if (tokenMapManager == null)
            {
                relocationSource = default(Vector2Int);
                return false;
            }

            List<Vector2Int> ownedCells = tokenMapManager.GetOwnedCells(player);
            if (ownedCells.Count <= 0)
            {
                relocationSource = default(Vector2Int);
                return false;
            }

            relocationSource = ownedCells[0];
            return true;
        }

        private PiecePlacementCandidate FindCandidateForCell(Vector2Int cell)
        {
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
    }
}
