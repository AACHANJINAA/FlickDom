using System.Collections;
using System.Collections.Generic;
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

        [Header("Flick Pull Zoom")]
        [SerializeField] private bool enableFlickPullZoomOut = true;
        [SerializeField, Range(0f, 0.45f)] private float pullZoomOutDistanceRatio = 0.18f;
        [SerializeField] private float pullZoomOutOrthographicSize = 0.55f;
        [SerializeField] private float pullZoomSharpness = 16f;

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
        private readonly List<TurnBasedFlickPiece> boundFlickPieces = new List<TurnBasedFlickPiece>(8);
        private TurnBasedFlickPiece activePullPiece;
        private float targetPullZoom;
        private float currentPullZoom;
        private bool pullZoomPoseApplied;

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
                SetPlacementBoardVisible(!IsFlickViewState(gameModeManager.CurrentState));
            }

            BindFlickPieces();
        }

        private void OnDisable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.StateChanged -= HandleStateChanged;
            }

            StopActiveTransition();
            UnbindFlickPieces();
            ResetFlickPullZoom();
        }

        private void OnValidate()
        {
            transitionDuration = Mathf.Max(0f, transitionDuration);
            flickOrthographicSize = Mathf.Max(0.1f, flickOrthographicSize);
            flickOrbitSensitivity = Mathf.Max(0.01f, flickOrbitSensitivity);
            pullZoomOutDistanceRatio = Mathf.Max(0f, pullZoomOutDistanceRatio);
            pullZoomOutOrthographicSize = Mathf.Max(0f, pullZoomOutOrthographicSize);
            pullZoomSharpness = Mathf.Max(0.01f, pullZoomSharpness);
            placementOrthographicSize = Mathf.Max(0.1f, placementOrthographicSize);
        }

        private void Start()
        {
            BindFlickPieces();
        }

        private void LateUpdate()
        {
            UpdateFlickPullZoom(Time.deltaTime);
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

            SetPlacementBoardVisible(!IsFlickViewState(gameModeManager != null
                ? gameModeManager.CurrentState
                : FlickDomGameState.NotStarted));
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
                BindFlickPieces();
                ResetFlickPullZoom();
                MoveToFlickView();
                return;
            }

            ResetFlickPullZoom();
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
            SetPlacementBoardVisible(true);
            Vector3 focus = tokenMapGridView != null ? tokenMapGridView.GridCenter : Vector3.zero;
            Vector3 targetPosition = focus + placementOffset;
            Quaternion targetRotation = Quaternion.Euler(placementEulerAngles);
            bool targetOrthographic = useOrthographicDuringPlacement;
            float targetOrthographicSize = placementOrthographicSize;

            BeginTransition(targetPosition, targetRotation, targetOrthographic, targetOrthographicSize);
        }

        private void MoveToFlickView()
        {
            SetPlacementBoardVisible(false);
            Vector3 focus = GetFlickBoardCenter();
            flickOrbitYaw = flickEulerAngles.y;
            Vector3 targetPosition = GetFlickOrbitPosition(focus, 0f);
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
            ApplyFlickCameraPose(currentPullZoom);
        }

        private void MoveToGameplayView()
        {
            SetPlacementBoardVisible(true);
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

        private void SetPlacementBoardVisible(bool visible)
        {
            if (tokenMapGridView != null)
            {
                tokenMapGridView.SetPlacementBoardVisible(visible);
            }
        }

        private Vector3 GetFlickOrbitPosition(Vector3 focus)
        {
            return GetFlickOrbitPosition(focus, 0f);
        }

        private Vector3 GetFlickOrbitPosition(Vector3 focus, float pullZoom)
        {
            Quaternion yawRotation = Quaternion.Euler(0f, flickOrbitYaw, 0f);
            Vector3 offset = yawRotation * flickOffset;
            if (enableFlickPullZoomOut && pullZoom > 0f)
            {
                offset *= 1f + pullZoomOutDistanceRatio * Mathf.Clamp01(pullZoom);
            }

            return focus + offset;
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

        private void BindFlickPieces()
        {
            TurnBasedFlickPiece[] pieces =
                FindObjectsByType<TurnBasedFlickPiece>(FindObjectsInactive.Include);
            for (int i = 0; i < pieces.Length; i++)
            {
                TryBindFlickPiece(pieces[i]);
            }
        }

        private void TryBindFlickPiece(TurnBasedFlickPiece piece)
        {
            if (piece == null || boundFlickPieces.Contains(piece))
            {
                return;
            }

            boundFlickPieces.Add(piece);
            piece.FlickDragStarted += HandleFlickDragStarted;
            piece.FlickDragUpdated += HandleFlickDragUpdated;
            piece.FlickDragCancelled += HandleFlickDragEnded;
            piece.FlickReleased += HandleFlickReleased;
        }

        private void UnbindFlickPieces()
        {
            for (int i = 0; i < boundFlickPieces.Count; i++)
            {
                TurnBasedFlickPiece piece = boundFlickPieces[i];
                if (piece == null)
                {
                    continue;
                }

                piece.FlickDragStarted -= HandleFlickDragStarted;
                piece.FlickDragUpdated -= HandleFlickDragUpdated;
                piece.FlickDragCancelled -= HandleFlickDragEnded;
                piece.FlickReleased -= HandleFlickReleased;
            }

            boundFlickPieces.Clear();
        }

        private void HandleFlickDragStarted(TurnBasedFlickPiece piece)
        {
            if (!CanApplyFlickPullZoom(piece))
            {
                return;
            }

            activePullPiece = piece;
            targetPullZoom = 0f;
        }

        private void HandleFlickDragUpdated(TurnBasedFlickPiece piece, Vector3 launchVector, float normalizedPower)
        {
            if (piece != activePullPiece && !CanApplyFlickPullZoom(piece))
            {
                return;
            }

            activePullPiece = piece;
            targetPullZoom = Mathf.Clamp01(normalizedPower);
        }

        private void HandleFlickDragEnded(TurnBasedFlickPiece piece)
        {
            if (piece == activePullPiece)
            {
                activePullPiece = null;
            }

            targetPullZoom = 0f;
        }

        private void HandleFlickReleased(TurnBasedFlickPiece piece, Vector3 impulse)
        {
            HandleFlickDragEnded(piece);
        }

        private bool CanApplyFlickPullZoom(TurnBasedFlickPiece piece)
        {
            if (!enableFlickPullZoomOut || piece == null || targetCamera == null)
            {
                return false;
            }

            return gameModeManager == null
                || gameModeManager.CurrentState == FlickDomGameState.PlayerFlicking;
        }

        private void UpdateFlickPullZoom(float deltaTime)
        {
            bool isFlickViewState = gameModeManager == null
                || IsFlickViewState(gameModeManager.CurrentState);
            if (!enableFlickPullZoomOut
                || targetCamera == null
                || !isFlickViewState
                || activeTransition != null)
            {
                return;
            }

            float t = 1f - Mathf.Exp(-pullZoomSharpness * deltaTime);
            currentPullZoom = Mathf.Lerp(currentPullZoom, targetPullZoom, t);
            if (Mathf.Abs(currentPullZoom - targetPullZoom) <= 0.001f)
            {
                currentPullZoom = targetPullZoom;
            }

            bool shouldApply = currentPullZoom > 0.0001f
                || targetPullZoom > 0.0001f
                || pullZoomPoseApplied;
            if (!shouldApply)
            {
                return;
            }

            ApplyFlickCameraPose(currentPullZoom);
            pullZoomPoseApplied = currentPullZoom > 0.0001f;
        }

        private void ApplyFlickCameraPose(float pullZoom)
        {
            Vector3 focus = GetFlickBoardCenter();
            Vector3 targetPosition = GetFlickOrbitPosition(focus, pullZoom);
            Quaternion targetRotation = GetFlickOrbitRotation(focus, targetPosition);
            float targetOrthographicSize = flickOrthographicSize
                + pullZoomOutOrthographicSize * Mathf.Clamp01(pullZoom);
            ApplyCameraPose(
                targetPosition,
                targetRotation,
                useOrthographicDuringFlick,
                targetOrthographicSize);
        }

        private void ResetFlickPullZoom()
        {
            activePullPiece = null;
            targetPullZoom = 0f;
            currentPullZoom = 0f;
            pullZoomPoseApplied = false;
        }
    }
}
