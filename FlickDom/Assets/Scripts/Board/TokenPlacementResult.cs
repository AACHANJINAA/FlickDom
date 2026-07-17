using UnityEngine;

namespace FlickDom.Gameplay
{
    public enum TokenPlacementStatus
    {
        Placed = 0,
        CapturedOpponent = 1,
        RelocatedOwnToken = 2,
        CapturedOpponentAndRelocated = 3,
        AlreadyOwned = 4,
        InvalidPlayer = 5,
        InvalidCell = 6,
        NeedsRelocationSource = 7,
        InvalidRelocationSource = 8
    }

    public readonly struct TokenPlacementResult
    {
        public TokenPlacementResult(
            TokenPlacementStatus status,
            FlickDomPlayerId player,
            Vector2Int destination,
            FlickDomPlayerId previousOwner = FlickDomPlayerId.None,
            Vector2Int? relocationSource = null)
        {
            Status = status;
            Player = player;
            Destination = destination;
            PreviousOwner = previousOwner;
            RelocationSource = relocationSource;
        }

        public TokenPlacementStatus Status { get; }
        public FlickDomPlayerId Player { get; }
        public Vector2Int Destination { get; }
        public FlickDomPlayerId PreviousOwner { get; }
        public Vector2Int? RelocationSource { get; }

        public bool IsSuccess
        {
            get
            {
                return Status == TokenPlacementStatus.Placed
                    || Status == TokenPlacementStatus.CapturedOpponent
                    || Status == TokenPlacementStatus.RelocatedOwnToken
                    || Status == TokenPlacementStatus.CapturedOpponentAndRelocated
                    || Status == TokenPlacementStatus.AlreadyOwned;
            }
        }

        public bool CapturedOpponent
        {
            get
            {
                return Status == TokenPlacementStatus.CapturedOpponent
                    || Status == TokenPlacementStatus.CapturedOpponentAndRelocated;
            }
        }

        public bool RelocatedOwnToken
        {
            get
            {
                return Status == TokenPlacementStatus.RelocatedOwnToken
                    || Status == TokenPlacementStatus.CapturedOpponentAndRelocated;
            }
        }
    }
}
