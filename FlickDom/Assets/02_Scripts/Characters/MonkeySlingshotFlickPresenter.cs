using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace FlickDom.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class MonkeySlingshotFlickPresenter : MonoBehaviour
    {
        private enum PresentationState
        {
            Idle = 0,
            Pulling = 1,
            Releasing = 2,
            Returning = 3
        }

        private const float MinDirectionSqr = 0.0001f;
        private static readonly int DefaultAnimationParameterHash = Animator.StringToHash("animation");

        [Header("Binding")]
        [SerializeField] private FlickDomPlayerId owner = FlickDomPlayerId.Player1;
        [SerializeField] private bool reactToAllPlayers = true;
        [SerializeField] private bool enableFlickPresentation;
        [SerializeField] private TurnBasedFlickPiece[] pieces;
        [SerializeField] private MonkeyThirdPersonController movementController;
        [SerializeField] private Animator animator;
        [SerializeField] private Rigidbody cachedRigidbody;

        [Header("WASD Aim")]
        [SerializeField] private Camera aimCamera;
        [SerializeField] private float aimMoveSpeed = 2.4f;
        [SerializeField] private float aimTurnSpeed = 140f;
        [SerializeField] private float aimPowerAdjustSpeed = 1.8f;
        [SerializeField] private float distanceBehindStone = 0.72f;
        [SerializeField] private float minimumAimDistance = 0.55f;
        [SerializeField] private float maximumAimDistance = 3f;
        [SerializeField] private Vector3 aimCameraLocalOffset = new Vector3(0f, 1.35f, 0.08f);
        [SerializeField] private Vector3 aimLookTargetOffset = new Vector3(0f, 0.12f, 0f);

        [Header("Pull Pose")]
        [SerializeField] private float sidewaysOffset;
        [SerializeField] private float heightOffset;
        [SerializeField] private float positionSmoothTime = 0.045f;
        [SerializeField] private float rotationSpeed = 900f;

        [Header("Release")]
        [SerializeField] private float releasePoseSeconds = 0.42f;
        [SerializeField] private float recoilDistance = 0.14f;
        [SerializeField] private bool returnToHomeAfterRelease;
        [SerializeField] private float returnSpeed = 5f;
        [SerializeField] private float returnRotationSpeed = 720f;

        [Header("Animation")]
        [SerializeField] private bool driveAnimator = true;
        [SerializeField] private string animationParameter = "animation";
        [SerializeField] private int idleAnimationValue = 1;
        [SerializeField] private int aimMoveAnimationValue = 21;
        [SerializeField] private int pullAnimationValue = 4;
        [SerializeField] private int releaseAnimationValue = 37;

        [Header("Slingshot Bands")]
        [SerializeField] private bool showPullBands = true;
        [SerializeField] private Color bandColor = new Color(0.34f, 0.12f, 0.035f, 1f);
        [SerializeField] private float bandWidth = 0.035f;
        [SerializeField] private float gripHeight = 0.62f;
        [SerializeField] private float gripHalfWidth = 0.18f;
        [SerializeField] private float stoneBandHeight = 0.12f;
        [SerializeField] private bool disableCollidersDuringPresentation = true;

        [Header("Slingshot Launcher Model")]
        [SerializeField] private GameObject launcherPrefab;
        [SerializeField] private Material launcherMaterial;
        [SerializeField] private Vector3 launcherScale = new Vector3(0.4f, 0.4f, 0.4f);
        [SerializeField] private Vector3 launcherEulerOffset = new Vector3(0f, 90f, 0f);
        [SerializeField] private Vector3 launcherStoneCenterOffset = Vector3.zero;
        [FormerlySerializedAs("pouchPullDistance")]
        [SerializeField] private float launcherGripForwardOffset = 0.28f;
        [SerializeField] private float stoneInPouchHeightOffset = 0.04f;
        [SerializeField] private float launcherForwardOvershootDistance = 0.24f;
        [SerializeField] private float launcherBackwardReboundDistance = 0.08f;

        private readonly List<TurnBasedFlickPiece> boundPieces = new List<TurnBasedFlickPiece>(6);
        private Collider[] cachedColliders;
        private bool[] colliderEnabledBeforePresentation;
        private LineRenderer leftBand;
        private LineRenderer rightBand;
        private Material bandMaterial;
        private GameObject launcherInstance;
        private Transform launcherTransform;
        private SlingshotMeshVisualRig launcherRig;
        private Vector3 launcherStoneAnchorWorld;
        private bool hasLauncherStoneAnchor;
        private Transform cachedTransform;
        private TurnBasedFlickPiece activePiece;
        private PresentationState state;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private Vector3 desiredAimPosition;
        private Vector3 positionVelocity;
        private Vector3 launchDirection;
        private Vector3 releaseStartPosition;
        private Vector3 releasePouchStartWorld;
        private float pullCharacterHeight;
        private float releaseTimer;
        private float currentNormalizedPower;
        private int animationParameterHash = DefaultAnimationParameterHash;
        private int currentAnimationValue = int.MinValue;
        private bool hasHomePose;
        private bool physicsOverridden;
        private bool controllerWasEnabled;
        private bool bodyWasKinematic;
        private bool bodyUsedGravity;
        private RigidbodyConstraints bodyConstraints;
        private CollisionDetectionMode bodyCollisionMode;
        private MonkeyThirdPersonCameraFollow cameraFollow;

        public FlickDomPlayerId Owner
        {
            get { return owner; }
        }

        private void Reset()
        {
            CacheComponents();
        }

        private void Awake()
        {
            CacheComponents();
            CaptureHomePose();
            RefreshAnimationParameterHash();
            if (aimCamera == null)
            {
                aimCamera = Camera.main;
            }

            if (cameraFollow == null && aimCamera != null)
            {
                cameraFollow = aimCamera.GetComponent<MonkeyThirdPersonCameraFollow>();
            }

            EnsurePullBands();
            SetBandsVisible(false);
        }

        private void OnValidate()
        {
            distanceBehindStone = Mathf.Max(0.05f, distanceBehindStone);
            aimMoveSpeed = Mathf.Max(0.01f, aimMoveSpeed);
            aimTurnSpeed = Mathf.Max(1f, aimTurnSpeed);
            aimPowerAdjustSpeed = Mathf.Max(0.01f, aimPowerAdjustSpeed);
            minimumAimDistance = Mathf.Max(0.05f, minimumAimDistance);
            maximumAimDistance = Mathf.Max(minimumAimDistance, maximumAimDistance);
            positionSmoothTime = Mathf.Max(0.001f, positionSmoothTime);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            releasePoseSeconds = Mathf.Max(0.01f, releasePoseSeconds);
            recoilDistance = Mathf.Max(0f, recoilDistance);
            returnSpeed = Mathf.Max(0.01f, returnSpeed);
            returnRotationSpeed = Mathf.Max(0f, returnRotationSpeed);
            bandWidth = Mathf.Max(0.001f, bandWidth);
            gripHalfWidth = Mathf.Max(0f, gripHalfWidth);
            launcherScale.x = Mathf.Max(0.001f, launcherScale.x);
            launcherScale.y = Mathf.Max(0.001f, launcherScale.y);
            launcherScale.z = Mathf.Max(0.001f, launcherScale.z);
            launcherGripForwardOffset = Mathf.Max(0f, launcherGripForwardOffset);
            launcherForwardOvershootDistance =
                Mathf.Max(0f, launcherForwardOvershootDistance);
            launcherBackwardReboundDistance =
                Mathf.Max(0f, launcherBackwardReboundDistance);
            RefreshAnimationParameterHash();
        }

        private void OnEnable()
        {
            RebindPieces();
        }

        private void Start()
        {
            // Runtime-generated pieces are created during other components' Awake calls.
            // Rebinding in Start includes those clones without searching every frame.
            RebindPieces();
        }

        private void OnDisable()
        {
            UnbindPieces();
            StopPresentation(false);
        }

        private void OnDestroy()
        {
            if (bandMaterial != null)
            {
                Destroy(bandMaterial);
                bandMaterial = null;
            }

            if (launcherInstance != null)
            {
                Destroy(launcherInstance);
                launcherInstance = null;
            }
        }

        private void LateUpdate()
        {
            switch (state)
            {
                case PresentationState.Pulling:
                    UpdatePullPose();
                    break;
                case PresentationState.Releasing:
                    UpdateReleasePose();
                    break;
                case PresentationState.Returning:
                    UpdateReturnPose();
                    break;
            }
        }

        private void Update()
        {
            if (state == PresentationState.Pulling)
            {
                ReadAimMovement();
            }
        }

        public void SetOwner(FlickDomPlayerId playerId)
        {
            if (owner == playerId)
            {
                return;
            }

            owner = playerId;
            if (isActiveAndEnabled)
            {
                RebindPieces();
            }
        }

        public void SetReactToAllPlayers(bool value)
        {
            if (reactToAllPlayers == value)
            {
                return;
            }

            reactToAllPlayers = value;
            if (isActiveAndEnabled)
            {
                RebindPieces();
            }
        }

        public void SetFlickPresentationEnabled(bool value)
        {
            if (enableFlickPresentation == value)
            {
                return;
            }

            enableFlickPresentation = value;
            if (!enableFlickPresentation)
            {
                StopPresentation(true);
            }

            if (isActiveAndEnabled)
            {
                RebindPieces();
            }
        }

        public void SetPieces(TurnBasedFlickPiece[] flickPieces)
        {
            pieces = flickPieces;
            if (isActiveAndEnabled)
            {
                RebindPieces();
            }
        }

        public void UseSuriyunAnimationPreset()
        {
            animationParameter = "animation";
            idleAnimationValue = 1;
            pullAnimationValue = 4;
            releaseAnimationValue = 37;
            RefreshAnimationParameterHash();
        }

        public void ConfigureLauncher(GameObject prefab, Material material)
        {
            if (launcherPrefab == prefab && launcherMaterial == material)
            {
                return;
            }

            launcherPrefab = prefab;
            launcherMaterial = material;
            if (launcherInstance != null)
            {
                Destroy(launcherInstance);
                launcherInstance = null;
                launcherTransform = null;
                launcherRig = null;
            }
        }

        public void ConfigureLauncherTransform(
            Vector3 scale,
            Vector3 eulerOffset,
            Vector3 stoneCenterOffset,
            float gripForwardOffset)
        {
            launcherScale = new Vector3(
                Mathf.Max(0.001f, scale.x),
                Mathf.Max(0.001f, scale.y),
                Mathf.Max(0.001f, scale.z));
            launcherEulerOffset = eulerOffset;
            launcherStoneCenterOffset = stoneCenterOffset;
            launcherGripForwardOffset = Mathf.Max(0f, gripForwardOffset);
        }

        private void CacheComponents()
        {
            cachedTransform = transform;

            if (movementController == null)
            {
                movementController = GetComponent<MonkeyThirdPersonController>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (cachedRigidbody == null)
            {
                cachedRigidbody = GetComponent<Rigidbody>();
            }

            cachedColliders = GetComponentsInChildren<Collider>(true);
        }

        private void CaptureHomePose()
        {
            if (cachedTransform == null)
            {
                cachedTransform = transform;
            }

            homePosition = cachedTransform.position;
            homeRotation = cachedTransform.rotation;
            hasHomePose = true;
        }

        private void RebindPieces()
        {
            UnbindPieces();

            if (pieces != null && pieces.Length > 0)
            {
                for (int i = 0; i < pieces.Length; i++)
                {
                    TryBindPiece(pieces[i]);
                }

                return;
            }

            TurnBasedFlickPiece[] scenePieces = FindObjectsByType<TurnBasedFlickPiece>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < scenePieces.Length; i++)
            {
                if (CanPresentPiece(scenePieces[i]))
                {
                    TryBindPiece(scenePieces[i]);
                }
            }
        }

        private void TryBindPiece(TurnBasedFlickPiece piece)
        {
            if (piece == null || boundPieces.Contains(piece))
            {
                return;
            }

            boundPieces.Add(piece);
            piece.FlickDragStarted += HandleDragStarted;
            piece.FlickDragUpdated += HandleDragUpdated;
            piece.FlickDragCancelled += HandleDragCancelled;
            piece.FlickReleased += HandleFlickReleased;
            piece.FlickStarted += HandleAuthoritativeFlickStarted;
        }

        private void UnbindPieces()
        {
            for (int i = 0; i < boundPieces.Count; i++)
            {
                TurnBasedFlickPiece piece = boundPieces[i];
                if (piece == null)
                {
                    continue;
                }

                piece.FlickDragStarted -= HandleDragStarted;
                piece.FlickDragUpdated -= HandleDragUpdated;
                piece.FlickDragCancelled -= HandleDragCancelled;
                piece.FlickReleased -= HandleFlickReleased;
                piece.FlickStarted -= HandleAuthoritativeFlickStarted;
            }

            boundPieces.Clear();
        }

        private void HandleDragStarted(TurnBasedFlickPiece piece)
        {
            if (!CanPresentPiece(piece))
            {
                return;
            }

            activePiece = piece;
            launchDirection = GetInitialFacingDirection(piece);
            releaseTimer = 0f;
            currentNormalizedPower = 0f;
            positionVelocity = Vector3.zero;
            pullCharacterHeight = cachedTransform.position.y;
            OverrideCharacterPhysics();

            Vector3 right = Vector3.Cross(Vector3.up, launchDirection);
            desiredAimPosition = piece.transform.position
                - launchDirection * distanceBehindStone
                + right * sidewaysOffset;
            desiredAimPosition.y = pullCharacterHeight + heightOffset;
            ClampDesiredAimPosition();

            cachedTransform.SetPositionAndRotation(
                desiredAimPosition,
                Quaternion.LookRotation(launchDirection, Vector3.up));
            SyncKinematicBody();

            SetAnimation(pullAnimationValue);
            SetState(PresentationState.Pulling);
            BeginLauncherPresentation();
            EnableAimCameraFocus();
            piece.TrySetCharacterAim(
                GetCharacterLaunchVector(),
                GetStonePouchPosition());
            UpdatePullBands();
        }

        private void HandleDragUpdated(TurnBasedFlickPiece piece, Vector3 launchVector, float normalizedPower)
        {
            if (piece != activePiece || state != PresentationState.Pulling)
            {
                return;
            }

            launchVector.y = 0f;
            if (launchVector.sqrMagnitude > MinDirectionSqr)
            {
                launchDirection = launchVector.normalized;
            }

            currentNormalizedPower = Mathf.Clamp01(normalizedPower);
            bool useLegacyBands = launcherRig == null || !launcherRig.HasDeformableBands;
            SetBandsVisible(
                showPullBands
                && useLegacyBands
                && currentNormalizedPower > 0.001f);
            UpdateLauncherPose(1f);
        }

        private void HandleDragCancelled(TurnBasedFlickPiece piece)
        {
            if (piece == activePiece)
            {
                StopPresentation(true);
            }
        }

        private void HandleFlickReleased(TurnBasedFlickPiece piece, Vector3 impulse)
        {
            if (piece != activePiece || state != PresentationState.Pulling)
            {
                return;
            }

            Vector3 horizontalImpulse = impulse;
            horizontalImpulse.y = 0f;
            if (horizontalImpulse.sqrMagnitude > MinDirectionSqr)
            {
                launchDirection = horizontalImpulse.normalized;
            }

            BeginRelease();
        }

        private void HandleAuthoritativeFlickStarted(TurnBasedFlickPiece piece)
        {
            if (!CanPresentPiece(piece) || state == PresentationState.Releasing)
            {
                return;
            }

            activePiece = piece;
            launchDirection = GetInitialFacingDirection(piece);
            OverrideCharacterPhysics();
            currentNormalizedPower = 1f;
            BeginLauncherPresentation();
            BeginRelease();
        }

        private void BeginRelease()
        {
            releaseTimer = 0f;
            releaseStartPosition = cachedTransform.position;
            releasePouchStartWorld = launcherRig != null
                ? launcherRig.PouchCenterWorld
                : launcherStoneAnchorWorld;
            positionVelocity = Vector3.zero;
            SetBandsVisible(false);
            SetAnimation(releaseAnimationValue);
            SetState(PresentationState.Releasing);
        }

        private void UpdatePullPose()
        {
            if (activePiece == null)
            {
                StopPresentation(returnToHomeAfterRelease);
                return;
            }

            cachedTransform.position = Vector3.SmoothDamp(
                cachedTransform.position,
                desiredAimPosition,
                ref positionVelocity,
                positionSmoothTime,
                aimMoveSpeed * 2f,
                Time.deltaTime);

            Vector3 characterLaunchVector = GetCharacterLaunchVector();
            if (characterLaunchVector.sqrMagnitude > MinDirectionSqr)
            {
                launchDirection = characterLaunchVector.normalized;
            }

            Quaternion targetRotation = Quaternion.LookRotation(launchDirection, Vector3.up);
            cachedTransform.rotation = Quaternion.RotateTowards(
                cachedTransform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);

            SyncKinematicBody();
            UpdateLauncherPose(1f);
            activePiece.TrySetCharacterAim(
                characterLaunchVector,
                GetStonePouchPosition());
            UpdatePullBands();
        }

        private void ReadAimMovement()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || activePiece == null)
            {
                SetAnimation(pullAnimationValue);
                return;
            }

            float horizontal = 0f;
            float vertical = 0f;
            if (keyboard.aKey.isPressed)
            {
                horizontal -= 1f;
            }

            if (keyboard.dKey.isPressed)
            {
                horizontal += 1f;
            }

            if (keyboard.sKey.isPressed)
            {
                vertical -= 1f;
            }

            if (keyboard.wKey.isPressed)
            {
                vertical += 1f;
            }

            bool changed = false;
            Vector3 piecePosition = GetAimStoneOrigin();
            Vector3 offset = desiredAimPosition - piecePosition;
            offset.y = 0f;

            if (offset.sqrMagnitude <= MinDirectionSqr)
            {
                Vector3 fallbackDirection = launchDirection.sqrMagnitude > MinDirectionSqr
                    ? -launchDirection
                    : -cachedTransform.forward;
                fallbackDirection.y = 0f;
                offset = fallbackDirection.normalized * Mathf.Max(distanceBehindStone, minimumAimDistance);
            }

            float currentDistance = Mathf.Clamp(offset.magnitude, minimumAimDistance, maximumAimDistance);
            Vector3 currentDirection = offset.normalized;

            if (Mathf.Abs(horizontal) > MinDirectionSqr)
            {
                float yawDelta = horizontal * aimTurnSpeed * Time.deltaTime;
                currentDirection = Quaternion.AngleAxis(yawDelta, Vector3.up) * currentDirection;
                currentDirection.y = 0f;
                if (currentDirection.sqrMagnitude > MinDirectionSqr)
                {
                    currentDirection.Normalize();
                    changed = true;
                }
            }

            if (Mathf.Abs(vertical) > MinDirectionSqr)
            {
                currentDistance = Mathf.Clamp(
                    currentDistance - vertical * aimPowerAdjustSpeed * Time.deltaTime,
                    minimumAimDistance,
                    maximumAimDistance);
                changed = true;
            }

            if (!changed)
            {
                SetAnimation(pullAnimationValue);
                return;
            }

            desiredAimPosition = piecePosition + currentDirection * currentDistance;
            desiredAimPosition.y = pullCharacterHeight + heightOffset;
            ClampDesiredAimPosition();
            SetAnimation(aimMoveAnimationValue);
        }

        private void ClampDesiredAimPosition()
        {
            if (activePiece == null)
            {
                return;
            }

            Vector3 piecePosition = GetAimStoneOrigin();
            Vector3 offset = desiredAimPosition - piecePosition;
            offset.y = 0f;
            if (offset.sqrMagnitude <= MinDirectionSqr)
            {
                Vector3 fallbackDirection = launchDirection.sqrMagnitude > MinDirectionSqr
                    ? -launchDirection
                    : -cachedTransform.forward;
                fallbackDirection.y = 0f;
                offset = fallbackDirection.normalized * minimumAimDistance;
            }

            float clampedDistance = Mathf.Clamp(
                offset.magnitude,
                minimumAimDistance,
                maximumAimDistance);
            desiredAimPosition = piecePosition + offset.normalized * clampedDistance;
            desiredAimPosition.y = pullCharacterHeight + heightOffset;
        }

        private Vector3 GetCharacterLaunchVector()
        {
            if (activePiece == null)
            {
                return Vector3.zero;
            }

            Vector3 vector = GetAimStoneOrigin() - cachedTransform.position;
            vector.y = 0f;
            return Vector3.ClampMagnitude(vector, maximumAimDistance);
        }

        private Vector3 GetAimStoneOrigin()
        {
            if (hasLauncherStoneAnchor)
            {
                return launcherStoneAnchorWorld;
            }

            return activePiece != null
                ? activePiece.transform.position
                : cachedTransform.position;
        }

        private Vector3 GetStonePouchPosition()
        {
            if (launcherRig == null
                || launcherInstance == null
                || !launcherInstance.activeSelf)
            {
                return activePiece != null
                    ? activePiece.transform.position
                    : cachedTransform.position;
            }

            return launcherRig.PouchCenterWorld
                + Vector3.up * stoneInPouchHeightOffset;
        }

        private void UpdateReleasePose()
        {
            releaseTimer += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(releaseTimer / releasePoseSeconds);
            float recoil = Mathf.Sin(normalizedTime * Mathf.PI) * recoilDistance;
            cachedTransform.position = releaseStartPosition - launchDirection * recoil;
            SyncKinematicBody();
            UpdateLauncherReleasePose(normalizedTime);

            if (normalizedTime < 1f)
            {
                return;
            }

            SetAnimation(idleAnimationValue);
            if (returnToHomeAfterRelease && hasHomePose)
            {
                SetState(PresentationState.Returning);
                return;
            }

            FinishPresentation();
        }

        private void UpdateLauncherReleasePose(float normalizedTime)
        {
            if (launcherRig == null
                || launcherInstance == null
                || !launcherInstance.activeSelf
                || !hasLauncherStoneAnchor)
            {
                return;
            }

            Vector3 forwardOvershoot = launcherStoneAnchorWorld
                + launchDirection * launcherForwardOvershootDistance;
            Vector3 backwardRebound = launcherStoneAnchorWorld
                - launchDirection * launcherBackwardReboundDistance;
            Vector3 target;

            if (normalizedTime < 0.34f)
            {
                float stageTime = Mathf.SmoothStep(
                    0f,
                    1f,
                    normalizedTime / 0.34f);
                target = Vector3.Lerp(
                    releasePouchStartWorld,
                    forwardOvershoot,
                    stageTime);
            }
            else if (normalizedTime < 0.68f)
            {
                float stageTime = Mathf.SmoothStep(
                    0f,
                    1f,
                    (normalizedTime - 0.34f) / 0.34f);
                target = Vector3.Lerp(
                    forwardOvershoot,
                    backwardRebound,
                    stageTime);
            }
            else
            {
                float stageTime = Mathf.SmoothStep(
                    0f,
                    1f,
                    (normalizedTime - 0.68f) / 0.32f);
                target = Vector3.Lerp(
                    backwardRebound,
                    launcherStoneAnchorWorld,
                    stageTime);
            }

            launcherRig.SetPouchTarget(launchDirection, target);
        }

        private void UpdateReturnPose()
        {
            cachedTransform.position = Vector3.MoveTowards(
                cachedTransform.position,
                homePosition,
                returnSpeed * Time.deltaTime);
            cachedTransform.rotation = Quaternion.RotateTowards(
                cachedTransform.rotation,
                homeRotation,
                returnRotationSpeed * Time.deltaTime);
            SyncKinematicBody();

            if ((cachedTransform.position - homePosition).sqrMagnitude > 0.0001f
                || Quaternion.Angle(cachedTransform.rotation, homeRotation) > 0.25f)
            {
                return;
            }

            cachedTransform.SetPositionAndRotation(homePosition, homeRotation);
            FinishPresentation();
        }

        private void StopPresentation(bool returnHome)
        {
            SetBandsVisible(false);
            HideLauncher();
            SetAnimation(idleAnimationValue);

            if (returnHome && hasHomePose)
            {
                cachedTransform.SetPositionAndRotation(homePosition, homeRotation);
                SyncKinematicBody();
            }

            FinishPresentation();
        }

        private void FinishPresentation()
        {
            HideLauncher();
            DisableAimCameraFocus();
            activePiece = null;
            releaseTimer = 0f;
            currentNormalizedPower = 0f;
            positionVelocity = Vector3.zero;
            SetState(PresentationState.Idle);
            RestoreCharacterPhysics();
        }

        private void OverrideCharacterPhysics()
        {
            if (physicsOverridden)
            {
                return;
            }

            physicsOverridden = true;
            if (movementController != null)
            {
                controllerWasEnabled = movementController.enabled;
                movementController.enabled = false;
            }

            if (cachedRigidbody != null)
            {
                bodyWasKinematic = cachedRigidbody.isKinematic;
                bodyUsedGravity = cachedRigidbody.useGravity;
                bodyConstraints = cachedRigidbody.constraints;
                bodyCollisionMode = cachedRigidbody.collisionDetectionMode;

                if (!cachedRigidbody.isKinematic)
                {
                    cachedRigidbody.linearVelocity = Vector3.zero;
                    cachedRigidbody.angularVelocity = Vector3.zero;
                }

                cachedRigidbody.useGravity = false;
                cachedRigidbody.isKinematic = true;
                cachedRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            if (!disableCollidersDuringPresentation || cachedColliders == null)
            {
                return;
            }

            colliderEnabledBeforePresentation = new bool[cachedColliders.Length];
            for (int i = 0; i < cachedColliders.Length; i++)
            {
                Collider characterCollider = cachedColliders[i];
                if (characterCollider == null)
                {
                    continue;
                }

                colliderEnabledBeforePresentation[i] = characterCollider.enabled;
                characterCollider.enabled = false;
            }
        }

        private void RestoreCharacterPhysics()
        {
            if (!physicsOverridden)
            {
                return;
            }

            if (cachedRigidbody != null)
            {
                cachedRigidbody.position = cachedTransform.position;
                cachedRigidbody.rotation = cachedTransform.rotation;
                cachedRigidbody.constraints = bodyConstraints;
                cachedRigidbody.useGravity = bodyUsedGravity;
                cachedRigidbody.isKinematic = bodyWasKinematic;
                cachedRigidbody.collisionDetectionMode = bodyCollisionMode;
                if (!cachedRigidbody.isKinematic)
                {
                    cachedRigidbody.linearVelocity = Vector3.zero;
                    cachedRigidbody.angularVelocity = Vector3.zero;
                }
            }

            if (disableCollidersDuringPresentation
                && cachedColliders != null
                && colliderEnabledBeforePresentation != null)
            {
                int restoreCount = Mathf.Min(cachedColliders.Length, colliderEnabledBeforePresentation.Length);
                for (int i = 0; i < restoreCount; i++)
                {
                    if (cachedColliders[i] != null)
                    {
                        cachedColliders[i].enabled = colliderEnabledBeforePresentation[i];
                    }
                }
            }

            if (movementController != null)
            {
                movementController.enabled = controllerWasEnabled;
            }

            colliderEnabledBeforePresentation = null;
            physicsOverridden = false;
        }

        private void SyncKinematicBody()
        {
            if (cachedRigidbody == null || !cachedRigidbody.isKinematic)
            {
                return;
            }

            cachedRigidbody.position = cachedTransform.position;
            cachedRigidbody.rotation = cachedTransform.rotation;
        }

        private Vector3 GetInitialFacingDirection(TurnBasedFlickPiece piece)
        {
            Vector3 direction = piece.transform.position - cachedTransform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= MinDirectionSqr)
            {
                direction = cachedTransform.forward;
                direction.y = 0f;
            }

            return direction.normalized;
        }

        private bool CanPresentPiece(TurnBasedFlickPiece piece)
        {
            return enableFlickPresentation
                && piece != null
                && (reactToAllPlayers || piece.Owner == owner);
        }

        private void EnableAimCameraFocus()
        {
            if (cameraFollow == null)
            {
                if (aimCamera == null)
                {
                    aimCamera = Camera.main;
                }

                if (aimCamera != null)
                {
                    cameraFollow = aimCamera.GetComponent<MonkeyThirdPersonCameraFollow>();
                }
            }

            if (cameraFollow == null || activePiece == null)
            {
                return;
            }

            cameraFollow.EnableAimFocus(
                cachedTransform,
                aimCameraLocalOffset,
                activePiece.transform,
                aimLookTargetOffset);
        }

        private void DisableAimCameraFocus()
        {
            if (cameraFollow != null)
            {
                cameraFollow.DisableAimFocus();
            }
        }

        private void RefreshAnimationParameterHash()
        {
            animationParameterHash = string.IsNullOrWhiteSpace(animationParameter)
                ? DefaultAnimationParameterHash
                : Animator.StringToHash(animationParameter);
            currentAnimationValue = int.MinValue;
        }

        private void SetAnimation(int animationValue)
        {
            if (driveAnimator
                && animator != null
                && currentAnimationValue != animationValue)
            {
                animator.SetInteger(animationParameterHash, animationValue);
                currentAnimationValue = animationValue;
            }
        }

        private void SetState(PresentationState nextState)
        {
            state = nextState;
        }

        private void EnsurePullBands()
        {
            if (!showPullBands || leftBand != null || rightBand != null)
            {
                return;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader != null)
            {
                bandMaterial = new Material(shader);
                bandMaterial.color = bandColor;
            }

            leftBand = CreateBandRenderer("Slingshot Band Left");
            rightBand = CreateBandRenderer("Slingshot Band Right");
        }

        private LineRenderer CreateBandRenderer(string objectName)
        {
            GameObject bandObject = new GameObject(objectName);
            bandObject.transform.SetParent(cachedTransform, false);
            LineRenderer line = bandObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = bandWidth;
            line.endWidth = bandWidth;
            line.startColor = bandColor;
            line.endColor = bandColor;
            line.sharedMaterial = bandMaterial;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        private void SetBandsVisible(bool visible)
        {
            if (launcherRig != null && launcherRig.HasDeformableBands)
            {
                visible = false;
            }

            if (visible)
            {
                EnsurePullBands();
            }

            if (leftBand != null)
            {
                leftBand.enabled = visible;
            }

            if (rightBand != null)
            {
                rightBand.enabled = visible;
            }
        }

        private void UpdatePullBands()
        {
            if (activePiece == null
                || leftBand == null
                || rightBand == null
                || !leftBand.enabled)
            {
                return;
            }

            Vector3 gripCenter = cachedTransform.position + Vector3.up * gripHeight;
            Vector3 gripRight = cachedTransform.right * gripHalfWidth;
            Vector3 stonePoint = activePiece.transform.position + Vector3.up * stoneBandHeight;

            leftBand.SetPosition(0, gripCenter - gripRight);
            leftBand.SetPosition(1, stonePoint);
            rightBand.SetPosition(0, gripCenter + gripRight);
            rightBand.SetPosition(1, stonePoint);
        }

        private void BeginLauncherPresentation()
        {
            if (activePiece == null || !EnsureLauncherVisual())
            {
                return;
            }

            launcherInstance.SetActive(true);
            launcherTransform.localScale = launcherScale;
            Quaternion launcherRotation = Quaternion.LookRotation(
                launchDirection,
                Vector3.up) * Quaternion.Euler(launcherEulerOffset);
            Vector3 targetPouchCenter =
                activePiece.transform.position + launcherStoneCenterOffset;
            launcherStoneAnchorWorld = targetPouchCenter;
            hasLauncherStoneAnchor = true;

            launcherTransform.SetPositionAndRotation(targetPouchCenter, launcherRotation);
            if (launcherRig != null)
            {
                launcherRig.ResetPose();
                AlignLauncherFrame(targetPouchCenter);
                launcherRig.BeginPresentation(launchDirection);
                UpdateLauncherPose(1f);
            }
        }

        private void AlignLauncherFrame(Vector3 stoneCenter)
        {
            Vector3 frameAxis = launcherRig.FrameAxisWorld;
            frameAxis.y = 0f;
            Vector3 desiredFrameAxis = Vector3.Cross(Vector3.up, launchDirection);
            desiredFrameAxis.y = 0f;
            if (frameAxis.sqrMagnitude > MinDirectionSqr
                && desiredFrameAxis.sqrMagnitude > MinDirectionSqr)
            {
                float correctionAngle = Vector3.SignedAngle(
                    frameAxis,
                    desiredFrameAxis,
                    Vector3.up);
                launcherTransform.rotation =
                    Quaternion.AngleAxis(correctionAngle, Vector3.up)
                    * launcherTransform.rotation;
            }

            launcherTransform.position +=
                stoneCenter - launcherRig.RestPouchCenterWorld;

            Vector3 frameCenterOffset = stoneCenter - launcherRig.FrameCenterWorld;
            frameCenterOffset.y = 0f;
            launcherTransform.position += frameCenterOffset;
        }

        private void UpdateLauncherPose(float pullBlend)
        {
            if (launcherRig == null
                || launcherInstance == null
                || !launcherInstance.activeSelf)
            {
                return;
            }

            Vector3 stoneCenter = hasLauncherStoneAnchor
                ? launcherStoneAnchorWorld
                : launcherRig.RestPouchCenterWorld;
            Vector3 monkeyGrip = cachedTransform.position
                + Vector3.up * gripHeight
                + cachedTransform.forward * launcherGripForwardOffset;
            launcherRig.SetPouchTarget(
                launchDirection,
                Vector3.Lerp(stoneCenter, monkeyGrip, Mathf.Clamp01(pullBlend)));
        }

        private bool EnsureLauncherVisual()
        {
            if (launcherInstance != null)
            {
                return true;
            }

            if (launcherPrefab == null)
            {
                return false;
            }

            launcherInstance = Instantiate(launcherPrefab);
            launcherInstance.name = launcherPrefab.name + " (Runtime Launcher)";
            launcherTransform = launcherInstance.transform;
            launcherRig = launcherInstance.GetComponent<SlingshotMeshVisualRig>();
            if (launcherRig == null)
            {
                launcherRig = launcherInstance.AddComponent<SlingshotMeshVisualRig>();
            }

            if (!launcherRig.TryInitialize(launcherMaterial))
            {
                launcherRig = null;
            }

            launcherInstance.SetActive(false);
            return true;
        }

        private void HideLauncher()
        {
            hasLauncherStoneAnchor = false;

            if (launcherRig != null)
            {
                launcherRig.ResetPose();
            }

            if (launcherInstance != null)
            {
                launcherInstance.SetActive(false);
            }
        }
    }
}
