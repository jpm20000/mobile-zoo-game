using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Data;
using ZooGame.Gameplay;
using ZooGame.UI;
using ZooGame.Visitors;

namespace ZooGame.Editor
{
    /// <summary>
    /// Idempotent generator for M6 content: the visitor config, a placeholder visitor body, the five placeable test
    /// structures (Entrance, Food Stall, Drink Stall, Toilet, Bench) added to the placement catalog, and the scene
    /// objects and debug UI for visitors. Run via ZooGame > M6 > Create Or Update Project Assets, after the M5 menu.
    /// </summary>
    public static class M6ProjectSetup
    {
        const string Root = "Assets/ZooGame";
        const string ConfigFolder = Root + "/ScriptableObjects/Visitors";
        const string ConfigPath = ConfigFolder + "/VisitorConfig.asset";
        const string PlaceableFolder = Root + "/ScriptableObjects/Placeables";
        const string PlaceablePrefabFolder = Root + "/Prefabs/Placeables";
        const string VisitorPrefabFolder = Root + "/Prefabs/Visitors";
        const string CatalogPath = Root + "/ScriptableObjects/PlaceableCatalog.asset";
        const string ZooScenePath = Root + "/Scenes/Zoo.unity";

        struct FacilitySpec
        {
            public string Id, Name;
            public int Width, Height;
            public VisitorFacilityKind Kind;
            public Color Color;
            public float BodyHeight;
        }

        static readonly FacilitySpec[] Facilities =
        {
            new FacilitySpec { Id = "zoo-entrance", Name = "Entrance", Width = 2, Height = 2, Kind = VisitorFacilityKind.Entrance,
                Color = new Color(0.20f, 0.65f, 0.35f), BodyHeight = 0.5f },
            new FacilitySpec { Id = "food-stall", Name = "Food Stall", Width = 1, Height = 1, Kind = VisitorFacilityKind.FoodStall,
                Color = new Color(0.95f, 0.55f, 0.15f), BodyHeight = 0.8f },
            new FacilitySpec { Id = "drink-stall", Name = "Drink Stall", Width = 1, Height = 1, Kind = VisitorFacilityKind.DrinkStall,
                Color = new Color(0.25f, 0.60f, 0.95f), BodyHeight = 0.8f },
            new FacilitySpec { Id = "toilet", Name = "Toilet", Width = 1, Height = 1, Kind = VisitorFacilityKind.Toilet,
                Color = new Color(0.60f, 0.62f, 0.68f), BodyHeight = 1.0f },
            new FacilitySpec { Id = "bench", Name = "Bench", Width = 2, Height = 1, Kind = VisitorFacilityKind.Bench,
                Color = new Color(0.55f, 0.38f, 0.22f), BodyHeight = 0.35f },
        };

        [MenuItem("ZooGame/M6/Create Or Update Project Assets")]
        public static void Run()
        {
            M0ProjectSetup.EnsureFolder(ConfigFolder);
            M0ProjectSetup.EnsureFolder(PlaceableFolder);
            M0ProjectSetup.EnsureFolder(PlaceablePrefabFolder);
            M0ProjectSetup.EnsureFolder(VisitorPrefabFolder);
            M0ProjectSetup.EnsureFolder(Root + "/Materials");

            var config = AssetDatabase.LoadAssetAtPath<VisitorConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<VisitorConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            var definitions = new PlaceableDefinition[Facilities.Length];
            for (int i = 0; i < Facilities.Length; i++)
            {
                // M2 already made a placeholder "Food Stall" (id food_stall) that M2's tests use. It is the same object
                // as far as visitors are concerned, so it gets the facility role instead of being overwritten.
                var existing = AssetDatabase.LoadAssetAtPath<PlaceableDefinition>(PlaceableFolder + "/" + Facilities[i].Name + ".asset");
                if (existing != null && existing.Id != Facilities[i].Id)
                {
                    SetFacilityKind(existing, Facilities[i].Kind);
                    definitions[i] = existing;
                    continue;
                }
                definitions[i] = BuildFacilityDefinition(Facilities[i], BuildFacilityPrefab(Facilities[i]));
            }
            AddToCatalog(definitions);
            var visitorPrefab = BuildVisitorPrefab();
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ZooScenePath, OpenSceneMode.Single);
            BuildSceneContent(AssetDatabase.LoadAssetAtPath<VisitorConfig>(ConfigPath), visitorPrefab);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("M6 project assets created/updated.");
        }

