using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Animals;
using ZooGame.Data;
using ZooGame.Gameplay;
using ZooGame.World;

namespace ZooGame.Editor
{
    /// <summary>
    /// Idempotent generator for M5 content: the needs config, species needs/habitat tuning for Rabbit / Zebra / Lion,
    /// four placeholder habitat objects (food, drinking water, shelter, enrichment) added to the placement catalog, and
    /// the larger info panel with its factor-score debug label. Run via ZooGame > M5 > Create Or Update Project Assets,
    /// after the M4 menu.
    /// </summary>
    public static class M5ProjectSetup
    {
        const string Root = "Assets/ZooGame";
        const string AnimalFolder = Root + "/ScriptableObjects/Animals";
        const string PrefabFolder = Root + "/Prefabs/Placeables";
        const string DefinitionFolder = Root + "/ScriptableObjects/Placeables";
        const string CatalogPath = Root + "/ScriptableObjects/PlaceableCatalog.asset";
        const string ConfigPath = AnimalFolder + "/AnimalNeedsConfig.asset";
        const string ZooScenePath = Root + "/Scenes/Zoo.unity";

        struct SpeciesTuning
        {
            public string Name;
            public float HungerDecay, ThirstDecay, HungerRecovery, ThirstRecovery;
            public bool Shelter;
            public float Enrichment;
            public float GrassMin, GrassMax;
        }

        // Terrain is painted all-grass for now (no terrain tool yet), so every species prefers mostly grass.
        static readonly SpeciesTuning[] Species =
        {
            new SpeciesTuning { Name = "Rabbit", HungerDecay = 0.4f, ThirstDecay = 0.6f, HungerRecovery = 6f, ThirstRecovery = 8f,
                Shelter = true, Enrichment = 2f, GrassMin = 0.5f, GrassMax = 1f },
            new SpeciesTuning { Name = "Zebra", HungerDecay = 0.5f, ThirstDecay = 0.6f, HungerRecovery = 6f, ThirstRecovery = 8f,
                Shelter = false, Enrichment = 3f, GrassMin = 0.6f, GrassMax = 1f },
            new SpeciesTuning { Name = "Lion", HungerDecay = 0.6f, ThirstDecay = 0.5f, HungerRecovery = 6f, ThirstRecovery = 8f,
                Shelter = true, Enrichment = 4f, GrassMin = 0.5f, GrassMax = 1f },
        };

        struct ResourceSpec
        {
            public string Id, Name;
            public int Width, Height;
            public HabitatResourceKind Kind;
            public float Amount;
            public Color Color;
            public float BodyHeight;
            public PrimitiveType Shape;
        }

        static readonly ResourceSpec[] ResourceSpecs =
        {
            new ResourceSpec { Id = "food-source", Name = "Food Source", Width = 1, Height = 1, Kind = HabitatResourceKind.Food, Amount = 1f,
                Color = new Color(0.55f, 0.38f, 0.18f), BodyHeight = 0.3f, Shape = PrimitiveType.Cube },
            new ResourceSpec { Id = "drinking-water-source", Name = "Drinking Water", Width = 1, Height = 1, Kind = HabitatResourceKind.DrinkingWater, Amount = 1f,
                Color = new Color(0.25f, 0.55f, 0.90f), BodyHeight = 0.3f, Shape = PrimitiveType.Cube },
            new ResourceSpec { Id = "shelter", Name = "Shelter", Width = 2, Height = 2, Kind = HabitatResourceKind.Shelter, Amount = 4f,
                Color = new Color(0.62f, 0.50f, 0.40f), BodyHeight = 0.9f, Shape = PrimitiveType.Cube },
            new ResourceSpec { Id = "enrichment-object", Name = "Enrichment Object", Width = 1, Height = 1, Kind = HabitatResourceKind.Enrichment, Amount = 2f,
                Color = new Color(0.90f, 0.35f, 0.55f), BodyHeight = 0.6f, Shape = PrimitiveType.Sphere },
        };

        [MenuItem("ZooGame/M5/Create Or Update Project Assets")]
        public static void Run()
        {
            M0ProjectSetup.EnsureFolder(PrefabFolder);
            M0ProjectSetup.EnsureFolder(DefinitionFolder);
            M0ProjectSetup.EnsureFolder(Root + "/Materials");

            var config = LoadOrCreate<AnimalNeedsConfig>(ConfigPath);
            for (int i = 0; i < Species.Length; i++) TuneSpecies(Species[i]);

            var placeables = new PlaceableDefinition[ResourceSpecs.Length];
            for (int i = 0; i < ResourceSpecs.Length; i++)
                placeables[i] = BuildResourceDefinition(ResourceSpecs[i], BuildResourcePrefab(ResourceSpecs[i]));
            AddToCatalog(placeables);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ZooScenePath, OpenSceneMode.Single);
            config = AssetDatabase.LoadAssetAtPath<AnimalNeedsConfig>(ConfigPath);
            BuildSceneContent(config);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("M5 project assets created/updated.");
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ---- Species ---------------------------------------------------------------------------------------

