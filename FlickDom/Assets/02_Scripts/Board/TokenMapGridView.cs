using System.Collections.Generic;
using UnityEngine;

namespace FlickDom.Gameplay
{
    public sealed class TokenMapGridView : MonoBehaviour
    {
        private const int Player1CandidateFlag = 1;
        private const int Player2CandidateFlag = 2;

        [Header("References")]
        [SerializeField] private TokenMapManager tokenMapManager;

        [Header("Grid Layout")]
        [SerializeField] private bool syncBoardSizeFromTokenMap = true;
        [SerializeField] private int boardSize = 5;
        [SerializeField] private Vector3 gridCenter = new Vector3(0f, 0.04f, -5.5f);
        [SerializeField] private float cellSize = 0.8f;
        [SerializeField] private float gap = 0.05f;
        [SerializeField] private float tileHeight = 0.04f;
        [SerializeField] private bool enableCellColliders = true;

        [Header("Board Art")]
        [SerializeField] private GameObject cellVisualPrefab;
        [SerializeField] private Material emptyMaterialOverride;
        [SerializeField] private Material player1OwnedMaterialOverride;
        [SerializeField] private Material player2OwnedMaterialOverride;

        [Header("Colors")]
        [SerializeField] private Color emptyColor = new Color(0.45f, 0.48f, 0.5f);
        [SerializeField] private Color player1CandidateColor = new Color(0.05f, 0.28f, 1f);
        [SerializeField] private Color player2CandidateColor = new Color(1f, 0.12f, 0.08f);
        [SerializeField] private Color sharedCandidateColor = new Color(0.72f, 0.16f, 0.95f);
        [SerializeField] private Color player1OwnedColor = new Color(0.02f, 0.12f, 0.65f);
        [SerializeField] private Color player2OwnedColor = new Color(0.65f, 0.04f, 0.02f);

        [Header("Candidate Marker")]
        [SerializeField] private bool showCandidateMarkers = true;
        [SerializeField] private float candidateMarkerSizeRatio = 0.52f;
        [SerializeField] private float candidateMarkerHeight = 0.035f;
        [SerializeField] private float candidateMarkerYOffset = 0.045f;

        private Renderer[][] cellRendererGroups;
        private Renderer[,] candidateMarkerRenderers;
        private GameObject[,] candidateMarkerObjects;
        private FlickDomPlayerId[,] ownerCells;
        private int[,] candidateFlags;
        private Material emptyMaterial;
        private Material player1CandidateMaterial;
        private Material player2CandidateMaterial;
        private Material sharedCandidateMaterial;
        private Material player1OwnedMaterial;
        private Material player2OwnedMaterial;
        private Transform cachedTransform;
        private readonly Dictionary<Collider, TokenMapGridCell> cellsByCollider = new Dictionary<Collider, TokenMapGridCell>();

        public Vector3 GridCenter
        {
            get { return gridCenter; }
        }

        private void Awake()
        {
            cachedTransform = transform;

            if (tokenMapManager == null)
            {
                tokenMapManager = GetComponent<TokenMapManager>();
            }

            if (syncBoardSizeFromTokenMap && tokenMapManager != null)
            {
                boardSize = tokenMapManager.BoardSize;
            }

            CreateMaterials();
            BuildGrid();
        }

        private void OnEnable()
        {
            if (tokenMapManager == null)
            {
                return;
            }

            tokenMapManager.CellOwnerChanged += HandleCellOwnerChanged;
            tokenMapManager.MapCleared += HandleMapCleared;
        }

        private void OnDisable()
        {
            if (tokenMapManager == null)
            {
                return;
            }

            tokenMapManager.CellOwnerChanged -= HandleCellOwnerChanged;
            tokenMapManager.MapCleared -= HandleMapCleared;
        }

