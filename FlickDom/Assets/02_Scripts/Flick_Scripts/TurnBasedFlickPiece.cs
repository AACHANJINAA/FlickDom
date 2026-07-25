using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlickDom.Gameplay
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public sealed class TurnBasedFlickPiece : MonoBehaviour
    {
        [Header("Turn")]
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private FlickDomPlayerId owner = FlickDomPlayerId.Player1;
        [SerializeField] private string pieceId = "Piece";

        [Header("Flick")]
        [SerializeField] private Camera inputCamera;
        [SerializeField] private float forceMultiplier = 10f;
        [SerializeField] private float maxDragDistance = 3f;
        [SerializeField] private float tokenRadius = 0.5f;
        [SerializeField] private float stopSpeed = 0.05f;
        [SerializeField] private float stopConfirmSeconds = 0.4f;

        [Header("Invalid Flick")]
        [SerializeField] private float fallYThreshold = -1f;
        [SerializeField] private bool hideDeadPiece = true;
        [SerializeField] private Color deadTint = new Color(0.08f, 0.08f, 0.08f);

        [Header("Visuals")]
        [SerializeField] private Color player1Color = new Color(0.1f, 0.35f, 1f);
        [SerializeField] private Color player2Color = new Color(1f, 0.2f, 0.15f);
        [SerializeField] private Color inactiveTint = new Color(0.45f, 0.45f, 0.45f);
        [SerializeField] private Color currentFlickTargetColor = new Color(1f, 0.86f, 0.05f);
        [SerializeField] private Color selectedOrderColor = new Color(1f, 0.68f, 0.05f);
        [SerializeField] private Color firstOrderBadgeColor = new Color(1f, 0.68f, 0.05f);
        [SerializeField] private Color secondOrderBadgeColor = new Color(0.18f, 0.82f, 0.48f);
        [SerializeField] private Color thirdOrderBadgeColor = new Color(0.15f, 0.68f, 1f);
        [SerializeField] private bool showStateIndicator;
        [SerializeField] private float stateIndicatorYOffset = 0.72f;
        [SerializeField] private float stateIndicatorHeight = 0.035f;
        [SerializeField] private float currentTargetIndicatorDiameterMultiplier = 1.5f;
        [SerializeField] private float activeIndicatorDiameterMultiplier = 1.1f;
        [SerializeField] private float inactiveIndicatorDiameterMultiplier = 0.75f;

        private Rigidbody cachedRigidbody;
        private Collider cachedCollider;
        private Renderer cachedRenderer;
        private FlickVisuals cachedVisuals;
        private GameObject stateIndicatorObject;
        private Renderer stateIndicatorRenderer;
        private Material stateIndicatorMaterial;
        private TextMesh stateIndicatorText;
        private MeshRenderer stateIndicatorTextRenderer;
        private Material stateIndicatorTextMaterial;
        private MaterialPropertyBlock rendererPropertyBlock;

        private Vector3 mouseStartPosition;
        private Vector3 mouseEndPosition;
        private Vector3 initialPiecePosition;
        private Vector3 dragTargetPosition;
        private Vector3 queuedImpulse;
        private Vector3 flickStartPosition;
        private Quaternion flickStartRotation;

        private bool isDragging;
        private bool launchQueued;
        private bool waitingForStop;
        private bool launchedThisTurn;
        private bool isDead;
        private bool invalidatedThisTurn;
        private bool enteredPlayableBoardAfterLaunch;
        private bool canInteractThisTurn = true;
        private bool waitForPointerReleaseBeforeInput;
        private float stoppedTimer;
        private bool originalUseGravity;
        private bool originalIsKinematic;
        private bool originalColliderEnabled;
        private bool originalColliderIsTrigger;
        private bool originalRendererEnabled;
        private RigidbodyConstraints originalConstraints;
        private CollisionDetectionMode originalCollisionDetectionMode;
        private float activeIndicatorDiameterMultiplierRuntime;
        private int selectionOrderNumber;

        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");

        public event Action<TurnBasedFlickPiece> FlickStarted;
        public event Action<TurnBasedFlickPiece> SettledAfterFlick;
        public event Action<TurnBasedFlickPiece> InvalidatedAfterFlick;

        public FlickDomPlayerId Owner
        {
            get { return owner; }
        }

        public string PieceId
        {
            get { return pieceId; }
        }

        public float TokenRadius
        {
            get { return tokenRadius; }
        }

        public bool IsDead
        {
            get { return isDead; }
        }

        public bool HasLaunchedThisRound
        {
            get { return launchedThisTurn; }
        }

        private void Awake()
        {
            cachedRigidbody = GetComponent<Rigidbody>();
            cachedCollider = GetComponent<Collider>();
            cachedRenderer = GetComponentInChildren<Renderer>();
            cachedVisuals = GetComponent<FlickVisuals>();
            flickStartPosition = transform.position;
            flickStartRotation = transform.rotation;
            originalUseGravity = cachedRigidbody.useGravity;
            originalIsKinematic = cachedRigidbody.isKinematic;
            originalColliderEnabled = cachedCollider.enabled;
            originalColliderIsTrigger = cachedCollider.isTrigger;
            originalRendererEnabled = cachedRenderer == null || cachedRenderer.enabled;
            originalConstraints = cachedRigidbody.constraints;
            originalCollisionDetectionMode = cachedRigidbody.collisionDetectionMode;
            activeIndicatorDiameterMultiplierRuntime = activeIndicatorDiameterMultiplier;

            if (inputCamera == null)
            {
                inputCamera = Camera.main;
            }

            ApplyBaseColor();
        }

        private void OnValidate()
        {
            forceMultiplier = Mathf.Max(0f, forceMultiplier);
            maxDragDistance = Mathf.Max(0.01f, maxDragDistance);
            tokenRadius = Mathf.Max(0.01f, tokenRadius);
            stopSpeed = Mathf.Max(0.001f, stopSpeed);
            stopConfirmSeconds = Mathf.Max(0f, stopConfirmSeconds);
            stateIndicatorYOffset = Mathf.Max(0.01f, stateIndicatorYOffset);
            stateIndicatorHeight = Mathf.Max(0.001f, stateIndicatorHeight);
            currentTargetIndicatorDiameterMultiplier = Mathf.Max(0.1f, currentTargetIndicatorDiameterMultiplier);
            activeIndicatorDiameterMultiplier = Mathf.Max(0.1f, activeIndicatorDiameterMultiplier);
            inactiveIndicatorDiameterMultiplier = Mathf.Max(0.1f, inactiveIndicatorDiameterMultiplier);
        }

        private void LateUpdate()
        {
            if (stateIndicatorObject != null && stateIndicatorObject.activeSelf)
            {
                UpdateStateIndicatorTransform();
            }
        }

        private void OnDisable()
        {
            HideFlickPreview();

            if (stateIndicatorObject != null)
            {
                stateIndicatorObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (stateIndicatorObject != null)
            {
                Destroy(stateIndicatorObject);
                stateIndicatorObject = null;
            }

            if (stateIndicatorMaterial != null)
            {
                Destroy(stateIndicatorMaterial);
                stateIndicatorMaterial = null;
            }

            if (stateIndicatorTextMaterial != null)
            {
                Destroy(stateIndicatorTextMaterial);
                stateIndicatorTextMaterial = null;
            }
        }

        private void Update()
        {
            if (Mouse.current == null || inputCamera == null)
            {
                return;
            }

            if (waitForPointerReleaseBeforeInput)
            {
                if (!Mouse.current.leftButton.isPressed)
                {
                    waitForPointerReleaseBeforeInput = false;
                }

                return;
            }

            if (!CanInteractThisFrame())
            {
                return;
            }

            if (Mouse.current.leftButton.wasPressedThisFrame && IsMouseOverPiece())
            {
                BeginDrag();
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame && isDragging)
            {
                EndDragAndQueueFlick();
            }
            else if (isDragging)
            {
                UpdateDragTarget();
            }
        }

        private void FixedUpdate()
        {
            if (isDragging)
            {
                if (!cachedRigidbody.isKinematic)
                {
                    cachedRigidbody.linearVelocity = Vector3.zero;
                    cachedRigidbody.angularVelocity = Vector3.zero;
                }

                cachedRigidbody.MovePosition(dragTargetPosition);
            }

            if (launchQueued)
            {
                launchQueued = false;
                EnableDynamicFlickPhysics();
                cachedRigidbody.linearVelocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
                cachedRigidbody.AddForce(queuedImpulse, ForceMode.Impulse);
                waitingForStop = true;
                launchedThisTurn = true;
                enteredPlayableBoardAfterLaunch = IsInsidePlayableBoard();
                stoppedTimer = 0f;
                FlickStarted?.Invoke(this);
            }

            if (waitingForStop)
            {
                TickStopDetection();
            }
        }

        public void Configure(FlickDomPlayerId newOwner, string newPieceId, GameModeManager manager)
        {
            owner = newOwner;
            pieceId = newPieceId;
            gameModeManager = manager;
            ApplyBaseColor();
        }

        public void SetRoundStartPose(Vector3 position, Quaternion rotation)
        {
            EnsureCachedComponents();

            flickStartPosition = position;
            flickStartRotation = rotation;
            transform.SetPositionAndRotation(position, rotation);

            if (cachedRigidbody == null)
            {
                return;
            }

            cachedRigidbody.position = position;
            cachedRigidbody.rotation = rotation;
            if (!cachedRigidbody.isKinematic)
            {
                cachedRigidbody.linearVelocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
            }
        }

        public void SetTurnHighlight(bool isActiveTurn)
        {
            EnsureCachedComponents();
            canInteractThisTurn = isActiveTurn;
            UpdateCollisionForTurnState(isActiveTurn);

            if (cachedRenderer == null)
            {
                return;
            }

            if (isDead)
            {
                RestorePieceVisual();
                SetStateIndicator(false, deadTint, inactiveIndicatorDiameterMultiplier, 0);
                return;
            }

            if (!IsInsidePlayableBoard())
            {
                RestorePieceVisual();
                SetStateIndicator(false, inactiveTint, inactiveIndicatorDiameterMultiplier, 0);
                return;
            }

            RestorePieceVisual();
            SetStateIndicator(isActiveTurn, currentFlickTargetColor, currentTargetIndicatorDiameterMultiplier, 0);
        }

        public void SetFlickTurnHighlight(bool isActivePlayerPiece, bool isCurrentFlickTarget)
        {
            EnsureCachedComponents();
            canInteractThisTurn = isCurrentFlickTarget;
            UpdateCollisionForTurnState(isCurrentFlickTarget);

            if (cachedRenderer == null)
            {
                return;
            }

            if (isDead)
            {
                RestorePieceVisual();
                SetStateIndicator(false, deadTint, inactiveIndicatorDiameterMultiplier, 0);
                return;
            }

            if (isCurrentFlickTarget)
            {
                RestorePieceVisual();
                SetStateIndicator(true, currentFlickTargetColor, currentTargetIndicatorDiameterMultiplier, 0);
                return;
            }

            Color color = isActivePlayerPiece ? GetOwnerColor() : inactiveTint;
            float indicatorMultiplier = isActivePlayerPiece
                ? activeIndicatorDiameterMultiplier
                : inactiveIndicatorDiameterMultiplier;

            RestorePieceVisual();
            SetStateIndicator(true, color, indicatorMultiplier, 0);
        }

        public void SetOrderSelectionHighlight(bool isSelectingPlayerPiece, int orderNumber)
        {
            EnsureCachedComponents();
            canInteractThisTurn = false;
            selectionOrderNumber = Mathf.Max(0, orderNumber);
            bool isAlreadySelected = selectionOrderNumber > 0;

            if (isDead)
            {
                StopDeadPieceSimulation();
                RestorePieceVisual();
                SetStateIndicator(false, deadTint, inactiveIndicatorDiameterMultiplier, 0);
                return;
            }

            if (isSelectingPlayerPiece && !isAlreadySelected)
            {
                EnableInputOnlyCollision();
            }
            else
            {
                ParkWithoutCollision();
            }

            if (cachedRenderer == null)
            {
                return;
            }

            if (isSelectingPlayerPiece && isAlreadySelected)
            {
                RestorePieceVisual();
                SetStateIndicator(
                    true,
                    GetOwnerColor(),
                    activeIndicatorDiameterMultiplier,
                    0);
                return;
            }

            Color color = isSelectingPlayerPiece ? GetOwnerColor() : inactiveTint;
            float indicatorMultiplier = isSelectingPlayerPiece
                ? activeIndicatorDiameterMultiplier
                : inactiveIndicatorDiameterMultiplier;

            RestorePieceVisual();
            SetStateIndicator(true, color, indicatorMultiplier, 0);
        }

        public void ResetRoundUse()
        {
            ResetToFlickStartPose();
            isDragging = false;
            launchQueued = false;
            waitingForStop = false;
            launchedThisTurn = false;
            isDead = false;
            invalidatedThisTurn = false;
            enteredPlayableBoardAfterLaunch = false;
            canInteractThisTurn = false;
            waitForPointerReleaseBeforeInput = false;
            stoppedTimer = 0f;
            HideFlickPreview();
            RestorePieceVisual();
            UpdateCollisionForTurnState(false);
        }

        public bool IsSettledForPlacement()
        {
            EnsureCachedComponents();

            if (isDead || !launchedThisTurn || ShouldBeRemovedAfterLeavingPlayableBoard())
            {
                return true;
            }

            if (cachedRigidbody == null || cachedRigidbody.isKinematic)
            {
                return true;
            }

            float stopSpeedSqr = stopSpeed * stopSpeed;
            return cachedRigidbody.linearVelocity.sqrMagnitude <= stopSpeedSqr
                && cachedRigidbody.angularVelocity.sqrMagnitude <= stopSpeedSqr;
        }

        public bool ShouldBeRemovedAfterLeavingPlayableBoard()
        {
            if (isDead)
            {
                return false;
            }

            if (transform.position.y <= fallYThreshold)
            {
                return true;
            }

            return launchedThisTurn && !IsInsidePlayableBoard();
        }

        public void MarkDeadAfterExternalBoardExit()
        {
            EnsureCachedComponents();
            invalidatedThisTurn = true;
            KillPiece();
        }

        public void BlockInputUntilPointerReleased()
        {
            waitForPointerReleaseBeforeInput = true;
            isDragging = false;
            launchQueued = false;
            HideFlickPreview();
        }

        public bool TryRaycast(Ray ray, float maxDistance, out float distance)
        {
            EnsureCachedComponents();

            if (cachedCollider == null || !cachedCollider.enabled)
            {
                distance = 0f;
                return false;
            }

            if (cachedCollider.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                distance = hit.distance;
                return true;
            }

            distance = 0f;
            return false;
        }

        private bool CanInteractThisFrame()
        {
            if (isDragging)
            {
                return true;
            }

            if (isDead || launchedThisTurn)
            {
                return false;
            }

            if (!canInteractThisTurn)
            {
                return false;
            }

            if (gameModeManager == null)
            {
                return true;
            }

            return gameModeManager.CurrentState == FlickDomGameState.PlayerFlicking
                && gameModeManager.ActivePlayer == owner;
        }

        private void BeginDrag()
        {
            mouseStartPosition = GetMousePositionOnBoard();
            initialPiecePosition = transform.position;
            dragTargetPosition = initialPiecePosition;
            isDragging = true;
            ShowFlickPreview(Vector3.zero);
        }

        private void UpdateDragTarget()
        {
            Vector3 currentMousePosition = GetMousePositionOnBoard();
            Vector3 pullVector = currentMousePosition - mouseStartPosition;
            pullVector.y = 0f;

            if (pullVector.magnitude > maxDragDistance)
            {
                pullVector = pullVector.normalized * maxDragDistance;
            }

            dragTargetPosition = initialPiecePosition + pullVector;
            ShowFlickPreview(-pullVector);
        }

        private void EndDragAndQueueFlick()
        {
            mouseEndPosition = GetMousePositionOnBoard();
            isDragging = false;
            HideFlickPreview();

            Vector3 forceVector = mouseStartPosition - mouseEndPosition;
            forceVector.y = 0f;

            if (forceVector.magnitude > maxDragDistance)
            {
                forceVector = forceVector.normalized * maxDragDistance;
            }

            queuedImpulse = forceVector * forceMultiplier;
            launchQueued = true;
        }

        private void TickStopDetection()
        {
            if (transform.position.y <= fallYThreshold || HasExitedBoardAfterLaunch())
            {
                InvalidateCurrentFlick();
                return;
            }

            float linearSpeedSqr = cachedRigidbody.linearVelocity.sqrMagnitude;
            float angularSpeedSqr = cachedRigidbody.angularVelocity.sqrMagnitude;
            float stopSpeedSqr = stopSpeed * stopSpeed;

            if (linearSpeedSqr <= stopSpeedSqr && angularSpeedSqr <= stopSpeedSqr)
            {
                stoppedTimer += Time.fixedDeltaTime;
                if (stoppedTimer >= stopConfirmSeconds)
                {
                    waitingForStop = false;
                    SettledAfterFlick?.Invoke(this);
                }
            }
            else
            {
                stoppedTimer = 0f;
            }
        }

        private bool HasExitedBoardAfterLaunch()
        {
            bool isInsideBoard = IsInsidePlayableBoard();
            if (isInsideBoard)
            {
                enteredPlayableBoardAfterLaunch = true;
                return false;
            }

            return enteredPlayableBoardAfterLaunch;
        }

        private void InvalidateCurrentFlick()
        {
            if (invalidatedThisTurn)
            {
                return;
            }

            isDragging = false;
            launchQueued = false;
            waitingForStop = false;
            invalidatedThisTurn = true;
            stoppedTimer = 0f;
            HideFlickPreview();

            cachedRigidbody.linearVelocity = Vector3.zero;
            cachedRigidbody.angularVelocity = Vector3.zero;

            KillPiece();

            InvalidatedAfterFlick?.Invoke(this);
        }

        private void KillPiece()
        {
            if (isDead)
            {
                return;
            }

            isDead = true;
            StopDeadPieceSimulation();

            if (cachedRenderer != null)
            {
                if (hideDeadPiece)
                {
                    cachedRenderer.enabled = false;
                }
            }

            SetStateIndicator(false, deadTint, inactiveIndicatorDiameterMultiplier, 0);
        }

        private void StopDeadPieceSimulation()
        {
            isDragging = false;
            launchQueued = false;
            waitingForStop = false;
            stoppedTimer = 0f;
            HideFlickPreview();

            if (!cachedRigidbody.isKinematic)
            {
                cachedRigidbody.linearVelocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
            }

            cachedRigidbody.useGravity = false;
            cachedRigidbody.isKinematic = true;
            cachedCollider.isTrigger = originalColliderIsTrigger;
            cachedCollider.enabled = false;
        }

        private void ResetToFlickStartPose()
        {
            cachedRigidbody.useGravity = false;
            cachedRigidbody.isKinematic = true;
            cachedRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            cachedRigidbody.position = flickStartPosition;
            cachedRigidbody.rotation = flickStartRotation;
            transform.SetPositionAndRotation(flickStartPosition, flickStartRotation);

            cachedRigidbody.constraints = originalConstraints;
            cachedRigidbody.useGravity = originalUseGravity;
            cachedRigidbody.isKinematic = originalIsKinematic;
            cachedRigidbody.collisionDetectionMode = originalCollisionDetectionMode;
            if (!cachedRigidbody.isKinematic)
            {
                cachedRigidbody.linearVelocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
            }

            cachedCollider.enabled = originalColliderEnabled;
            cachedCollider.isTrigger = originalColliderIsTrigger;
            if (cachedRenderer != null)
            {
                cachedRenderer.enabled = originalRendererEnabled;
            }
        }

        private void UpdateCollisionForTurnState(bool isActiveTurn)
        {
            if (isDead)
            {
                StopDeadPieceSimulation();
                return;
            }

            if (isDragging)
            {
                EnableInputOnlyCollision();
                return;
            }

            if (waitingForStop)
            {
                EnableDynamicFlickPhysics();
                return;
            }

            if (launchedThisTurn)
            {
                if (IsInsidePlayableBoard())
                {
                    EnableDynamicFlickPhysics();
                }
                else
                {
                    ParkWithoutCollision();
                }

                return;
            }

            if (isActiveTurn)
            {
                EnableInputOnlyCollision();
                return;
            }

            ParkWithoutCollision();
        }

        private void EnableInputOnlyCollision()
        {
            cachedRigidbody.useGravity = false;
            cachedRigidbody.isKinematic = true;
            cachedRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            cachedCollider.enabled = true;
            cachedCollider.isTrigger = true;
        }

        private void EnableDynamicFlickPhysics()
        {
            cachedCollider.enabled = true;
            cachedCollider.isTrigger = originalColliderIsTrigger;
            cachedRigidbody.useGravity = originalUseGravity;
            cachedRigidbody.isKinematic = false;
            cachedRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private void ParkWithoutCollision()
        {
            if (!cachedRigidbody.isKinematic)
            {
                cachedRigidbody.linearVelocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
            }

            cachedRigidbody.useGravity = false;
            cachedRigidbody.isKinematic = true;
            cachedRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            cachedCollider.isTrigger = originalColliderIsTrigger;
            cachedCollider.enabled = false;
        }

        private bool IsInsidePlayableBoard()
        {
            if (gameModeManager == null)
            {
                return true;
            }

            return gameModeManager.IsWorldPositionInsideFlickBoard(transform.position);
        }

        private Vector3 GetMousePositionOnBoard()
        {
            Ray ray = inputCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            Plane boardPlane = new Plane(Vector3.up, Vector3.zero);

            if (boardPlane.Raycast(ray, out float enter))
            {
                return ray.GetPoint(enter);
            }

            return transform.position;
        }

        private bool IsMouseOverPiece()
        {
            if (!cachedCollider.enabled)
            {
                return false;
            }

            Ray ray = inputCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            return cachedCollider.Raycast(ray, out _, 1000f);
        }

        private void ApplyBaseColor()
        {
            EnsureCachedComponents();

            if (cachedRenderer == null)
            {
                return;
            }

            RestorePieceVisual();
            SetStateIndicator(false, GetOwnerColor(), activeIndicatorDiameterMultiplier, 0);
        }

        private void EnsureCachedComponents()
        {
            if (cachedRigidbody == null)
            {
                cachedRigidbody = GetComponent<Rigidbody>();
            }

            if (cachedCollider == null)
            {
                cachedCollider = GetComponent<Collider>();
            }

            if (cachedRenderer == null)
            {
                cachedRenderer = GetComponentInChildren<Renderer>();
            }

            if (cachedVisuals == null)
            {
                cachedVisuals = GetComponent<FlickVisuals>();
            }
        }

        private void ShowFlickPreview(Vector3 forceDirection)
        {
            EnsureCachedComponents();

            if (cachedVisuals == null)
            {
                return;
            }

            cachedVisuals.SetHighlight(true);
            cachedVisuals.ShowTrajectory(true);
            cachedVisuals.UpdateTrajectory(initialPiecePosition, forceDirection);
        }

        private void HideFlickPreview()
        {
            if (cachedVisuals == null)
            {
                cachedVisuals = GetComponent<FlickVisuals>();
            }

            if (cachedVisuals == null)
            {
                return;
            }

            cachedVisuals.SetHighlight(false);
            cachedVisuals.ShowTrajectory(false);
        }

        private Color GetOwnerColor()
        {
            return owner == FlickDomPlayerId.Player2 ? player2Color : player1Color;
        }

        private void RestorePieceVisual()
        {
            EnsureCachedComponents();

            if (cachedRenderer == null)
            {
                return;
            }

            if (rendererPropertyBlock == null)
            {
                rendererPropertyBlock = new MaterialPropertyBlock();
            }

            rendererPropertyBlock.Clear();
            cachedRenderer.SetPropertyBlock(rendererPropertyBlock);
        }

        private void SetStateIndicator(bool visible, Color color, float diameterMultiplier, int orderNumber)
        {
            if (!showStateIndicator)
            {
                if (stateIndicatorObject != null)
                {
                    stateIndicatorObject.SetActive(false);
                }

                return;
            }

            if (isDead)
            {
                visible = false;
            }

            EnsureStateIndicator();
            if (stateIndicatorObject == null)
            {
                return;
            }

            stateIndicatorObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            activeIndicatorDiameterMultiplierRuntime = diameterMultiplier;
            UpdateStateIndicatorTransform();
            SetMaterialColor(stateIndicatorMaterial, color);
            UpdateStateIndicatorLabel(orderNumber);
        }

        private void EnsureStateIndicator()
        {
            if (stateIndicatorObject != null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                return;
            }

            stateIndicatorMaterial = new Material(shader);
            stateIndicatorMaterial.name = name + " State Indicator";

            stateIndicatorObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stateIndicatorObject.name = name + " State Indicator";
            stateIndicatorRenderer = stateIndicatorObject.GetComponent<Renderer>();
            if (stateIndicatorRenderer != null)
            {
                stateIndicatorRenderer.sharedMaterial = stateIndicatorMaterial;
            }

            Collider indicatorCollider = stateIndicatorObject.GetComponent<Collider>();
            if (indicatorCollider != null)
            {
                Destroy(indicatorCollider);
            }

            GameObject textObject = new GameObject("Label");
            textObject.transform.SetParent(stateIndicatorObject.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            textObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            stateIndicatorText = textObject.AddComponent<TextMesh>();
            stateIndicatorText.alignment = TextAlignment.Center;
            stateIndicatorText.anchor = TextAnchor.MiddleCenter;
            stateIndicatorText.characterSize = 0.18f;
            stateIndicatorText.fontSize = 64;
            stateIndicatorText.color = Color.white;
            stateIndicatorText.text = string.Empty;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                stateIndicatorText.font = font;
            }

            stateIndicatorTextRenderer = textObject.GetComponent<MeshRenderer>();
            if (stateIndicatorTextRenderer != null)
            {
                stateIndicatorTextMaterial = new Material(shader);
                stateIndicatorTextMaterial.name = name + " State Indicator Text";
                SetMaterialColor(stateIndicatorTextMaterial, Color.white);
                stateIndicatorTextRenderer.sharedMaterial = stateIndicatorTextMaterial;
            }

            stateIndicatorObject.SetActive(false);
        }

        private void UpdateStateIndicatorTransform()
        {
            if (stateIndicatorObject == null)
            {
                return;
            }

            float diameter = Mathf.Max(0.05f, tokenRadius * 2f * activeIndicatorDiameterMultiplierRuntime);
            stateIndicatorObject.transform.position = transform.position + (Vector3.up * stateIndicatorYOffset);
            stateIndicatorObject.transform.rotation = Quaternion.identity;
            stateIndicatorObject.transform.localScale = new Vector3(
                diameter,
                stateIndicatorHeight * 0.5f,
                diameter);
        }

        private void UpdateStateIndicatorLabel(int orderNumber)
        {
            if (stateIndicatorText == null)
            {
                return;
            }

            if (orderNumber > 0)
            {
                stateIndicatorText.text = orderNumber.ToString();
                if (stateIndicatorTextRenderer != null)
                {
                    stateIndicatorTextRenderer.enabled = true;
                }
            }
            else
            {
                stateIndicatorText.text = string.Empty;
                if (stateIndicatorTextRenderer != null)
                {
                    stateIndicatorTextRenderer.enabled = false;
                }
            }
        }

        private Color GetSelectionOrderColor(int orderNumber)
        {
            switch (orderNumber)
            {
                case 1:
                    return firstOrderBadgeColor;
                case 2:
                    return secondOrderBadgeColor;
                case 3:
                    return thirdOrderBadgeColor;
                default:
                    return selectedOrderColor;
            }
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty(BaseColorPropertyId))
            {
                material.SetColor(BaseColorPropertyId, color);
            }

            if (material.HasProperty(ColorPropertyId))
            {
                material.SetColor(ColorPropertyId, color);
            }
        }
    }
}
