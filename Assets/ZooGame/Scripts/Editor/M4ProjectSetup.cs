using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Animals;
using ZooGame.Gameplay;
using ZooGame.UI;

namespace ZooGame.Editor
{
    /// <summary>
    /// Idempotent generator for M4 assets and Zoo-scene content: Rabbit / Zebra / Lion definitions with placeholder
    /// primitive prefabs, the animal spawner and selection controller, and the temporary info and debug-spawn panels.
    /// Run via ZooGame > M4 > Create Or Update Project Assets, after the M1, M2 and M3 menus (re-running M0 recreates
    /// the Zoo scene: run M1, M2, M3 and then this again).
    /// </summary>
    public static class M4ProjectSetup
    {
        const string Root = "Assets/ZooGame";
        const string PrefabFolder = Root + "/Prefabs/Animals";
        const string DefinitionFolder = Root + "/ScriptableObjects/Animals";
        const string ZooScenePath = Root + "/Scenes/Zoo.unity";

        struct Spec
        {
            public string SpeciesId, Name, Description;
            public PrimitiveType Shape;
            public Vector3 Scale;
            public Color Color;
            public AnimalBiome Biome;
            public float Speed;
            public int MinArea, GroupMin, GroupMax;
            public float Appeal;
        }

        static readonly Spec[] Specs =
        {
            new Spec { SpeciesId = "rabbit", Name = "Rabbit", Description = "A small, quick grazer. Content in a tiny pen.",
                Shape = PrimitiveType.Capsule, Scale = new Vector3(0.35f, 0.25f, 0.35f), Color = new Color(0.85f, 0.80f, 0.72f),
                Biome = AnimalBiome.Grassland, Speed = 1.8f, MinArea = 4, GroupMin = 2, GroupMax = 6, Appeal = 1f },
            new Spec { SpeciesId = "zebra", Name = "Zebra", Description = "A striped herd animal that needs room to run.",
                Shape = PrimitiveType.Capsule, Scale = new Vector3(0.6f, 0.5f, 0.6f), Color = new Color(0.92f, 0.92f, 0.92f),
                Biome = AnimalBiome.Savanna, Speed = 2.2f, MinArea = 16, GroupMin = 3, GroupMax = 8, Appeal = 2f },
            new Spec { SpeciesId = "lion", Name = "Lion", Description = "An apex predator that wants a large territory.",
                Shape = PrimitiveType.Capsule, Scale = new Vector3(0.7f, 0.5f, 0.7f), Color = new Color(0.85f, 0.62f, 0.25f),
                Biome = AnimalBiome.Savanna, Speed = 1.6f, MinArea = 36, GroupMin = 1, GroupMax = 4, Appeal = 4f },
        };

        [MenuItem("ZooGame/M4/Create Or Update Project Assets")]
        public static void Run()
        {
            M0ProjectSetup.EnsureFolder(PrefabFolder);
            M0ProjectSetup.EnsureFolder(DefinitionFolder);
            M0ProjectSetup.EnsureFolder(Root + "/Materials");

            var marker = LoadOrCreateMarkerMaterial();
            var definitions = new AnimalDefinition[Specs.Length];
            for (int i = 0; i < Specs.Length; i++)
            {
                var prefab = BuildPrefab(Specs[i], marker);
                definitions[i] = BuildDefinition(Specs[i], prefab);
            }
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ZooScenePath, OpenSceneMode.Single);
            // Reload by path: opening a scene can unload assets that were only held in memory.
            for (int i = 0; i < Specs.Length; i++)
                definitions[i] = AssetDatabase.LoadAssetAtPath<AnimalDefinition>(DefinitionFolder + "/" + Specs[i].Name + ".asset");
            BuildSceneContent(definitions);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("M4 project assets created/updated.");
        }

        // ---- Assets ----------------------------------------------------------------------------------------

