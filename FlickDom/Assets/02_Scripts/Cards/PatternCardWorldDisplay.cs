using UnityEngine;

namespace FlickDom.Gameplay
{
    public sealed class PatternCardWorldDisplay : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PatternCardManager cardManager;
        [SerializeField] private PatternCardData explicitCard;
        [SerializeField] private Texture2D cardTextureOverride;

        [Header("World Layout")]
        [SerializeField] private Vector3 displayCenter = new Vector3(0f, 0.18f, 4.35f);
        [SerializeField] private float cardHeight = 2.35f;
        [SerializeField] private bool preferTexture = true;

        [Header("Fallback Generated Card")]
        [SerializeField] private float fallbackCellSize = 0.34f;
        [SerializeField] private float fallbackGap = 0.015f;
        [SerializeField] private float fallbackHeaderHeight = 0.46f;
        [SerializeField] private float fallbackPadding = 0.12f;
        [SerializeField] private float fallbackThickness = 0.025f;
        [SerializeField] private float fallbackLineWidth = 0.018f;

        [Header("Colors")]
        [SerializeField] private Color cardTint = Color.white;
        [SerializeField] private Color completedTint = new Color(1f, 0.9f, 0.35f, 1f);
        [SerializeField] private Color cardBaseColor = Color.white;
        [SerializeField] private Color easyColor = new Color(0.42f, 0.68f, 0.12f, 1f);
        [SerializeField] private Color normalColor = new Color(0.95f, 0.72f, 0.12f, 1f);
        [SerializeField] private Color hardColor = new Color(0.82f, 0.16f, 0.16f, 1f);

