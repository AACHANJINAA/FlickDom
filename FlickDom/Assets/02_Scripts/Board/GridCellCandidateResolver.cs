using System.Collections.Generic;
using UnityEngine;

namespace FlickDom.Gameplay
{
    // Keeps the physical flick board in float world space, then resolves touched logical cells.
    public sealed class GridCellCandidateResolver : MonoBehaviour
    {
        [SerializeField] private int boardSize = 5;
        [SerializeField] private float cellSize = 1f;
        [SerializeField] private Vector3 boardOrigin = new Vector3(-2.5f, 0f, -2.5f);
        [SerializeField] private float defaultTokenRadius = 0.5f;
        [SerializeField] private bool includeCellsTouchedOnlyByLine = true;
        [SerializeField] private float contactTolerance = 0.001f;

        [Header("Board Visual")]
        [SerializeField] private bool showBoardVisual;
        [SerializeField] private Vector3 visualOffset = new Vector3(0f, 0.03f, 0f);
        [SerializeField] private float visualCellHeight = 0.04f;
        [SerializeField] private float visualGap = 0.035f;
        [SerializeField] private Color visualCellColor = new Color(0.45f, 0.48f, 0.5f);
        [SerializeField] private Color visualAlternateCellColor = new Color(0.55f, 0.58f, 0.6f);

        private Material visualCellMaterial;
        private Material visualAlternateCellMaterial;

        public int BoardSize
        {
            get { return boardSize; }
        }

        public float CellSize
        {
            get { return cellSize; }
        }

        public Vector3 BoardOrigin
        {
            get { return boardOrigin; }
        }

        public Vector3 BoardMax
        {
            get
            {
                return new Vector3(
                    boardOrigin.x + boardSize * cellSize,
                    boardOrigin.y,
                    boardOrigin.z + boardSize * cellSize);
            }
        }

        public float DefaultTokenRadius
        {
            get { return defaultTokenRadius; }
        }

        private void OnValidate()
        {
            boardSize = Mathf.Max(1, boardSize);
            cellSize = Mathf.Max(0.01f, cellSize);
            defaultTokenRadius = Mathf.Max(0.01f, defaultTokenRadius);
            contactTolerance = Mathf.Max(0f, contactTolerance);
            visualCellHeight = Mathf.Max(0.001f, visualCellHeight);
            visualGap = Mathf.Clamp(visualGap, 0f, cellSize * 0.45f);
        }

        private void Awake()
        {
            if (showBoardVisual)
            {
                BuildBoardVisual();
            }
        }

        private void OnDestroy()
        {
            if (visualCellMaterial != null)
            {
                Destroy(visualCellMaterial);
            }

            if (visualAlternateCellMaterial != null)
            {
                Destroy(visualAlternateCellMaterial);
            }
        }

        public PiecePlacementCandidate ResolveCandidate(
            FlickDomPlayerId owner,
            string pieceId,
            Vector3 worldPosition)
        {
            return ResolveCandidate(owner, pieceId, worldPosition, defaultTokenRadius);
        }

        public PiecePlacementCandidate ResolveCandidate(
            FlickDomPlayerId owner,
            string pieceId,
            Vector3 worldPosition,
            float tokenRadius)
        {
            List<Vector2Int> cells = GetCandidateCells(worldPosition, tokenRadius);
            return new PiecePlacementCandidate(pieceId, owner, worldPosition, tokenRadius, cells);
        }

        public List<Vector2Int> GetCandidateCells(Vector3 worldPosition)
        {
            return GetCandidateCells(worldPosition, defaultTokenRadius);
        }

        public List<Vector2Int> GetCandidateCells(Vector3 worldPosition, float tokenRadius)
        {
            float radius = Mathf.Max(0.01f, tokenRadius);
            float scanPadding = radius + contactTolerance;

            int minX = Mathf.FloorToInt((worldPosition.x - scanPadding - boardOrigin.x) / cellSize);
            int maxX = Mathf.FloorToInt((worldPosition.x + scanPadding - boardOrigin.x) / cellSize);
            int minY = Mathf.FloorToInt((worldPosition.z - scanPadding - boardOrigin.z) / cellSize);
            int maxY = Mathf.FloorToInt((worldPosition.z + scanPadding - boardOrigin.z) / cellSize);

            minX = Mathf.Clamp(minX, 0, boardSize - 1);
            maxX = Mathf.Clamp(maxX, 0, boardSize - 1);
            minY = Mathf.Clamp(minY, 0, boardSize - 1);
            maxY = Mathf.Clamp(maxY, 0, boardSize - 1);

            List<Vector2Int> cells = new List<Vector2Int>();
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (CircleIntersectsCell(worldPosition, radius, cell))
                    {
                        cells.Add(cell);
                    }
                }
            }