        static Material LoadOrCreateMarkerMaterial()
        {
            string path = Root + "/Materials/AnimalSelectionMarker.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Sprites/Default")) { name = "AnimalSelectionMarker" };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = new Color(1f, 0.9f, 0.2f, 0.85f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreateBodyMaterial(string name, Color color)
        {
            string path = Root + "/Materials/Animal" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Animal" + name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static GameObject BuildPrefab(Spec spec, Material markerMaterial)
        {
            var root = new GameObject(spec.Name);

            // Body: a primitive standing on the ground. No collider: selection projects to the screen instead.
            var body = GameObject.CreatePrimitive(spec.Shape);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = spec.Scale;
            body.transform.localPosition = new Vector3(0f, spec.Scale.y, 0f);
            body.GetComponent<MeshRenderer>().sharedMaterial = LoadOrCreateBodyMaterial(spec.Name, spec.Color);

            // A small nose so the heading is visible.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            nose.name = "Nose";
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.transform.SetParent(root.transform, false);
            nose.transform.localScale = Vector3.one * spec.Scale.x * 0.45f;
            nose.transform.localPosition = new Vector3(0f, spec.Scale.y * 1.4f, spec.Scale.x * 0.5f);
            nose.GetComponent<MeshRenderer>().sharedMaterial = body.GetComponent<MeshRenderer>().sharedMaterial;

            // Selection ring under the animal.
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Selection Marker";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(root.transform, false);
            float d = Mathf.Max(spec.Scale.x, 0.5f) * 2.2f;
            ring.transform.localScale = new Vector3(d, 0.01f, d);
            ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;
            ring.SetActive(false);

            root.AddComponent<AnimalController>();
            var selectable = root.AddComponent<AnimalSelectable>();
            var so = new SerializedObject(selectable);
            so.FindProperty("selectionMarker").objectReferenceValue = ring;
            so.FindProperty("pickRadius").floatValue = Mathf.Max(0.5f, spec.Scale.x * 1.2f);
            so.FindProperty("pickHeight").floatValue = spec.Scale.y;
            so.ApplyModifiedPropertiesWithoutUndo();

            string path = PrefabFolder + "/" + spec.Name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static AnimalDefinition BuildDefinition(Spec spec, GameObject prefab)
        {
            string path = DefinitionFolder + "/" + spec.Name + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<AnimalDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<AnimalDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            var so = new SerializedObject(def);
            so.FindProperty("speciesId").stringValue = spec.SpeciesId;
            so.FindProperty("displayName").stringValue = spec.Name;
            so.FindProperty("description").stringValue = spec.Description;
            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.FindProperty("biome").enumValueIndex = (int)spec.Biome;
            so.FindProperty("baseMoveSpeed").floatValue = spec.Speed;
            so.FindProperty("minimumEnclosureArea").intValue = spec.MinArea;
            so.FindProperty("preferredGroupMin").intValue = spec.GroupMin;
            so.FindProperty("preferredGroupMax").intValue = spec.GroupMax;
            so.FindProperty("baseAppeal").floatValue = spec.Appeal;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        // ---- Scene -----------------------------------------------------------------------------------------

        static void BuildSceneContent(AnimalDefinition[] definitions)
        {
            var spawner = Object.FindAnyObjectByType<AnimalSpawner>(FindObjectsInactive.Include);
            AnimalSelectionController selection;
            if (spawner == null)
            {
                var go = new GameObject("Animals", typeof(AnimalSpawner), typeof(AnimalSelectionController));
                spawner = go.GetComponent<AnimalSpawner>();
                selection = go.GetComponent<AnimalSelectionController>();
            }
            else selection = spawner.GetComponent<AnimalSelectionController>();

            var hud = Object.FindAnyObjectByType<DebugHud>(FindObjectsInactive.Include);
            var info = Object.FindAnyObjectByType<AnimalInfoPanel>(FindObjectsInactive.Include);
            if (info == null) info = BuildInfoPanel(hud.transform.parent);
            // The panel layout changed in the placement rework: always rebuild it so re-running stays correct.
            var spawn = Object.FindAnyObjectByType<AnimalDebugSpawnPanel>(FindObjectsInactive.Include);
            if (spawn != null) Object.DestroyImmediate(spawn.gameObject);
            spawn = BuildSpawnPanel(hud.transform.parent);

            var binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            var so = new SerializedObject(binder);
            so.FindProperty("animalSpawner").objectReferenceValue = spawner;
            so.FindProperty("animalSelection").objectReferenceValue = selection;
            so.FindProperty("animalInfoPanel").objectReferenceValue = info;
            so.FindProperty("animalDebugPanel").objectReferenceValue = spawn;
            var list = so.FindProperty("animalDefinitions");
            list.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static AnimalInfoPanel BuildInfoPanel(Transform safeArea)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = M0ProjectSetup.NewRect("Animal Info Panel", safeArea, typeof(AnimalInfoPanel));
            M0ProjectSetup.Stretch(root);

            var content = M0ProjectSetup.NewRect("Content", root, typeof(CanvasRenderer), typeof(Image));
            M0ProjectSetup.Anchor(content, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -170f), new Vector2(560f, 190f));
            var bg = content.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = false;

            var label = M0ProjectSetup.NewText("Label", content, font, 28, TextAnchor.UpperLeft);
            M0ProjectSetup.Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(14f, 8f);
            label.rectTransform.offsetMax = new Vector2(-14f, -8f);

            var panel = root.GetComponent<AnimalInfoPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("content").objectReferenceValue = content.gameObject;
            so.FindProperty("label").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
            return panel;
        }

        static AnimalDebugSpawnPanel BuildSpawnPanel(Transform safeArea)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = M0ProjectSetup.NewRect("Animal Debug Spawn Panel", safeArea, typeof(AnimalDebugSpawnPanel));
            M0ProjectSetup.Stretch(root);

            var status = M0ProjectSetup.NewText("Status", root, font, 28, TextAnchor.LowerLeft);
            M0ProjectSetup.Anchor(status.rectTransform, Vector2.zero, Vector2.zero, new Vector2(24f, 700f), new Vector2(700f, 50f));

            var column = M0ProjectSetup.NewRect("Buttons", root, typeof(VerticalLayoutGroup));
            M0ProjectSetup.Anchor(column, Vector2.zero, Vector2.zero, new Vector2(24f, 290f), new Vector2(420f, 400f));
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var template = M0ProjectSetup.NewButton("Spawn Template", column, font, "Species", out var templateLabel);
            templateLabel.fontSize = 30;
            AddLayout(template.gameObject, 420f, 80f);
            template.gameObject.SetActive(false);
            var place = M0ProjectSetup.NewButton("Place", column, font, "Place", out var placeLabel);
            placeLabel.fontSize = 30;
            AddLayout(place.gameObject, 420f, 80f);
            var cancel = M0ProjectSetup.NewButton("Cancel", column, font, "Cancel", out var cancelLabel);
            cancelLabel.fontSize = 30;
            AddLayout(cancel.gameObject, 420f, 80f);

            var panel = root.GetComponent<AnimalDebugSpawnPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("statusLabel").objectReferenceValue = status;
            so.FindProperty("buttonRoot").objectReferenceValue = column;
            so.FindProperty("buttonTemplate").objectReferenceValue = template;
            so.FindProperty("placeButton").objectReferenceValue = place;
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