        private void OnValidate()
        {
            boardSize = Mathf.Max(1, boardSize);
            cellSize = Mathf.Max(0.05f, cellSize);
            gap = Mathf.Max(0f, gap);
            tileHeight = Mathf.Max(0.01f, tileHeight);
            candidateMarkerSizeRatio = Mathf.Clamp(candidateMarkerSizeRatio, 0.1f, 0.95f);
            candidateMarkerHeight = Mathf.Max(0.001f, candidateMarkerHeight);
            candidateMarkerYOffset = Mathf.Max(0.001f, candidateMarkerYOffset);
        }

        public void ClearCandidateHighlights()
        {
            if (candidateFlags == null)
            {
                return;
            }

            for (int x = 0; x < boardSize; x++)
            {
                for (int y = 0; y < boardSize; y++)
                {
                    candidateFlags[x, y] = 0;
                    RepaintCell(new Vector2Int(x, y));
                }
            }
        }

        public void ShowCandidateCells(PiecePlacementCandidate candidate)
        {
            if (candidate == null)
            {
                return;
            }

            ShowCandidateCells(candidate.Owner, candidate.CandidateCells);
        }

        public void ClearCandidateHighlights(PiecePlacementCandidate candidate)
        {
            if (candidate == null)
            {
                return;
            }

            ClearCandidateHighlights(candidate.Owner, candidate.CandidateCells);
        }

        public void ClearCandidateHighlights(FlickDomPlayerId player, IReadOnlyList<Vector2Int> cells)
        {
            if (cells == null || candidateFlags == null)
            {
                return;
            }

            int flag = GetCandidateFlag(player);
            if (flag == 0)
            {
                return;
            }

            int removeMask = ~flag;
            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int cell = cells[i];
                if (!IsValidCell(cell))
                {
                    continue;
                }

                candidateFlags[cell.x, cell.y] &= removeMask;
                RepaintCell(cell);
            }
        }

        public void ShowCandidateCells(FlickDomPlayerId player, IReadOnlyList<Vector2Int> cells)
        {
            if (cells == null || candidateFlags == null)
            {
                return;
            }

            int flag = GetCandidateFlag(player);
            if (flag == 0)
            {
                return;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int cell = cells[i];
                if (!IsValidCell(cell))
                {
                    continue;
                }

                candidateFlags[cell.x, cell.y] |= flag;
                RepaintCell(cell);
            }
        }

        public void RefreshOwnerCells(TokenMapManager map)
        {
            if (map == null || ownerCells == null)
            {
                return;
            }

            for (int x = 0; x < boardSize; x++)
            {
                for (int y = 0; y < boardSize; y++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    ownerCells[x, y] = map.GetOwner(cell);
                    RepaintCell(cell);
                }
            }
        }

        public bool TryGetCell(Collider cellCollider, out Vector2Int cell)
        {
            if (cellCollider != null
                && cellsByCollider.TryGetValue(cellCollider, out TokenMapGridCell gridCell)
                && gridCell != null)
            {
                cell = gridCell.Cell;
                return true;
            }

            cell = default(Vector2Int);
            return false;
        }

        private void BuildGrid()
        {
            cellRendererGroups = new Renderer[boardSize * boardSize][];
            candidateMarkerRenderers = new Renderer[boardSize, boardSize];
            candidateMarkerObjects = new GameObject[boardSize, boardSize];
            ownerCells = new FlickDomPlayerId[boardSize, boardSize];
            candidateFlags = new int[boardSize, boardSize];
            cellsByCollider.Clear();

            float step = cellSize + gap;
            float offset = (boardSize - 1) * step * 0.5f;

            for (int x = 0; x < boardSize; x++)
            {
                for (int y = 0; y < boardSize; y++)
                {
                    GameObject cellObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cellObject.name = "Token Map Cell " + x + "," + y;
                    cellObject.transform.SetParent(cachedTransform, false);
                    cellObject.transform.position = new Vector3(
                        gridCenter.x + (x * step) - offset,
                        gridCenter.y,
                        gridCenter.z + (y * step) - offset);
                    cellObject.transform.localScale = new Vector3(cellSize, tileHeight, cellSize);

                    Vector2Int cell = new Vector2Int(x, y);
                    TokenMapGridCell gridCell = cellObject.AddComponent<TokenMapGridCell>();
                    gridCell.Initialize(cell, this);

                    Collider cellCollider = cellObject.GetComponent<Collider>();
                    if (cellCollider != null && enableCellColliders)
                    {
                        cellsByCollider[cellCollider] = gridCell;
                    }
                    else if (cellCollider != null)
                    {
                        Destroy(cellCollider);
                    }

                    Renderer cellRenderer = cellObject.GetComponent<Renderer>();
                    Renderer[] renderers = CreateCellVisual(cellObject, cellRenderer);
                    SetSharedMaterial(renderers, emptyMaterial);
                    cellRendererGroups[GetCellIndex(x, y)] = renderers;

                    CreateCandidateMarker(x, y, cellObject.transform.position);
                }
            }
        }

