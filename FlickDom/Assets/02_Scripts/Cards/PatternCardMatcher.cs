using UnityEngine;

namespace FlickDom.Gameplay
{
    public static class PatternCardMatcher
    {
        public static bool TryFindMatch(
            TokenMapManager tokenMapManager,
            PatternCardData card,
            FlickDomPlayerId player,
            bool matchAnywhereOnBoard,
            out Vector2Int matchOrigin)
        {
            matchOrigin = default(Vector2Int);

            if (tokenMapManager == null
                || card == null
                || player == FlickDomPlayerId.None
                || card.FilledCells == null
                || card.FilledCells.Length <= 0)
            {
                return false;
            }

            int boardSize = tokenMapManager.BoardSize;
            Vector2Int[] normalizedCells = BuildNormalizedFilledCells(card.FilledCells, out Vector2Int shapeSize);
            int maxOriginX = matchAnywhereOnBoard ? boardSize - shapeSize.x : 0;
            int maxOriginY = matchAnywhereOnBoard ? boardSize - shapeSize.y : 0;

            if (maxOriginX < 0 || maxOriginY < 0)
            {
                return false;
            }

            for (int y = 0; y <= maxOriginY; y++)
            {
                for (int x = 0; x <= maxOriginX; x++)
                {
                    Vector2Int origin = new Vector2Int(x, y);
                    if (MatchesAt(tokenMapManager, normalizedCells, player, origin))
                    {
                        matchOrigin = origin;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool MatchesAt(
            TokenMapManager tokenMapManager,
            Vector2Int[] filledCells,
            FlickDomPlayerId player,
            Vector2Int origin)
        {
            for (int i = 0; i < filledCells.Length; i++)
            {
                Vector2Int boardCell = origin + filledCells[i];
                if (!tokenMapManager.IsValidCell(boardCell)
                    || tokenMapManager.GetOwner(boardCell) != player)
                {
                    return false;
                }
            }

            return true;
        }

        private static Vector2Int[] BuildNormalizedFilledCells(Vector2Int[] filledCells, out Vector2Int shapeSize)
        {
            int minX = filledCells[0].x;
            int minY = filledCells[0].y;
            int maxX = filledCells[0].x;
            int maxY = filledCells[0].y;

            for (int i = 1; i < filledCells.Length; i++)
            {
                Vector2Int cell = filledCells[i];
                minX = Mathf.Min(minX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxX = Mathf.Max(maxX, cell.x);
                maxY = Mathf.Max(maxY, cell.y);
            }

            Vector2Int[] normalizedCells = new Vector2Int[filledCells.Length];
            for (int i = 0; i < filledCells.Length; i++)
            {
                normalizedCells[i] = new Vector2Int(filledCells[i].x - minX, filledCells[i].y - minY);
            }

            shapeSize = new Vector2Int(maxX - minX + 1, maxY - minY + 1);
            return normalizedCells;
        }
    }
}