        // ---- Materials and prefabs -------------------------------------------------------------------------

        static Material ColorMaterial(string name, Color color)
        {
            string path = Root + "/Materials/Visitor_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Visitor_" + name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Prefabs are authored for the unrotated footprint, centred on the pivot, pivot at ground level, 1 unit = 1 cell.
        static GameObject BuildFacilityPrefab(FacilitySpec s)
        {
            var root = new GameObject(s.Name);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(s.Width * 0.8f, s.BodyHeight, s.Height * 0.8f);
            body.transform.localPosition = new Vector3(0f, s.BodyHeight * 0.5f, 0f);
            body.GetComponent<MeshRenderer>().sharedMaterial = ColorMaterial(s.Id, s.Color);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlaceablePrefabFolder + "/" + s.Name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static PlaceableDefinition BuildFacilityDefinition(FacilitySpec s, GameObject prefab)
        {
            string path = PlaceableFolder + "/" + s.Name + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<PlaceableDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<PlaceableDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = s.Id;
            so.FindProperty("displayName").stringValue = s.Name;
            so.FindProperty("category").enumValueIndex = (int)PlaceableCategory.Service;
            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.FindProperty("footprintWidth").intValue = s.Width;
            so.FindProperty("footprintHeight").intValue = s.Height;
            so.FindProperty("allowRotation").boolValue = true;
            so.FindProperty("visitorFacility.kind").enumValueIndex = (int)s.Kind;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        static void SetFacilityKind(PlaceableDefinition def, VisitorFacilityKind kind)
        {
            var so = new SerializedObject(def);
            so.FindProperty("visitorFacility.kind").enumValueIndex = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
        }

        static void AddToCatalog(PlaceableDefinition[] defs)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlaceableCatalog>(CatalogPath);
            if (catalog == null) { Debug.LogWarning("Missing placeable catalog: run the M2 menu first."); return; }
            var so = new SerializedObject(catalog);
            var list = so.FindProperty("definitions");
            foreach (var def in defs)
            {
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == def) { present = true; break; }
                if (present) continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = def;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        static VisitorController BuildVisitorPrefab()
        {
            var root = new GameObject("Visitor", typeof(VisitorController));

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.35f, 0.45f, 0.35f);
            body.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            body.GetComponent<MeshRenderer>().sharedMaterial = ColorMaterial("Body", new Color(0.95f, 0.80f, 0.25f));

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Selection Marker";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(root.transform, false);
            marker.transform.localScale = Vector3.one * 0.2f;
            marker.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            marker.GetComponent<MeshRenderer>().sharedMaterial = ColorMaterial("Marker", new Color(1f, 0.95f, 0.2f));
            marker.SetActive(false);

            var so = new SerializedObject(root.GetComponent<VisitorController>());
            so.FindProperty("selectionMarker").objectReferenceValue = marker;
            so.FindProperty("pickRadius").floatValue = 0.5f;
            so.FindProperty("pickHeight").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, VisitorPrefabFolder + "/Visitor.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<VisitorController>();
        }

        // ---- Scene -----------------------------------------------------------------------------------------

        static void BuildSceneContent(VisitorConfig config, VisitorController visitorPrefab)
        {
            var spawner = Object.FindAnyObjectByType<VisitorSpawner>(FindObjectsInactive.Include);
            VisitorSelectionController selection;
            if (spawner == null)
            {
                var go = new GameObject("Visitors", typeof(VisitorSpawner), typeof(VisitorSelectionController));
                spawner = go.GetComponent<VisitorSpawner>();
                selection = go.GetComponent<VisitorSelectionController>();
            }
            else selection = spawner.GetComponent<VisitorSelectionController>();

            var sso = new SerializedObject(spawner);
            sso.FindProperty("prefab").objectReferenceValue = visitorPrefab;
            sso.FindProperty("autoSpawn").boolValue = true;
            sso.ApplyModifiedPropertiesWithoutUndo();

            var hud = Object.FindAnyObjectByType<DebugHud>(FindObjectsInactive.Include);
            // The layout may change between runs: always rebuild the visitor UI so re-running stays correct.
            var oldInfo = Object.FindAnyObjectByType<VisitorInfoPanel>(FindObjectsInactive.Include);
            if (oldInfo != null) Object.DestroyImmediate(oldInfo.gameObject);
            var oldDebug = Object.FindAnyObjectByType<VisitorDebugPanel>(FindObjectsInactive.Include);
            if (oldDebug != null) Object.DestroyImmediate(oldDebug.gameObject);
            var info = BuildInfoPanel(hud.transform.parent);
            var debug = BuildDebugPanel(hud.transform.parent);

            var binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            var bso = new SerializedObject(binder);
            bso.FindProperty("visitorConfig").objectReferenceValue = config;
            bso.FindProperty("visitorSpawner").objectReferenceValue = spawner;
            bso.FindProperty("visitorSelection").objectReferenceValue = selection;
            bso.FindProperty("visitorInfoPanel").objectReferenceValue = info;
            bso.FindProperty("visitorDebugPanel").objectReferenceValue = debug;
            bso.ApplyModifiedPropertiesWithoutUndo();
        }

        static VisitorInfoPanel BuildInfoPanel(Transform safeArea)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = M0ProjectSetup.NewRect("Visitor Info Panel", safeArea, typeof(VisitorInfoPanel));
            M0ProjectSetup.Stretch(root);

            // Same corner as the animal info panel: selecting a visitor clears the animal selection, so only one shows.
            var content = M0ProjectSetup.NewRect("Content", root, typeof(CanvasRenderer), typeof(Image));
            M0ProjectSetup.Anchor(content, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -170f), new Vector2(600f, 400f));
            var bg = content.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = false;

            var label = M0ProjectSetup.NewText("Label", content, font, 26, TextAnchor.UpperLeft);
            M0ProjectSetup.Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(14f, 8f);
            label.rectTransform.offsetMax = new Vector2(-14f, -8f);

            var panel = root.GetComponent<VisitorInfoPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("content").objectReferenceValue = content.gameObject;
            so.FindProperty("label").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
            return panel;
        }

