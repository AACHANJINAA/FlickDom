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
            int maxOriginX = matchAnywhereOnBoard ? boardSize - card.Width : 0;
            int maxOriginY = matchAnywhereOnBoard ? boardSize - card.Height : 0;

            if (maxOriginX < 0 || maxOriginY < 0)
            {
                return false;
            }

            for (int y = 0; y <= maxOriginY; y++)
            {
                for (int x = 0; x <= maxOriginX; x++)
                {
                    Vector2Int origin = new Vector2Int(x, y);
                    if (MatchesAt(tokenMapManager, card, player, origin))
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
            PatternCardData card,
            FlickDomPlayerId player,
            Vector2Int origin)
        {
            Vector2Int[] filledCells = card.FilledCells;
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
    }
}
