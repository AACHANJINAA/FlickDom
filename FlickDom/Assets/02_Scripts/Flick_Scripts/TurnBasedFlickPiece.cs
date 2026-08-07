using System;
using FlickDom.Networking;
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
        private Vector3 characterAimVector;
        private Vector3 characterAimPiecePosition;
        private Vector3 queuedImpulse;
        private Vector3 flickStartPosition;
        private Quaternion flickStartRotation;

        private bool isDragging;
        private bool characterAimActive;
        private bool launchQueued;
        private bool waitingForStop;
        private bool launchedThisTurn;
        private bool isDead;
        private bool invalidatedThisTurn;
        private bool enteredPlayableBoardAfterLaunch;
        private bool touchedRequiredTargetThisFlick;
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
        public event Action<TurnBasedFlickPiece> FlickDragStarted;
        public event Action<TurnBasedFlickPiece, Vector3, float> FlickDragUpdated;
        public event Action<TurnBasedFlickPiece> FlickDragCancelled;
        public event Action<TurnBasedFlickPiece, Vector3> FlickReleased;

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

        public bool HasRequiredContactForPlacement
        {
            get { return touchedRequiredTargetThisFlick; }
        }

        public bool IsDragging
        {
            get { return isDragging; }
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
            CancelDragPresentation();
            HideFlickPreview();

            if (stateIndicatorObject != null)
            {
                stateIndicatorObject.SetActive(false);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            RegisterRequiredContact(collision != null ? collision.collider : null);
        }

        private void OnCollisionStay(Collision collision)
        {
            RegisterRequiredContact(collision != null ? collision.collider : null);
        }

        private void OnTriggerEnter(Collider other)
        {
            RegisterRequiredContact(other);
        }

        private void OnTriggerStay(Collider other)
        {
            RegisterRequiredContact(other);
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
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;

            if (waitForPointerReleaseBeforeInput)
            {
                bool isMouseHeld = mouse != null && mouse.leftButton.isPressed;
                bool isSpaceHeld = keyboard != null && keyboard.spaceKey.isPressed;
                if (!isMouseHeld && !isSpaceHeld)
                {
                    waitForPointerReleaseBeforeInput = false;
                }

                return;
            }

            if (!CanInteractThisFrame())
            {
                return;
            }

            if (!isDragging
                && mouse != null
                && mouse.leftButton.wasPressedThisFrame
                && IsMouseOverPiece())
            {
                BeginDrag();
            }
            else if (isDragging
                && mouse != null
                && mouse.leftButton.wasReleasedThisFrame)
            {
                EndDragAndQueueFlick();
            }
            else if (isDragging
                && keyboard != null
                && keyboard.escapeKey.wasPressedThisFrame)
            {
                CancelDragPresentation();
                HideFlickPreview();
            }
            else if (keyboard != null
                && keyboard.spaceKey.wasReleasedThisFrame
                && isDragging)
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

                cachedRigidbody.MovePosition(
                    characterAimActive ? characterAimPiecePosition : dragTargetPosition);
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
                touchedRequiredTargetThisFlick = false;
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
            CancelDragPresentation();
            launchQueued = false;
            waitingForStop = false;
            launchedThisTurn = false;
            isDead = false;
            invalidatedThisTurn = false;
            enteredPlayableBoardAfterLaunch = false;
            touchedRequiredTargetThisFlick = false;
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

        public void RemoveFromFieldAfterMissedContact()
        {
            EnsureCachedComponents();
            invalidatedThisTurn = true;
            KillPiece();
        }

        private void RegisterRequiredContact(Collider other)
        {
            if (touchedRequiredTargetThisFlick
                || !launchedThisTurn
                || isDead
                || other == null)
            {
                return;
            }

            TurnBasedFlickPiece otherPiece = other.GetComponentInParent<TurnBasedFlickPiece>();
            if (otherPiece != null)
            {
                if (otherPiece != this)
                {
                    touchedRequiredTargetThisFlick = true;
                }

                return;
            }

            if (IsWallCollider(other))
            {
                touchedRequiredTargetThisFlick = true;
            }
        }

        private static bool IsWallCollider(Collider other)
        {
            Transform current = other.transform;
            while (current != null)
            {
                if (current.name.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0
                    || current.name.IndexOf("Boundary", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        public void BlockInputUntilPointerReleased()
        {
            waitForPointerReleaseBeforeInput = true;
            CancelDragPresentation();
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

            if (!CanProvideLocalNetworkInput())
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

        private bool CanProvideLocalNetworkInput()
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            return bootstrap == null || bootstrap.AllowsLocalInputFor(owner);
        }

        private void BeginDrag()
        {
            mouseStartPosition = GetMousePositionOnBoard();
            initialPiecePosition = transform.position;
            dragTargetPosition = initialPiecePosition;
            characterAimVector = Vector3.zero;
            characterAimPiecePosition = initialPiecePosition;
            characterAimActive = false;
            isDragging = true;
            ShowFlickPreview(Vector3.zero);
            FlickDragStarted?.Invoke(this);
            if (!characterAimActive)
            {
                FlickDragUpdated?.Invoke(this, Vector3.zero, 0f);
            }
        }

        private void UpdateDragTarget()
        {
            if (characterAimActive)
            {
                return;
            }

            Vector3 currentMousePosition = GetMousePositionOnBoard();
            Vector3 pullVector = currentMousePosition - mouseStartPosition;
            pullVector.y = 0f;

            if (pullVector.magnitude > maxDragDistance)
            {
                pullVector = pullVector.normalized * maxDragDistance;
            }

            dragTargetPosition = initialPiecePosition + pullVector;
            Vector3 launchVector = -pullVector;
            ShowFlickPreview(launchVector);
            FlickDragUpdated?.Invoke(
                this,
                launchVector,
                Mathf.Clamp01(launchVector.magnitude / maxDragDistance));
        }

        public bool TrySetCharacterAim(Vector3 launchVector)
        {
            return TrySetCharacterAim(launchVector, initialPiecePosition);
        }

        public bool TrySetCharacterAim(
            Vector3 launchVector,
            Vector3 presentationPosition)
        {
            if (!isDragging)
            {
                return false;
            }

            launchVector.y = 0f;
            if (launchVector.magnitude > maxDragDistance)
            {
                launchVector = launchVector.normalized * maxDragDistance;
            }

            characterAimActive = true;
            characterAimVector = launchVector;
            presentationPosition = PreserveAimHeight(presentationPosition);
            characterAimPiecePosition = presentationPosition;
            dragTargetPosition = presentationPosition;
            ShowFlickPreview(characterAimVector);
            FlickDragUpdated?.Invoke(
                this,
                characterAimVector,
                Mathf.Clamp01(characterAimVector.magnitude / maxDragDistance));
            return true;
        }

        private void EndDragAndQueueFlick()
        {
            Vector3 forceVector;
            Vector3 launchPosition = characterAimActive
                ? characterAimPiecePosition
                : cachedRigidbody.position;
            launchPosition = PreserveAimHeight(launchPosition);
            if (characterAimActive)
            {
                forceVector = characterAimVector;
            }
            else
            {
                mouseEndPosition = GetMousePositionOnBoard();
                forceVector = mouseStartPosition - mouseEndPosition;
                forceVector.y = 0f;
            }

            isDragging = false;
            characterAimActive = false;
            characterAimVector = Vector3.zero;
            characterAimPiecePosition = initialPiecePosition;
            HideFlickPreview();

            if (forceVector.magnitude > maxDragDistance)
            {
                forceVector = forceVector.normalized * maxDragDistance;
            }

            queuedImpulse = forceVector * forceMultiplier;
            cachedRigidbody.position = launchPosition;
            transform.position = launchPosition;
            FlickReleased?.Invoke(this, queuedImpulse);
            if (TrySubmitNetworkFlickRequest(queuedImpulse, launchPosition))
            {
                return;
            }

            launchQueued = true;
        }

        private void CancelDragPresentation()
        {
            if (!isDragging)
            {
                return;
            }

            isDragging = false;
            characterAimActive = false;
            characterAimVector = Vector3.zero;
            characterAimPiecePosition = initialPiecePosition;
            cachedRigidbody.position = initialPiecePosition;
            transform.position = initialPiecePosition;
            HideFlickPreview();
            FlickDragCancelled?.Invoke(this);
        }

        public bool TryQueueAuthoritativeFlick(Vector3 impulse)
        {
            return TryQueueAuthoritativeFlick(impulse, transform.position);
        }

        public bool TryQueueAuthoritativeFlick(Vector3 impulse, Vector3 requestedLaunchPosition)
        {
            if (isDead || launchedThisTurn || launchQueued)
            {
                return false;
            }

            if (!IsFinite(requestedLaunchPosition))
            {
                return false;
            }

            EnsureCachedComponents();
            requestedLaunchPosition = PreserveCurrentHeight(requestedLaunchPosition);
            Vector3 launchOffset = Vector3.ClampMagnitude(
                requestedLaunchPosition - transform.position,
                maxDragDistance);
            Vector3 safeLaunchPosition = transform.position + launchOffset;
            safeLaunchPosition = PreserveCurrentHeight(safeLaunchPosition);
            cachedRigidbody.position = safeLaunchPosition;
            transform.position = safeLaunchPosition;
            queuedImpulse = impulse;
            launchQueued = true;
            return true;
        }

        public void ApplyNetworkPose(Vector3 position, Quaternion rotation)
        {
            EnsureCachedComponents();
            if (ShouldIgnoreNetworkPoseWhileLocallyInteractive())
            {
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
            if (cachedRigidbody != null)
            {
                cachedRigidbody.position = position;
                cachedRigidbody.rotation = rotation;
                if (!cachedRigidbody.isKinematic)
                {
                    cachedRigidbody.linearVelocity = Vector3.zero;
                    cachedRigidbody.angularVelocity = Vector3.zero;
                }
            }
        }

        public void ApplyNetworkState(bool networkIsDead)
        {
            if (networkIsDead)
            {
                MarkDeadAfterExternalBoardExit();
            }
        }

        public void MarkNetworkFlickAccepted()
        {
            launchedThisTurn = true;
            launchQueued = false;
            waitingForStop = false;
            isDragging = false;
            characterAimActive = false;
            characterAimVector = Vector3.zero;
            HideFlickPreview();
            ParkWithoutCollision();
        }

        private bool ShouldIgnoreNetworkPoseWhileLocallyInteractive()
        {
            if (isDragging)
            {
                return true;
            }

            return false;
        }

        private bool TrySubmitNetworkFlickRequest(Vector3 impulse, Vector3 launchPosition)
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            if (bootstrap == null || !bootstrap.IsRunning || bootstrap.IsHost)
            {
                return false;
            }

            bootstrap.SubmitFlickRequestToHost(owner, pieceId, impulse, launchPosition);
            cachedRigidbody.position = initialPiecePosition;
            transform.position = initialPiecePosition;
            return true;
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
            characterAimActive = false;
            characterAimVector = Vector3.zero;
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
            characterAimActive = false;
            characterAimVector = Vector3.zero;
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
            if (Mouse.current == null || inputCamera == null)
            {
                return transform.position;
            }

            Ray ray = inputCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            Plane boardPlane = new Plane(Vector3.up, Vector3.up * GetInputProjectionHeight());

            if (boardPlane.Raycast(ray, out float enter))
            {
                return ray.GetPoint(enter);
            }

            return transform.position;
        }

        private float GetInputProjectionHeight()
        {
            if (isDragging)
            {
                return initialPiecePosition.y;
            }

            return cachedRigidbody != null
                ? cachedRigidbody.position.y
                : transform.position.y;
        }

        private Vector3 PreserveAimHeight(Vector3 position)
        {
            position.y = initialPiecePosition.y;
            return position;
        }

        private Vector3 PreserveCurrentHeight(Vector3 position)
        {
            position.y = cachedRigidbody != null
                ? cachedRigidbody.position.y
                : transform.position.y;
            return position;
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
            Vector3 previewOrigin = characterAimActive
                ? characterAimPiecePosition
                : initialPiecePosition;
            cachedVisuals.UpdateTrajectory(previewOrigin, forceDirection);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x)
                && !float.IsNaN(value.y)
                && !float.IsNaN(value.z)
                && !float.IsInfinity(value.x)
                && !float.IsInfinity(value.y)
                && !float.IsInfinity(value.z);
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