        private GameObject generatedRoot;
        private Material textureMaterial;
        private Material baseMaterial;
        private Material lineMaterial;
        private Material fillMaterial;
        private Mesh textureMesh;
        private bool displayCompleted;

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
        }

        private void Start()
        {
            displayCompleted = cardManager != null && cardManager.IsActiveCardClaimed;
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
        }

        private void OnDestroy()
        {
            DestroyGeneratedRoot();
            DestroyMaterial(textureMaterial);
            DestroyMaterial(baseMaterial);
            DestroyMaterial(lineMaterial);
            DestroyMaterial(fillMaterial);
        }

        private void HandleActiveCardChanged(PatternCardData card)
        {
            displayCompleted = cardManager != null && cardManager.IsActiveCardClaimed;
            Rebuild();
        }

        private void HandleCardCompleted(
            PatternCardData card,
            FlickDomPlayerId player,
            int score,
            Vector2Int matchOrigin)
        {
            displayCompleted = true;
            Rebuild();
        }

        private void Rebuild()
        {
            DestroyGeneratedRoot();

            PatternCardData card = ResolveCard();
            if (card == null)
            {
                return;
            }

            generatedRoot = new GameObject("Generated Pattern Card Display");
            generatedRoot.transform.SetParent(transform, false);

            Texture2D texture = ResolveTexture(card);
            if (preferTexture && texture != null)
            {
                BuildTextureCard(texture);
                return;
            }

            BuildFallbackCard(card);
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

        private void BuildTextureCard(Texture2D texture)
        {
            GameObject cardObject = new GameObject("Pattern Card Texture");
            cardObject.transform.SetParent(generatedRoot.transform, false);
            cardObject.transform.position = displayCenter;

            MeshFilter meshFilter = cardObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = cardObject.AddComponent<MeshRenderer>();

            float aspect = texture.height > 0 ? texture.width / (float)texture.height : 0.75f;
            float halfWidth = cardHeight * aspect * 0.5f;
            float halfHeight = cardHeight * 0.5f;

            textureMesh = new Mesh();
            textureMesh.name = "Pattern Card Quad";
            textureMesh.vertices = new[]
            {
                new Vector3(-halfWidth, 0f, -halfHeight),
                new Vector3(-halfWidth, 0f, halfHeight),
                new Vector3(halfWidth, 0f, halfHeight),
                new Vector3(halfWidth, 0f, -halfHeight)
            };
            textureMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            textureMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            textureMesh.RecalculateNormals();
            meshFilter.sharedMesh = textureMesh;

            SetMaterialTexture(textureMaterial, texture);
            SetMaterialColor(textureMaterial, displayCompleted ? completedTint : cardTint);
            meshRenderer.sharedMaterial = textureMaterial;
        }

        private void BuildFallbackCard(PatternCardData card)
        {
            Color accent = GetDifficultyColor(card.Difficulty);
            SetMaterialColor(baseMaterial, cardBaseColor);
            SetMaterialColor(lineMaterial, accent);
            SetMaterialColor(fillMaterial, displayCompleted ? completedTint : accent);

            float gridWidth = (card.Width * fallbackCellSize) + ((card.Width - 1) * fallbackGap);
            float gridHeight = (card.Height * fallbackCellSize) + ((card.Height - 1) * fallbackGap);
            float totalWidth = gridWidth + (fallbackPadding * 2f);
            float totalHeight = gridHeight + fallbackHeaderHeight + (fallbackPadding * 2f);
            float gridBottom = displayCenter.z - (totalHeight * 0.5f) + fallbackPadding;
            float gridLeft = displayCenter.x - (gridWidth * 0.5f);

            CreateCube(
                "Pattern Card Base",
                new Vector3(displayCenter.x, displayCenter.y - 0.01f, displayCenter.z),
                new Vector3(totalWidth, fallbackThickness, totalHeight),
                baseMaterial);

            for (int x = 0; x <= card.Width; x++)
            {
                float lineX = gridLeft + (x * (fallbackCellSize + fallbackGap)) - (fallbackGap * 0.5f);
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
                    new Vector3(lineX, displayCenter.y + 0.005f, gridBottom + (gridHeight * 0.5f)),
                    new Vector3(fallbackLineWidth, fallbackThickness, gridHeight + fallbackLineWidth),
                    lineMaterial);
            }

            for (int y = 0; y <= card.Height; y++)
            {
                float lineZ = gridBottom + (y * (fallbackCellSize + fallbackGap)) - (fallbackGap * 0.5f);
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
                    new Vector3(displayCenter.x, displayCenter.y + 0.005f, lineZ),
                    new Vector3(gridWidth + fallbackLineWidth, fallbackThickness, fallbackLineWidth),
                    lineMaterial);
            }

            Vector2Int[] filledCells = card.FilledCells;
            for (int i = 0; i < filledCells.Length; i++)
            {
                Vector2Int cell = filledCells[i];
                Vector3 cellCenter = new Vector3(
                    gridLeft + (cell.x * (fallbackCellSize + fallbackGap)) + (fallbackCellSize * 0.5f),
                    displayCenter.y + 0.012f,
                    gridBottom + (cell.y * (fallbackCellSize + fallbackGap)) + (fallbackCellSize * 0.5f));

                CreateCube(
                    "Pattern Card Filled Cell",
                    cellCenter,
                    new Vector3(fallbackCellSize * 0.9f, fallbackThickness, fallbackCellSize * 0.9f),
                    fillMaterial);
            }
        }

        private GameObject CreateCube(string objectName, Vector3 position, Vector3 scale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(generatedRoot.transform, false);
            cube.transform.position = position;
            cube.transform.localScale = scale;

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
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
            {
                unlitShader = Shader.Find("Unlit/Texture");
            }

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                litShader = Shader.Find("Standard");
            }

            textureMaterial = new Material(unlitShader != null ? unlitShader : litShader);
            textureMaterial.name = "Pattern Card Texture Material";
            baseMaterial = new Material(litShader);
            baseMaterial.name = "Pattern Card Base Material";
            lineMaterial = new Material(litShader);
            lineMaterial.name = "Pattern Card Line Material";
            fillMaterial = new Material(litShader);
            fillMaterial.name = "Pattern Card Fill Material";
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

            if (textureMesh != null)
            {
                Destroy(textureMesh);
                textureMesh = null;
            }

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
