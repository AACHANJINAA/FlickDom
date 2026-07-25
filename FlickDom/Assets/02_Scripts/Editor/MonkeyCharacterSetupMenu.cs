using FlickDom.Gameplay;
using UnityEditor;
using UnityEngine;

namespace FlickDom.EditorTools
{
    public static class MonkeyCharacterSetupMenu
    {
        private const string MenuPath = "FlickDom/Characters/Setup Selected Monkey Controller";
        private const string RemovePlateMenuPath = "FlickDom/Characters/Remove Selected Monkey Strike Plate";
        private const string PlateObjectName = "Monkey Strike Plate";

        [MenuItem(MenuPath, true)]
        private static bool CanSetupSelectedMonkey()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }

        [MenuItem(MenuPath)]
        private static void SetupSelectedMonkey()
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            Camera sceneCamera = Camera.main;
            MonkeyThirdPersonCameraFollow cameraFollow = null;

            if (sceneCamera)
            {
                if (!sceneCamera.TryGetComponent(out cameraFollow))
                {
                    cameraFollow = Undo.AddComponent<MonkeyThirdPersonCameraFollow>(sceneCamera.gameObject);
                }
            }

            for (int i = 0; i < selectedObjects.Length; i++)
            {
                SetupMonkeyObject(selectedObjects[i], sceneCamera, cameraFollow, i);
            }

            Debug.Log("Selected monkey controller setup complete.");
        }

        private static void SetupMonkeyObject(
            GameObject monkeyObject,
            Camera sceneCamera,
            MonkeyThirdPersonCameraFollow cameraFollow,
            int selectionIndex)
        {
            if (!monkeyObject)
            {
                return;
            }

            Rigidbody body = GetOrAddComponent<Rigidbody>(monkeyObject);
            Undo.RecordObject(body, "Configure Monkey Rigidbody");
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            CapsuleCollider capsuleCollider = GetOrAddComponent<CapsuleCollider>(monkeyObject);
            Undo.RecordObject(capsuleCollider, "Configure Monkey Capsule");
            capsuleCollider.center = new Vector3(0f, 0.55f, 0f);
            capsuleCollider.height = 1.1f;
            capsuleCollider.radius = 0.35f;
            capsuleCollider.direction = 1;

            MonkeyThirdPersonController controller = GetOrAddComponent<MonkeyThirdPersonController>(monkeyObject);
            Undo.RecordObject(controller, "Configure Monkey Controller");
            controller.SetOwner(selectionIndex == 0 ? FlickDomPlayerId.Player1 : FlickDomPlayerId.Player2);
            controller.SetInputEnabled(selectionIndex == 0);
            controller.UseSuriyunStandingAnimationPreset();

            if (sceneCamera)
            {
                controller.SetCameraTransform(sceneCamera.transform);
            }

            RemoveStrikePlate(monkeyObject);

            if (selectionIndex == 0 && cameraFollow)
            {
                Undo.RecordObject(cameraFollow, "Configure Monkey Camera Follow");
                cameraFollow.SetTarget(monkeyObject.transform);
                EditorUtility.SetDirty(cameraFollow);
            }

            EditorUtility.SetDirty(body);
            EditorUtility.SetDirty(capsuleCollider);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(monkeyObject);
        }

        private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
        {
            if (gameObject.TryGetComponent(out T component))
            {
                return component;
            }

            return Undo.AddComponent<T>(gameObject);
        }

        [MenuItem(RemovePlateMenuPath, true)]
        private static bool CanRemoveSelectedMonkeyStrikePlate()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }

        [MenuItem(RemovePlateMenuPath)]
        private static void RemoveSelectedMonkeyStrikePlate()
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                RemoveStrikePlate(selectedObjects[i]);
            }

            Debug.Log("Selected monkey strike plate cleanup complete.");
        }

        private static void RemoveStrikePlate(GameObject monkeyObject)
        {
            if (!monkeyObject)
            {
                return;
            }

            if (monkeyObject.TryGetComponent(out MonkeyStrikePlate strikePlate))
            {
                Undo.DestroyObjectImmediate(strikePlate);
            }

            Transform existingPlate = monkeyObject.transform.Find(PlateObjectName);
            if (existingPlate)
            {
                Undo.DestroyObjectImmediate(existingPlate.gameObject);
            }
        }
    }
}