        private Renderer[] CreateCellVisual(GameObject cellObject, Renderer fallbackRenderer)
        {
            if (cellVisualPrefab == null)
            {
                return new[] { fallbackRenderer };
            }

            GameObject visualObject = InstantiateVisualObject(cellVisualPrefab, cellObject.transform);
            if (visualObject == null)
            {
                return new[] { fallbackRenderer };
            }

            if (fallbackRenderer != null)
            {
                fallbackRenderer.enabled = false;
            }

            visualObject.name = cellVisualPrefab.name + " Visual";
            visualObject.transform.localPosition = Vector3.zero;
            visualObject.transform.localRotation = Quaternion.identity;
            visualObject.transform.localScale = Vector3.one;

            RemoveVisualColliders(visualObject);
            FitVisualToSize(visualObject, new Vector3(cellSize, tileHeight, cellSize));

            Renderer[] renderers = visualObject.GetComponentsInChildren<Renderer>(true);
            return renderers.Length > 0 ? renderers : new[] { fallbackRenderer };
        }

        private void CreateCandidateMarker(int x, int y, Vector3 cellPosition)
        {
            GameObject markerObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            markerObject.name = "Token Map Candidate Marker " + x + "," + y;
            markerObject.transform.SetParent(cachedTransform, false);
            markerObject.transform.position = new Vector3(
                cellPosition.x,
                cellPosition.y + (tileHeight * 0.5f) + candidateMarkerYOffset,
                cellPosition.z);
            float markerSize = cellSize * candidateMarkerSizeRatio;
            markerObject.transform.localScale = new Vector3(markerSize, candidateMarkerHeight, markerSize);

            Collider markerCollider = markerObject.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            Renderer markerRenderer = markerObject.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = sharedCandidateMaterial;
            markerObject.SetActive(false);

            candidateMarkerObjects[x, y] = markerObject;
            candidateMarkerRenderers[x, y] = markerRenderer;
        }

        private void CreateMaterials()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            emptyMaterial = CreateMaterial(shader, "Token Map Empty", emptyColor);
            player1CandidateMaterial = CreateMaterial(shader, "Token Map Player1 Candidate", player1CandidateColor);
            player2CandidateMaterial = CreateMaterial(shader, "Token Map Player2 Candidate", player2CandidateColor);
            sharedCandidateMaterial = CreateMaterial(shader, "Token Map Shared Candidate", sharedCandidateColor);
            player1OwnedMaterial = CreateMaterial(shader, "Token Map Player1 Owned", player1OwnedColor);
            player2OwnedMaterial = CreateMaterial(shader, "Token Map Player2 Owned", player2OwnedColor);

            if (emptyMaterialOverride != null)
            {
                emptyMaterial = emptyMaterialOverride;
            }

            if (player1OwnedMaterialOverride != null)
            {
                player1OwnedMaterial = player1OwnedMaterialOverride;
            }

