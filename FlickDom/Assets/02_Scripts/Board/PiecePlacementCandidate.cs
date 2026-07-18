using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlickDom.Gameplay
{
    [Serializable]
    public sealed class PiecePlacementCandidate
    {
        [SerializeField] private string pieceId;
        [SerializeField] private FlickDomPlayerId owner;
        [SerializeField] private Vector3 worldPosition;
        [SerializeField] private float tokenRadius;
        [SerializeField] private List<Vector2Int> candidateCells = new List<Vector2Int>();

        public PiecePlacementCandidate(
            string pieceId,
            FlickDomPlayerId owner,
            Vector3 worldPosition,
            float tokenRadius,
            IReadOnlyList<Vector2Int> candidateCells)
        {
            this.pieceId = pieceId;
            this.owner = owner;
            this.worldPosition = worldPosition;
            this.tokenRadius = tokenRadius;
            this.candidateCells = new List<Vector2Int>(candidateCells);
        }

        public string PieceId
        {
            get { return pieceId; }
        }

        public FlickDomPlayerId Owner
        {
            get { return owner; }
        }

        public Vector3 WorldPosition
        {
            get { return worldPosition; }
        }

        public float TokenRadius
        {
            get { return tokenRadius; }
        }

        public IReadOnlyList<Vector2Int> CandidateCells
        {
            get { return candidateCells; }
        }

        public bool ContainsCell(Vector2Int cell)
        {
            return candidateCells.Contains(cell);
        }
    }
}
