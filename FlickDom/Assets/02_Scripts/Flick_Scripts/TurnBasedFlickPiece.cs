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

        private Rigidbody cachedRigidbody;
        private Collider cachedCollider;
        private Renderer cachedRenderer;

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
        private float stoppedTimer;
        private bool originalUseGravity;
        private bool originalIsKinematic;
        private bool originalColliderEnabled;
        private bool originalColliderIsTrigger;
        private bool originalRendererEnabled;
        private RigidbodyConstraints originalConstraints;

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

        private void Awake()
        {
            cachedRigidbody = GetComponent<Rigidbody>();
            cachedCollider = GetComponent<Collider>();
            cachedRenderer = GetComponentInChildren<Renderer>();
            flickStartPosition = transform.position;
            flickStartRotation = transform.rotation;
            originalUseGravity = cachedRigidbody.useGravity;
            originalIsKinematic = cachedRigidbody.isKinematic;
            originalColliderEnabled = cachedCollider.enabled;
            originalColliderIsTrigger = cachedCollider.isTrigger;
            originalRendererEnabled = cachedRenderer == null || cachedRenderer.enabled;
            originalConstraints = cachedRigidbody.constraints;

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
        }

        private void Update()
        {
            if (Mouse.current == null || inputCamera == null)
            {
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

        public void SetTurnHighlight(bool isActiveTurn)
        {
            UpdateCollisionForTurnState(isActiveTurn);

            if (cachedRenderer == null)
            {
                return;
            }

            if (isDead)
            {
                cachedRenderer.material.color = deadTint;
                return;
            }

            if (!IsInsidePlayableBoard())
            {
                cachedRenderer.material.color = inactiveTint;
                return;
            }

            cachedRenderer.material.color = isActiveTurn ? GetOwnerColor() : inactiveTint;
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
            stoppedTimer = 0f;
            ApplyBaseColor();
            UpdateCollisionForTurnState(false);
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
        }

        private void EndDragAndQueueFlick()
        {
            mouseEndPosition = GetMousePositionOnBoard();
            isDragging = false;

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
                else
                {
                    cachedRenderer.material.color = deadTint;
                }
            }
        }

        private void StopDeadPieceSimulation()
        {
            isDragging = false;
            launchQueued = false;
            waitingForStop = false;
            stoppedTimer = 0f;

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
            cachedRigidbody.position = flickStartPosition;
            cachedRigidbody.rotation = flickStartRotation;
            transform.SetPositionAndRotation(flickStartPosition, flickStartRotation);

            cachedRigidbody.constraints = originalConstraints;
            cachedRigidbody.useGravity = originalUseGravity;
            cachedRigidbody.isKinematic = originalIsKinematic;
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
            cachedCollider.enabled = true;
            cachedCollider.isTrigger = true;
        }

        private void EnableDynamicFlickPhysics()
        {
            cachedCollider.enabled = true;
            cachedCollider.isTrigger = originalColliderIsTrigger;
            cachedRigidbody.useGravity = originalUseGravity;
            cachedRigidbody.isKinematic = false;
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
            if (cachedRenderer == null)
            {
                return;
            }

            cachedRenderer.material.color = GetOwnerColor();
        }

        private Color GetOwnerColor()
        {
            return owner == FlickDomPlayerId.Player2 ? player2Color : player1Color;
        }
    }
}
