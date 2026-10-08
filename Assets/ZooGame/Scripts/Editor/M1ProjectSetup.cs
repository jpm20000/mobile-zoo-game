using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Cameras;
using ZooGame.Data;
using ZooGame.Gameplay;
using ZooGame.UI;
using ZooGame.World;

namespace ZooGame.Editor
{
    /// <summary>
    /// Idempotent generator for M1 assets and Zoo-scene content (world/camera configs, terrain, debug grid overlay,
    /// camera controller, HUD grid button). Run via ZooGame > M1 > Create Or Update Project Assets, after the M0 menu
    /// (re-running the M0 menu regenerates the Zoo scene without M1 content; run this again afterwards).
    /// </summary>
    public static class M1ProjectSetup
    {
        const string Root = "Assets/ZooGame";
        const string WorldConfigPath = Root + "/ScriptableObjects/WorldConfig.asset";
        const string CameraConfigPath = Root + "/ScriptableObjects/CameraConfig.asset";
        const string TerrainMaterialPath = Root + "/Materials/Ground.mat";
        const string OverlayMaterialPath = Root + "/Materials/GridOverlay.mat";
        const string ZooScenePath = Root + "/Scenes/Zoo.unity";

        [MenuItem("ZooGame/M1/Create Or Update Project Assets")]
        public static void Run()
        {
            M0ProjectSetup.EnsureFolder(Root + "/ScriptableObjects");
            M0ProjectSetup.EnsureFolder(Root + "/Materials");

            LoadOrCreate<WorldConfig>(WorldConfigPath);
            LoadOrCreate<CameraConfig>(CameraConfigPath);
            LoadOrCreateOverlayMaterial();
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ZooScenePath, OpenSceneMode.Single);
            // Reload by path: opening a scene can unload assets that were only held in memory.
            BuildZooContent(scene,
                AssetDatabase.LoadAssetAtPath<WorldConfig>(WorldConfigPath),
                AssetDatabase.LoadAssetAtPath<CameraConfig>(CameraConfigPath),
                AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath),
                AssetDatabase.LoadAssetAtPath<Material>(OverlayMaterialPath));
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("M1 project assets created/updated.");
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Material LoadOrCreateOverlayMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OverlayMaterialPath);
            if (mat != null) return mat;
            // Sprites/Default is unlit, supports vertex colour + alpha, and works in URP.
            mat = new Material(Shader.Find("Sprites/Default")) { name = "GridOverlay" };
            AssetDatabase.CreateAsset(mat, OverlayMaterialPath);
            return mat;
        }

        static void BuildZooContent(UnityEngine.SceneManagement.Scene scene, WorldConfig worldConfig, CameraConfig cameraConfig,
            Material terrainMaterial, Material overlayMaterial)
        {
            // M0's placeholder 50x50 plane is replaced by the grid-matched terrain.
            var oldGround = FindRoot(scene, "Ground");
            if (oldGround != null) Object.DestroyImmediate(oldGround);

            var terrainGo = FindRoot(scene, "Terrain");
            if (terrainGo == null) terrainGo = new GameObject("Terrain", typeof(MeshFilter), typeof(MeshRenderer), typeof(TerrainView));
            var terrainRenderer = terrainGo.GetComponent<MeshRenderer>();
            terrainRenderer.sharedMaterial = terrainMaterial;
            terrainRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var overlayGo = FindRoot(scene, "Grid Debug Overlay");
            if (overlayGo == null) overlayGo = new GameObject("Grid Debug Overlay", typeof(MeshFilter), typeof(MeshRenderer), typeof(GridDebugOverlay));
            overlayGo.GetComponent<MeshRenderer>().sharedMaterial = overlayMaterial;

            var camGo = FindRoot(scene, "Main Camera");
            var controller = camGo.GetComponent<ZooCameraController>();
            if (controller == null) controller = camGo.AddComponent<ZooCameraController>();

            var hud = Object.FindAnyObjectByType<DebugHud>(FindObjectsInactive.Include);
            var gridButton = EnsureGridButton(hud);

            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("gridButton").objectReferenceValue = gridButton;
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            var binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            var so = new SerializedObject(binder);
            so.FindProperty("worldConfig").objectReferenceValue = worldConfig;
            so.FindProperty("cameraConfig").objectReferenceValue = cameraConfig;
            so.FindProperty("terrainView").objectReferenceValue = terrainGo.GetComponent<TerrainView>();
            so.FindProperty("gridOverlay").objectReferenceValue = overlayGo.GetComponent<GridDebugOverlay>();
            so.FindProperty("cameraController").objectReferenceValue = controller;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Button EnsureGridButton(DebugHud hud)
        {
            var existing = hud.transform.Find("Grid");
            if (existing != null) return existing.GetComponent<Button>();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var button = M0ProjectSetup.NewButton("Grid", hud.transform, font, "Grid", out _);
            // Sits after Pause (220) and four speed buttons (130 each), 16 px gaps, matching the M0 row.
            M0ProjectSetup.Anchor((RectTransform)button.transform, Vector2.zero, Vector2.zero,
                new Vector2(24f + 220f + 16f + 4 * (130f + 16f), 24f), new Vector2(130f, 110f));
            return button;
        }

        static GameObject FindRoot(UnityEngine.SceneManagement.Scene scene, string name)
        {
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == name) return go;
            return null;
        }
    }
}
