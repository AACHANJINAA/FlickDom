using UnityEngine;

namespace FlickDom.Gameplay
{
    public sealed class PatternCardSlot : MonoBehaviour
    {
        [Header("Slot Layout")]
        [SerializeField] private Vector2 slotSize = new Vector2(2.2f, 2.95f);
        [SerializeField] private bool showEmptySlot = true;
        [SerializeField] private float baseThickness = 0.025f;
        [SerializeField] private float borderThickness = 0.045f;
        [SerializeField] private float surfaceYOffset = -0.01f;

        [Header("Colors")]
        [SerializeField] private Color baseColor = new Color(1f, 1f, 1f, 0.88f);
        [SerializeField] private Color borderColor = new Color(0.42f, 0.68f, 0.12f, 1f);

        private GameObject generatedRoot;
        private Material baseMaterial;
        private Material borderMaterial;

        public Vector2 SlotSize
        {
            get { return slotSize; }
        }

        private void Awake()
        {
            CreateMaterials();
            Rebuild();
        }

        private void OnDestroy()
        {
            DestroyGeneratedRoot();
            DestroyMaterial(baseMaterial);
            DestroyMaterial(borderMaterial);
        }

        private void OnValidate()
        {
            slotSize.x = Mathf.Max(0.1f, slotSize.x);
            slotSize.y = Mathf.Max(0.1f, slotSize.y);
            baseThickness = Mathf.Max(0.001f, baseThickness);
            borderThickness = Mathf.Max(0.001f, borderThickness);

            if (Application.isPlaying && baseMaterial != null && borderMaterial != null)
            {
                SetMaterialColor(baseMaterial, baseColor);
                SetMaterialColor(borderMaterial, borderColor);
                Rebuild();
            }
        }

        public void Rebuild()
        {
            DestroyGeneratedRoot();
            if (!showEmptySlot)
            {
                return;
            }

            generatedRoot = new GameObject("Generated Card Slot");
            generatedRoot.transform.SetParent(transform, false);
            generatedRoot.transform.localPosition = new Vector3(0f, surfaceYOffset, 0f);

            CreateCube("Card Slot Base", Vector3.zero, new Vector3(slotSize.x, baseThickness, slotSize.y), baseMaterial);

            float halfWidth = slotSize.x * 0.5f;
            float halfHeight = slotSize.y * 0.5f;
            CreateCube(
                "Card Slot Top Border",
                new Vector3(0f, 0.01f, halfHeight),
                new Vector3(slotSize.x + borderThickness, baseThickness, borderThickness),
                borderMaterial);
            CreateCube(
                "Card Slot Bottom Border",
                new Vector3(0f, 0.01f, -halfHeight),
                new Vector3(slotSize.x + borderThickness, baseThickness, borderThickness),
                borderMaterial);
            CreateCube(
                "Card Slot Left Border",
                new Vector3(-halfWidth, 0.01f, 0f),
                new Vector3(borderThickness, baseThickness, slotSize.y + borderThickness),
                borderMaterial);
            CreateCube(
                "Card Slot Right Border",
                new Vector3(halfWidth, 0.01f, 0f),
                new Vector3(borderThickness, baseThickness, slotSize.y + borderThickness),
                borderMaterial);
        }

        private GameObject CreateCube(string objectName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(generatedRoot.transform, false);
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

        private void OnDrawGizmos()
        {
            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.18f);
            Gizmos.DrawCube(new Vector3(0f, surfaceYOffset, 0f), new Vector3(slotSize.x, 0.01f, slotSize.y));
            Gizmos.color = new Color(borderColor.r, borderColor.g, borderColor.b, 0.75f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(slotSize.x, 0.04f, slotSize.y));

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private void CreateMaterials()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            baseMaterial = new Material(shader);
            baseMaterial.name = "Card Slot Base Material";
            borderMaterial = new Material(shader);
            borderMaterial.name = "Card Slot Border Material";
            SetMaterialColor(baseMaterial, baseColor);
            SetMaterialColor(borderMaterial, borderColor);
        }

        private void DestroyGeneratedRoot()
        {
            if (generatedRoot == null)
            {
                return;
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
    }
}
