using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Gameplay;
using ZooGame.Staff;
using ZooGame.UI;

namespace ZooGame.Editor
{
    /// <summary>
    /// Idempotent generator for M8 content: the staff config, a placeholder staff body, and the scene object and
    /// staff panel in the Zoo scene, wired into the binder. Run via ZooGame > M8 > Create Or Update Project Assets,
    /// after the M7 menu.
    /// </summary>
    public static class M8ProjectSetup
    {
        const string Root = "Assets/ZooGame";
        const string ConfigFolder = Root + "/ScriptableObjects/Staff";
        const string ConfigPath = ConfigFolder + "/StaffConfig.asset";
        const string PrefabFolder = Root + "/Prefabs/Staff";
        const string ZooScenePath = Root + "/Scenes/Zoo.unity";

        [MenuItem("ZooGame/M8/Create Or Update Project Assets")]
        public static void Run()
        {
            M0ProjectSetup.EnsureFolder(ConfigFolder);
            M0ProjectSetup.EnsureFolder(PrefabFolder);
            M0ProjectSetup.EnsureFolder(Root + "/Materials");

            var config = AssetDatabase.LoadAssetAtPath<StaffConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<StaffConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            var prefab = BuildStaffPrefab();
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ZooScenePath, OpenSceneMode.Single);
            BuildSceneContent(AssetDatabase.LoadAssetAtPath<StaffConfig>(ConfigPath), prefab);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("M8 project assets created/updated.");
        }

        // ---- Prefab ----------------------------------------------------------------------------------------

