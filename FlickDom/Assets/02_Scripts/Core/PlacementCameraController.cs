using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlickDom.Gameplay
{
    public sealed class PlacementCameraController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private TokenMapGridView tokenMapGridView;
        [SerializeField] private GridCellCandidateResolver flickBoardResolver;
        [SerializeField] private Camera targetCamera;

        [Header("Flick View")]
        [SerializeField] private Vector3 flickOffset = new Vector3(0f, 4.8f, -2.8f);
        [SerializeField] private Vector3 flickEulerAngles = new Vector3(60f, 0f, 0f);
        [SerializeField] private bool useOrthographicDuringFlick = false;
        [SerializeField] private float flickOrthographicSize = 3.5f;
        [SerializeField] private bool allowRightMouseFlickOrbit = true;
        [SerializeField] private float flickOrbitSensitivity = 0.18f;

        [Header("Placement View")]
        [SerializeField] private Vector3 placementOffset = new Vector3(0f, 6f, 0f);
        [SerializeField] private Vector3 placementEulerAngles = new Vector3(90f, 0f, 0f);
        [SerializeField] private bool useOrthographicDuringPlacement = true;
        [SerializeField] private float placementOrthographicSize = 2.7f;
        [SerializeField] private float transitionDuration = 0.45f;
        [SerializeField] private bool returnWhenLeavingPlacement = true;

        private Vector3 gameplayPosition;
        private Quaternion gameplayRotation;
        private bool gameplayOrthographic;
        private float gameplayOrthographicSize;
        private Vector3 manualPreviewPosition;
        private Quaternion manualPreviewRotation;
        private bool manualPreviewOrthographic;
        private float manualPreviewOrthographicSize;
        private bool hasManualPreviewPose;
        private Coroutine activeTransition;
        private float flickOrbitYaw;

        private void Awake()
        {
            if (gameModeManager == null)
            {
                gameModeManager = GetComponent<GameModeManager>();
            }

            if (tokenMapGridView == null)
            {
                tokenMapGridView = GetComponent<TokenMapGridView>();
            }

            if (flickBoardResolver == null)
            {
                flickBoardResolver = GetComponent<GridCellCandidateResolver>();
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            CacheGameplayCameraPose();
            flickOrbitYaw = flickEulerAngles.y;
        }

        private void Update()
        {
            if (targetCamera == null
                || gameModeManager == null
                || !allowRightMouseFlickOrbit
                || !IsFlickViewState(gameModeManager.CurrentState))
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed)
            {
                return;
            }

            Vector2 delta = mouse.delta.ReadValue();
            if (Mathf.Abs(delta.x) <= 0.01f)
            {
                return;
            }

            flickOrbitYaw += delta.x * flickOrbitSensitivity;
            ApplyFlickOrbitPose();
        }

        private void OnEnable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.StateChanged += HandleStateChanged;
            }
        }

        private void OnDisable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.StateChanged -= HandleStateChanged;
            }

            StopActiveTransition();
        }

        private void OnValidate()
        {
            transitionDuration = Mathf.Max(0f, transitionDuration);
            flickOrthographicSize = Mathf.Max(0.1f, flickOrthographicSize);
            flickOrbitSensitivity = Mathf.Max(0.01f, flickOrbitSensitivity);
            placementOrthographicSize = Mathf.Max(0.1f, placementOrthographicSize);
        }

        public void ShowPlacementBoardPreview()
        {
            if (targetCamera == null)
            {
                return;
            }

            if (!hasManualPreviewPose)
            {
                CacheManualPreviewCameraPose();
                hasManualPreviewPose = true;
            }

            MoveToPlacementView();
        }

        public void ReturnFromPlacementBoardPreview()
        {
            if (targetCamera == null || !hasManualPreviewPose)
            {
                return;
            }

            hasManualPreviewPose = false;

            if (gameModeManager != null && gameModeManager.CurrentState == FlickDomGameState.PlacementSelection)
            {
                MoveToPlacementView();
                return;
            }

            BeginTransition(
                manualPreviewPosition,
                manualPreviewRotation,
                manualPreviewOrthographic,
                manualPreviewOrthographicSize);
        }

        private void HandleStateChanged(FlickDomGameState previousState, FlickDomGameState nextState)
        {
            if (targetCamera == null)
            {
                return;
            }

            if (IsFlickViewState(nextState))
            {
                MoveToFlickView();
                return;
            }

            if (nextState == FlickDomGameState.PlacementSelection)
            {
                MoveToPlacementView();
                return;
            }

            if ((previousState == FlickDomGameState.PlacementSelection || IsFlickViewState(previousState))
                && returnWhenLeavingPlacement)
            {
                MoveToGameplayView();
            }
        }

        private void CacheGameplayCameraPose()
        {
            if (targetCamera == null)
            {
                return;
            }

            Transform cameraTransform = targetCamera.transform;
            gameplayPosition = cameraTransform.position;
            gameplayRotation = cameraTransform.rotation;
            gameplayOrthographic = targetCamera.orthographic;
            gameplayOrthographicSize = targetCamera.orthographicSize;
        }

        private void CacheManualPreviewCameraPose()
        {
            Transform cameraTransform = targetCamera.transform;
            manualPreviewPosition = cameraTransform.position;
            manualPreviewRotation = cameraTransform.rotation;
            manualPreviewOrthographic = targetCamera.orthographic;
            manualPreviewOrthographicSize = targetCamera.orthographicSize;
        }

        private void MoveToPlacementView()
        {
            Vector3 focus = tokenMapGridView != null ? tokenMapGridView.GridCenter : Vector3.zero;
            Vector3 targetPosition = focus + placementOffset;
            Quaternion targetRotation = Quaternion.Euler(placementEulerAngles);
            bool targetOrthographic = useOrthographicDuringPlacement;
            float targetOrthographicSize = placementOrthographicSize;

            BeginTransition(targetPosition, targetRotation, targetOrthographic, targetOrthographicSize);
        }

        private void MoveToFlickView()
        {
            Vector3 focus = GetFlickBoardCenter();
            flickOrbitYaw = flickEulerAngles.y;
            Vector3 targetPosition = GetFlickOrbitPosition(focus);
            Quaternion targetRotation = GetFlickOrbitRotation(focus, targetPosition);
            BeginTransition(
                targetPosition,
                targetRotation,
                useOrthographicDuringFlick,
                flickOrthographicSize);
        }

        private void ApplyFlickOrbitPose()
        {
            StopActiveTransition();
            Vector3 focus = GetFlickBoardCenter();
            Vector3 targetPosition = GetFlickOrbitPosition(focus);
            Quaternion targetRotation = GetFlickOrbitRotation(focus, targetPosition);
            ApplyCameraPose(targetPosition, targetRotation, useOrthographicDuringFlick, flickOrthographicSize);
        }

        private void MoveToGameplayView()
        {
            BeginTransition(gameplayPosition, gameplayRotation, gameplayOrthographic, gameplayOrthographicSize);
        }

        private void BeginTransition(
            Vector3 targetPosition,
            Quaternion targetRotation,
            bool targetOrthographic,
            float targetOrthographicSize)
        {
            StopActiveTransition();

            if (transitionDuration <= 0f)
            {
                ApplyCameraPose(targetPosition, targetRotation, targetOrthographic, targetOrthographicSize);
                return;
            }

            activeTransition = StartCoroutine(TransitionCamera(targetPosition, targetRotation, targetOrthographic, targetOrthographicSize));
        }

        private IEnumerator TransitionCamera(
            Vector3 targetPosition,
            Quaternion targetRotation,
            bool targetOrthographic,
            float targetOrthographicSize)
        {
            Transform cameraTransform = targetCamera.transform;
            Vector3 startPosition = cameraTransform.position;
            Quaternion startRotation = cameraTransform.rotation;
            bool startOrthographic = targetCamera.orthographic;
            float startOrthographicSize = targetCamera.orthographicSize;
            float elapsed = 0f;

            targetCamera.orthographic = targetOrthographic;

            while (elapsed < transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / transitionDuration);
                float smoothT = t * t * (3f - (2f * t));

                cameraTransform.position = Vector3.Lerp(startPosition, targetPosition, smoothT);
                cameraTransform.rotation = Quaternion.Slerp(startRotation, targetRotation, smoothT);
                targetCamera.orthographicSize = Mathf.Lerp(startOrthographicSize, targetOrthographicSize, smoothT);
                yield return null;
            }

            ApplyCameraPose(targetPosition, targetRotation, targetOrthographic, targetOrthographicSize);
            activeTransition = null;
        }

        private void ApplyCameraPose(
            Vector3 targetPosition,
            Quaternion targetRotation,
            bool targetOrthographic,
            float targetOrthographicSize)
        {
            Transform cameraTransform = targetCamera.transform;
            cameraTransform.position = targetPosition;
            cameraTransform.rotation = targetRotation;
            targetCamera.orthographic = targetOrthographic;
            targetCamera.orthographicSize = targetOrthographicSize;
        }

        private void StopActiveTransition()
        {
            if (activeTransition == null)
            {
                return;
            }

            StopCoroutine(activeTransition);
            activeTransition = null;
        }

        private static bool IsFlickViewState(FlickDomGameState state)
        {
            return state == FlickDomGameState.PieceOrderSelection
                || state == FlickDomGameState.PlayerFlicking
                || state == FlickDomGameState.PhysicsProcessing;
        }

        private Vector3 GetFlickOrbitPosition(Vector3 focus)
        {
            Quaternion yawRotation = Quaternion.Euler(0f, flickOrbitYaw, 0f);
            return focus + (yawRotation * flickOffset);
        }

        private static Quaternion GetFlickOrbitRotation(Vector3 focus, Vector3 cameraPosition)
        {
            Vector3 lookDirection = focus - cameraPosition;
            if (lookDirection.sqrMagnitude <= 0.0001f)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }

        private Vector3 GetFlickBoardCenter()
        {
            if (flickBoardResolver != null)
            {
                Vector3 boardMax = flickBoardResolver.BoardMax;
                Vector3 boardOrigin = flickBoardResolver.BoardOrigin;
                return new Vector3(
                    (boardOrigin.x + boardMax.x) * 0.5f,
                    boardOrigin.y,
                    (boardOrigin.z + boardMax.z) * 0.5f);
            }

            return tokenMapGridView != null ? tokenMapGridView.GridCenter : Vector3.zero;
        }
    }
}
