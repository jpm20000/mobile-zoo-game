using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Construction;
using ZooGame.Data;
using ZooGame.Gameplay;
using ZooGame.UI;
using ZooGame.World;

namespace ZooGame.Editor
{
    /// <summary>
    /// Idempotent generator for M3 assets and Zoo-scene content: the vertex-colour material, construction config,
    /// Basic Path / Basic Fence / Basic Gate definitions and catalog, the path, fence and preview renderers, the
    /// build-mode controller and the temporary construction toolbar. Run via ZooGame > M3 > Create Or Update Project
    /// Assets, after the M2 menu (re-running M0 recreates the Zoo scene: run M1, M2 and then this again).
    /// </summary>
    public static class M3ProjectSetup
    {
        const string Root = "Assets/ZooGame";
        const string DefinitionFolder = Root + "/ScriptableObjects/Construction";
        const string MaterialPath = Root + "/Materials/ConstructionVertexColor.mat";
        const string ConfigPath = Root + "/ScriptableObjects/ConstructionConfig.asset";
        const string CatalogPath = Root + "/ScriptableObjects/ConstructionCatalog.asset";
        const string ZooScenePath = Root + "/Scenes/Zoo.unity";

        [MenuItem("ZooGame/M3/Create Or Update Project Assets")]
        public static void Run()
        {
            M0ProjectSetup.EnsureFolder(DefinitionFolder);
            M0ProjectSetup.EnsureFolder(Root + "/Materials");

            var material = LoadOrCreateMaterial();
            var config = LoadOrCreate<ConstructionConfig>(ConfigPath);
            var catalog = LoadOrCreate<ConstructionCatalog>(CatalogPath);

            var configSo = new SerializedObject(config);
            configSo.FindProperty("vertexColorMaterial").objectReferenceValue = material;
            configSo.ApplyModifiedPropertiesWithoutUndo();

            var path = BuildPath("Basic Path", "basic_path", new Color(0.78f, 0.68f, 0.50f));
            var fence = BuildFence("Basic Fence", "basic_fence", EdgeKind.Fence, new Color(0.55f, 0.38f, 0.22f), 0.7f);
            var square = BuildFence("Fence 8x8 Square", "fence_square_8", EdgeKind.Fence, new Color(0.55f, 0.38f, 0.22f), 0.7f, 8);
            var gate = BuildFence("Basic Gate", "basic_gate", EdgeKind.Gate, new Color(0.25f, 0.50f, 0.90f), 0.7f);

            var catalogSo = new SerializedObject(catalog);
            SetList(catalogSo.FindProperty("paths"), path);
            SetList(catalogSo.FindProperty("fences"), fence, square, gate);
            catalogSo.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(config);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ZooScenePath, OpenSceneMode.Single);
            // Reload by path: opening a scene can unload assets that were only held in memory.
            BuildSceneContent(AssetDatabase.LoadAssetAtPath<ConstructionCatalog>(CatalogPath),
                AssetDatabase.LoadAssetAtPath<ConstructionConfig>(ConfigPath));
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("M3 project assets created/updated.");
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Material LoadOrCreateMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) return mat;
            // Sprites/Default is unlit and takes vertex colour and alpha; shading is baked into the meshes.
            mat = new Material(Shader.Find("Sprites/Default")) { name = "ConstructionVertexColor" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
            return mat;
        }

