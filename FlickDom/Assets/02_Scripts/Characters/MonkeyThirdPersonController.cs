using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FlickDom.Networking;

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
        private const float NetworkInputSendInterval = 0.02f;
        private const float NetworkInputTimeoutSeconds = 0.25f;
        private const float NetworkInputChangeSqrThreshold = 0.0004f;
        private static readonly int AnimationParameterHash = Animator.StringToHash("animation");

        [Header("Ownership")]
        [SerializeField] private FlickDomPlayerId owner = FlickDomPlayerId.Player1;
        [SerializeField] private bool inputEnabled = true;
        [SerializeField] private bool requireActivePlayer;
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private bool autoSetupSlingshotPresenter = true;
        [SerializeField] private MonkeySlingshotFlickPresenter slingshotPresenter;
        [SerializeField] private GameObject slingshotLauncherPrefab;
        [SerializeField] private Material slingshotLauncherMaterial;
        [SerializeField] private Vector3 slingshotLauncherScale = new Vector3(0.4f, 0.4f, 0.4f);
        [SerializeField] private Vector3 slingshotLauncherEulerOffset = new Vector3(0f, 90f, 0f);
        [SerializeField] private Vector3 slingshotLauncherStoneCenterOffset = Vector3.zero;
        [SerializeField] private float slingshotLauncherGripForwardOffset = 0.28f;

        [Header("Movement")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float walkSpeed = 2.6f;
        [SerializeField] private float sprintSpeed = 4.2f;
        [SerializeField] private float rotationSpeed = 540f;
        [SerializeField] private bool faceCameraDirectionWhenIdle = true;

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
        [SerializeField] private bool disableWebGlLocomotionAnimation;
        [SerializeField] private bool useWebGlMaterialFallback = true;

        private static readonly Dictionary<Material, Material> WebGlMaterialFallbacks =
            new Dictionary<Material, Material>();
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
        private Vector3 networkMoveDirection;
        private bool networkSprintHeld;
        private float networkInputExpiresAt;
        private Vector3 lastSubmittedNetworkMoveDirection;
        private bool lastSubmittedNetworkSprintHeld;
        private float nextNetworkInputSubmitTime;
        private bool allowLocalStandaloneInput = true;

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
            owner = ResolveOwnerFromSceneName(owner, gameObject.name);
            RefreshLocalStandaloneInputPolicy();
            cachedTransform = transform;
            cachedRigidbody = GetComponent<Rigidbody>();
            ConfigureRigidbody(cachedRigidbody);

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            ApplyLegacySuriyunAnimationDefaults();
            ApplyWebGlCompatibilityOverrides();
            CacheAnimationStates();
            SetupSlingshotPresenter();
        }

        private void OnValidate()
        {
            walkSpeed = Mathf.Max(0f, walkSpeed);
            sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            animationCrossFadeSeconds = Mathf.Max(0f, animationCrossFadeSeconds);
            slingshotLauncherScale.x = Mathf.Max(0.001f, slingshotLauncherScale.x);
            slingshotLauncherScale.y = Mathf.Max(0.001f, slingshotLauncherScale.y);
            slingshotLauncherScale.z = Mathf.Max(0.001f, slingshotLauncherScale.z);
            slingshotLauncherGripForwardOffset = Mathf.Max(0f, slingshotLauncherGripForwardOffset);
        }

        private void Update()
        {
            ReadMovementInput();
            SubmitNetworkMovementInputIfNeeded();
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
            owner = NormalizeOwner(playerId);
            RefreshLocalStandaloneInputPolicy();

            if (slingshotPresenter != null)
            {
                slingshotPresenter.SetOwner(owner);
            }
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
        }

        public void ApplyNetworkMovementInput(Vector3 moveDirection, bool sprint)
        {
            moveDirection.y = 0f;
            if (moveDirection.sqrMagnitude > 1f)
            {
                moveDirection.Normalize();
            }

            networkMoveDirection = moveDirection;
            networkSprintHeld = sprint;
            networkInputExpiresAt = Time.time + NetworkInputTimeoutSeconds;
        }

        public void ApplyNetworkPose(Vector3 position, Quaternion rotation)
        {
            if (cachedTransform == null)
            {
                cachedTransform = transform;
            }

            if (cachedRigidbody == null)
            {
                cachedRigidbody = GetComponent<Rigidbody>();
            }

            cachedTransform.SetPositionAndRotation(position, rotation);
            if (cachedRigidbody != null)
            {
                cachedRigidbody.position = position;
                cachedRigidbody.rotation = rotation;
                cachedRigidbody.linearVelocity = Vector3.zero;
                cachedRigidbody.angularVelocity = Vector3.zero;
            }
        }

        public void ConfigureSlingshotLauncher(GameObject prefab, Material material)
        {
            ConfigureSlingshotLauncher(
                prefab,
                material,
                new Vector3(0.4f, 0.4f, 0.4f),
                new Vector3(0f, 90f, 0f),
                Vector3.zero,
                0.28f);
        }

        public void ConfigureSlingshotLauncher(
            GameObject prefab,
            Material material,
            Vector3 launcherScale,
            Vector3 launcherEulerOffset,
            Vector3 launcherStoneCenterOffset,
            float launcherGripForwardOffset)
        {
            slingshotLauncherPrefab = prefab;
            slingshotLauncherMaterial = material;
            slingshotLauncherScale = new Vector3(
                Mathf.Max(0.001f, launcherScale.x),
                Mathf.Max(0.001f, launcherScale.y),
                Mathf.Max(0.001f, launcherScale.z));
            slingshotLauncherEulerOffset = launcherEulerOffset;
            slingshotLauncherStoneCenterOffset = launcherStoneCenterOffset;
            slingshotLauncherGripForwardOffset = Mathf.Max(0f, launcherGripForwardOffset);
            if (slingshotPresenter != null)
            {
                slingshotPresenter.ConfigureLauncher(prefab, material);
                slingshotPresenter.ConfigureLauncherTransform(
                    slingshotLauncherScale,
                    slingshotLauncherEulerOffset,
                    slingshotLauncherStoneCenterOffset,
                    slingshotLauncherGripForwardOffset);
            }
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

            if (slingshotPresenter != null)
            {
                slingshotPresenter.UseSuriyunAnimationPreset();
            }
        }

        private void SetupSlingshotPresenter()
        {
            if (!autoSetupSlingshotPresenter)
            {
                return;
            }

            if (slingshotPresenter == null
                && !TryGetComponent(out slingshotPresenter))
            {
                slingshotPresenter = gameObject.AddComponent<MonkeySlingshotFlickPresenter>();
            }

            slingshotPresenter.SetOwner(owner);
            slingshotPresenter.ConfigureLauncher(
                slingshotLauncherPrefab,
                slingshotLauncherMaterial);
            slingshotPresenter.ConfigureLauncherTransform(
                slingshotLauncherScale,
                slingshotLauncherEulerOffset,
                slingshotLauncherStoneCenterOffset,
                slingshotLauncherGripForwardOffset);
            MonkeyThirdPersonController[] sceneMonkeys =
                FindObjectsByType<MonkeyThirdPersonController>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            slingshotPresenter.SetReactToAllPlayers(sceneMonkeys.Length <= 1);
            slingshotPresenter.UseSuriyunAnimationPreset();
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
#if UNITY_WEBGL && !UNITY_EDITOR
            sprintHeld = false;
#else
            sprintHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
#endif
        }

        private bool CanReadInput()
        {
            if (!inputEnabled || Keyboard.current == null)
            {
                return false;
            }

            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            if (bootstrap != null && !bootstrap.AllowsLocalInputFor(owner))
            {
                return false;
            }

            if (bootstrap != null && bootstrap.IsRunning)
            {
                if (!bootstrap.IsGameActive)
                {
                    return false;
                }

                if (gameModeManager == null)
                {
                    return true;
                }

                return gameModeManager.CurrentState != FlickDomGameState.PhysicsProcessing
                    && gameModeManager.CurrentState != FlickDomGameState.CardMatch
                    && gameModeManager.CurrentState != FlickDomGameState.RoundEnd;
            }

            if (!allowLocalStandaloneInput)
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
            if (ShouldUseNetworkPoseOnly())
            {
                StopHorizontalMovement();
                return;
            }

            if (HasActiveNetworkMovementInput())
            {
                desiredMoveDirection = networkMoveDirection;
                sprintHeld = networkSprintHeld;
            }
            else
            {
                BuildCameraRelativeDirection();
            }

            float speed = sprintHeld ? sprintSpeed : walkSpeed;
            Vector3 velocity = cachedRigidbody.linearVelocity;
            targetVelocity.Set(
                desiredMoveDirection.x * speed,
                velocity.y,
                desiredMoveDirection.z * speed);
            cachedRigidbody.linearVelocity = targetVelocity;

            Vector3 facingDirection = desiredMoveDirection;
            if (facingDirection.sqrMagnitude <= MinInputMagnitude && faceCameraDirectionWhenIdle)
            {
                facingDirection = cameraForward;
            }

            if (facingDirection.sqrMagnitude <= MinInputMagnitude)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
            Quaternion nextRotation = Quaternion.RotateTowards(
                cachedRigidbody.rotation,
                targetRotation,
                rotationSpeed * Time.fixedDeltaTime);
            cachedRigidbody.MoveRotation(nextRotation);
        }

        private void SubmitNetworkMovementInputIfNeeded()
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            if (bootstrap == null || !bootstrap.IsClientOnly || bootstrap.LocalPlayerId != owner)
            {
                return;
            }

            BuildCameraRelativeDirection();

            bool changed = (desiredMoveDirection - lastSubmittedNetworkMoveDirection).sqrMagnitude
                > NetworkInputChangeSqrThreshold
                || sprintHeld != lastSubmittedNetworkSprintHeld;
            bool hasMovement = desiredMoveDirection.sqrMagnitude > MinInputMagnitude
                || lastSubmittedNetworkMoveDirection.sqrMagnitude > MinInputMagnitude;

            if (!changed && (!hasMovement || Time.unscaledTime < nextNetworkInputSubmitTime))
            {
                return;
            }

            nextNetworkInputSubmitTime = Time.unscaledTime + NetworkInputSendInterval;
            lastSubmittedNetworkMoveDirection = desiredMoveDirection;
            lastSubmittedNetworkSprintHeld = sprintHeld;
            bootstrap.SubmitMonkeyMovementInputToHost(owner, desiredMoveDirection, sprintHeld);
        }

        private bool ShouldUseNetworkPoseOnly()
        {
            FlickDomNetworkBootstrap bootstrap = FlickDomNetworkBootstrap.Active;
            return bootstrap != null && bootstrap.IsClientOnly;
        }

        private bool HasActiveNetworkMovementInput()
        {
            return Time.time <= networkInputExpiresAt
                && networkMoveDirection.sqrMagnitude > MinInputMagnitude;
        }

        private void StopHorizontalMovement()
        {
            if (cachedRigidbody == null)
            {
                return;
            }

            Vector3 velocity = cachedRigidbody.linearVelocity;
            velocity.x = 0f;
            velocity.z = 0f;
            cachedRigidbody.linearVelocity = velocity;
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

            if (ShouldDisableWebGlLocomotionAnimation())
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
            bool hasMoveInput = movementInput.sqrMagnitude > MinInputMagnitude
                || HasActiveNetworkMovementInput();
            if (!hasMoveInput)
            {
                return LocomotionAnimationState.Idle;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            if (sprintHeld)
            {
                return LocomotionAnimationState.Walk;
            }
#endif

            return sprintHeld ? LocomotionAnimationState.Run : LocomotionAnimationState.Walk;
        }

        private void ApplyWebGlCompatibilityOverrides()
        {
            if (ShouldUseWebGlMaterialFallback())
            {
                ReplaceUnsupportedWebGlMaterials();
            }
        }

        private bool ShouldUseWebGlMaterialFallback()
        {
            return useWebGlMaterialFallback && IsWebGlRuntime();
        }

        private bool ShouldDisableWebGlLocomotionAnimation()
        {
            return disableWebGlLocomotionAnimation && IsWebGlRuntime();
        }

        private static bool IsWebGlRuntime()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        private void ReplaceUnsupportedWebGlMaterials()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer targetRenderer = renderers[rendererIndex];
                Material[] materials = targetRenderer.sharedMaterials;
                bool changed = false;

                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (!NeedsWebGlFallbackMaterial(material))
                    {
                        continue;
                    }

                    Material fallback = GetWebGlFallbackMaterial(material);
                    if (fallback == null || fallback == material)
                    {
                        continue;
                    }

                    materials[materialIndex] = fallback;
                    changed = true;
                }

                if (changed)
                {
                    targetRenderer.sharedMaterials = materials;
                }
            }
        }

        private static bool NeedsWebGlFallbackMaterial(Material material)
        {
            if (material == null || material.shader == null)
            {
                return false;
            }

            string shaderName = material.shader.name;
            return material.HasProperty("_isUnityToonshader")
                || shaderName.IndexOf("Toon", StringComparison.OrdinalIgnoreCase) >= 0
                || shaderName.IndexOf("UnityChanToonShader", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Material GetWebGlFallbackMaterial(Material source)
        {
            if (source == null)
            {
                return null;
            }

            if (WebGlMaterialFallbacks.TryGetValue(source, out Material cachedFallback))
            {
                return cachedFallback;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader == null)
            {
                WebGlMaterialFallbacks[source] = source;
                return source;
            }

            Material fallback = new Material(shader)
            {
                name = source.name + " WebGL Fallback"
            };

            Texture mainTexture = null;
            if (source.HasProperty("_MainTex"))
            {
                mainTexture = source.GetTexture("_MainTex");
            }
            else if (source.HasProperty("_BaseMap"))
            {
                mainTexture = source.GetTexture("_BaseMap");
            }

            if (mainTexture != null)
            {
                if (fallback.HasProperty("_BaseMap"))
                {
                    fallback.SetTexture("_BaseMap", mainTexture);
                }

                if (fallback.HasProperty("_MainTex"))
                {
                    fallback.SetTexture("_MainTex", mainTexture);
                }
            }

            Color baseColor = Color.white;
            if (source.HasProperty("_BaseColor"))
            {
                baseColor = source.GetColor("_BaseColor");
            }
            else if (source.HasProperty("_Color"))
            {
                baseColor = source.GetColor("_Color");
            }

            if (fallback.HasProperty("_BaseColor"))
            {
                fallback.SetColor("_BaseColor", baseColor);
            }

            if (fallback.HasProperty("_Color"))
            {
                fallback.SetColor("_Color", baseColor);
            }

            WebGlMaterialFallbacks[source] = fallback;
            return fallback;
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

        private static FlickDomPlayerId ResolveOwnerFromSceneName(FlickDomPlayerId fallbackOwner, string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return NormalizeOwner(fallbackOwner);
            }

            string lowerName = objectName.ToLowerInvariant();
            if (lowerName.Contains("player2") || lowerName.Contains("p2"))
            {
                return FlickDomPlayerId.Player2;
            }

            if (lowerName.Contains("player1") || lowerName.Contains("p1"))
            {
                return FlickDomPlayerId.Player1;
            }

            return NormalizeOwner(fallbackOwner);
        }

        private static FlickDomPlayerId NormalizeOwner(FlickDomPlayerId playerId)
        {
            return playerId == FlickDomPlayerId.Player2
                ? FlickDomPlayerId.Player2
                : FlickDomPlayerId.Player1;
        }

        private static bool HasMultiplePlayerMonkeys()
        {
            bool hasPlayer1 = false;
            bool hasPlayer2 = false;
            MonkeyThirdPersonController[] monkeys =
                FindObjectsByType<MonkeyThirdPersonController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < monkeys.Length; i++)
            {
                MonkeyThirdPersonController monkey = monkeys[i];
                if (monkey == null)
                {
                    continue;
                }

                if (monkey.Owner == FlickDomPlayerId.Player1)
                {
                    hasPlayer1 = true;
                }
                else if (monkey.Owner == FlickDomPlayerId.Player2)
                {
                    hasPlayer2 = true;
                }

                if (hasPlayer1 && hasPlayer2)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshLocalStandaloneInputPolicy()
        {
            allowLocalStandaloneInput = owner != FlickDomPlayerId.Player2 || !HasMultiplePlayerMonkeys();
        }
    }
}
