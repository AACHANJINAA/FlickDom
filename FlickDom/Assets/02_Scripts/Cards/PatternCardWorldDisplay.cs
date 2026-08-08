using System.Collections.Generic;
using UnityEngine;

namespace FlickDom.Gameplay
{
    public sealed class PatternCardWorldDisplay : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PatternCardManager cardManager;
        [SerializeField] private PatternCardSlot cardSlot;
        [SerializeField] private PatternCardData explicitCard;
        [SerializeField] private PatternCardTextureBinding[] cardTextureBindings;
        [SerializeField] private Texture2D cardTextureOverride;

        [Header("Display")]
        [SerializeField] private bool preferTexture = true;
        [SerializeField] private bool preserveTextureAspect = true;
        [SerializeField] private bool hideWhenClaimed = true;
        [SerializeField] private bool showAllRemainingCards = true;
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 0.035f, 0f);
        [SerializeField] private Vector2 displayCardSize = new Vector2(1f, 1f);
        [SerializeField] private int cardsPerRow = 3;
        [SerializeField] private Vector2 cardSpacing = new Vector2(1.18f, 1.18f);
        [SerializeField] private Vector2 fallbackCardSize = new Vector2(1.9f, 2.55f);

        [Header("Fallback Generated Card")]
        [SerializeField] private float fallbackGap = 0.015f;
        [SerializeField] private float fallbackHeaderRatio = 0.2f;
        [SerializeField] private float fallbackPadding = 0.12f;
        [SerializeField] private float fallbackThickness = 0.025f;
        [SerializeField] private float fallbackLineWidth = 0.018f;

        [Header("Colors")]
        [SerializeField] private Color cardTint = Color.white;
        [SerializeField] private Color cardBaseColor = Color.white;
        [SerializeField] private Color easyColor = new Color(0.42f, 0.68f, 0.12f, 1f);
        [SerializeField] private Color normalColor = new Color(0.95f, 0.72f, 0.12f, 1f);
        [SerializeField] private Color hardColor = new Color(0.82f, 0.16f, 0.16f, 1f);

        private GameObject generatedRoot;
        private Material textureMaterial;
        private Material baseMaterial;
        private Material lineMaterial;
        private Material fillMaterial;
        private readonly List<Mesh> textureMeshes = new List<Mesh>();
        private readonly List<Material> textureMaterials = new List<Material>();

        private void Awake()
        {
            if (cardManager == null)
            {
                cardManager = GetComponent<PatternCardManager>();
            }

            CreateMaterials();
        }

        private void OnEnable()
        {
            if (cardManager == null)
            {
                return;
            }

            cardManager.ActiveCardChanged += HandleActiveCardChanged;
            cardManager.CardCompleted += HandleCardCompleted;
            cardManager.CardsExhausted += HandleCardsExhausted;
        }

        private void Start()
        {
            Rebuild();
        }

        private void OnDisable()
        {
            if (cardManager == null)
            {
                return;
            }

            cardManager.ActiveCardChanged -= HandleActiveCardChanged;
            cardManager.CardCompleted -= HandleCardCompleted;
            cardManager.CardsExhausted -= HandleCardsExhausted;
        }

        private void OnDestroy()
        {
            DestroyGeneratedRoot();
            DestroyMaterial(textureMaterial);
            DestroyMaterial(baseMaterial);
            DestroyMaterial(lineMaterial);
            DestroyMaterial(fillMaterial);
        }

        private void OnValidate()
        {
            fallbackCardSize.x = Mathf.Max(0.1f, fallbackCardSize.x);
            fallbackCardSize.y = Mathf.Max(0.1f, fallbackCardSize.y);
            fallbackGap = Mathf.Max(0f, fallbackGap);
            fallbackHeaderRatio = Mathf.Clamp01(fallbackHeaderRatio);
            fallbackPadding = Mathf.Max(0f, fallbackPadding);
            fallbackThickness = Mathf.Max(0.001f, fallbackThickness);
            fallbackLineWidth = Mathf.Max(0.001f, fallbackLineWidth);
            displayCardSize.x = Mathf.Max(0.1f, displayCardSize.x);
            displayCardSize.y = Mathf.Max(0.1f, displayCardSize.y);
            cardsPerRow = Mathf.Max(1, cardsPerRow);
            cardSpacing.x = Mathf.Max(0.1f, cardSpacing.x);
            cardSpacing.y = Mathf.Max(0.1f, cardSpacing.y);
        }

        private void HandleActiveCardChanged(PatternCardData card)
        {
            Rebuild();
        }

        private void HandleCardsExhausted()
        {
            Rebuild();
        }

        private void HandleCardCompleted(
            PatternCardData card,
            FlickDomPlayerId player,
            int score,
            Vector2Int matchOrigin)
        {
            Rebuild();
        }

        private void Rebuild()
        {
            DestroyGeneratedRoot();

            Transform rootParent = cardSlot != null ? cardSlot.transform : transform;
            generatedRoot = new GameObject("Generated Pattern Card Display");
            generatedRoot.transform.SetParent(rootParent, false);
            generatedRoot.transform.localPosition = localOffset;
            generatedRoot.transform.localRotation = Quaternion.identity;
            generatedRoot.transform.localScale = Vector3.one;

            if (showAllRemainingCards && explicitCard == null && cardManager != null)
            {
                BuildRemainingCards();
                if (generatedRoot.transform.childCount <= 0)
                {
                    DestroyGeneratedRoot();
                }

                return;
            }

            PatternCardData card = ResolveCard();
            if (card == null)
            {
                DestroyGeneratedRoot();
                return;
            }

            if (hideWhenClaimed && cardManager != null && cardManager.IsCardClaimed(card))
            {
                DestroyGeneratedRoot();
                return;
            }

            BuildCard(card, Vector3.zero);
        }

        private void BuildRemainingCards()
        {
            int remainingCount = cardManager.RemainingCardCount;
            if (remainingCount <= 0)
            {
                return;
            }

            int columnCount = Mathf.Min(cardsPerRow, remainingCount);
            int rowCount = Mathf.CeilToInt(remainingCount / (float)columnCount);
            int cardIndex = 0;

            for (int i = 0; i < remainingCount; i++)
            {
                PatternCardData card = cardManager.GetRemainingCard(i);
                if (card == null)
                {
                    continue;
                }

                int column = cardIndex % columnCount;
                int row = cardIndex / columnCount;
                Vector3 cardOffset = new Vector3(
                    (column - ((columnCount - 1) * 0.5f)) * cardSpacing.x,
                    0f,
                    (((rowCount - 1) * 0.5f) - row) * cardSpacing.y);
                BuildCard(card, cardOffset);
                cardIndex++;
            }
        }

        private void BuildCard(PatternCardData card, Vector3 localPosition)
        {
            GameObject cardRoot = new GameObject(card != null ? "Pattern Card " + card.CardId : "Pattern Card");
            cardRoot.transform.SetParent(generatedRoot.transform, false);
            cardRoot.transform.localPosition = localPosition;
            cardRoot.transform.localRotation = Quaternion.identity;
            cardRoot.transform.localScale = Vector3.one;

            Texture2D texture = ResolveTexture(card);
            if (preferTexture && texture != null)
            {
                BuildTextureCard(cardRoot.transform, texture);
                return;
            }

            BuildFallbackCard(cardRoot.transform, card);
        }

        private PatternCardData ResolveCard()
        {
            if (explicitCard != null)
            {
                return explicitCard;
            }

            return cardManager != null ? cardManager.ActiveCard : null;
        }

        private Texture2D ResolveTexture(PatternCardData card)
        {
            Texture2D boundTexture = PatternCardTextureBinding.Resolve(cardTextureBindings, card);
            if (boundTexture != null)
            {
                return boundTexture;
            }

            if (cardTextureOverride != null)
            {
                return cardTextureOverride;
            }

            if (card == null || string.IsNullOrEmpty(card.ResourcesImagePath))
            {
                return null;
            }

            return Resources.Load<Texture2D>(card.ResourcesImagePath);
        }

        private Vector2 ResolveCardSize(Texture2D texture)
        {
            Vector2 slotSize = displayCardSize;
            if (!preserveTextureAspect || texture == null || texture.height <= 0)
            {
                return slotSize;
            }

            float textureAspect = texture.width / (float)texture.height;
            float slotAspect = slotSize.x / slotSize.y;

            if (textureAspect >= slotAspect)
            {
                return new Vector2(slotSize.x, slotSize.x / textureAspect);
            }

            return new Vector2(slotSize.y * textureAspect, slotSize.y);
        }

        private void BuildTextureCard(Transform parent, Texture2D texture)
        {
            GameObject cardObject = new GameObject("Pattern Card Texture");
            cardObject.transform.SetParent(parent, false);
            cardObject.transform.localPosition = Vector3.zero;

            MeshFilter meshFilter = cardObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = cardObject.AddComponent<MeshRenderer>();

            Vector2 size = ResolveCardSize(texture);
            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;

            Mesh cardMesh = new Mesh();
            cardMesh.name = "Pattern Card Quad";
            cardMesh.vertices = new[]
            {
                new Vector3(-halfWidth, 0f, -halfHeight),
                new Vector3(-halfWidth, 0f, halfHeight),
                new Vector3(halfWidth, 0f, halfHeight),
                new Vector3(halfWidth, 0f, -halfHeight)
            };
            cardMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            cardMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            cardMesh.RecalculateNormals();
            meshFilter.sharedMesh = cardMesh;
            textureMeshes.Add(cardMesh);

            Material cardMaterial = new Material(textureMaterial);
            cardMaterial.name = "Pattern Card Texture Instance Material";
            SetMaterialTexture(cardMaterial, texture);
            SetMaterialColor(cardMaterial, cardTint);
            meshRenderer.sharedMaterial = cardMaterial;
            textureMaterials.Add(cardMaterial);
        }

        private void BuildFallbackCard(Transform parent, PatternCardData card)
        {
            Vector2 cardSize = displayCardSize;
            Color accent = GetDifficultyColor(card.Difficulty);
            SetMaterialColor(baseMaterial, cardBaseColor);
            SetMaterialColor(lineMaterial, accent);
            SetMaterialColor(fillMaterial, accent);

            float headerHeight = cardSize.y * fallbackHeaderRatio;
            float gridAvailableWidth = Mathf.Max(0.1f, cardSize.x - (fallbackPadding * 2f));
            float gridAvailableHeight = Mathf.Max(0.1f, cardSize.y - headerHeight - (fallbackPadding * 2f));
            float cellSize = Mathf.Min(
                (gridAvailableWidth - ((card.Width - 1) * fallbackGap)) / card.Width,
                (gridAvailableHeight - ((card.Height - 1) * fallbackGap)) / card.Height);
            cellSize = Mathf.Max(0.01f, cellSize);

            float gridWidth = (card.Width * cellSize) + ((card.Width - 1) * fallbackGap);
            float gridHeight = (card.Height * cellSize) + ((card.Height - 1) * fallbackGap);
            float gridBottom = (-cardSize.y * 0.5f) + fallbackPadding;
            float gridLeft = -gridWidth * 0.5f;

            CreateCube(
                "Pattern Card Base",
                parent,
                Vector3.zero,
                new Vector3(cardSize.x, fallbackThickness, cardSize.y),
                baseMaterial);

            for (int x = 0; x <= card.Width; x++)
            {
                float lineX = gridLeft + (x * (cellSize + fallbackGap)) - (fallbackGap * 0.5f);
                if (x == 0)
                {
                    lineX = gridLeft - (fallbackLineWidth * 0.5f);
                }
                else if (x == card.Width)
                {
                    lineX = gridLeft + gridWidth + (fallbackLineWidth * 0.5f);
                }

                CreateCube(
                    "Pattern Card Vertical Line",
                    parent,
                    new Vector3(lineX, 0.015f, gridBottom + (gridHeight * 0.5f)),
                    new Vector3(fallbackLineWidth, fallbackThickness, gridHeight + fallbackLineWidth),
                    lineMaterial);
            }

            for (int y = 0; y <= card.Height; y++)
            {
                float lineZ = gridBottom + (y * (cellSize + fallbackGap)) - (fallbackGap * 0.5f);
                if (y == 0)
                {
                    lineZ = gridBottom - (fallbackLineWidth * 0.5f);
                }
                else if (y == card.Height)
                {
                    lineZ = gridBottom + gridHeight + (fallbackLineWidth * 0.5f);
                }

                CreateCube(
                    "Pattern Card Horizontal Line",
                    parent,
                    new Vector3(0f, 0.015f, lineZ),
                    new Vector3(gridWidth + fallbackLineWidth, fallbackThickness, fallbackLineWidth),
                    lineMaterial);
            }

            Vector2Int[] filledCells = card.FilledCells;
            for (int i = 0; i < filledCells.Length; i++)
            {
                Vector2Int cell = filledCells[i];
                Vector3 cellCenter = new Vector3(
                    gridLeft + (cell.x * (cellSize + fallbackGap)) + (cellSize * 0.5f),
                    0.025f,
                    gridBottom + (cell.y * (cellSize + fallbackGap)) + (cellSize * 0.5f));

                CreateCube(
                    "Pattern Card Filled Cell",
                    parent,
                    cellCenter,
                    new Vector3(cellSize * 0.9f, fallbackThickness, cellSize * 0.9f),
                    fillMaterial);
            }
        }

        private GameObject CreateCube(
            string objectName,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;

            Collider cubeCollider = cube.GetComponent<Collider>();
            if (cubeCollider != null)
            {
                Destroy(cubeCollider);
            }

            Renderer renderer = cube.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return cube;
        }

        private void CreateMaterials()
        {
            textureMaterial = RuntimeMaterialUtility.CreateMaterial(
                "Pattern Card Texture Material",
                cardTint,
                "Universal Render Pipeline/Unlit",
                "Unlit/Texture",
                "Sprites/Default");
            baseMaterial = RuntimeMaterialUtility.CreateMaterial(
                "Pattern Card Base Material",
                cardBaseColor,
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit",
                "Standard");
            lineMaterial = RuntimeMaterialUtility.CreateMaterial(
                "Pattern Card Line Material",
                normalColor,
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit",
                "Standard");
            fillMaterial = RuntimeMaterialUtility.CreateMaterial(
                "Pattern Card Fill Material",
                normalColor,
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit",
                "Standard");
        }

        private Color GetDifficultyColor(PatternCardDifficulty difficulty)
        {
            if (difficulty == PatternCardDifficulty.Hard)
            {
                return hardColor;
            }

            if (difficulty == PatternCardDifficulty.Normal)
            {
                return normalColor;
            }

            return easyColor;
        }

        private void DestroyGeneratedRoot()
        {
            if (generatedRoot == null)
            {
                return;
            }

            for (int i = 0; i < textureMeshes.Count; i++)
            {
                Destroy(textureMeshes[i]);
            }

            textureMeshes.Clear();
            for (int i = 0; i < textureMaterials.Count; i++)
            {
                DestroyMaterial(textureMaterials[i]);
            }

            textureMaterials.Clear();
            generatedRoot.SetActive(false);
            Destroy(generatedRoot);
            generatedRoot = null;
        }

        private static void DestroyMaterial(Material material)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static void SetMaterialTexture(Material material, Texture texture)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }

            material.mainTexture = texture;
        }
    }
}
