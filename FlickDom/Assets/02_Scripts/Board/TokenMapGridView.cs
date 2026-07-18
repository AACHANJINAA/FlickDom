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

        [Header("Colors")]
        [SerializeField] private Color emptyColor = new Color(0.45f, 0.48f, 0.5f);
        [SerializeField] private Color player1CandidateColor = new Color(0.05f, 0.28f, 1f);
        [SerializeField] private Color player2CandidateColor = new Color(1f, 0.12f, 0.08f);
        [SerializeField] private Color sharedCandidateColor = new Color(0.72f, 0.16f, 0.95f);
        [SerializeField] private Color player1OwnedColor = new Color(0.02f, 0.12f, 0.65f);
        [SerializeField] private Color player2OwnedColor = new Color(0.65f, 0.04f, 0.02f);

        private Renderer[,] cellRenderers;
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
            cellRenderers = new Renderer[boardSize, boardSize];
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
                    cellRenderer.sharedMaterial = emptyMaterial;
                    cellRenderers[x, y] = cellRenderer;
                }
            }
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
            if (!IsValidCell(cell) || cellRenderers == null)
            {
                return;
            }

            cellRenderers[cell.x, cell.y].sharedMaterial = ResolveMaterial(cell);
        }

        private Material ResolveMaterial(Vector2Int cell)
        {
            int flags = candidateFlags[cell.x, cell.y];
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

        private void HandleCellOwnerChanged(Vector2Int cell, FlickDomPlayerId previousOwner, FlickDomPlayerId nextOwner)
        {
            if (!IsValidCell(cell))
            {
                return;
            }

            ownerCells[cell.x, cell.y] = nextOwner;
            candidateFlags[cell.x, cell.y] = 0;
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
    }
}
