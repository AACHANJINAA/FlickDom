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

        private bool isDragging;
        private bool launchQueued;
        private bool waitingForStop;
        private bool launchedThisTurn;
        private float stoppedTimer;

        public event Action<TurnBasedFlickPiece> FlickStarted;
        public event Action<TurnBasedFlickPiece> SettledAfterFlick;

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

        private void Awake()
        {
            cachedRigidbody = GetComponent<Rigidbody>();
            cachedCollider = GetComponent<Collider>();
            cachedRenderer = GetComponentInChildren<Renderer>();

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
                cachedRigidbody.linearVelocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
                cachedRigidbody.MovePosition(dragTargetPosition);
            }

            if (launchQueued)
            {
                launchQueued = false;
                cachedRigidbody.linearVelocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
                cachedRigidbody.AddForce(queuedImpulse, ForceMode.Impulse);
                waitingForStop = true;
                launchedThisTurn = true;
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
            if (cachedRenderer == null)
            {
                return;
            }

            cachedRenderer.material.color = isActiveTurn ? GetOwnerColor() : inactiveTint;
        }

        public void ResetRoundUse()
        {
            isDragging = false;
            launchQueued = false;
            waitingForStop = false;
            launchedThisTurn = false;
            stoppedTimer = 0f;
        }

        private bool CanInteractThisFrame()
        {
            if (isDragging)
            {
                return true;
            }

            if (launchedThisTurn)
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