            if (player2OwnedMaterialOverride != null)
            {
                player2OwnedMaterial = player2OwnedMaterialOverride;
            }
        }

        private Material CreateMaterial(Shader shader, string materialName, Color color)
        {
            Material material = new Material(shader);
            material.name = materialName;
            material.color = color;
            return material;
        }

        private void RepaintCell(Vector2Int cell)
        {
            if (!IsValidCell(cell) || cellRendererGroups == null)
            {
                return;
            }

            SetSharedMaterial(cellRendererGroups[GetCellIndex(cell.x, cell.y)], ResolveBaseMaterial(cell));
            RepaintCandidateMarker(cell);
        }

        private Material ResolveBaseMaterial(Vector2Int cell)
        {
            FlickDomPlayerId owner = ownerCells[cell.x, cell.y];
            if (owner == FlickDomPlayerId.Player1)
            {
                return player1OwnedMaterial;
            }

            if (owner == FlickDomPlayerId.Player2)
            {
                return player2OwnedMaterial;
            }

            return emptyMaterial;
        }

        private void RepaintCandidateMarker(Vector2Int cell)
        {
            if (candidateMarkerObjects == null || candidateMarkerRenderers == null)
            {
                return;
            }

            GameObject markerObject = candidateMarkerObjects[cell.x, cell.y];
            Renderer markerRenderer = candidateMarkerRenderers[cell.x, cell.y];
            if (markerObject == null || markerRenderer == null)
            {
                return;
            }

            int flags = candidateFlags[cell.x, cell.y];
            bool hasCandidate = showCandidateMarkers && flags != 0;
            markerObject.SetActive(hasCandidate);
            if (!hasCandidate)
            {
                return;
            }

            markerRenderer.sharedMaterial = ResolveCandidateMarkerMaterial(flags);
        }

        private Material ResolveCandidateMarkerMaterial(int flags)
        {
            if ((flags & Player1CandidateFlag) != 0 && (flags & Player2CandidateFlag) != 0)
            {
                return sharedCandidateMaterial;
            }

            if ((flags & Player1CandidateFlag) != 0)
            {
                return player1CandidateMaterial;
            }

            if ((flags & Player2CandidateFlag) != 0)
            {
                return player2CandidateMaterial;
            }

            return sharedCandidateMaterial;
        }

        private void HandleCellOwnerChanged(Vector2Int cell, FlickDomPlayerId previousOwner, FlickDomPlayerId nextOwner)
        {
            if (!IsValidCell(cell))
            {
                return;
            }

            ownerCells[cell.x, cell.y] = nextOwner;
            RepaintCell(cell);
        }

        private void HandleMapCleared()
        {
            if (ownerCells == null || candidateFlags == null)
            {
                return;
            }

            for (int x = 0; x < boardSize; x++)
            {
                for (int y = 0; y < boardSize; y++)
                {
                    ownerCells[x, y] = FlickDomPlayerId.None;
                    candidateFlags[x, y] = 0;
                    RepaintCell(new Vector2Int(x, y));
                }
            }
        }

        private bool IsValidCell(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < boardSize && cell.y >= 0 && cell.y < boardSize;
        }

        private static int GetCandidateFlag(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                return Player1CandidateFlag;
            }

            if (player == FlickDomPlayerId.Player2)
            {
                return Player2CandidateFlag;
            }

            return 0;
        }

        private int GetCellIndex(int x, int y)
        {
            return (y * boardSize) + x;
        }

        private static void SetSharedMaterial(Renderer[] renderers, Material material)
        {
            if (renderers == null || material == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].sharedMaterial = material;
                }
            }
        }

        private static void RemoveVisualColliders(GameObject rootObject)
        {
            Collider[] colliders = rootObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Destroy(colliders[i]);
            }
        }

        private static void FitVisualToSize(GameObject rootObject, Vector3 targetSize)
        {
            Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            Vector3 size = bounds.size;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                return;
            }

            rootObject.transform.localScale = new Vector3(
                targetSize.x / size.x,
                targetSize.y / size.y,
                targetSize.z / size.z);
        }

        private static GameObject InstantiateVisualObject(GameObject prefab, Transform parent)
        {
            Object instance = Instantiate((Object)prefab, parent);
            if (instance is GameObject gameObject)
            {
                return gameObject;
            }

            if (instance is Component component)
            {
                return component.gameObject;
            }

            if (instance != null)
            {
                Destroy(instance);
            }

            return null;
        }
    }
}
