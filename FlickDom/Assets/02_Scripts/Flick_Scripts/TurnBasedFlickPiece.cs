using System;
using System.Collections;
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

        [Header("Network Smoothing")]
        [SerializeField, Min(0f)] private float networkCorrectionIgnoreDistance = 0.03f;
        [SerializeField, Min(0.01f)] private float networkCorrectionSnapDistance = 0.75f;
        [SerializeField, Range(0.01f, 1f)] private float networkPositionCorrectionBlend = 0.35f;
        [SerializeField, Range(0.01f, 1f)] private float networkVelocityCorrectionBlend = 0.5f;
        [SerializeField, Min(0.02f)] private float snapshotInterpolationBackTime = 0.1f;
        [SerializeField, Min(0.0001f)] private float movingStateVelocityThreshold = 0.02f;

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
        private bool awaitingNetworkFlickAcceptance;
        private bool usingLocalFlickPrediction;
        private bool waitingForStop;
        private bool launchedThisTurn;
        private bool isDead;
        private bool invalidatedThisTurn;
        private bool enteredPlayableBoardAfterLaunch;
        private bool touchedRequiredTargetThisFlick;
        private bool canInteractThisTurn = true;
        private bool waitForPointerReleaseBeforeInput;
        private float stoppedTimer;
        private uint queuedFlickShotId;
        private uint pendingNetworkFlickShotId;
        private bool originalUseGravity;
        private bool originalIsKinematic;
        private bool originalColliderEnabled;
        private bool originalColliderIsTrigger;
        private bool originalRendererEnabled;
        private RigidbodyConstraints originalConstraints;
        private CollisionDetectionMode originalCollisionDetectionMode;
        private float activeIndicatorDiameterMultiplierRuntime;
        private int selectionOrderNumber;
        private readonly PieceSnapshot[] pieceSnapshots = new PieceSnapshot[SnapshotBufferSize];
        private int pieceSnapshotCount;
        private uint latestNetworkStateTick;
        private bool hasLatestNetworkStateTick;

        private const string HitSoundResourcePath = "Audio/Hit";
        private const string FlickFailedSoundResourcePath = "Audio/Flick_Failed";
        private const string PieceAudioObjectName = "Flick Piece Audio";
        private const float HitSoundVolumeScale = 1.8f;
        private const float HitSoundCooldownSeconds = 0.03f;
        private const int SnapshotBufferSize = 4;

        private static AudioSource sharedAudioSource;
        private static AudioClip hitSoundClip;
        private static AudioClip flickFailedSoundClip;
        private static float nextHitSoundTime;

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
            PreloadHitSound();
            PreloadFlickFailedSound();
            FlickDomCollisionRules.IgnoreMonkeyCollisionsForPiece(this);
        }

        private void OnValidate()
        {
            forceMultiplier = Mathf.Max(0f, forceMultiplier);
            maxDragDistance = Mathf.Max(0.01f, maxDragDistance);
            tokenRadius = Mathf.Max(0.01f, tokenRadius);
            stopSpeed = Mathf.Max(0.001f, stopSpeed);
            stopConfirmSeconds = Mathf.Max(0f, stopConfirmSeconds);
            networkCorrectionIgnoreDistance = Mathf.Max(0f, networkCorrectionIgnoreDistance);
            networkCorrectionSnapDistance = Mathf.Max(networkCorrectionIgnoreDistance + 0.01f, networkCorrectionSnapDistance);
            networkPositionCorrectionBlend = Mathf.Clamp01(networkPositionCorrectionBlend);
            networkVelocityCorrectionBlend = Mathf.Clamp01(networkVelocityCorrectionBlend);
            snapshotInterpolationBackTime = Mathf.Max(0.02f, snapshotInterpolationBackTime);
            movingStateVelocityThreshold = Mathf.Max(0.0001f, movingStateVelocityThreshold);
            stateIndicatorYOffset = Mathf.Max(0.01f, stateIndicatorYOffset);
            stateIndicatorHeight = Mathf.Max(0.001f, stateIndicatorHeight);
            currentTargetIndicatorDiameterMultiplier = Mathf.Max(0.1f, currentTargetIndicatorDiameterMultiplier);
            activeIndicatorDiameterMultiplier = Mathf.Max(0.1f, activeIndicatorDiameterMultiplier);
            inactiveIndicatorDiameterMultiplier = Mathf.Max(0.1f, inactiveIndicatorDiameterMultiplier);
        }

        private void LateUpdate()
        {
            RenderNetworkSnapshotIfNeeded();

            if (stateIndicatorObject != null && stateIndicatorObject.activeSelf)
            {
                UpdateStateIndicatorTransform();
            }
        }

        private void OnEnable()
        {
            FlickDomCollisionRules.IgnoreMonkeyCollisionsForPiece(this);
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
            Collider other = collision != null ? collision.collider : null;
            RegisterRequiredContact(other);
            PlayHitSoundIfNeeded(other);
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
                FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
                if (bootstrap != null && bootstrap.IsHost)
                {
                    FlickLatencyProbe.RecordHostPhysicsApplied(queuedFlickShotId, owner, pieceId);
                    if (queuedFlickShotId != 0u)
                    {
                        StartCoroutine(RecordHostPhysicsStepCompleteAfterFixedUpdate(queuedFlickShotId));
                    }
                }
                else if (bootstrap != null && bootstrap.IsClientOnly && bootstrap.LocalPlayerId == owner)
                {
                    FlickLatencyProbe.RecordClientFirstVisibleMovement(queuedFlickShotId, owner, pieceId);
                }

                queuedFlickShotId = 0u;
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

        private Vector3 CalculateLaunchPositionFromForceVector(Vector3 forceVector)
        {
            forceVector.y = 0f;
            if (forceVector.magnitude > maxDragDistance)
            {
                forceVector = forceVector.normalized * maxDragDistance;
            }

            return PreserveAimHeight(initialPiecePosition - forceVector);
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
            awaitingNetworkFlickAcceptance = false;
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

        public bool ShouldSendMovingNetworkState()
        {
            EnsureCachedComponents();
            if (isDead)
            {
                return true;
            }

            if (cachedRigidbody == null || cachedRigidbody.isKinematic)
            {
                return waitingForStop || launchQueued;
            }

            float thresholdSqr = movingStateVelocityThreshold * movingStateVelocityThreshold;
            return waitingForStop
                || launchQueued
                || cachedRigidbody.linearVelocity.sqrMagnitude > thresholdSqr
                || cachedRigidbody.angularVelocity.sqrMagnitude > thresholdSqr;
        }

        public void GetNetworkPhysicsState(
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 velocity,
            out Vector3 angularVelocity)
        {
            EnsureCachedComponents();
            position = cachedRigidbody != null ? cachedRigidbody.position : transform.position;
            rotation = cachedRigidbody != null ? cachedRigidbody.rotation : transform.rotation;
            velocity = cachedRigidbody != null && !cachedRigidbody.isKinematic
                ? cachedRigidbody.linearVelocity
                : Vector3.zero;
            angularVelocity = cachedRigidbody != null && !cachedRigidbody.isKinematic
                ? cachedRigidbody.angularVelocity
                : Vector3.zero;
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

        private void PlayHitSoundIfNeeded(Collider other)
        {
            if (other == null || isDead)
            {
                return;
            }

            bool hitPiece = other.GetComponentInParent<TurnBasedFlickPiece>() != null;
            if (!hitPiece && !IsWallCollider(other))
            {
                return;
            }

            PlaySharedSound(ref hitSoundClip, HitSoundResourcePath, true, HitSoundVolumeScale);
        }

        private static void PlaySharedSound(
            ref AudioClip clip,
            string resourcePath,
            bool useHitCooldown,
            float volumeScale = 1f)
        {
            if (useHitCooldown)
            {
                if (Time.unscaledTime < nextHitSoundTime)
                {
                    return;
                }

                nextHitSoundTime = Time.unscaledTime + HitSoundCooldownSeconds;
            }

            EnsureSharedAudioSource();
            EnsureAudioClip(ref clip, resourcePath);
            if (sharedAudioSource == null || clip == null)
            {
                return;
            }

            sharedAudioSource.PlayOneShot(clip, volumeScale);
        }

        private static void PreloadHitSound()
        {
            EnsureSharedAudioSource();
            EnsureAudioClip(ref hitSoundClip, HitSoundResourcePath);
        }

        private void PlayFlickFailedSoundIfNeeded()
        {
            if (!launchedThisTurn || touchedRequiredTargetThisFlick)
            {
                return;
            }

            PlaySharedSound(ref flickFailedSoundClip, FlickFailedSoundResourcePath, false);
        }

        private static void PreloadFlickFailedSound()
        {
            EnsureSharedAudioSource();
            EnsureAudioClip(ref flickFailedSoundClip, FlickFailedSoundResourcePath);
        }

        private static void EnsureSharedAudioSource()
        {
            if (sharedAudioSource != null)
            {
                return;
            }

            GameObject audioObject = GameObject.Find(PieceAudioObjectName);
            if (audioObject == null)
            {
                audioObject = new GameObject(PieceAudioObjectName);
                DontDestroyOnLoad(audioObject);
            }

            if (!audioObject.TryGetComponent(out sharedAudioSource))
            {
                sharedAudioSource = audioObject.AddComponent<AudioSource>();
            }

            sharedAudioSource.playOnAwake = false;
            sharedAudioSource.loop = false;
            sharedAudioSource.spatialBlend = 0f;
        }

        private static void EnsureAudioClip(ref AudioClip clip, string resourcePath)
        {
            if (clip != null)
            {
                return;
            }

            clip = Resources.Load<AudioClip>(resourcePath);
            if (clip == null)
            {
                Debug.LogWarning("[Piece Audio] Could not load sound at Resources/" + resourcePath + ".", null);
            }
        }

        public void BlockInputUntilPointerReleased()
        {
            waitForPointerReleaseBeforeInput = true;
            CancelDragPresentation();
            launchQueued = false;
            awaitingNetworkFlickAcceptance = false;
            usingLocalFlickPrediction = false;
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
            characterAimPiecePosition = CalculateLaunchPositionFromForceVector(launchVector);
            dragTargetPosition = characterAimPiecePosition;
            ShowFlickPreview(characterAimVector);
            FlickDragUpdated?.Invoke(
                this,
                characterAimVector,
                Mathf.Clamp01(characterAimVector.magnitude / maxDragDistance));
            return true;
        }

        private void EndDragAndQueueFlick()
        {
            uint shotId = BeginNetworkLatencySampleIfNeeded();
            Vector3 forceVector;
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
            Vector3 launchPosition = CalculateLaunchPositionFromForceVector(forceVector);
            cachedRigidbody.position = launchPosition;
            transform.position = launchPosition;
            FlickReleased?.Invoke(this, queuedImpulse);
            if (TrySubmitNetworkFlickRequest(queuedImpulse, launchPosition, shotId))
            {
                return;
            }

            launchQueued = true;
        }

        private void BeginPendingNetworkFlick(Vector3 impulse, Vector3 launchPosition, uint shotId)
        {
            awaitingNetworkFlickAcceptance = true;
            usingLocalFlickPrediction = true;
            launchedThisTurn = true;
            launchQueued = true;
            queuedFlickShotId = shotId;
            waitingForStop = false;
            stoppedTimer = 0f;
            pieceSnapshotCount = 0;

            launchPosition = PreserveCurrentHeight(launchPosition);
            cachedRigidbody.position = launchPosition;
            transform.position = launchPosition;
            queuedImpulse = impulse;
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
            return TryQueueAuthoritativeFlick(impulse, transform.position, 0u);
        }

        public bool TryQueueAuthoritativeFlick(Vector3 impulse, uint shotId)
        {
            return TryQueueAuthoritativeFlick(impulse, transform.position, shotId);
        }

        public bool TryQueueAuthoritativeFlickCommand(
            Vector3 direction,
            float power,
            uint shotId,
            out Vector3 queuedImpulse,
            out Vector3 queuedLaunchPosition)
        {
            queuedImpulse = Vector3.zero;
            queuedLaunchPosition = transform.position;

            direction.y = 0f;
            if (!IsFinite(direction) || float.IsNaN(power) || float.IsInfinity(power))
            {
                return false;
            }

            Vector3 safeDirection = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.zero;
            float maxPower = Mathf.Max(0f, forceMultiplier * maxDragDistance);
            float safePower = maxPower > 0f ? Mathf.Clamp(power, 0f, maxPower) : 0f;
            if (safeDirection == Vector3.zero)
            {
                safePower = 0f;
            }

            queuedImpulse = safeDirection * safePower;

            float dragDistance = forceMultiplier > 0.0001f
                ? Mathf.Clamp(safePower / forceMultiplier, 0f, maxDragDistance)
                : 0f;
            Vector3 requestedLaunchPosition = transform.position - safeDirection * dragDistance;

            if (!TryQueueAuthoritativeFlick(queuedImpulse, requestedLaunchPosition, shotId))
            {
                return false;
            }

            queuedLaunchPosition = transform.position;
            return true;
        }

        public bool TryQueueAuthoritativeFlick(Vector3 impulse, Vector3 requestedLaunchPosition)
        {
            return TryQueueAuthoritativeFlick(impulse, requestedLaunchPosition, 0u);
        }

        public bool TryQueueAuthoritativeFlick(Vector3 impulse, Vector3 requestedLaunchPosition, uint shotId)
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
            queuedFlickShotId = shotId;
            launchQueued = true;
            return true;
        }

        public void ApplyNetworkPose(Vector3 position, Quaternion rotation)
        {
            ApplyNetworkPhysicsState(position, rotation, Vector3.zero, Vector3.zero, 0u, Time.unscaledTimeAsDouble, false);
        }

        public void ApplyNetworkPhysicsState(
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity,
            Vector3 angularVelocity,
            uint serverTick,
            double timestamp,
            bool isFinal)
        {
            if (!ShouldAcceptNetworkStateTick(serverTick))
            {
                return;
            }

            EnsureCachedComponents();
            if (ShouldIgnoreNetworkPoseWhileLocallyInteractive())
            {
                return;
            }

            if (!isFinal && ShouldKeepLocalPredictionForMovingNetworkState())
            {
                return;
            }

            if (isFinal)
            {
                pieceSnapshotCount = 0;
                usingLocalFlickPrediction = false;
                awaitingNetworkFlickAcceptance = false;
                launchQueued = false;
                waitingForStop = false;
                queuedFlickShotId = 0u;
                stoppedTimer = 0f;
                SnapToNetworkPhysicsState(position, rotation, velocity, angularVelocity);
                return;
            }

            if (ShouldReconcilePredictedPhysics())
            {
                ReconcilePredictedPhysics(position, rotation, velocity, angularVelocity);
                return;
            }

            AddPieceSnapshot(serverTick, timestamp, position, rotation, velocity, angularVelocity);
        }

        public void ApplyNetworkState(bool networkIsDead)
        {
            if (networkIsDead)
            {
                MarkDeadAfterExternalBoardExit();
                return;
            }

            RestoreAliveFromNetworkState();
        }

        private void RestoreAliveFromNetworkState()
        {
            if (!isDead)
            {
                return;
            }

            isDead = false;
            invalidatedThisTurn = false;

            if (cachedRenderer != null)
            {
                cachedRenderer.enabled = originalRendererEnabled;
            }

            RestorePieceVisual();
            if (launchedThisTurn || waitingForStop || ShouldReconcilePredictedPhysics())
            {
                EnableDynamicFlickPhysics();
            }
            else
            {
                UpdateCollisionForTurnState(canInteractThisTurn);
            }
        }

        public void MarkNetworkFlickAccepted()
        {
            MarkNetworkFlickAccepted(Vector3.zero, Vector3.zero, 0u);
        }

        public void MarkNetworkFlickAccepted(Vector3 acceptedImpulse, Vector3 acceptedLaunchPosition)
        {
            MarkNetworkFlickAccepted(acceptedImpulse, acceptedLaunchPosition, 0u);
        }

        public void MarkNetworkFlickAccepted(Vector3 acceptedImpulse, Vector3 acceptedLaunchPosition, uint shotId)
        {
            if (awaitingNetworkFlickAcceptance)
            {
                if (launchQueued || waitingForStop || launchedThisTurn)
                {
                    awaitingNetworkFlickAcceptance = false;
                    launchedThisTurn = true;
                    isDragging = false;
                    characterAimActive = false;
                    characterAimVector = Vector3.zero;
                    HideFlickPreview();
                    return;
                }

                if (acceptedImpulse.sqrMagnitude > 0.0001f && IsFinite(acceptedLaunchPosition))
                {
                    BeginAcceptedNetworkPrediction(acceptedImpulse, acceptedLaunchPosition, shotId);
                }
                else
                {
                    awaitingNetworkFlickAcceptance = false;
                    usingLocalFlickPrediction = false;
                    launchedThisTurn = true;
                    launchQueued = false;
                    waitingForStop = false;
                    isDragging = false;
                    characterAimActive = false;
                    characterAimVector = Vector3.zero;
                    HideFlickPreview();
                    ParkWithoutCollision();
                }

                return;
            }

            if (ShouldReconcilePredictedPhysics())
            {
                launchedThisTurn = true;
                isDragging = false;
                characterAimActive = false;
                characterAimVector = Vector3.zero;
                HideFlickPreview();
                return;
            }

            launchedThisTurn = true;
            launchQueued = false;
            waitingForStop = false;
            isDragging = false;
            characterAimActive = false;
            characterAimVector = Vector3.zero;
            HideFlickPreview();
            ParkWithoutCollision();
        }

        private void BeginAcceptedNetworkPrediction(Vector3 acceptedImpulse, Vector3 acceptedLaunchPosition)
        {
            BeginAcceptedNetworkPrediction(acceptedImpulse, acceptedLaunchPosition, 0u);
        }

        private void BeginAcceptedNetworkPrediction(Vector3 acceptedImpulse, Vector3 acceptedLaunchPosition, uint shotId)
        {
            if (shotId == 0u)
            {
                shotId = pendingNetworkFlickShotId;
            }

            awaitingNetworkFlickAcceptance = false;
            usingLocalFlickPrediction = true;
            launchedThisTurn = true;
            launchQueued = true;
            queuedFlickShotId = shotId;
            waitingForStop = false;
            isDragging = false;
            characterAimActive = false;
            characterAimVector = Vector3.zero;
            stoppedTimer = 0f;
            pieceSnapshotCount = 0;

            acceptedLaunchPosition = PreserveCurrentHeight(acceptedLaunchPosition);
            cachedRigidbody.position = acceptedLaunchPosition;
            transform.position = acceptedLaunchPosition;
            queuedImpulse = acceptedImpulse;
            HideFlickPreview();
        }

        private bool ShouldIgnoreNetworkPoseWhileLocallyInteractive()
        {
            if (isDragging)
            {
                return true;
            }

            return false;
        }

        private bool TrySubmitNetworkFlickRequest(Vector3 impulse, Vector3 launchPosition, uint shotId)
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            if (bootstrap == null || !bootstrap.IsRunning || bootstrap.IsHost)
            {
                return false;
            }

            bootstrap.SubmitFlickRequestToHost(owner, pieceId, impulse, shotId);
            pendingNetworkFlickShotId = shotId;
            BeginPendingNetworkFlick(impulse, launchPosition, shotId);
            return true;
        }

        private uint BeginNetworkLatencySampleIfNeeded()
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            if (bootstrap == null || !bootstrap.IsRunning || bootstrap.IsHost)
            {
                return 0u;
            }

            return bootstrap.BeginClientFlickLatencySample(owner, pieceId);
        }

        private IEnumerator RecordHostPhysicsStepCompleteAfterFixedUpdate(uint shotId)
        {
            yield return new WaitForFixedUpdate();
            FlickLatencyProbe.RecordHostPhysicsStepComplete(shotId, owner, pieceId);
        }

        private bool ShouldReconcilePredictedPhysics()
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            return bootstrap != null
                && bootstrap.IsClientOnly
                && bootstrap.LocalPlayerId == owner
                && (awaitingNetworkFlickAcceptance || usingLocalFlickPrediction || waitingForStop || launchQueued);
        }

        private bool ShouldKeepLocalPredictionForMovingNetworkState()
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            return usingLocalFlickPrediction
                && bootstrap != null
                && bootstrap.IsClientOnly
                && bootstrap.LocalPlayerId == owner;
        }

        private void ReconcilePredictedPhysics(
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity,
            Vector3 angularVelocity)
        {
            if (cachedRigidbody == null)
            {
                transform.SetPositionAndRotation(position, rotation);
                return;
            }

            float error = Vector3.Distance(cachedRigidbody.position, position);
            if (error <= networkCorrectionIgnoreDistance)
            {
                return;
            }

            if (error >= networkCorrectionSnapDistance)
            {
                SnapToNetworkPhysicsState(position, rotation, velocity, angularVelocity);
                return;
            }

            Vector3 correctedPosition = Vector3.Lerp(
                cachedRigidbody.position,
                position,
                networkPositionCorrectionBlend);
            Quaternion correctedRotation = Quaternion.Slerp(
                cachedRigidbody.rotation,
                rotation,
                networkPositionCorrectionBlend);
            cachedRigidbody.position = correctedPosition;
            cachedRigidbody.rotation = correctedRotation;
            transform.SetPositionAndRotation(correctedPosition, correctedRotation);

            if (!cachedRigidbody.isKinematic)
            {
                cachedRigidbody.linearVelocity = Vector3.Lerp(
                    cachedRigidbody.linearVelocity,
                    velocity,
                    networkVelocityCorrectionBlend);
                cachedRigidbody.angularVelocity = Vector3.Lerp(
                    cachedRigidbody.angularVelocity,
                    angularVelocity,
                    networkVelocityCorrectionBlend);
            }
        }

        private void SnapToNetworkPhysicsState(
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity,
            Vector3 angularVelocity)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (cachedRigidbody == null)
            {
                return;
            }

            cachedRigidbody.position = position;
            cachedRigidbody.rotation = rotation;
            if (!cachedRigidbody.isKinematic)
            {
                cachedRigidbody.linearVelocity = velocity;
                cachedRigidbody.angularVelocity = angularVelocity;
            }
        }

        private void AddPieceSnapshot(
            uint tick,
            double timestamp,
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity,
            Vector3 angularVelocity)
        {
            if (pieceSnapshotCount >= SnapshotBufferSize)
            {
                for (int i = 1; i < SnapshotBufferSize; i++)
                {
                    pieceSnapshots[i - 1] = pieceSnapshots[i];
                }

                pieceSnapshotCount = SnapshotBufferSize - 1;
            }

            pieceSnapshots[pieceSnapshotCount++] = new PieceSnapshot
            {
                Tick = tick,
                Timestamp = timestamp,
                Position = position,
                Rotation = rotation,
                Velocity = velocity,
                AngularVelocity = angularVelocity
            };
        }

        private bool ShouldAcceptNetworkStateTick(uint tick)
        {
            if (tick == 0u)
            {
                return true;
            }

            if (!hasLatestNetworkStateTick || IsTickNewerOrEqual(tick, latestNetworkStateTick))
            {
                hasLatestNetworkStateTick = true;
                latestNetworkStateTick = tick;
                return true;
            }

            return false;
        }

        private static bool IsTickNewerOrEqual(uint incoming, uint previous)
        {
            return incoming == previous || (int)(incoming - previous) > 0;
        }

        private void RenderNetworkSnapshotIfNeeded()
        {
            if (ShouldReconcilePredictedPhysics() || isDragging || pieceSnapshotCount <= 0)
            {
                return;
            }

            double renderTime = GetNetworkRenderTime();
            PieceSnapshot target = pieceSnapshots[pieceSnapshotCount - 1];
            if (pieceSnapshotCount >= 2)
            {
                for (int i = 0; i < pieceSnapshotCount - 1; i++)
                {
                    PieceSnapshot from = pieceSnapshots[i];
                    PieceSnapshot to = pieceSnapshots[i + 1];
                    if (renderTime < from.Timestamp || renderTime > to.Timestamp)
                    {
                        continue;
                    }

                    double duration = Math.Max(0.0001d, to.Timestamp - from.Timestamp);
                    float t = Mathf.Clamp01((float)((renderTime - from.Timestamp) / duration));
                    target = new PieceSnapshot
                    {
                        Tick = to.Tick,
                        Timestamp = renderTime,
                        Position = Vector3.Lerp(from.Position, to.Position, t),
                        Rotation = Quaternion.Slerp(from.Rotation, to.Rotation, t),
                        Velocity = Vector3.Lerp(from.Velocity, to.Velocity, t),
                        AngularVelocity = Vector3.Lerp(from.AngularVelocity, to.AngularVelocity, t)
                    };
                    break;
                }
            }

            SnapToNetworkPhysicsState(target.Position, target.Rotation, target.Velocity, target.AngularVelocity);
        }

        private double GetNetworkRenderTime()
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            if (bootstrap != null && bootstrap.NetworkManager != null)
            {
                return bootstrap.NetworkManager.ServerTime.Time - snapshotInterpolationBackTime;
            }

            return Time.unscaledTimeAsDouble - snapshotInterpolationBackTime;
        }

        private void TickStopDetection()
        {
            if (transform.position.y <= fallYThreshold || HasExitedBoardAfterLaunch())
            {
                if (ShouldDeferAuthoritativeFlickOutcomeToHost())
                {
                    stoppedTimer = 0f;
                    return;
                }

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

        private static bool ShouldDeferAuthoritativeFlickOutcomeToHost()
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            return bootstrap != null && bootstrap.IsClientOnly;
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
            awaitingNetworkFlickAcceptance = false;
            usingLocalFlickPrediction = false;
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

            PlayFlickFailedSoundIfNeeded();
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
            awaitingNetworkFlickAcceptance = false;
            usingLocalFlickPrediction = false;
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

        private struct PieceSnapshot
        {
            public uint Tick;
            public double Timestamp;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Velocity;
            public Vector3 AngularVelocity;
        }
    }

    public static class FlickDomCollisionRules
    {
        public static void IgnoreMonkeyPieceCollisions()
        {
            TurnBasedFlickPiece[] pieces =
                UnityEngine.Object.FindObjectsByType<TurnBasedFlickPiece>(FindObjectsInactive.Include);
            MonkeyThirdPersonController[] monkeys =
                UnityEngine.Object.FindObjectsByType<MonkeyThirdPersonController>(FindObjectsInactive.Include);

            for (int pieceIndex = 0; pieceIndex < pieces.Length; pieceIndex++)
            {
                TurnBasedFlickPiece piece = pieces[pieceIndex];
                if (piece == null)
                {
                    continue;
                }

                for (int monkeyIndex = 0; monkeyIndex < monkeys.Length; monkeyIndex++)
                {
                    IgnorePieceAgainstMonkey(piece, monkeys[monkeyIndex]);
                }
            }
        }

        public static void IgnoreMonkeyCollisionsForPiece(TurnBasedFlickPiece piece)
        {
            if (piece == null)
            {
                return;
            }

            MonkeyThirdPersonController[] monkeys =
                UnityEngine.Object.FindObjectsByType<MonkeyThirdPersonController>(FindObjectsInactive.Include);
            for (int i = 0; i < monkeys.Length; i++)
            {
                IgnorePieceAgainstMonkey(piece, monkeys[i]);
            }
        }

        public static void IgnorePieceCollisionsForMonkey(MonkeyThirdPersonController monkey)
        {
            if (monkey == null)
            {
                return;
            }

            TurnBasedFlickPiece[] pieces =
                UnityEngine.Object.FindObjectsByType<TurnBasedFlickPiece>(FindObjectsInactive.Include);
            for (int i = 0; i < pieces.Length; i++)
            {
                IgnorePieceAgainstMonkey(pieces[i], monkey);
            }
        }

        private static void IgnorePieceAgainstMonkey(TurnBasedFlickPiece piece, MonkeyThirdPersonController monkey)
        {
            if (piece == null || monkey == null)
            {
                return;
            }

            Collider[] pieceColliders = piece.GetComponentsInChildren<Collider>(true);
            Collider[] monkeyColliders = monkey.GetComponentsInChildren<Collider>(true);
            for (int pieceIndex = 0; pieceIndex < pieceColliders.Length; pieceIndex++)
            {
                Collider pieceCollider = pieceColliders[pieceIndex];
                if (pieceCollider == null)
                {
                    continue;
                }

                for (int monkeyIndex = 0; monkeyIndex < monkeyColliders.Length; monkeyIndex++)
                {
                    Collider monkeyCollider = monkeyColliders[monkeyIndex];
                    if (monkeyCollider == null || monkeyCollider == pieceCollider)
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(pieceCollider, monkeyCollider, true);
                }
            }
        }
    }
}
