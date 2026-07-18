using UnityEngine;

namespace FlickDom.Gameplay
{
    public sealed class TokenMapGridCell : MonoBehaviour
    {
        public Vector2Int Cell { get; private set; }
        public TokenMapGridView GridView { get; private set; }

        public void Initialize(Vector2Int cell, TokenMapGridView gridView)
        {
            Cell = cell;
            GridView = gridView;
        }
    }
}
