using UnityEngine;

namespace FlickDom.Gameplay
{
    public sealed class MonkeyStrikePlate : MonoBehaviour
    {
        [SerializeField] private Transform plateInstance;
        [SerializeField] private bool showPlate;
        [SerializeField] private bool createPlateIfMissing;
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 0.04f, 0.95f);
        [SerializeField] private Vector3 localScale = new Vector3(0.85f, 0.08f, 0.5f);
        [SerializeField] private Color runtimePlateColor = new Color(0.95f, 0.78f, 0.22f, 1f);

        private Material generatedMaterial;

        public Transform PlateTransform
        {
            get { return plateInstance; }
        }

        public Vector3 HitOrigin
        {
            get
            {
                return plateInstance
                    ? plateInstance.position
                    : transform.TransformPoint(localOffset);
            }
        }

        public Vector3 HitForward
        {
            get { return transform.forward; }
        }

        private void Awake()
        {
            ApplyPlateVisibility();
            EnsurePlateInstance();
            UpdatePlateTransform();
        }

        private void LateUpdate()
        {
            if (!showPlate)
            {
                return;
            }

            UpdatePlateTransform();
        }

        private void OnDestroy()
        {
            if (generatedMaterial)
            {
                Destroy(generatedMaterial);
                generatedMaterial = null;
            }
        }

        private void OnValidate()
        {
            localScale.x = Mathf.Max(0.05f, localScale.x);
            localScale.y = Mathf.Max(0.01f, localScale.y);
            localScale.z = Mathf.Max(0.05f, localScale.z);
        }

        public void SetPlateInstance(Transform plateTransform)
        {
            plateInstance = plateTransform;
            ApplyPlateVisibility();
            UpdatePlateTransform();
        }

        private void EnsurePlateInstance()
        {
            if (!showPlate || plateInstance || !createPlateIfMissing || !Application.isPlaying)
            {
                return;
            }

            GameObject plateObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plateObject.name = "Monkey Strike Plate";
            plateObject.transform.SetParent(transform, false);
            plateInstance = plateObject.transform;
            ApplyRuntimeMaterial(plateObject);
        }

        private void ApplyRuntimeMaterial(GameObject plateObject)
        {
            if (!plateObject.TryGetComponent(out Renderer plateRenderer))
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                return;
            }

            generatedMaterial = new Material(shader);
            generatedMaterial.color = runtimePlateColor;
            plateRenderer.sharedMaterial = generatedMaterial;
        }

        private void ApplyPlateVisibility()
        {
            if (plateInstance)
            {
                plateInstance.gameObject.SetActive(showPlate);
            }
        }

        private void UpdatePlateTransform()
        {
            if (!showPlate || !plateInstance)
            {
                return;
            }

            plateInstance.localPosition = localOffset;
            plateInstance.localRotation = Quaternion.identity;
            plateInstance.localScale = localScale;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = runtimePlateColor;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(localOffset, localScale);
            Gizmos.matrix = previousMatrix;
        }
    }
}