        static Material BodyMaterial()
        {
            string path = Root + "/Materials/Staff_Body.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Staff_Body" };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", Color.white); // tinted per role at runtime
            mat.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static StaffController BuildStaffPrefab()
        {
            var root = new GameObject("Staff", typeof(StaffController));
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.4f, 0.5f, 0.4f);
            body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            body.GetComponent<MeshRenderer>().sharedMaterial = BodyMaterial();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/Staff.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<StaffController>();
        }

        // ---- Scene -----------------------------------------------------------------------------------------

        static void BuildSceneContent(StaffConfig config, StaffController prefab)
        {
            var spawner = Object.FindAnyObjectByType<StaffSpawner>(FindObjectsInactive.Include);
            if (spawner == null) spawner = new GameObject("Staff", typeof(StaffSpawner)).GetComponent<StaffSpawner>();
            var sso = new SerializedObject(spawner);
            sso.FindProperty("prefab").objectReferenceValue = prefab;
            sso.ApplyModifiedPropertiesWithoutUndo();

            // The layout may change between runs: always rebuild the panel so re-running stays correct.
            var old = Object.FindAnyObjectByType<StaffPanel>(FindObjectsInactive.Include);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var hud = Object.FindAnyObjectByType<DebugHud>(FindObjectsInactive.Include);
            var panel = BuildPanel(hud.transform.parent);

            var binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            var bso = new SerializedObject(binder);
            bso.FindProperty("staffConfig").objectReferenceValue = config;
            bso.FindProperty("staffSpawner").objectReferenceValue = spawner;
            bso.FindProperty("staffPanel").objectReferenceValue = panel;
            bso.ApplyModifiedPropertiesWithoutUndo();
        }

        static StaffPanel BuildPanel(Transform safeArea)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = M0ProjectSetup.NewRect("Staff Panel", safeArea, typeof(StaffPanel));
            M0ProjectSetup.Stretch(root);

            // Top-right, left of the build toolbar and below the economy HUD and the build status lines.
            var content = M0ProjectSetup.NewRect("Content", root, typeof(CanvasRenderer), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            M0ProjectSetup.Anchor(content, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-300f, -290f), new Vector2(460f, 100f));
            content.pivot = new Vector2(1f, 1f); // grows downwards, anchored by its top-right corner
            content.anchoredPosition = new Vector2(-300f, -290f);
            var bg = content.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);
            bg.raycastTarget = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Column(content.GetComponent<VerticalLayoutGroup>(), 12);

            var status = LayoutText("Status", content, font, 24, 76f);

            var hireGrid = M0ProjectSetup.NewRect("Hire", content, typeof(GridLayoutGroup));
            var grid = hireGrid.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(212f, 52f);
            grid.spacing = new Vector2(8f, 8f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            var hireVendor = LayoutButton("Hire Vendor", hireGrid, font, "Hire Vendor", 24, 52f);
            var hireKeeper = LayoutButton("Hire Keeper", hireGrid, font, "Hire Keeper", 24, 52f);
            var hireMaint = LayoutButton("Hire Maintenance", hireGrid, font, "Hire Maint.", 24, 52f);
            var hireVet = LayoutButton("Hire Veterinarian", hireGrid, font, "Hire Vet", 24, 52f);
            hireGrid.gameObject.AddComponent<LayoutElement>().preferredHeight = 112f;

            var staffLabel = LayoutText("Staff Label", content, font, 24, 36f);
            var staffRow = Row("Staff Row", content);
            var next = LayoutButton("Next Staff", staffRow, font, "Next staff", 24, 52f);
            var dismiss = LayoutButton("Dismiss", staffRow, font, "Dismiss", 24, 52f);

            var facility = M0ProjectSetup.NewRect("Facility Section", content, typeof(VerticalLayoutGroup));
            Column(facility.GetComponent<VerticalLayoutGroup>(), 0);
            var facilityLabel = LayoutText("Facility Label", facility, font, 24, 70f);
            var assign = LayoutButton("Assign", facility, font, "Assign", 24, 52f, out var assignLabel);
            var unassign = LayoutButton("Unassign", facility, font, "Unassign vendor", 24, 52f);

            var debugRow = Row("Debug Row", content);
            var postKeeper = LayoutButton("Post Keeper Task", debugRow, font, "Keeper task", 22, 48f);
            var postMaint = LayoutButton("Post Maintenance Task", debugRow, font, "Maint. task", 22, 48f);
            var postVet = LayoutButton("Post Veterinarian Task", debugRow, font, "Vet task", 22, 48f);

            var panel = root.GetComponent<StaffPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("statusLabel").objectReferenceValue = status;
            so.FindProperty("hireVendorButton").objectReferenceValue = hireVendor;
            so.FindProperty("hireKeeperButton").objectReferenceValue = hireKeeper;
            so.FindProperty("hireMaintenanceButton").objectReferenceValue = hireMaint;
            so.FindProperty("hireVeterinarianButton").objectReferenceValue = hireVet;
            so.FindProperty("staffLabel").objectReferenceValue = staffLabel;
            so.FindProperty("nextStaffButton").objectReferenceValue = next;
            so.FindProperty("dismissButton").objectReferenceValue = dismiss;
            so.FindProperty("facilitySection").objectReferenceValue = facility.gameObject;
            so.FindProperty("facilityLabel").objectReferenceValue = facilityLabel;
            so.FindProperty("assignButton").objectReferenceValue = assign;
            so.FindProperty("assignButtonLabel").objectReferenceValue = assignLabel;
            so.FindProperty("unassignButton").objectReferenceValue = unassign;
            so.FindProperty("postKeeperTaskButton").objectReferenceValue = postKeeper;
            so.FindProperty("postMaintenanceTaskButton").objectReferenceValue = postMaint;
            so.FindProperty("postVeterinarianTaskButton").objectReferenceValue = postVet;
            so.ApplyModifiedPropertiesWithoutUndo();
            return panel;
        }

        // ---- Layout helpers --------------------------------------------------------------------------------

        static void Column(VerticalLayoutGroup layout, int padding)
        {
            layout.spacing = 8f;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        static RectTransform Row(string name, Transform parent)
        {
            var rt = M0ProjectSetup.NewRect(name, parent, typeof(HorizontalLayoutGroup));
            var layout = rt.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rt;
        }

        static Text LayoutText(string name, Transform parent, Font font, int size, float height)
        {
            var text = M0ProjectSetup.NewText(name, parent, font, size, TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return text;
        }

        static Button LayoutButton(string name, Transform parent, Font font, string text, int size, float height) =>
            LayoutButton(name, parent, font, text, size, height, out _);

        static Button LayoutButton(string name, Transform parent, Font font, string text, int size, float height, out Text label)
        {
            var b = M0ProjectSetup.NewButton(name, parent, font, text, out label);
            label.fontSize = size;
            var layout = b.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
            return b;
        }
    }
}
