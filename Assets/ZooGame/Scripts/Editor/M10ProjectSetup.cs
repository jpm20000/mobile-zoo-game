using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Gameplay;
using ZooGame.Quests;
using ZooGame.UI;

namespace ZooGame.Editor
{
    /// <summary>
    /// Idempotent generator for M10 content: the starter quest definitions and catalogue (from
    /// <see cref="StarterQuests"/>), and the quest panel and tracker in the Zoo scene, wired into the binder.
    /// Run via ZooGame > M10 > Create Or Update Project Assets, after the M9 menu.
    /// </summary>
    public static class M10ProjectSetup
    {
        const string Root = "Assets/ZooGame";
        const string QuestFolder = Root + "/ScriptableObjects/Quests";
        const string CatalogPath = QuestFolder + "/QuestCatalog.asset";
        const string ZooScenePath = Root + "/Scenes/Zoo.unity";
        const int RowCount = 3;

        [MenuItem("ZooGame/M10/Create Or Update Project Assets")]
        public static void Run()
        {
            M0ProjectSetup.EnsureFolder(QuestFolder);
            var catalog = CreateQuestAssets();

            var scene = EditorSceneManager.OpenScene(ZooScenePath, OpenSceneMode.Single);
            BuildSceneContent(catalog);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("M10 project assets created/updated.");
        }

        // ---- Assets ----------------------------------------------------------------------------------------

        static QuestCatalog CreateQuestAssets()
        {
            var sources = StarterQuests.Create();
            var assets = new QuestDefinition[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                string path = QuestFolder + "/" + sources[i].QuestId + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<QuestDefinition>(path);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(sources[i], path);
                    assets[i] = sources[i];
                }
                else
                {
                    // Keep the asset (and every reference to it); refresh its content from the spec.
                    EditorUtility.CopySerialized(sources[i], existing);
                    EditorUtility.SetDirty(existing);
                    Object.DestroyImmediate(sources[i]);
                    assets[i] = existing;
                }
            }

            var catalog = AssetDatabase.LoadAssetAtPath<QuestCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<QuestCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.Set(assets);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        // ---- Scene -----------------------------------------------------------------------------------------

        static void BuildSceneContent(QuestCatalog catalog)
        {
            // The layout may change between runs: always rebuild the UI so re-running stays correct.
            var oldPanel = Object.FindAnyObjectByType<QuestPanel>(FindObjectsInactive.Include);
            if (oldPanel != null) Object.DestroyImmediate(oldPanel.gameObject);
            var oldTracker = Object.FindAnyObjectByType<QuestTrackerHud>(FindObjectsInactive.Include);
            if (oldTracker != null) Object.DestroyImmediate(oldTracker.gameObject);

            var debugHud = Object.FindAnyObjectByType<DebugHud>(FindObjectsInactive.Include);
            var safeArea = debugHud.transform.parent;
            var tracker = BuildTracker(safeArea);
            var panel = BuildPanel(safeArea);

            var binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            var bso = new SerializedObject(binder);
            bso.FindProperty("questCatalog").objectReferenceValue = catalog;
            bso.FindProperty("questPanel").objectReferenceValue = panel;
            bso.FindProperty("questTracker").objectReferenceValue = tracker;
            bso.ApplyModifiedPropertiesWithoutUndo();
        }

        // Top-centre, under the build status lines: the pinned/next quest and its claim button.
        static QuestTrackerHud BuildTracker(Transform safeArea)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = M0ProjectSetup.NewRect("Quest Tracker", safeArea, typeof(QuestTrackerHud));
            M0ProjectSetup.Stretch(root);

            var topCentre = new Vector2(0.5f, 1f);
            var label = M0ProjectSetup.NewText("Label", root, font, 26, TextAnchor.UpperLeft);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            M0ProjectSetup.Anchor(label.rectTransform, topCentre, topCentre, new Vector2(0f, -300f), new Vector2(700f, 130f));

            var claim = M0ProjectSetup.NewButton("Claim", root, font, "Claim", out var claimLabel);
            claimLabel.fontSize = 26;
            M0ProjectSetup.Anchor((RectTransform)claim.transform, topCentre, topCentre, new Vector2(0f, -436f), new Vector2(700f, 56f));

