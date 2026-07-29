using FlickDom.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlickDom.EditorTools
{
    public static class MonkeyCharacterSetupMenu
    {
        private const string MenuPath = "FlickDom/Characters/Setup Selected Monkey Controller";
        private const string RemovePlateMenuPath = "FlickDom/Characters/Remove Selected Monkey Strike Plate";
        private const string SetupLauncherMenuPath =
            "FlickDom/Characters/Setup Slingshot Launcher In Open Scene";
        private const string PlateObjectName = "Monkey Strike Plate";
        private const string LauncherModelPath = "Assets/04_Arts/shooter/shooter.fbx";
        private const string LauncherTexturePath =
            "Assets/04_Arts/shooter/Slingshot_BaseColor.png";
        private const string LauncherMaterialPath =
            "Assets/04_Arts/shooter/FlickDom_Shooter_URP.mat";

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
                SetupMonkeyObject(
                    selectedObjects[i],
                    sceneCamera,
                    cameraFollow,
                    i,
                    selectedObjects.Length);
            }

            Debug.Log("Selected monkey controller setup complete.");
        }

        private static void SetupMonkeyObject(
            GameObject monkeyObject,
            Camera sceneCamera,
            MonkeyThirdPersonCameraFollow cameraFollow,
            int selectionIndex,
            int selectionCount)
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
            controller.ConfigureSlingshotLauncher(
                AssetDatabase.LoadAssetAtPath<GameObject>(LauncherModelPath),
                GetOrCreateLauncherMaterial());

            MonkeySlingshotFlickPresenter slingshotPresenter =
                GetOrAddComponent<MonkeySlingshotFlickPresenter>(monkeyObject);
            Undo.RecordObject(slingshotPresenter, "Configure Monkey Slingshot Presenter");
            slingshotPresenter.SetOwner(selectionIndex == 0 ? FlickDomPlayerId.Player1 : FlickDomPlayerId.Player2);
            slingshotPresenter.SetReactToAllPlayers(selectionCount == 1);
            slingshotPresenter.UseSuriyunAnimationPreset();

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
            EditorUtility.SetDirty(slingshotPresenter);
            EditorUtility.SetDirty(monkeyObject);
        }

        [MenuItem(SetupLauncherMenuPath)]
        private static void SetupSlingshotLauncherInOpenScene()
        {
            GameObject launcherPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(LauncherModelPath);
            if (launcherPrefab == null)
            {
                Debug.LogError(
                    $"Slingshot model was not found at '{LauncherModelPath}'.");
                return;
            }

            Material launcherMaterial = GetOrCreateLauncherMaterial();
            MonkeyThirdPersonController[] controllers =
                Object.FindObjectsByType<MonkeyThirdPersonController>(
                    FindObjectsInactive.Include);
            if (controllers.Length == 0)
            {
                Debug.LogWarning("No monkey controller was found in the open scene.");
                return;
            }

            for (int i = 0; i < controllers.Length; i++)
            {
                Undo.RecordObject(controllers[i], "Configure Slingshot Launcher");
                controllers[i].ConfigureSlingshotLauncher(
                    launcherPrefab,
                    launcherMaterial);
                EditorUtility.SetDirty(controllers[i]);
            }

            DisablePlacedLauncherPreview(launcherPrefab);
            if (EditorSceneManager.GetActiveScene().IsValid())
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }

            Debug.Log(
                $"Slingshot launcher setup complete for {controllers.Length} monkey controller(s).");
        }

        private static Material GetOrCreateLauncherMaterial()
        {
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(LauncherMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogError("Universal Render Pipeline/Lit shader was not found.");
                    return null;
                }

                material = new Material(shader)
                {
                    name = "FlickDom_Shooter_URP"
                };
                AssetDatabase.CreateAsset(material, LauncherMaterialPath);
            }

            Texture2D baseColor =
                AssetDatabase.LoadAssetAtPath<Texture2D>(LauncherTexturePath);
            if (baseColor == null)
            {
                Debug.LogWarning(
                    $"Slingshot texture was not found at '{LauncherTexturePath}'.");
            }

            material.SetTexture("_BaseMap", baseColor);
            material.SetTexture("_MainTex", baseColor);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.28f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void DisablePlacedLauncherPreview(GameObject launcherPrefab)
        {
            if (launcherPrefab == null || !EditorSceneManager.GetActiveScene().IsValid())
            {
                return;
            }

            GameObject[] roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                string sourcePath =
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(roots[i]);
                bool isLauncherPreview = sourcePath == LauncherModelPath
                    || roots[i].name == launcherPrefab.name;
                if (!isLauncherPreview || !roots[i].activeSelf)
                {
                    continue;
                }

                Undo.RecordObject(roots[i], "Disable Slingshot Preview");
                roots[i].SetActive(false);
                EditorUtility.SetDirty(roots[i]);
            }
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
