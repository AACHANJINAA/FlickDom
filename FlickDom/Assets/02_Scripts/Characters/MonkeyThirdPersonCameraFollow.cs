using UnityEngine;
using UnityEngine.InputSystem;

namespace FlickDom.Gameplay
{
    public sealed class MonkeyThirdPersonCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private GameModeManager gameModeManager;
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
        [SerializeField] private bool allowRightMouseOrbit;
        [SerializeField] private bool allowScrollZoom = true;
        [Header("Aim Focus")]
        [SerializeField] private float aimFocusSharpness = 18f;
        [SerializeField] private float aimRotationSharpness = 24f;

        private Transform cachedTransform;
        private bool followEnabled = true;
        private bool aimFocusActive;
        private Transform aimFocusAnchor;
        private Vector3 aimFocusLocalOffset;
        private Transform aimLookTarget;
        private Vector3 aimLookTargetOffset;

        private void Awake()
        {
            if (gameModeManager == null)
            {
                gameModeManager = FindAnyObjectByType<GameModeManager>();
            }

            cachedTransform = transform;
            SnapToTarget();
        }

        private void OnEnable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.StateChanged += HandleStateChanged;
                followEnabled = gameModeManager.CurrentState != FlickDomGameState.PlacementSelection;
            }
        }

        private void OnDisable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.StateChanged -= HandleStateChanged;
            }
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
            aimFocusSharpness = Mathf.Max(0.01f, aimFocusSharpness);
            aimRotationSharpness = Mathf.Max(0.01f, aimRotationSharpness);
            mouseSensitivity = Mathf.Max(0f, mouseSensitivity);
            zoomSensitivity = Mathf.Max(0f, zoomSensitivity);
        }

        private void LateUpdate()
        {
            if (!followEnabled || !target)
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
            if (aimFocusActive)
            {
                SnapToAimFocus();
                return;
            }

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

        public void EnableAimFocus(
            Transform focusAnchor,
            Vector3 focusLocalOffset,
            Transform lookTarget,
            Vector3 lookTargetOffset)
        {
            aimFocusAnchor = focusAnchor;
            aimFocusLocalOffset = focusLocalOffset;
            aimLookTarget = lookTarget;
            aimLookTargetOffset = lookTargetOffset;
            aimFocusActive = focusAnchor != null && lookTarget != null;
            SnapToTarget();
        }

        public void DisableAimFocus()
        {
            aimFocusActive = false;
            aimFocusAnchor = null;
            aimLookTarget = null;
        }

        private void ReadCameraInput()
        {
            if (aimFocusActive)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            bool shouldOrbit = !allowRightMouseOrbit || mouse.rightButton.isPressed;
            if (shouldOrbit)
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
            if (aimFocusActive)
            {
                UpdateAimFocusCamera(deltaTime);
                return;
            }

            Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focusPoint = target.position + targetOffset;
            Vector3 desiredPosition = focusPoint - orbitRotation * Vector3.forward * distance;
            Quaternion desiredRotation = Quaternion.LookRotation(focusPoint - desiredPosition, Vector3.up);
            float positionT = 1f - Mathf.Exp(-followSharpness * deltaTime);
            float rotationT = 1f - Mathf.Exp(-rotationSharpness * deltaTime);

            cachedTransform.position = Vector3.Lerp(cachedTransform.position, desiredPosition, positionT);
            cachedTransform.rotation = Quaternion.Slerp(cachedTransform.rotation, desiredRotation, rotationT);
        }

        private void HandleStateChanged(FlickDomGameState previousState, FlickDomGameState nextState)
        {
            followEnabled = nextState != FlickDomGameState.PlacementSelection;
            if (followEnabled)
            {
                SnapToTarget();
            }
        }

        private void SnapToAimFocus()
        {
            if (!TryGetAimFocusPose(out Vector3 desiredPosition, out Quaternion desiredRotation))
            {
                DisableAimFocus();
                return;
            }

            cachedTransform.SetPositionAndRotation(desiredPosition, desiredRotation);
        }

        private void UpdateAimFocusCamera(float deltaTime)
        {
            if (!TryGetAimFocusPose(out Vector3 desiredPosition, out Quaternion desiredRotation))
            {
                DisableAimFocus();
                return;
            }

            float positionT = 1f - Mathf.Exp(-aimFocusSharpness * deltaTime);
            float rotationT = 1f - Mathf.Exp(-aimRotationSharpness * deltaTime);
            cachedTransform.position = Vector3.Lerp(cachedTransform.position, desiredPosition, positionT);
            cachedTransform.rotation = Quaternion.Slerp(cachedTransform.rotation, desiredRotation, rotationT);
        }

        private bool TryGetAimFocusPose(out Vector3 desiredPosition, out Quaternion desiredRotation)
        {
            desiredPosition = Vector3.zero;
            desiredRotation = Quaternion.identity;

            if (!aimFocusActive || aimFocusAnchor == null || aimLookTarget == null)
            {
                return false;
            }

            desiredPosition = aimFocusAnchor.TransformPoint(aimFocusLocalOffset);
            Vector3 lookPoint = aimLookTarget.position + aimLookTargetOffset;
            Vector3 lookDirection = lookPoint - desiredPosition;
            if (lookDirection.sqrMagnitude <= 0.0001f)
            {
                lookDirection = aimFocusAnchor.forward;
            }

            desiredRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            return true;
        }
    }
}