        static PathDefinition BuildPath(string name, string id, Color color)
        {
            var def = LoadOrCreate<PathDefinition>(DefinitionFolder + "/" + name + ".asset");
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("displayName").stringValue = name;
            so.FindProperty("color").colorValue = color;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        static FenceDefinition BuildFence(string name, string id, EdgeKind kind, Color color, float height, int squareSize = 0)
        {
            var def = LoadOrCreate<FenceDefinition>(DefinitionFolder + "/" + name + ".asset");
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("displayName").stringValue = name;
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("color").colorValue = color;
            so.FindProperty("height").floatValue = height;
            so.FindProperty("squareSize").intValue = squareSize;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        static void SetList(SerializedProperty list, params Object[] items)
        {
            list.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        // ---- Scene -----------------------------------------------------------------------------------------

        static void BuildSceneContent(ConstructionCatalog catalog, ConstructionConfig config)
        {
            var controller = EnsureComponentObject<BuildModeController>("Build Mode");
            var paths = EnsureRendererObject<PathRenderer>("Paths");
            var fences = EnsureRendererObject<FenceRenderer>("Fences");
            var preview = EnsureRendererObject<ConstructionPreview>("Construction Preview");

            var hud = Object.FindAnyObjectByType<DebugHud>(FindObjectsInactive.Include);
            var panel = Object.FindAnyObjectByType<ConstructionTestPanel>(FindObjectsInactive.Include);
            if (panel == null) panel = BuildPanel(hud.transform.parent);

            var binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            var so = new SerializedObject(binder);
            so.FindProperty("buildModeController").objectReferenceValue = controller;
            so.FindProperty("constructionCatalog").objectReferenceValue = catalog;
            so.FindProperty("constructionConfig").objectReferenceValue = config;
            so.FindProperty("pathRenderer").objectReferenceValue = paths;
            so.FindProperty("fenceRenderer").objectReferenceValue = fences;
            so.FindProperty("constructionPreview").objectReferenceValue = preview;
            so.FindProperty("constructionPanel").objectReferenceValue = panel;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static T EnsureComponentObject<T>(string name) where T : Component
        {
            var existing = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            return existing != null ? existing : new GameObject(name, typeof(T)).GetComponent<T>();
        }

        static T EnsureRendererObject<T>(string name) where T : Component
        {
            var existing = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            return existing != null ? existing : new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(T)).GetComponent<T>();
        }

        static ConstructionTestPanel BuildPanel(Transform safeArea)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var root = M0ProjectSetup.NewRect("Construction Panel", safeArea, typeof(ConstructionTestPanel));
            M0ProjectSetup.Stretch(root);

            // Status: top, below the placement status line.
            var status = M0ProjectSetup.NewText("Status", root, font, 34, TextAnchor.UpperLeft);
            M0ProjectSetup.Anchor(status.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(600f, -140f), new Vector2(1000f, 140f));

            // Toolbar: vertical stack on the right, centred vertically.
            var toolbar = M0ProjectSetup.NewRect("Toolbar", root, typeof(VerticalLayoutGroup));
            M0ProjectSetup.Anchor(toolbar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(260f, 640f));
            var layout = toolbar.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var template = M0ProjectSetup.NewButton("Tool Template", toolbar, font, "Tool", out var templateLabel);
            templateLabel.fontSize = 34;
            AddLayout(template.gameObject, 260f, 90f);
            template.gameObject.SetActive(false);

            // Confirm / Cancel: bottom-right (the object-placement actions use the same spot but only in their own modes).
            var actions = M0ProjectSetup.NewRect("Actions", root, typeof(HorizontalLayoutGroup));
            M0ProjectSetup.Anchor(actions, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(520f, 110f));
            var row = actions.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 16f;
            row.childAlignment = TextAnchor.LowerRight;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var confirm = M0ProjectSetup.NewButton("Confirm", actions, font, "Confirm", out _);
            AddLayout(confirm.gameObject, 240f, 110f);
            var cancel = M0ProjectSetup.NewButton("Done", actions, font, "Done", out _);
            AddLayout(cancel.gameObject, 240f, 110f);

            var panel = root.GetComponent<ConstructionTestPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("statusLabel").objectReferenceValue = status;
            so.FindProperty("toolbarRoot").objectReferenceValue = toolbar;
            so.FindProperty("toolButtonTemplate").objectReferenceValue = template;
            so.FindProperty("confirmButton").objectReferenceValue = confirm;
            so.FindProperty("cancelButton").objectReferenceValue = cancel;
            so.ApplyModifiedPropertiesWithoutUndo();
            return panel;
        }

        static void AddLayout(GameObject go, float width, float height)
        {
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;
        }
    }
}