            var hud = root.GetComponent<QuestTrackerHud>();
            var so = new SerializedObject(hud);
            so.FindProperty("label").objectReferenceValue = label;
            so.FindProperty("claimButton").objectReferenceValue = claim;
            so.FindProperty("claimLabel").objectReferenceValue = claimLabel;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }

        static QuestPanel BuildPanel(Transform safeArea)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = M0ProjectSetup.NewRect("Quest Panel", safeArea, typeof(QuestPanel));
            M0ProjectSetup.Stretch(root);

            var content = M0ProjectSetup.NewRect("Content", root, typeof(CanvasRenderer), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var topCentre = new Vector2(0.5f, 1f);
            M0ProjectSetup.Anchor(content, topCentre, topCentre, new Vector2(0f, -504f), new Vector2(700f, 100f));
            var bg = content.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);
            bg.raycastTarget = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Column(content.GetComponent<VerticalLayoutGroup>(), 10);

            var toggle = LayoutButton("Toggle", content, font, "Quests", 26, 56f, out var toggleLabel);

            var body = M0ProjectSetup.NewRect("Body", content, typeof(VerticalLayoutGroup));
            Column(body.GetComponent<VerticalLayoutGroup>(), 0);
            var header = LayoutText("Header", body, font, 22, 56f);

            var rows = new QuestRowView[RowCount];
            for (int i = 0; i < RowCount; i++) rows[i] = BuildRow(body, font, i);

            var pageButton = LayoutButton("Page", body, font, "More", 24, 48f, out _);
            var pageLabel = LayoutText("Page Label", body, font, 20, 28f);

            var panel = root.GetComponent<QuestPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("body").objectReferenceValue = body.gameObject;
            so.FindProperty("toggleButton").objectReferenceValue = toggle;
            so.FindProperty("toggleLabel").objectReferenceValue = toggleLabel;
            so.FindProperty("headerLabel").objectReferenceValue = header;
            so.FindProperty("pageButton").objectReferenceValue = pageButton;
            so.FindProperty("pageLabel").objectReferenceValue = pageLabel;

            var rowsProperty = so.FindProperty("rows");
            rowsProperty.arraySize = RowCount;
            for (int i = 0; i < RowCount; i++)
            {
                var element = rowsProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("root").objectReferenceValue = rows[i].root;
                element.FindPropertyRelative("titleLabel").objectReferenceValue = rows[i].titleLabel;
                element.FindPropertyRelative("objectivesLabel").objectReferenceValue = rows[i].objectivesLabel;
                element.FindPropertyRelative("rewardLabel").objectReferenceValue = rows[i].rewardLabel;
                element.FindPropertyRelative("claimButton").objectReferenceValue = rows[i].claimButton;
                element.FindPropertyRelative("claimLabel").objectReferenceValue = rows[i].claimLabel;
                element.FindPropertyRelative("pinButton").objectReferenceValue = rows[i].pinButton;
                element.FindPropertyRelative("pinLabel").objectReferenceValue = rows[i].pinLabel;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // Starts collapsed; the panel script shows the body when the player opens it.
            body.gameObject.SetActive(false);
            return panel;
        }

        static QuestRowView BuildRow(Transform parent, Font font, int index)
        {
            var row = M0ProjectSetup.NewRect("Row " + (index + 1), parent, typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
            row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            row.GetComponent<Image>().raycastTarget = false;
            Column(row.GetComponent<VerticalLayoutGroup>(), 8);

            var title = LayoutText("Title", row, font, 26, 34f);
            var objectives = LayoutText("Objectives", row, font, 22, 110f);
            var reward = LayoutText("Reward", row, font, 22, 30f);

            var buttons = M0ProjectSetup.NewRect("Buttons", row, typeof(HorizontalLayoutGroup));
            var layout = buttons.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var claim = LayoutButton("Claim", buttons, font, "Claim", 24, 52f, out var claimLabel);
            var pin = LayoutButton("Pin", buttons, font, "Pin", 24, 52f, out var pinLabel);

            return new QuestRowView
            {
                root = row.gameObject,
                titleLabel = title,
                objectivesLabel = objectives,
                rewardLabel = reward,
                claimButton = claim,
                claimLabel = claimLabel,
                pinButton = pin,
                pinLabel = pinLabel
            };
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

        static Text LayoutText(string name, Transform parent, Font font, int size, float height)
        {
            var text = M0ProjectSetup.NewText(name, parent, font, size, TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return text;
        }

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
