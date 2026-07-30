using System;
using UnityEngine;

namespace FlickDom.Gameplay
{
    /// <summary>
    /// Drives the separated Pouch, LeftBand and RightBand meshes of the imported
    /// slingshot. The frame remains stationary while the pouch and bands follow
    /// the current launch direction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlingshotMeshVisualRig : MonoBehaviour
    {
        private const float MinDirectionSqr = 0.0001f;
        private const float EndpointSlice = 0.12f;

        private Transform pouch;
        private Transform pouchPivot;
        private Renderer pouchRenderer;
        private BandDeformer leftBand;
        private BandDeformer rightBand;
        private Vector3 restPouchCenterRoot;
        private Quaternion restPouchPivotLocalRotation;
        private Vector3 initialLaunchDirection;
        private bool initialized;

        public bool HasDeformableBands
        {
            get { return initialized && leftBand != null && rightBand != null; }
        }

        public Vector3 PouchCenterWorld
        {
            get
            {
                return pouchRenderer != null
                    ? pouchRenderer.bounds.center
                    : transform.TransformPoint(restPouchCenterRoot);
            }
        }

        public Vector3 RestPouchCenterWorld
        {
            get { return transform.TransformPoint(restPouchCenterRoot); }
        }

        public Vector3 FrameCenterWorld
        {
            get
            {
                if (leftBand == null || rightBand == null)
                {
                    return transform.position;
                }

                Vector3 centerRoot =
                    (leftBand.FrameAnchorRoot + rightBand.FrameAnchorRoot) * 0.5f;
                return transform.TransformPoint(centerRoot);
            }
        }

        public Vector3 FrameAxisWorld
        {
            get
            {
                if (leftBand == null || rightBand == null)
                {
                    return transform.right;
                }

                Vector3 axisRoot =
                    rightBand.FrameAnchorRoot - leftBand.FrameAnchorRoot;
                return axisRoot.sqrMagnitude > MinDirectionSqr
                    ? transform.TransformVector(axisRoot).normalized
                    : transform.right;
            }
        }

        public bool TryInitialize(Material materialOverride)
        {
            if (initialized)
            {
                ApplyMaterialOverride(materialOverride);
                return pouchPivot != null;
            }

            pouch = FindDescendant(transform, "Pouch");
            Transform leftBandTransform = FindDescendant(transform, "LeftBand");
            Transform rightBandTransform = FindDescendant(transform, "RightBand");
            if (pouch == null || leftBandTransform == null || rightBandTransform == null)
            {
                Debug.LogWarning(
                    "Slingshot rig requires children named Pouch, LeftBand and RightBand.",
                    this);
                return false;
            }

            pouchRenderer = pouch.GetComponentInChildren<Renderer>(true);
            if (pouchRenderer == null)
            {
                Debug.LogWarning("Slingshot Pouch does not contain a Renderer.", this);
                return false;
            }

            DisableColliders();
            ApplyMaterialOverride(materialOverride);

            restPouchCenterRoot = transform.InverseTransformPoint(pouchRenderer.bounds.center);
            CreatePouchPivot();

            leftBand = BandDeformer.TryCreate(
                transform,
                leftBandTransform,
                restPouchCenterRoot,
                EndpointSlice);
            rightBand = BandDeformer.TryCreate(
                transform,
                rightBandTransform,
                restPouchCenterRoot,
                EndpointSlice);

            initialized = true;
            ResetPose();
            return true;
        }

        public void BeginPresentation(Vector3 worldLaunchDirection)
        {
            if (!initialized)
            {
                return;
            }

            initialLaunchDirection = FlattenDirection(worldLaunchDirection, transform.forward);
            ResetPose();
        }

        public void SetPose(
            Vector3 worldLaunchDirection,
            float pullDistance,
            float normalizedPower)
        {
            if (!initialized || pouchPivot == null)
            {
                return;
            }

            Vector3 currentDirection = FlattenDirection(
                worldLaunchDirection,
                initialLaunchDirection);
            float power = Mathf.Clamp01(normalizedPower);
            Vector3 pullWorld = -currentDirection * Mathf.Max(0f, pullDistance) * power;
            SetPouchTarget(
                currentDirection,
                transform.TransformPoint(restPouchCenterRoot) + pullWorld);
        }

        public void SetPouchTarget(
            Vector3 worldLaunchDirection,
            Vector3 targetPouchCenterWorld)
        {
            if (!initialized || pouchPivot == null)
            {
                return;
            }

            Vector3 currentDirection = FlattenDirection(
                worldLaunchDirection,
                initialLaunchDirection);
            Vector3 targetPouchCenterRoot =
                transform.InverseTransformPoint(targetPouchCenterWorld);

            Quaternion aimDeltaWorld = Quaternion.FromToRotation(
                initialLaunchDirection,
                currentDirection);
            Quaternion aimDeltaRoot = Quaternion.Inverse(transform.rotation)
                * aimDeltaWorld
                * transform.rotation;

            pouchPivot.position = transform.TransformPoint(targetPouchCenterRoot);
            pouchPivot.localRotation = aimDeltaRoot * restPouchPivotLocalRotation;

            if (leftBand != null && rightBand != null)
            {
                Vector3 leftPouchAnchor = targetPouchCenterRoot
                    + aimDeltaRoot
                    * (leftBand.PouchAnchorRoot - restPouchCenterRoot);
                Vector3 rightPouchAnchor = targetPouchCenterRoot
                    + aimDeltaRoot
                    * (rightBand.PouchAnchorRoot - restPouchCenterRoot);

                float directDistance =
                    (leftBand.FrameAnchorRoot - leftPouchAnchor).sqrMagnitude
                    + (rightBand.FrameAnchorRoot - rightPouchAnchor).sqrMagnitude;
                float crossedDistance =
                    (leftBand.FrameAnchorRoot - rightPouchAnchor).sqrMagnitude
                    + (rightBand.FrameAnchorRoot - leftPouchAnchor).sqrMagnitude;
                if (crossedDistance < directDistance)
                {
                    (leftPouchAnchor, rightPouchAnchor) =
                        (rightPouchAnchor, leftPouchAnchor);
                }

                leftBand.SetPouchAnchor(leftPouchAnchor);
                rightBand.SetPouchAnchor(rightPouchAnchor);
            }
            else
            {
                leftBand?.SetPouchPose(
                    restPouchCenterRoot,
                    targetPouchCenterRoot,
                    aimDeltaRoot);
                rightBand?.SetPouchPose(
                    restPouchCenterRoot,
                    targetPouchCenterRoot,
                    aimDeltaRoot);
            }
        }

        public void ResetPose()
        {
            if (!initialized || pouchPivot == null)
            {
                return;
            }

            pouchPivot.position = transform.TransformPoint(restPouchCenterRoot);
            pouchPivot.localRotation = restPouchPivotLocalRotation;
            leftBand?.ResetPose();
            rightBand?.ResetPose();
        }

        private void OnDestroy()
        {
            leftBand?.Dispose();
            rightBand?.Dispose();
        }

        private void CreatePouchPivot()
        {
            Transform originalParent = pouch.parent;
            GameObject pivotObject = new GameObject("Pouch Runtime Pivot");
            pouchPivot = pivotObject.transform;
            pouchPivot.SetParent(originalParent, false);
            pouchPivot.position = transform.TransformPoint(restPouchCenterRoot);
            pouchPivot.rotation = originalParent != null
                ? originalParent.rotation
                : transform.rotation;
            pouchPivot.localScale = Vector3.one;
            pouch.SetParent(pouchPivot, true);
            restPouchPivotLocalRotation = pouchPivot.localRotation;
        }

        private void DisableColliders()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        private void ApplyMaterialOverride(Material materialOverride)
        {
            if (materialOverride == null)
            {
                return;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    materials[materialIndex] = materialOverride;
                }

                renderers[i].sharedMaterials = materials;
            }
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                if (string.Equals(
                    descendants[i].name,
                    objectName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return descendants[i];
                }
            }

            return null;
        }

