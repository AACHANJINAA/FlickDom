using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlickDom.Gameplay
{
    public sealed class TokenMapManager : MonoBehaviour
    {
        [SerializeField] private int boardSize = 5;
        [SerializeField] private bool limitOwnedCellsPerPlayer = true;
        [SerializeField] private int maxTokensPerPlayer = 5;

        private FlickDomPlayerId[,] owners;
        private readonly List<Vector2Int> player1Cells = new List<Vector2Int>();
        private readonly List<Vector2Int> player2Cells = new List<Vector2Int>();

        public event Action<Vector2Int, FlickDomPlayerId, FlickDomPlayerId> CellOwnerChanged;
        public event Action MapChanged;
        public event Action MapCleared;

        public int BoardSize
        {
            get { return boardSize; }
        }

        public int MaxTokensPerPlayer
        {
            get { return maxTokensPerPlayer; }
        }

        public bool LimitOwnedCellsPerPlayer
        {
            get { return limitOwnedCellsPerPlayer; }
        }

        private void Awake()
        {
            EnsureGrid();
        }

        private void OnValidate()
        {
            boardSize = Mathf.Max(1, boardSize);
            maxTokensPerPlayer = Mathf.Max(1, maxTokensPerPlayer);
        }

        public void ClearMap()
        {
            owners = new FlickDomPlayerId[boardSize, boardSize];
            player1Cells.Clear();
            player2Cells.Clear();

            MapCleared?.Invoke();
            MapChanged?.Invoke();
        }

        public bool TryClaimCell(
            FlickDomPlayerId player,
            Vector2Int destination,
            Vector2Int? relocationSource,
            out TokenPlacementResult result)
        {
            EnsureGrid();

            if (player == FlickDomPlayerId.None)
            {
                result = new TokenPlacementResult(TokenPlacementStatus.InvalidPlayer, player, destination);
                return false;
            }

            if (!IsValidCell(destination))
            {
                result = new TokenPlacementResult(TokenPlacementStatus.InvalidCell, player, destination);
                return false;
            }

            FlickDomPlayerId previousOwner = owners[destination.x, destination.y];
            if (previousOwner == player)
            {
                result = new TokenPlacementResult(
                    TokenPlacementStatus.AlreadyOwned,
                    player,
                    destination,
                    previousOwner);
                return true;
            }

            List<Vector2Int> playerCells = GetMutableCells(player);
            bool requiresRelocation = limitOwnedCellsPerPlayer && playerCells.Count >= maxTokensPerPlayer;
            Vector2Int source = default(Vector2Int);

            if (requiresRelocation)
            {
                if (!relocationSource.HasValue)
                {
                    result = new TokenPlacementResult(
                        TokenPlacementStatus.NeedsRelocationSource,
                        player,
                        destination,
                        previousOwner);
                    return false;
                }

                source = relocationSource.Value;
                if (!IsValidCell(source) || owners[source.x, source.y] != player)
                {
                    result = new TokenPlacementResult(
                        TokenPlacementStatus.InvalidRelocationSource,
                        player,
                        destination,
                        previousOwner,
                        source);
                    return false;
                }
            }

            bool capturedOpponent = previousOwner != FlickDomPlayerId.None && previousOwner != player;
            if (capturedOpponent)
            {
                GetMutableCells(previousOwner).Remove(destination);
            }

            if (requiresRelocation)
            {
                owners[source.x, source.y] = FlickDomPlayerId.None;
                playerCells.Remove(source);
                CellOwnerChanged?.Invoke(source, player, FlickDomPlayerId.None);
            }

            owners[destination.x, destination.y] = player;
            if (!playerCells.Contains(destination))
            {
                playerCells.Add(destination);
            }

            CellOwnerChanged?.Invoke(destination, previousOwner, player);
            MapChanged?.Invoke();

            TokenPlacementStatus status = ResolveStatus(capturedOpponent, requiresRelocation);
            result = new TokenPlacementResult(status, player, destination, previousOwner, relocationSource);
            return true;
        }

        public FlickDomPlayerId GetOwner(Vector2Int cell)
        {
            EnsureGrid();
            if (!IsValidCell(cell))
            {
                return FlickDomPlayerId.None;
            }

            return owners[cell.x, cell.y];
        }

        public bool IsValidCell(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < boardSize && cell.y >= 0 && cell.y < boardSize;
        }

        public int GetOwnedTokenCount(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.None)
            {
                return 0;
            }

            return GetMutableCells(player).Count;
        }

        public List<Vector2Int> GetOwnedCells(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.None)
            {
                return new List<Vector2Int>();
            }

            return new List<Vector2Int>(GetMutableCells(player));
        }

        public FlickDomPlayerId[,] ExportOwnerGrid()
        {
            EnsureGrid();
            FlickDomPlayerId[,] copy = new FlickDomPlayerId[boardSize, boardSize];
            Array.Copy(owners, copy, owners.Length);
            return copy;
        }

        public void ApplyNetworkOwnerGrid(int sourceBoardSize, IReadOnlyList<Vector2Int> player1OwnedCells, IReadOnlyList<Vector2Int> player2OwnedCells)
        {
            EnsureGrid();
            FlickDomPlayerId[,] previousOwners = owners;
            boardSize = Mathf.Max(1, sourceBoardSize);
            owners = new FlickDomPlayerId[boardSize, boardSize];
            player1Cells.Clear();
            player2Cells.Clear();

            ApplyNetworkOwnedCells(FlickDomPlayerId.Player1, player1OwnedCells);
            ApplyNetworkOwnedCells(FlickDomPlayerId.Player2, player2OwnedCells);

            for (int x = 0; x < boardSize; x++)
            {
                for (int y = 0; y < boardSize; y++)
                {
                    FlickDomPlayerId previousOwner = GetPreviousNetworkOwner(previousOwners, x, y);
                    FlickDomPlayerId nextOwner = owners[x, y];
                    if (previousOwner != nextOwner)
                    {
                        CellOwnerChanged?.Invoke(new Vector2Int(x, y), previousOwner, nextOwner);
                    }
                }
            }

            MapChanged?.Invoke();
        }

        private void ApplyNetworkOwnedCells(FlickDomPlayerId owner, IReadOnlyList<Vector2Int> cells)
        {
            if (cells == null)
            {
                return;
            }

            List<Vector2Int> ownedCells = GetMutableCells(owner);
            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int cell = cells[i];
                if (!IsValidCell(cell) || owners[cell.x, cell.y] != FlickDomPlayerId.None)
                {
                    continue;
                }

                owners[cell.x, cell.y] = owner;
                ownedCells.Add(cell);
            }
        }

        private static FlickDomPlayerId GetPreviousNetworkOwner(FlickDomPlayerId[,] previousOwners, int x, int y)
        {
            if (previousOwners == null
                || x < 0
                || y < 0
                || x >= previousOwners.GetLength(0)
                || y >= previousOwners.GetLength(1))
            {
                return FlickDomPlayerId.None;
            }

            return previousOwners[x, y];
        }

        private void EnsureGrid()
        {
            if (owners == null || owners.GetLength(0) != boardSize || owners.GetLength(1) != boardSize)
            {
                owners = new FlickDomPlayerId[boardSize, boardSize];
                player1Cells.Clear();
                player2Cells.Clear();
            }
        }

        private List<Vector2Int> GetMutableCells(FlickDomPlayerId player)
        {
            if (player == FlickDomPlayerId.Player1)
            {
                return player1Cells;
            }

            if (player == FlickDomPlayerId.Player2)
            {
                return player2Cells;
            }

            throw new ArgumentOutOfRangeException(nameof(player), player, "Only Player1 and Player2 can own token map cells.");
        }

        private static TokenPlacementStatus ResolveStatus(bool capturedOpponent, bool relocatedOwnToken)
        {
            if (capturedOpponent && relocatedOwnToken)
            {
                return TokenPlacementStatus.CapturedOpponentAndRelocated;
            }

            if (capturedOpponent)
            {
                return TokenPlacementStatus.CapturedOpponent;
            }

            if (relocatedOwnToken)
            {
                return TokenPlacementStatus.RelocatedOwnToken;
            }

            return TokenPlacementStatus.Placed;
        }
    }
}
