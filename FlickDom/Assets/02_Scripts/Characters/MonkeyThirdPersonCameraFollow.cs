using UnityEngine;
using UnityEngine.InputSystem;

namespace FlickDom.Gameplay
{
    public sealed class MonkeyThirdPersonCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 0.75f, 0f);
        [SerializeField] private float distance = 4.2f;
        [SerializeField] private float minDistance = 2f;
        [SerializeField] private float maxDistance = 7f;
        [SerializeField] private float yaw = 0f;
        [SerializeField] private float pitch = 18f;
        [SerializeField] private float minPitch = 5f;
        [SerializeField] private float maxPitch = 55f;
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float zoomSensitivity = 0.01f;
        [SerializeField] private float followSharpness = 12f;
        [SerializeField] private float rotationSharpness = 18f;
        [SerializeField] private bool allowRightMouseOrbit = true;
        [SerializeField] private bool allowScrollZoom = true;

        private Transform cachedTransform;

        private void Awake()
        {
            cachedTransform = transform;
            SnapToTarget();
        }

        private void OnValidate()
        {
            minDistance = Mathf.Max(0.5f, minDistance);
            maxDistance = Mathf.Max(minDistance, maxDistance);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            maxPitch = Mathf.Max(minPitch, maxPitch);
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            followSharpness = Mathf.Max(0.01f, followSharpness);
            rotationSharpness = Mathf.Max(0.01f, rotationSharpness);
            mouseSensitivity = Mathf.Max(0f, mouseSensitivity);
            zoomSensitivity = Mathf.Max(0f, zoomSensitivity);
        }

        private void LateUpdate()
        {
            if (!target)
            {
                return;
            }

            ReadCameraInput();
            UpdateCamera(Time.deltaTime);
        }

        public void SetTarget(Transform followTarget)
        {
            target = followTarget;
            SnapToTarget();
        }

        public void SnapToTarget()
        {
            if (!target)
            {
                return;
            }

            if (!cachedTransform)
            {
                cachedTransform = transform;
            }

            Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + targetOffset;
            Vector3 desiredPosition = focusPoint - orbitRotation * Vector3.forward * distance;
            Quaternion desiredRotation = Quaternion.LookRotation(focusPoint - desiredPosition, Vector3.up);

            cachedTransform.SetPositionAndRotation(desiredPosition, desiredRotation);
        }

        private void ReadCameraInput()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (allowRightMouseOrbit && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, minPitch, maxPitch);
            }

            if (allowScrollZoom)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (!Mathf.Approximately(scroll, 0f))
                {
                    distance = Mathf.Clamp(distance - scroll * zoomSensitivity, minDistance, maxDistance);
                }
            }
        }

        private void UpdateCamera(float deltaTime)
        {
            Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + targetOffset;
            Vector3 desiredPosition = focusPoint - orbitRotation * Vector3.forward * distance;
            Quaternion desiredRotation = Quaternion.LookRotation(focusPoint - desiredPosition, Vector3.up);
            float positionT = 1f - Mathf.Exp(-followSharpness * deltaTime);
            float rotationT = 1f - Mathf.Exp(-rotationSharpness * deltaTime);

            cachedTransform.position = Vector3.Lerp(cachedTransform.position, desiredPosition, positionT);
            cachedTransform.rotation = Quaternion.Slerp(cachedTransform.rotation, desiredRotation, rotationT);
        }
    }
}
