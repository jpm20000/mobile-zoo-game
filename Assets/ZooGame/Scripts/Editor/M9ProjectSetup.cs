using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Data;
using ZooGame.Gameplay;
using ZooGame.Progression;
using ZooGame.UI;

namespace ZooGame.Editor
{
    /// <summary>
    /// Idempotent generator for M9 content: the zoo rating config, beauty values on the existing decoration
    /// placeables, and the rating HUD in the Zoo scene, wired into the binder. Run via
    /// ZooGame > M9 > Create Or Update Project Assets, after the M8 menu.
    /// </summary>
    public static class M9ProjectSetup
    {
        const string Root = "Assets/ZooGame";
        const string ConfigFolder = Root + "/ScriptableObjects/Progression";
        const string ConfigPath = ConfigFolder + "/ZooRatingConfig.asset";
        const string PlaceableFolder = Root + "/ScriptableObjects/Placeables";
        const string ZooScenePath = Root + "/Scenes/Zoo.unity";

        [MenuItem("ZooGame/M9/Create Or Update Project Assets")]
        public static void Run()
        {
            M0ProjectSetup.EnsureFolder(ConfigFolder);

            var config = AssetDatabase.LoadAssetAtPath<ZooRatingConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ZooRatingConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            AssignBeautyValues();
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ZooScenePath, OpenSceneMode.Single);
            BuildSceneContent(config);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("M9 project assets created/updated.");
        }

        // Only objects that are still at 0 are given a starting value, so hand tuning survives a re-run.
        static void AssignBeautyValues()
        {
            SetBeauty("Small Decoration", 5f);
            SetBeauty("Bench", 1f);
            SetBeauty("Enrichment Object", 2f);
        }

        static void SetBeauty(string assetName, float value)
        {
            var def = AssetDatabase.LoadAssetAtPath<PlaceableDefinition>(PlaceableFolder + "/" + assetName + ".asset");
            if (def == null || def.BeautyValue > 0f) return;
            var so = new SerializedObject(def);
            so.FindProperty("beautyValue").floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
        }

        static void BuildSceneContent(ZooRatingConfig config)
        {
            // The layout may change between runs: always rebuild the HUD so re-running stays correct.
            var old = Object.FindAnyObjectByType<ZooRatingHud>(FindObjectsInactive.Include);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var debugHud = Object.FindAnyObjectByType<DebugHud>(FindObjectsInactive.Include);
            var hud = BuildHud(debugHud.transform.parent);

            var binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            var bso = new SerializedObject(binder);
            bso.FindProperty("zooRatingConfig").objectReferenceValue = config;
            bso.FindProperty("zooRatingHud").objectReferenceValue = hud;
            bso.ApplyModifiedPropertiesWithoutUndo();
        }

        static ZooRatingHud BuildHud(Transform safeArea)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = M0ProjectSetup.NewRect("Zoo Rating HUD", safeArea, typeof(ZooRatingHud));
            M0ProjectSetup.Stretch(root);

            // Top-right, under the economy HUD's feedback line.
            var topRight = new Vector2(1f, 1f);
            var summary = M0ProjectSetup.NewText("Summary", root, font, 30, TextAnchor.UpperRight);
            M0ProjectSetup.Anchor(summary.rectTransform, topRight, topRight, new Vector2(-24f, -164f), new Vector2(620f, 40f));
            summary.rectTransform.pivot = topRight;
            var detail = M0ProjectSetup.NewText("Detail", root, font, 22, TextAnchor.UpperRight);
            M0ProjectSetup.Anchor(detail.rectTransform, topRight, topRight, new Vector2(-24f, -204f), new Vector2(620f, 60f));
            detail.rectTransform.pivot = topRight;

            var hud = root.GetComponent<ZooRatingHud>();
            var so = new SerializedObject(hud);
            so.FindProperty("summaryLabel").objectReferenceValue = summary;
            so.FindProperty("detailLabel").objectReferenceValue = detail;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }
    }
}
