using UnityEngine;
using UnityEngine.InputSystem;

namespace FlickDom.Gameplay
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class MonkeyThirdPersonController : MonoBehaviour
    {
        private enum LocomotionAnimationState
        {
            Idle = 0,
            Walk = 1,
            Run = 2
        }

        private const float MinInputMagnitude = 0.01f;
        private static readonly int AnimationParameterHash = Animator.StringToHash("animation");

        [Header("Ownership")]
        [SerializeField] private FlickDomPlayerId owner = FlickDomPlayerId.Player1;
        [SerializeField] private bool inputEnabled = true;
        [SerializeField] private bool requireActivePlayer;
        [SerializeField] private GameModeManager gameModeManager;

        [Header("Movement")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float walkSpeed = 2.6f;
        [SerializeField] private float sprintSpeed = 4.2f;
        [SerializeField] private float rotationSpeed = 540f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private bool driveAnimator = true;
        [SerializeField] private bool useAnimationStates;
        [SerializeField] private string idleAnimationState = "IdleA";
        [SerializeField] private string walkAnimationState = "Walk";
        [SerializeField] private string runAnimationState = "Run";
        [SerializeField] private float animationCrossFadeSeconds = 0.12f;
        [SerializeField] private bool useAnimationIntParameter = true;
        [SerializeField] private int idleAnimationValue = 1;
        [SerializeField] private int walkAnimationValue = 21;
        [SerializeField] private int runAnimationValue = 18;

        private Rigidbody cachedRigidbody;
        private Transform cachedTransform;
        private Vector2 movementInput;
        private Vector3 cameraForward;
        private Vector3 cameraRight;
        private Vector3 desiredMoveDirection;
        private Vector3 targetVelocity;
        private int idleStateHash;
        private int walkStateHash;
        private int runStateHash;
        private bool hasIdleState;
        private bool hasWalkState;
        private bool hasRunState;
        private LocomotionAnimationState currentAnimationState = (LocomotionAnimationState)(-1);
        private bool sprintHeld;

        public FlickDomPlayerId Owner
        {
            get { return owner; }
        }

        public Vector3 MoveDirection
        {
            get { return desiredMoveDirection; }
        }

        private void Reset()
        {
            if (TryGetComponent(out Rigidbody body))
            {
                cachedRigidbody = body;
                ConfigureRigidbody(cachedRigidbody);
            }

            if (TryGetComponent(out CapsuleCollider capsuleCollider))
            {
                ConfigureCapsule(capsuleCollider);
            }

            animator = GetComponentInChildren<Animator>();
        }

        private void Awake()
        {
            cachedTransform = transform;
            cachedRigidbody = GetComponent<Rigidbody>();
            ConfigureRigidbody(cachedRigidbody);

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            ApplyLegacySuriyunAnimationDefaults();
            CacheAnimationStates();
        }

        private void OnValidate()
        {
            walkSpeed = Mathf.Max(0f, walkSpeed);
            sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            animationCrossFadeSeconds = Mathf.Max(0f, animationCrossFadeSeconds);
        }

        private void Update()
        {
            ReadMovementInput();
            UpdateAnimationState();
        }

        private void FixedUpdate()
        {
            ApplyMovement();
        }

        private void OnDisable()
        {
            movementInput = Vector2.zero;
            desiredMoveDirection = Vector3.zero;

            if (cachedRigidbody)
            {
                Vector3 velocity = cachedRigidbody.linearVelocity;
                velocity.x = 0f;
                velocity.z = 0f;
                cachedRigidbody.linearVelocity = velocity;
            }
        }

        public void SetCameraTransform(Transform followCameraTransform)
        {
            cameraTransform = followCameraTransform;
        }

        public void SetOwner(FlickDomPlayerId playerId)
        {
            owner = playerId;
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
        }

        public void UseSuriyunStandingAnimationPreset()
        {
            useAnimationStates = false;
            useAnimationIntParameter = true;
            idleAnimationState = "IdleA";
            walkAnimationState = "Walk";
            runAnimationState = "Run";
            idleAnimationValue = 1;
            walkAnimationValue = 21;
            runAnimationValue = 18;
            CacheAnimationStates();
        }

        private void ApplyLegacySuriyunAnimationDefaults()
        {
            if (!useAnimationStates || useAnimationIntParameter || idleAnimationState != "IdleA")
            {
                return;
            }

            useAnimationStates = false;
            useAnimationIntParameter = true;
            walkAnimationState = "Walk";
            runAnimationState = "Run";
            idleAnimationValue = 1;
            walkAnimationValue = 21;
            runAnimationValue = 18;
        }

        private void ReadMovementInput()
        {
            movementInput = Vector2.zero;
            sprintHeld = false;

            if (!CanReadInput())
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
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

            movementInput.Set(horizontal, vertical);
            movementInput = Vector2.ClampMagnitude(movementInput, 1f);
            sprintHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }

        private bool CanReadInput()
        {
            if (!inputEnabled || Keyboard.current == null)
            {
                return false;
            }

            if (!requireActivePlayer || gameModeManager == null)
            {
                return true;
            }

            return gameModeManager.ActivePlayer == owner
                && gameModeManager.CurrentState != FlickDomGameState.PhysicsProcessing
                && gameModeManager.CurrentState != FlickDomGameState.CardMatch
                && gameModeManager.CurrentState != FlickDomGameState.RoundEnd;
        }

        private void ApplyMovement()
        {
            BuildCameraRelativeDirection();

            float speed = sprintHeld ? sprintSpeed : walkSpeed;
            Vector3 velocity = cachedRigidbody.linearVelocity;
            targetVelocity.Set(
                desiredMoveDirection.x * speed,
                velocity.y,
                desiredMoveDirection.z * speed);
            cachedRigidbody.linearVelocity = targetVelocity;

            if (desiredMoveDirection.sqrMagnitude <= MinInputMagnitude)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(desiredMoveDirection, Vector3.up);
            Quaternion nextRotation = Quaternion.RotateTowards(
                cachedRigidbody.rotation,
                targetRotation,
                rotationSpeed * Time.fixedDeltaTime);
            cachedRigidbody.MoveRotation(nextRotation);
        }

        private void BuildCameraRelativeDirection()
        {
            Transform referenceTransform = cameraTransform ? cameraTransform : cachedTransform;

            cameraForward = referenceTransform.forward;
            cameraForward.y = 0f;
            if (cameraForward.sqrMagnitude <= MinInputMagnitude)
            {
                cameraForward = cachedTransform.forward;
                cameraForward.y = 0f;
            }

            cameraForward.Normalize();

            cameraRight = referenceTransform.right;
            cameraRight.y = 0f;
            if (cameraRight.sqrMagnitude <= MinInputMagnitude)
            {
                cameraRight = cachedTransform.right;
                cameraRight.y = 0f;
            }

            cameraRight.Normalize();

            desiredMoveDirection = cameraForward * movementInput.y + cameraRight * movementInput.x;
            if (desiredMoveDirection.sqrMagnitude > 1f)
            {
                desiredMoveDirection.Normalize();
            }
        }

        private void UpdateAnimationState()
        {
            if (!driveAnimator || animator == null)
            {
                return;
            }

            LocomotionAnimationState nextState = GetLocomotionAnimationState();
            if (nextState == currentAnimationState)
            {
                return;
            }

            currentAnimationState = nextState;

            if (useAnimationStates)
            {
                int stateHash = GetAnimationStateHash(nextState);
                bool hasState = HasAnimationState(nextState);
                if (hasState)
                {
                    animator.CrossFade(stateHash, animationCrossFadeSeconds);
                }
            }

            if (useAnimationIntParameter)
            {
                animator.SetInteger(AnimationParameterHash, GetAnimationParameterValue(nextState));
            }
        }

        private LocomotionAnimationState GetLocomotionAnimationState()
        {
            if (movementInput.sqrMagnitude <= MinInputMagnitude)
            {
                return LocomotionAnimationState.Idle;
            }

            return sprintHeld ? LocomotionAnimationState.Run : LocomotionAnimationState.Walk;
        }

        private void CacheAnimationStates()
        {
            idleStateHash = Animator.StringToHash("Base Layer." + idleAnimationState);
            walkStateHash = Animator.StringToHash("Base Layer." + walkAnimationState);
            runStateHash = Animator.StringToHash("Base Layer." + runAnimationState);

            if (animator == null)
            {
                hasIdleState = false;
                hasWalkState = false;
                hasRunState = false;
                return;
            }

            hasIdleState = animator.HasState(0, idleStateHash);
            hasWalkState = animator.HasState(0, walkStateHash);
            hasRunState = animator.HasState(0, runStateHash);
        }

        private int GetAnimationStateHash(LocomotionAnimationState animationState)
        {
            switch (animationState)
            {
                case LocomotionAnimationState.Walk:
                    return walkStateHash;
                case LocomotionAnimationState.Run:
                    return runStateHash;
                default:
                    return idleStateHash;
            }
        }

        private bool HasAnimationState(LocomotionAnimationState animationState)
        {
            switch (animationState)
            {
                case LocomotionAnimationState.Walk:
                    return hasWalkState;
                case LocomotionAnimationState.Run:
                    return hasRunState;
                default:
                    return hasIdleState;
            }
        }

        private int GetAnimationParameterValue(LocomotionAnimationState animationState)
        {
            switch (animationState)
            {
                case LocomotionAnimationState.Walk:
                    return walkAnimationValue;
                case LocomotionAnimationState.Run:
                    return runAnimationValue;
                default:
                    return idleAnimationValue;
            }
        }

        private static void ConfigureRigidbody(Rigidbody body)
        {
            if (!body)
            {
                return;
            }

            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        private static void ConfigureCapsule(CapsuleCollider capsuleCollider)
        {
            if (!capsuleCollider)
            {
                return;
            }

            capsuleCollider.center = new Vector3(0f, 0.55f, 0f);
            capsuleCollider.height = 1.1f;
            capsuleCollider.radius = 0.35f;
            capsuleCollider.direction = 1;
        }
    }
}