            return cells;
        }

        public bool IsValidCell(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < boardSize && cell.y >= 0 && cell.y < boardSize;
        }

        public bool IsWorldPositionInsideBoard(Vector3 worldPosition)
        {
            Vector3 boardMax = BoardMax;
            return worldPosition.x >= boardOrigin.x
                && worldPosition.x <= boardMax.x
                && worldPosition.z >= boardOrigin.z
                && worldPosition.z <= boardMax.z;
        }

        public Vector2Int WorldToCellByCenter(Vector3 worldPosition)
        {
            int x = Mathf.FloorToInt((worldPosition.x - boardOrigin.x) / cellSize);
            int y = Mathf.FloorToInt((worldPosition.z - boardOrigin.z) / cellSize);
            return new Vector2Int(x, y);
        }

        public bool TryGetCellWorldBounds(Vector2Int cell, out Vector3 min, out Vector3 max)
        {
            if (!IsValidCell(cell))
            {
                min = default(Vector3);
                max = default(Vector3);
                return false;
            }

            min = new Vector3(
                boardOrigin.x + cell.x * cellSize,
                boardOrigin.y,
                boardOrigin.z + cell.y * cellSize);

            max = new Vector3(min.x + cellSize, boardOrigin.y, min.z + cellSize);
            return true;
        }

        public Vector3 GetCellCenter(Vector2Int cell)
        {
            return new Vector3(
                boardOrigin.x + (cell.x + 0.5f) * cellSize,
                boardOrigin.y,
                boardOrigin.z + (cell.y + 0.5f) * cellSize);
        }

        private bool CircleIntersectsCell(Vector3 circleCenter, float radius, Vector2Int cell)
        {
            float cellMinX = boardOrigin.x + cell.x * cellSize;
            float cellMaxX = cellMinX + cellSize;
            float cellMinZ = boardOrigin.z + cell.y * cellSize;
            float cellMaxZ = cellMinZ + cellSize;

            float closestX = Mathf.Clamp(circleCenter.x, cellMinX, cellMaxX);
            float closestZ = Mathf.Clamp(circleCenter.z, cellMinZ, cellMaxZ);
            float deltaX = circleCenter.x - closestX;
            float deltaZ = circleCenter.z - closestZ;
            float distanceSqr = deltaX * deltaX + deltaZ * deltaZ;

            if (includeCellsTouchedOnlyByLine)
            {
                float effectiveRadius = radius + contactTolerance;
                return distanceSqr <= effectiveRadius * effectiveRadius;
            }

            float overlapRadius = Mathf.Max(0f, radius - contactTolerance);
            return distanceSqr < overlapRadius * overlapRadius;
        }

        private void BuildBoardVisual()
        {
            CreateVisualMaterials();

            GameObject rootObject = new GameObject("Generated Physical Board Grid");
            rootObject.transform.SetParent(transform, false);

            float visibleCellSize = Mathf.Max(0.01f, cellSize - visualGap);
            for (int y = 0; y < boardSize; y++)
            {
                for (int x = 0; x < boardSize; x++)
                {
                    GameObject cellObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cellObject.name = "Physical Board Cell " + x + "," + y;
                    cellObject.transform.SetParent(rootObject.transform, false);
                    cellObject.transform.position = new Vector3(
                        boardOrigin.x + ((x + 0.5f) * cellSize) + visualOffset.x,
                        boardOrigin.y + visualOffset.y,
                        boardOrigin.z + ((y + 0.5f) * cellSize) + visualOffset.z);
                    cellObject.transform.localScale = new Vector3(visibleCellSize, visualCellHeight, visibleCellSize);

                    Collider cellCollider = cellObject.GetComponent<Collider>();
                    if (cellCollider != null)
                    {
                        Destroy(cellCollider);
                    }

                    Renderer cellRenderer = cellObject.GetComponent<Renderer>();
                    cellRenderer.sharedMaterial = ((x + y) & 1) == 0
                        ? visualCellMaterial
                        : visualAlternateCellMaterial;
                }
            }
        }

        private void CreateVisualMaterials()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            visualCellMaterial = CreateVisualMaterial(shader, "Physical Board Cell", visualCellColor);
            visualAlternateCellMaterial = CreateVisualMaterial(shader, "Physical Board Alternate Cell", visualAlternateCellColor);
        }

        private static Material CreateVisualMaterial(Shader shader, string materialName, Color color)
        {
            Material material = new Material(shader);
            material.name = materialName;
            material.color = color;
            return material;
        }
    }
}
