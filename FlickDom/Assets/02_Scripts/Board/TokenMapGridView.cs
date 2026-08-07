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

        [Header("Occupation Stars")]
        [SerializeField] private GameObject starMarkerPrefab;
        [SerializeField] private GameObject markerTrayPrefab;
        [SerializeField] private GridCellCandidateResolver flickBoardStarResolver;
        [SerializeField] private bool showFlickBoardStars = true;
        [SerializeField] private GameObject[] player1PreplacedStars;
        [SerializeField] private GameObject[] player2PreplacedStars;
        [SerializeField] private Material player1StarMaterial;
        [SerializeField] private Material player2StarMaterial;
        [SerializeField] private int starPoolSize = 5;
        [SerializeField] private Vector3 player1StarTrayStartOffset = new Vector3(-2.65f, 0.18f, -1f);
        [SerializeField] private Vector3 player2StarTrayStartOffset = new Vector3(2.65f, 0.18f, -1f);
        [SerializeField] private Vector3 starTrayStep = new Vector3(0f, 0f, 0.42f);
        [SerializeField] private float starMarkerSize = 0.55f;
        [SerializeField] private float starMarkerHeight = 0.12f;
        [SerializeField] private float starCellYOffset = 0.22f;
        [SerializeField] private Vector3 markerTraySize = new Vector3(0.7f, 0.04f, 2.35f);
        [SerializeField] private float markerTrayYOffset = 0.045f;
        [SerializeField] private float flickBoardStarSize = 0.5f;
        [SerializeField] private float flickBoardStarYOffset = 0.13f;

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
        private Transform placementGridRoot;
        private Transform player1StarRoot;
        private Transform player2StarRoot;
        private bool placementBoardVisible = true;
        private GameObject[] player1Stars;
        private GameObject[] player2Stars;
        private Vector2Int?[] player1StarCells;
        private Vector2Int?[] player2StarCells;
        private GameObject[] player1FlickBoardStars;
        private GameObject[] player2FlickBoardStars;
        private Vector2Int?[] player1FlickBoardStarCells;
        private Vector2Int?[] player2FlickBoardStarCells;
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

            if (flickBoardStarResolver == null)
            {
                flickBoardStarResolver = GetComponent<GridCellCandidateResolver>();
            }

            if (syncBoardSizeFromTokenMap && tokenMapManager != null)
            {
                boardSize = tokenMapManager.BoardSize;
            }

            CreateMaterials();
            BuildGrid();
            BuildStarPools();
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
            starPoolSize = Mathf.Max(1, starPoolSize);
            starMarkerSize = Mathf.Max(0.05f, starMarkerSize);
            starMarkerHeight = Mathf.Max(0.01f, starMarkerHeight);
            markerTraySize.x = Mathf.Max(0.05f, markerTraySize.x);
            markerTraySize.y = Mathf.Max(0.01f, markerTraySize.y);
            markerTraySize.z = Mathf.Max(0.05f, markerTraySize.z);
            markerTrayYOffset = Mathf.Max(0f, markerTrayYOffset);
            flickBoardStarSize = Mathf.Max(0.05f, flickBoardStarSize);
            flickBoardStarYOffset = Mathf.Max(0.001f, flickBoardStarYOffset);
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

            RefreshAllStarsFromOwnerCells();
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

        public void SetPlacementBoardVisible(bool visible)
        {
            if (placementBoardVisible == visible)
            {
                return;
            }

            placementBoardVisible = visible;
            if (placementGridRoot != null)
            {
                placementGridRoot.gameObject.SetActive(visible);
            }

            SetStarRootVisible(player1StarRoot, visible);
            SetStarRootVisible(player2StarRoot, visible);
            SetStarPoolVisible(player1Stars, visible);
            SetStarPoolVisible(player2Stars, visible);
        }

        private void BuildGrid()
        {
            placementGridRoot = new GameObject("Generated Token Map Board").transform;
            placementGridRoot.SetParent(cachedTransform, false);
            placementGridRoot.gameObject.SetActive(placementBoardVisible);

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
                    cellObject.transform.SetParent(placementGridRoot, false);
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
            markerObject.transform.SetParent(placementGridRoot, false);
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
            bool hasCandidate = placementBoardVisible && showCandidateMarkers && flags != 0;
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
            ReturnStarToTray(previousOwner, cell);
            PlaceStarOnCell(nextOwner, cell);
            ReturnFlickBoardStar(previousOwner, cell);
            PlaceFlickBoardStar(nextOwner, cell);
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

            ReturnAllStarsToTray();
            ReturnAllFlickBoardStars();
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

        private void BuildStarPools()
        {
            int poolSize = tokenMapManager != null
                ? Mathf.Max(starPoolSize, tokenMapManager.MaxTokensPerPlayer)
                : starPoolSize;

            player1Stars = ResolveStarPool(FlickDomPlayerId.Player1, player1PreplacedStars, poolSize);
            player2Stars = ResolveStarPool(FlickDomPlayerId.Player2, player2PreplacedStars, poolSize);
            player1StarCells = new Vector2Int?[player1Stars.Length];
            player2StarCells = new Vector2Int?[player2Stars.Length];
            BuildFlickBoardStarPools(poolSize);
            ReturnAllStarsToTray();
            ReturnAllFlickBoardStars();
        }

        private GameObject[] ResolveStarPool(FlickDomPlayerId player, GameObject[] preplacedStars, int fallbackPoolSize)
        {
            if (HasAnyStar(preplacedStars))
            {
                GameObject[] stars = new GameObject[preplacedStars.Length];
                for (int i = 0; i < preplacedStars.Length; i++)
                {
                    GameObject starObject = preplacedStars[i];
                    if (starObject == null)
                    {
                        continue;
                    }

                    PrepareStarObject(player, starObject);
                    stars[i] = starObject;
                }

                return stars;
            }

            if (starMarkerPrefab == null)
            {
                return CreateStarPool(player, fallbackPoolSize);
            }

            return CreateStarPool(player, fallbackPoolSize);
        }

        private GameObject[] CreateStarPool(FlickDomPlayerId player, int poolSize)
        {
            GameObject[] stars = new GameObject[poolSize];
            Transform root = new GameObject(player + " Occupation Stars").transform;
            root.SetParent(cachedTransform, false);
            root.gameObject.SetActive(placementBoardVisible);
            SetStarRoot(player, root);
            CreateMarkerTray(player, root, poolSize);

            for (int i = 0; i < poolSize; i++)
            {
                GameObject starObject = starMarkerPrefab != null
                    ? InstantiateVisualObject(starMarkerPrefab, root)
                    : CreateProceduralStarObject(player, root, starMarkerSize);
                if (starObject == null)
                {
                    continue;
                }

                starObject.name = player + " Star " + (i + 1);
                starObject.transform.localScale = Vector3.one;
                if (starMarkerPrefab != null)
                {
                    FitVisualToSize(starObject, new Vector3(starMarkerSize, starMarkerHeight, starMarkerSize));
                }

                PrepareStarObject(player, starObject);
                stars[i] = starObject;
            }

            return stars;
        }

        private void BuildFlickBoardStarPools(int poolSize)
        {
            if (!showFlickBoardStars || flickBoardStarResolver == null)
            {
                player1FlickBoardStars = null;
                player2FlickBoardStars = null;
                player1FlickBoardStarCells = null;
                player2FlickBoardStarCells = null;
                return;
            }

            player1FlickBoardStars = CreateFlickBoardStarPool(FlickDomPlayerId.Player1, poolSize);
            player2FlickBoardStars = CreateFlickBoardStarPool(FlickDomPlayerId.Player2, poolSize);
            player1FlickBoardStarCells = new Vector2Int?[player1FlickBoardStars.Length];
            player2FlickBoardStarCells = new Vector2Int?[player2FlickBoardStars.Length];
        }

        private GameObject[] CreateFlickBoardStarPool(FlickDomPlayerId player, int poolSize)
        {
            GameObject[] stars = new GameObject[poolSize];
            Transform root = new GameObject(player + " Flick Board Occupation Stars").transform;
            root.SetParent(cachedTransform, false);

            for (int i = 0; i < poolSize; i++)
            {
                GameObject starObject = starMarkerPrefab != null
                    ? InstantiateVisualObject(starMarkerPrefab, root)
                    : CreateProceduralStarObject(player, root, flickBoardStarSize);
                if (starObject == null)
                {
                    continue;
                }

                starObject.name = player + " Flick Board Star " + (i + 1);
                starObject.transform.localScale = Vector3.one;
                if (starMarkerPrefab != null)
                {
                    FitVisualToSize(starObject, new Vector3(flickBoardStarSize, starMarkerHeight, flickBoardStarSize));
                }

                PrepareStarObject(player, starObject);
                starObject.SetActive(false);
                stars[i] = starObject;
            }

            return stars;
        }

        private void CreateMarkerTray(FlickDomPlayerId player, Transform parent, int starCount)
        {
            GameObject trayObject = markerTrayPrefab != null
                ? InstantiateVisualObject(markerTrayPrefab, parent)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (trayObject == null)
            {
                return;
            }

            if (markerTrayPrefab == null)
            {
                trayObject.transform.SetParent(parent, false);
                Renderer trayRenderer = trayObject.GetComponent<Renderer>();
                if (trayRenderer != null)
                {
                    trayRenderer.sharedMaterial = emptyMaterial;
                }
            }

            trayObject.name = player + " Marker Tray";
            trayObject.transform.position = GetStarTrayCenterWorldPosition(player, starCount);
            trayObject.transform.localRotation = Quaternion.identity;
            trayObject.transform.localScale = Vector3.one;
            RemoveVisualColliders(trayObject);
            FitVisualToSize(trayObject, markerTraySize);
        }

        private GameObject CreateProceduralStarObject(FlickDomPlayerId player, Transform parent, float size)
        {
            GameObject starObject = new GameObject(player + " Occupation Star Visual");
            starObject.transform.SetParent(parent, false);

            MeshFilter meshFilter = starObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = CreateStarMesh(size);

            MeshRenderer meshRenderer = starObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = ResolveStarMaterial(player);
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            return starObject;
        }

        private static Mesh CreateStarMesh(float size)
        {
            const int PointCount = 5;
            const int VertexCount = (PointCount * 2) + 1;
            float outerRadius = Mathf.Max(0.05f, size) * 0.5f;
            float innerRadius = outerRadius * 0.48f;

            Vector3[] vertices = new Vector3[VertexCount];
            Vector3[] normals = new Vector3[VertexCount];
            Vector2[] uvs = new Vector2[VertexCount];
            int[] triangles = new int[PointCount * 2 * 3 * 2];

            vertices[0] = Vector3.zero;
            normals[0] = Vector3.up;
            uvs[0] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < VertexCount - 1; i++)
            {
                float radius = i % 2 == 0 ? outerRadius : innerRadius;
                float angle = (Mathf.PI * 0.5f) + (i * Mathf.PI / PointCount);
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                vertices[i + 1] = new Vector3(x, 0f, z);
                normals[i + 1] = Vector3.up;
                uvs[i + 1] = new Vector2(
                    0.5f + (x / (outerRadius * 2f)),
                    0.5f + (z / (outerRadius * 2f)));
            }

            int triangleIndex = 0;
            for (int i = 1; i < VertexCount; i++)
            {
                int next = i == VertexCount - 1 ? 1 : i + 1;
                triangles[triangleIndex++] = 0;
                triangles[triangleIndex++] = i;
                triangles[triangleIndex++] = next;
                triangles[triangleIndex++] = 0;
                triangles[triangleIndex++] = next;
                triangles[triangleIndex++] = i;
            }

            Mesh mesh = new Mesh();
            mesh.name = "Generated Occupation Star";
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void PrepareStarObject(FlickDomPlayerId player, GameObject starObject)
        {
            RemoveVisualColliders(starObject);
            SetSharedMaterial(starObject.GetComponentsInChildren<Renderer>(true), ResolveStarMaterial(player));
        }

        private static bool HasAnyStar(GameObject[] stars)
        {
            if (stars == null)
            {
                return false;
            }

            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshAllStarsFromOwnerCells()
        {
            ReturnAllStarsToTray();
            ReturnAllFlickBoardStars();

            if (ownerCells == null)
            {
                return;
            }

            for (int x = 0; x < boardSize; x++)
            {
                for (int y = 0; y < boardSize; y++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    PlaceStarOnCell(ownerCells[x, y], cell);
                    PlaceFlickBoardStar(ownerCells[x, y], cell);
                }
            }
        }

        private void PlaceStarOnCell(FlickDomPlayerId player, Vector2Int cell)
        {
            if (player == FlickDomPlayerId.None)
            {
                return;
            }

            GameObject[] stars = GetStars(player);
            Vector2Int?[] starCells = GetStarCells(player);
            if (stars == null || starCells == null)
            {
                return;
            }

            int starIndex = FindStarIndex(starCells, cell);
            if (starIndex < 0)
            {
                starIndex = FindStarIndex(starCells, null);
            }

            if (starIndex < 0 || starIndex >= stars.Length || stars[starIndex] == null)
            {
                return;
            }

            starCells[starIndex] = cell;
            stars[starIndex].transform.position = GetStarCellWorldPosition(cell);
            stars[starIndex].SetActive(placementBoardVisible);
        }

        private void PlaceFlickBoardStar(FlickDomPlayerId player, Vector2Int cell)
        {
            if (player == FlickDomPlayerId.None)
            {
                return;
            }

            GameObject[] stars = GetFlickBoardStars(player);
            Vector2Int?[] starCells = GetFlickBoardStarCells(player);
            if (stars == null || starCells == null)
            {
                return;
            }

            int starIndex = FindStarIndex(starCells, cell);
            if (starIndex < 0)
            {
                starIndex = FindStarIndex(starCells, null);
            }

            if (starIndex < 0 || starIndex >= stars.Length || stars[starIndex] == null)
            {
                return;
            }

            starCells[starIndex] = cell;
            stars[starIndex].transform.position = GetFlickBoardStarWorldPosition(cell);
            stars[starIndex].SetActive(true);
        }

        private void ReturnStarToTray(FlickDomPlayerId player, Vector2Int cell)
        {
            if (player == FlickDomPlayerId.None)
            {
                return;
            }

            GameObject[] stars = GetStars(player);
            Vector2Int?[] starCells = GetStarCells(player);
            if (stars == null || starCells == null)
            {
                return;
            }

            int starIndex = FindStarIndex(starCells, cell);
            if (starIndex < 0 || starIndex >= stars.Length || stars[starIndex] == null)
            {
                return;
            }

            starCells[starIndex] = null;
            MoveStarToTray(player, stars[starIndex], starIndex);
        }

        private void ReturnFlickBoardStar(FlickDomPlayerId player, Vector2Int cell)
        {
            if (player == FlickDomPlayerId.None)
            {
                return;
            }

            GameObject[] stars = GetFlickBoardStars(player);
            Vector2Int?[] starCells = GetFlickBoardStarCells(player);
            if (stars == null || starCells == null)
            {
                return;
            }

            int starIndex = FindStarIndex(starCells, cell);
            if (starIndex < 0 || starIndex >= stars.Length || stars[starIndex] == null)
            {
                return;
            }

            starCells[starIndex] = null;
            stars[starIndex].SetActive(false);
        }

        private void ReturnAllStarsToTray()
        {
            ReturnPlayerStarsToTray(FlickDomPlayerId.Player1);
            ReturnPlayerStarsToTray(FlickDomPlayerId.Player2);
        }

        private void ReturnAllFlickBoardStars()
        {
            ReturnPlayerFlickBoardStars(FlickDomPlayerId.Player1);
            ReturnPlayerFlickBoardStars(FlickDomPlayerId.Player2);
        }

        private void ReturnPlayerStarsToTray(FlickDomPlayerId player)
        {
            GameObject[] stars = GetStars(player);
            Vector2Int?[] starCells = GetStarCells(player);
            if (stars == null || starCells == null)
            {
                return;
            }

            for (int i = 0; i < stars.Length; i++)
            {
                starCells[i] = null;
                if (stars[i] != null)
                {
                    MoveStarToTray(player, stars[i], i);
                }
            }
        }

        private void ReturnPlayerFlickBoardStars(FlickDomPlayerId player)
        {
            GameObject[] stars = GetFlickBoardStars(player);
            Vector2Int?[] starCells = GetFlickBoardStarCells(player);
            if (stars == null || starCells == null)
            {
                return;
            }

            for (int i = 0; i < stars.Length; i++)
            {
                starCells[i] = null;
                if (stars[i] != null)
                {
                    stars[i].SetActive(false);
                }
            }
        }

        private void MoveStarToTray(FlickDomPlayerId player, GameObject starObject, int index)
        {
            starObject.transform.position = GetStarTrayWorldPosition(player, index);
            starObject.SetActive(placementBoardVisible);
        }

        private Vector3 GetStarCellWorldPosition(Vector2Int cell)
        {
            float step = cellSize + gap;
            float offset = (boardSize - 1) * step * 0.5f;
            return new Vector3(
                gridCenter.x + (cell.x * step) - offset,
                gridCenter.y + (tileHeight * 0.5f) + starCellYOffset,
                gridCenter.z + (cell.y * step) - offset);
        }

        private Vector3 GetFlickBoardStarWorldPosition(Vector2Int cell)
        {
            if (flickBoardStarResolver == null)
            {
                return GetStarCellWorldPosition(cell);
            }

            Vector3 center = flickBoardStarResolver.GetCellCenter(cell);
            return new Vector3(center.x, center.y + flickBoardStarYOffset, center.z);
        }

        private Vector3 GetStarTrayWorldPosition(FlickDomPlayerId player, int index)
        {
            Vector3 startOffset = player == FlickDomPlayerId.Player1
                ? player1StarTrayStartOffset
                : player2StarTrayStartOffset;
            return GetStarTrayAnchor() + startOffset + (starTrayStep * index);
        }

        private Vector3 GetStarTrayCenterWorldPosition(FlickDomPlayerId player, int starCount)
        {
            int lastIndex = Mathf.Max(0, starCount - 1);
            Vector3 center = GetStarTrayWorldPosition(player, lastIndex) + (GetStarTrayWorldPosition(player, 0) - GetStarTrayWorldPosition(player, lastIndex)) * 0.5f;
            center.y -= markerTrayYOffset;
            return center;
        }

        private Vector3 GetStarTrayAnchor()
        {
            return gridCenter;
        }

        private Material ResolveStarMaterial(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.Player1 && player1StarMaterial != null)
            {
                return player1StarMaterial;
            }

            if (player == FlickDomPlayerId.Player2 && player2StarMaterial != null)
            {
                return player2StarMaterial;
            }

            return player == FlickDomPlayerId.Player1 ? player1OwnedMaterial : player2OwnedMaterial;
        }

        private GameObject[] GetStars(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                return player1Stars;
            }

            if (player == FlickDomPlayerId.Player2)
            {
                return player2Stars;
            }

            return null;
        }

        private Vector2Int?[] GetStarCells(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                return player1StarCells;
            }

            if (player == FlickDomPlayerId.Player2)
            {
                return player2StarCells;
            }

            return null;
        }

        private void SetStarRoot(FlickDomPlayerId player, Transform root)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                player1StarRoot = root;
                return;
            }

            if (player == FlickDomPlayerId.Player2)
            {
                player2StarRoot = root;
            }
        }

        private static void SetStarRootVisible(Transform root, bool visible)
        {
            if (root != null)
            {
                root.gameObject.SetActive(visible);
            }
        }

        private static void SetStarPoolVisible(GameObject[] stars, bool visible)
        {
            if (stars == null)
            {
                return;
            }

            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i] != null)
                {
                    stars[i].SetActive(visible);
                }
            }
        }

        private GameObject[] GetFlickBoardStars(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                return player1FlickBoardStars;
            }

            if (player == FlickDomPlayerId.Player2)
            {
                return player2FlickBoardStars;
            }

            return null;
        }

        private Vector2Int?[] GetFlickBoardStarCells(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                return player1FlickBoardStarCells;
            }

            if (player == FlickDomPlayerId.Player2)
            {
                return player2FlickBoardStarCells;
            }

            return null;
        }

        private static int FindStarIndex(Vector2Int?[] starCells, Vector2Int? targetCell)
        {
            for (int i = 0; i < starCells.Length; i++)
            {
                if (!starCells[i].HasValue && !targetCell.HasValue)
                {
                    return i;
                }

                if (starCells[i].HasValue
                    && targetCell.HasValue
                    && starCells[i].Value == targetCell.Value)
                {
                    return i;
                }
            }

            return -1;
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
