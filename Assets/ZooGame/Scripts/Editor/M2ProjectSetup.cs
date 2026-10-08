using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Cameras;
using ZooGame.Data;
using ZooGame.Gameplay;
using ZooGame.Placement;
using ZooGame.UI;

namespace ZooGame.Editor
{
    /// <summary>
    /// Idempotent generator for M2 assets and Zoo-scene content: placeholder primitive prefabs, three placeable
    /// definitions, the catalog, the placement config, the placement controller and the temporary test panel. Run via
    /// ZooGame > M2 > Create Or Update Project Assets, after the M1 menu (re-running M0 recreates the Zoo scene, so
    /// run M1 and then this again).
    /// </summary>
    public static class M2ProjectSetup
    {
        const string Root = "Assets/ZooGame";
        const string PrefabFolder = Root + "/Prefabs/Placeables";
        const string DefinitionFolder = Root + "/ScriptableObjects/Placeables";
        const string CatalogPath = Root + "/ScriptableObjects/PlaceableCatalog.asset";
        const string ConfigPath = Root + "/ScriptableObjects/PlacementConfig.asset";
        const string GhostMaterialPath = Root + "/Materials/PlacementGhost.mat";
        const string ZooScenePath = Root + "/Scenes/Zoo.unity";

        struct Spec
        {
            public string Id, Name;
            public int Width, Height;
            public float BodyHeight;
            public Color Color;
            public PlaceableCategory Category;
        }

        static readonly Spec[] Specs =
        {
            new Spec { Id = "small_decoration", Name = "Small Decoration", Width = 1, Height = 1, BodyHeight = 0.8f,
                Color = new Color(0.85f, 0.45f, 0.70f), Category = PlaceableCategory.Decoration },
            new Spec { Id = "food_stall", Name = "Food Stall", Width = 2, Height = 2, BodyHeight = 1.2f,
                Color = new Color(0.95f, 0.60f, 0.20f), Category = PlaceableCategory.Service },
            new Spec { Id = "utility_building", Name = "Utility Building", Width = 2, Height = 3, BodyHeight = 1.6f,
                Color = new Color(0.35f, 0.55f, 0.85f), Category = PlaceableCategory.Building },
        };

        [MenuItem("ZooGame/M2/Create Or Update Project Assets")]
        public static void Run()
        {
            M0ProjectSetup.EnsureFolder(PrefabFolder);
            M0ProjectSetup.EnsureFolder(DefinitionFolder);
            M0ProjectSetup.EnsureFolder(Root + "/Materials");

            var ghostMaterial = LoadOrCreateGhostMaterial();
            var catalog = LoadOrCreate<PlaceableCatalog>(CatalogPath);
            var config = LoadOrCreate<PlacementConfig>(ConfigPath);

            var definitions = new PlaceableDefinition[Specs.Length];
            for (int i = 0; i < Specs.Length; i++)
            {
                var prefab = BuildPrefab(Specs[i]);
                definitions[i] = BuildDefinition(Specs[i], prefab);
            }

            var configSo = new SerializedObject(config);
            configSo.FindProperty("ghostMaterial").objectReferenceValue = ghostMaterial;
            configSo.ApplyModifiedPropertiesWithoutUndo();

            var catalogSo = new SerializedObject(catalog);
            var list = catalogSo.FindProperty("definitions");
            list.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            catalogSo.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(config);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ZooScenePath, OpenSceneMode.Single);
            // Reload by path: opening a scene can unload assets that were only held in memory.
            BuildSceneContent(AssetDatabase.LoadAssetAtPath<PlaceableCatalog>(CatalogPath),
                AssetDatabase.LoadAssetAtPath<PlacementConfig>(ConfigPath));
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("M2 project assets created/updated.");
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Material LoadOrCreateGhostMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
            if (mat != null) return mat;
            // Sprites/Default: unlit, honours _Color and alpha, works in URP. Tint is set per renderer at runtime.
            mat = new Material(Shader.Find("Sprites/Default")) { name = "PlacementGhost" };
            AssetDatabase.CreateAsset(mat, GhostMaterialPath);
            return mat;
        }