        static void TuneSpecies(SpeciesTuning t)
        {
            var def = AssetDatabase.LoadAssetAtPath<AnimalDefinition>(AnimalFolder + "/" + t.Name + ".asset");
            if (def == null) { Debug.LogWarning("Missing " + t.Name + " definition: run the M4 menu first."); return; }
            var so = new SerializedObject(def);
            so.FindProperty("hungerDecayPerMinute").floatValue = t.HungerDecay;
            so.FindProperty("thirstDecayPerMinute").floatValue = t.ThirstDecay;
            so.FindProperty("hungerRecoveryPerMinute").floatValue = t.HungerRecovery;
            so.FindProperty("thirstRecoveryPerMinute").floatValue = t.ThirstRecovery;
            so.FindProperty("requiresHabitatWater").boolValue = false;
            so.FindProperty("requiresShelter").boolValue = t.Shelter;
            so.FindProperty("shelterPerAnimal").floatValue = 1f;
            so.FindProperty("requiredEnrichmentValue").floatValue = t.Enrichment;
            so.FindProperty("terrainTolerance").floatValue = 0.5f;
            var prefs = so.FindProperty("terrainPreferences");
            prefs.arraySize = 1;
            var p = prefs.GetArrayElementAtIndex(0);
            p.FindPropertyRelative("terrain").enumValueIndex = (int)TerrainType.Grass;
            p.FindPropertyRelative("minShare").floatValue = t.GrassMin;
            p.FindPropertyRelative("maxShare").floatValue = t.GrassMax;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
        }

        // ---- Habitat objects -------------------------------------------------------------------------------

        static Material ColorMaterial(string name, Color color)
        {
            string path = Root + "/Materials/Habitat_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Habitat_" + name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Prefabs are authored for the unrotated footprint, centred on the pivot, pivot at ground level, 1 unit = 1 cell.
        static GameObject BuildResourcePrefab(ResourceSpec s)
        {
            var root = new GameObject(s.Name);
            var body = GameObject.CreatePrimitive(s.Shape);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            float w = s.Width * 0.8f, d = s.Height * 0.8f;
            body.transform.localScale = s.Shape == PrimitiveType.Sphere
                ? new Vector3(s.BodyHeight, s.BodyHeight, s.BodyHeight)
                : new Vector3(w, s.BodyHeight, d);
            body.transform.localPosition = new Vector3(0f, s.BodyHeight * 0.5f, 0f);
            body.GetComponent<MeshRenderer>().sharedMaterial = ColorMaterial(s.Id, s.Color);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + s.Name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static PlaceableDefinition BuildResourceDefinition(ResourceSpec s, GameObject prefab)
        {
            var def = LoadOrCreate<PlaceableDefinition>(DefinitionFolder + "/" + s.Name + ".asset");
            var so = new SerializedObject(def);
            so.FindProperty("id").stringValue = s.Id;
            so.FindProperty("displayName").stringValue = s.Name;
            so.FindProperty("category").enumValueIndex = (int)PlaceableCategory.Habitat;
            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.FindProperty("footprintWidth").intValue = s.Width;
            so.FindProperty("footprintHeight").intValue = s.Height;
            so.FindProperty("allowRotation").boolValue = true;
            so.FindProperty("habitatResource.kind").enumValueIndex = (int)s.Kind;
            so.FindProperty("habitatResource.amount").floatValue = s.Amount;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
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

        // ---- Scene -----------------------------------------------------------------------------------------

        static void BuildSceneContent(AnimalNeedsConfig config)
        {
            var binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            var bso = new SerializedObject(binder);
            bso.FindProperty("animalNeedsConfig").objectReferenceValue = config;
            bso.ApplyModifiedPropertiesWithoutUndo();

            var info = Object.FindAnyObjectByType<AnimalInfoPanel>(FindObjectsInactive.Include);
            if (info == null) { Debug.LogWarning("No animal info panel: run the M4 menu first."); return; }
            var iso = new SerializedObject(info);
            var content = (GameObject)iso.FindProperty("content").objectReferenceValue;
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.sizeDelta = new Vector2(560f, 330f);

            var factors = iso.FindProperty("factorsLabel").objectReferenceValue as Text;
            if (factors == null)
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var panel = M0ProjectSetup.NewRect("Factors", content.transform, typeof(CanvasRenderer), typeof(Image));
                M0ProjectSetup.Anchor(panel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(330f, 330f));
                panel.pivot = new Vector2(0f, 1f);
                panel.anchoredPosition = new Vector2(8f, 0f); // just right of the main panel
                var bg = panel.GetComponent<Image>();
                bg.color = new Color(0f, 0f, 0f, 0.55f);
                bg.raycastTarget = false;
                factors = M0ProjectSetup.NewText("Label", panel, font, 26, TextAnchor.UpperLeft);
                M0ProjectSetup.Stretch(factors.rectTransform);
                factors.rectTransform.offsetMin = new Vector2(12f, 8f);
                factors.rectTransform.offsetMax = new Vector2(-12f, -8f);
                iso.FindProperty("factorsLabel").objectReferenceValue = factors;
            }
            var label = (Text)iso.FindProperty("label").objectReferenceValue;
            label.fontSize = 26;
            iso.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