        static VisitorDebugPanel BuildDebugPanel(Transform safeArea)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = M0ProjectSetup.NewRect("Visitor Debug Panel", safeArea, typeof(VisitorDebugPanel));
            M0ProjectSetup.Stretch(root);

            // Bottom-left, to the right of the animal palette column.
            var status = M0ProjectSetup.NewText("Status", root, font, 28, TextAnchor.LowerLeft);
            M0ProjectSetup.Anchor(status.rectTransform, Vector2.zero, Vector2.zero, new Vector2(464f, 640f), new Vector2(760f, 50f));

            var column = M0ProjectSetup.NewRect("Buttons", root, typeof(VerticalLayoutGroup));
            M0ProjectSetup.Anchor(column, Vector2.zero, Vector2.zero, new Vector2(464f, 290f), new Vector2(380f, 340f));
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var auto = NewPanelButton("Auto Spawn", column, font, "Auto spawn: ON", out var autoLabel);
            var one = NewPanelButton("Spawn One", column, font, "Spawn 1 visitor", out _);
            var five = NewPanelButton("Spawn Five", column, font, "Spawn 5 visitors", out _);
            var clear = NewPanelButton("Clear Visitors", column, font, "Clear visitors", out _);

            var panel = root.GetComponent<VisitorDebugPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("statusLabel").objectReferenceValue = status;
            so.FindProperty("autoButton").objectReferenceValue = auto;
            so.FindProperty("autoButtonLabel").objectReferenceValue = autoLabel;
            so.FindProperty("spawnOneButton").objectReferenceValue = one;
            so.FindProperty("spawnFiveButton").objectReferenceValue = five;
            so.FindProperty("clearButton").objectReferenceValue = clear;
            so.ApplyModifiedPropertiesWithoutUndo();
            return panel;
        }

        static Button NewPanelButton(string name, Transform parent, Font font, string text, out Text label)
        {
            var b = M0ProjectSetup.NewButton(name, parent, font, text, out label);
            label.fontSize = 28;
            var layout = b.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = 380f;
            layout.preferredHeight = 72f;
            return b;
        }
    }
}