        static Material LoadOrCreateColorMaterial(string name, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Primitive-only prefab: a body box covering the footprint plus a darker "front" nub on the +Z side, so a
        /// rotation is visible even for the 1x1 object. Pivot at ground level in the middle of the footprint.
        /// </summary>
        static GameObject BuildPrefab(Spec s)
        {
            var body = LoadOrCreateColorMaterial("Placeable_" + s.Id, s.Color);
            var front = LoadOrCreateColorMaterial("Placeable_Front", new Color(0.15f, 0.15f, 0.18f));

            var root = new GameObject(s.Name);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Body";
            box.transform.SetParent(root.transform, false);
            box.transform.localScale = new Vector3(s.Width * 0.9f, s.BodyHeight, s.Height * 0.9f);
            box.transform.localPosition = new Vector3(0f, s.BodyHeight * 0.5f, 0f);
            box.GetComponent<MeshRenderer>().sharedMaterial = body;
            Object.DestroyImmediate(box.GetComponent<Collider>());

            var nub = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nub.name = "Front";
            nub.transform.SetParent(root.transform, false);
            nub.transform.localScale = new Vector3(Mathf.Min(0.5f, s.Width * 0.5f), 0.3f, 0.25f);
            nub.transform.localPosition = new Vector3(0f, s.BodyHeight + 0.15f, s.Height * 0.5f - 0.3f);
            nub.GetComponent<MeshRenderer>().sharedMaterial = front;
            Object.DestroyImmediate(nub.GetComponent<Collider>());

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + s.Name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static PlaceableDefinition BuildDefinition(Spec s, GameObject prefab)
        {
            string path = DefinitionFolder + "/" + s.Name + ".asset";
            var def = LoadOrCreate<PlaceableDefinition>(path);
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = s.Id;
            so.FindProperty("displayName").stringValue = s.Name;
            so.FindProperty("category").enumValueIndex = (int)s.Category;
            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.FindProperty("footprintWidth").intValue = s.Width;
            so.FindProperty("footprintHeight").intValue = s.Height;
            so.FindProperty("allowRotation").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        // ---- Scene -----------------------------------------------------------------------------------------

        static void BuildSceneContent(PlaceableCatalog catalog, PlacementConfig config)
        {
            var controller = Object.FindAnyObjectByType<PlacementController>(FindObjectsInactive.Include);
            if (controller == null) controller = new GameObject("Placement", typeof(PlacementController)).GetComponent<PlacementController>();

            var hud = Object.FindAnyObjectByType<DebugHud>(FindObjectsInactive.Include);
            var panel = Object.FindAnyObjectByType<PlacementTestPanel>(FindObjectsInactive.Include);
            if (panel == null) panel = BuildPanel(hud.transform.parent);

            var binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            var so = new SerializedObject(binder);
            so.FindProperty("placementController").objectReferenceValue = controller;
            so.FindProperty("placementCatalog").objectReferenceValue = catalog;
            so.FindProperty("placementConfig").objectReferenceValue = config;
            so.FindProperty("placementPanel").objectReferenceValue = panel;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static PlacementTestPanel BuildPanel(Transform safeArea)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var root = M0ProjectSetup.NewRect("Placement Panel", safeArea, typeof(PlacementTestPanel));
            M0ProjectSetup.Stretch(root);

            // Status: top centre-left, beside the HUD info label.
            var status = M0ProjectSetup.NewText("Status", root, font, 34, TextAnchor.UpperLeft);
            M0ProjectSetup.Anchor(status.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(600f, -24f), new Vector2(1000f, 140f));

            // Palette: bottom-left, above the HUD button row.
            var palette = M0ProjectSetup.NewRect("Palette", root, typeof(HorizontalLayoutGroup));
            M0ProjectSetup.Anchor(palette, Vector2.zero, Vector2.zero, new Vector2(24f, 150f), new Vector2(1000f, 110f));
            ConfigureRow(palette.GetComponent<HorizontalLayoutGroup>(), TextAnchor.LowerLeft);

            var template = M0ProjectSetup.NewButton("Palette Template", palette, font, "Item", out var templateLabel);
            templateLabel.fontSize = 30;
            AddLayout(template.gameObject, 300f, 110f);
            template.gameObject.SetActive(false);

            // Actions: bottom-right.
            var actions = M0ProjectSetup.NewRect("Actions", root, typeof(HorizontalLayoutGroup));
            M0ProjectSetup.Anchor(actions, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(1000f, 110f));
            ConfigureRow(actions.GetComponent<HorizontalLayoutGroup>(), TextAnchor.LowerRight);

            var move = ActionButton("Move", actions, font);
            var rotate = ActionButton("Rotate", actions, font);
            var delete = ActionButton("Delete", actions, font);
            var place = ActionButton("Place", actions, font);
            var cancel = ActionButton("Cancel", actions, font);

            var panel = root.GetComponent<PlacementTestPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("statusLabel").objectReferenceValue = status;
            so.FindProperty("paletteRoot").objectReferenceValue = palette;
            so.FindProperty("paletteButtonTemplate").objectReferenceValue = template;
            so.FindProperty("moveButton").objectReferenceValue = move;
            so.FindProperty("rotateButton").objectReferenceValue = rotate;
            so.FindProperty("deleteButton").objectReferenceValue = delete;
            so.FindProperty("placeButton").objectReferenceValue = place;
            so.FindProperty("cancelButton").objectReferenceValue = cancel;
            so.ApplyModifiedPropertiesWithoutUndo();
            return panel;
        }

        static void ConfigureRow(HorizontalLayoutGroup row, TextAnchor alignment)
        {
            row.spacing = 16f;
            row.childAlignment = alignment;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
        }

        static Button ActionButton(string label, Transform parent, Font font)
        {
            var button = M0ProjectSetup.NewButton(label, parent, font, label, out _);
            AddLayout(button.gameObject, 200f, 110f);
            return button;
        }

        static void AddLayout(GameObject go, float width, float height)
        {
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;
        }
    }
}