        private static Vector3 FlattenDirection(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= MinDirectionSqr)
            {
                direction = fallback;
                direction.y = 0f;
            }

            return direction.sqrMagnitude > MinDirectionSqr
                ? direction.normalized
                : Vector3.forward;
        }

        private sealed class BandDeformer : IDisposable
        {
            private readonly Transform root;
            private readonly MeshFilter meshFilter;
            private readonly Mesh runtimeMesh;
            private readonly Vector3[] restVerticesRoot;
            private readonly Vector3[] restNormalsRoot;
            private readonly Vector3[] deformedVertices;
            private readonly Vector3[] deformedNormals;
            private readonly float[] normalizedDistances;
            private readonly Vector3 frameAnchorRoot;
            private readonly Vector3 pouchAnchorRoot;
            private readonly Matrix4x4 rootToMesh;
            private readonly Matrix4x4 rootToMeshNormal;

            public Vector3 FrameAnchorRoot
            {
                get { return frameAnchorRoot; }
            }

            public Vector3 PouchAnchorRoot
            {
                get { return pouchAnchorRoot; }
            }

            private BandDeformer(
                Transform root,
                MeshFilter meshFilter,
                Mesh runtimeMesh,
                Vector3[] restVerticesRoot,
                Vector3[] restNormalsRoot,
                Vector3[] deformedVertices,
                Vector3[] deformedNormals,
                float[] normalizedDistances,
                Vector3 frameAnchorRoot,
                Vector3 pouchAnchorRoot,
                Matrix4x4 rootToMesh,
                Matrix4x4 rootToMeshNormal)
            {
                this.root = root;
                this.meshFilter = meshFilter;
                this.runtimeMesh = runtimeMesh;
                this.restVerticesRoot = restVerticesRoot;
                this.restNormalsRoot = restNormalsRoot;
                this.deformedVertices = deformedVertices;
                this.deformedNormals = deformedNormals;
                this.normalizedDistances = normalizedDistances;
                this.frameAnchorRoot = frameAnchorRoot;
                this.pouchAnchorRoot = pouchAnchorRoot;
                this.rootToMesh = rootToMesh;
                this.rootToMeshNormal = rootToMeshNormal;
            }

            public static BandDeformer TryCreate(
                Transform root,
                Transform bandTransform,
                Vector3 pouchCenterRoot,
                float endpointSlice)
            {
                MeshFilter meshFilter = bandTransform.GetComponentInChildren<MeshFilter>(true);
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    Debug.LogWarning(
                        $"Slingshot band '{bandTransform.name}' does not contain a readable MeshFilter.",
                        bandTransform);
                    return null;
                }

                Mesh sourceMesh = meshFilter.sharedMesh;
                Vector3[] sourceVertices;
                Vector3[] sourceNormals;
                try
                {
                    sourceVertices = sourceMesh.vertices;
                    sourceNormals = sourceMesh.normals;
                }
                catch (UnityException exception)
                {
                    Debug.LogWarning(
                        $"Slingshot band mesh must have Read/Write enabled. {exception.Message}",
                        bandTransform);
                    return null;
                }

                if (sourceVertices.Length == 0)
                {
                    return null;
                }

                Mesh runtimeMesh = UnityEngine.Object.Instantiate(sourceMesh);
                runtimeMesh.name = sourceMesh.name + " (Runtime Deformed)";
                runtimeMesh.MarkDynamic();
                meshFilter.sharedMesh = runtimeMesh;

                Matrix4x4 meshToRoot = root.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
                Matrix4x4 meshToRootNormal = meshToRoot.inverse.transpose;
                Matrix4x4 rootToMesh = meshFilter.transform.worldToLocalMatrix * root.localToWorldMatrix;
                Matrix4x4 rootToMeshNormal = rootToMesh.inverse.transpose;

                Vector3[] restVerticesRoot = new Vector3[sourceVertices.Length];
                Vector3[] restNormalsRoot = new Vector3[sourceVertices.Length];
                Vector3 centerRoot = Vector3.zero;
                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    restVerticesRoot[i] = meshToRoot.MultiplyPoint3x4(sourceVertices[i]);
                    centerRoot += restVerticesRoot[i];
                    restNormalsRoot[i] = sourceNormals.Length == sourceVertices.Length
                        ? meshToRootNormal.MultiplyVector(sourceNormals[i]).normalized
                        : Vector3.up;
                }

                centerRoot /= sourceVertices.Length;
                Vector3 projectionDirection = pouchCenterRoot - centerRoot;
                if (projectionDirection.sqrMagnitude <= MinDirectionSqr)
                {
                    projectionDirection = Vector3.forward;
                }

                projectionDirection.Normalize();
                float minProjection = float.PositiveInfinity;
                float maxProjection = float.NegativeInfinity;
                for (int i = 0; i < restVerticesRoot.Length; i++)
                {
                    float projection = Vector3.Dot(restVerticesRoot[i], projectionDirection);
                    minProjection = Mathf.Min(minProjection, projection);
                    maxProjection = Mathf.Max(maxProjection, projection);
                }

                float projectionRange = Mathf.Max(0.0001f, maxProjection - minProjection);
                float sliceWidth = projectionRange * Mathf.Clamp(endpointSlice, 0.02f, 0.35f);
                Vector3 frameAnchor = Vector3.zero;
                Vector3 pouchAnchor = Vector3.zero;
                int frameCount = 0;
                int pouchCount = 0;
                float[] normalizedDistances = new float[restVerticesRoot.Length];
                for (int i = 0; i < restVerticesRoot.Length; i++)
                {
                    float projection = Vector3.Dot(restVerticesRoot[i], projectionDirection);
                    normalizedDistances[i] = Mathf.InverseLerp(
                        minProjection,
                        maxProjection,
                        projection);
                    if (projection <= minProjection + sliceWidth)
                    {
                        frameAnchor += restVerticesRoot[i];
                        frameCount++;
                    }

                    if (projection >= maxProjection - sliceWidth)
                    {
                        pouchAnchor += restVerticesRoot[i];
                        pouchCount++;
                    }
                }

                frameAnchor = frameCount > 0 ? frameAnchor / frameCount : centerRoot;
                pouchAnchor = pouchCount > 0 ? pouchAnchor / pouchCount : pouchCenterRoot;
                return new BandDeformer(
                    root,
                    meshFilter,
                    runtimeMesh,
                    restVerticesRoot,
                    restNormalsRoot,
                    new Vector3[sourceVertices.Length],
                    new Vector3[sourceVertices.Length],
                    normalizedDistances,
                    frameAnchor,
                    pouchAnchor,
                    rootToMesh,
                    rootToMeshNormal);
            }

            public void SetPouchPose(
                Vector3 restPouchCenterRoot,
                Vector3 targetPouchCenterRoot,
                Quaternion pouchRotationRoot)
            {
                Vector3 targetPouchAnchor = targetPouchCenterRoot
                    + pouchRotationRoot * (pouchAnchorRoot - restPouchCenterRoot);
                SetPouchAnchor(targetPouchAnchor);
            }

            public void SetPouchAnchor(Vector3 targetPouchAnchor)
            {
                if (runtimeMesh == null || meshFilter == null || root == null)
                {
                    return;
                }

                Vector3 restSegment = pouchAnchorRoot - frameAnchorRoot;
                Vector3 targetSegment = targetPouchAnchor - frameAnchorRoot;
                Quaternion segmentRotation = restSegment.sqrMagnitude > MinDirectionSqr
                    && targetSegment.sqrMagnitude > MinDirectionSqr
                    ? Quaternion.FromToRotation(restSegment, targetSegment)
                    : Quaternion.identity;

                for (int i = 0; i < restVerticesRoot.Length; i++)
                {
                    float t = normalizedDistances[i];
                    Vector3 restCenterLine = Vector3.Lerp(frameAnchorRoot, pouchAnchorRoot, t);
                    Vector3 targetCenterLine = Vector3.Lerp(frameAnchorRoot, targetPouchAnchor, t);
                    Vector3 residual = restVerticesRoot[i] - restCenterLine;
                    Quaternion localRotation = Quaternion.Slerp(
                        Quaternion.identity,
                        segmentRotation,
                        t);
                    Vector3 targetRootVertex = targetCenterLine + localRotation * residual;
                    deformedVertices[i] = rootToMesh.MultiplyPoint3x4(targetRootVertex);
                    deformedNormals[i] = rootToMeshNormal.MultiplyVector(
                        localRotation * restNormalsRoot[i]).normalized;
                }

                runtimeMesh.vertices = deformedVertices;
                runtimeMesh.normals = deformedNormals;
                runtimeMesh.RecalculateBounds();
            }

            public void ResetPose()
            {
                SetPouchAnchor(pouchAnchorRoot);
            }

            public void Dispose()
            {
                if (runtimeMesh != null)
                {
                    UnityEngine.Object.Destroy(runtimeMesh);
                }
            }
        }
    }
}
