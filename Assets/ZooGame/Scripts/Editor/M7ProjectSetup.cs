using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZooGame.Animals;
using ZooGame.Data;
using ZooGame.Economy;
using ZooGame.Gameplay;
using ZooGame.UI;

namespace ZooGame.Editor
{
    /// <summary>
    /// Idempotent generator for M7 content: the economy config asset, starting prices on the existing definitions
    /// (placeables, paths, fences, animals) and the economy HUD in the Zoo scene. Prices are only written while a
    /// definition is still free (cost 0), so re-running never overwrites values tuned by hand.
    /// Run via ZooGame > M7 > Create Or Update Project Assets, after the M6 menu.
    /// </summary>
    public static class M7ProjectSetup
    {
        const string Root = "Assets/ZooGame";
        const string ConfigFolder = Root + "/ScriptableObjects/Economy";
        const string ConfigPath = ConfigFolder + "/EconomyConfig.asset";
        const string ZooScenePath = Root + "/Scenes/Zoo.unity";

        // Placeable id -> starting Money cost.
        static readonly Dictionary<string, int> PlaceableCosts = new Dictionary<string, int>
        {
            { "zoo-entrance", 500 }, { "food_stall", 300 }, { "drink-stall", 250 }, { "toilet", 150 }, { "bench", 50 },
            { "small_decoration", 40 }, { "utility_building", 400 },
            { "drinking-water-source", 100 }, { "food-source", 80 }, { "shelter", 200 }, { "enrichment-object", 150 },
        };

        // Species id -> adoption cost.
        static readonly Dictionary<string, int> AnimalCosts = new Dictionary<string, int>
        {
            { "rabbit", 300 }, { "zebra", 1200 }, { "lion", 2500 },
        };

        [MenuItem("ZooGame/M7/Create Or Update Project Assets")]
        public static void Run()
        {
            M0ProjectSetup.EnsureFolder(ConfigFolder);

            var config = AssetDatabase.LoadAssetAtPath<EconomyConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<EconomyConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            PriceDefinitions();
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ZooScenePath, OpenSceneMode.Single);
            BuildSceneContent(AssetDatabase.LoadAssetAtPath<EconomyConfig>(ConfigPath));
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("M7 project assets created/updated.");
        }

        // ---- Prices ----------------------------------------------------------------------------------------

        static void PriceDefinitions()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:PlaceableDefinition"))
            {
                var def = AssetDatabase.LoadAssetAtPath<PlaceableDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && PlaceableCosts.TryGetValue(def.Id, out int cost)) SetIfFree(def, "cost", cost);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:PathDefinition"))
            {
                var def = AssetDatabase.LoadAssetAtPath<PathDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null) SetIfFree(def, "costPerCell", 2);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:FenceDefinition"))
            {
                var def = AssetDatabase.LoadAssetAtPath<FenceDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null) SetIfFree(def, "costPerSegment", def.Kind == ZooGame.World.EdgeKind.Gate ? 8 : 5);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:AnimalDefinition"))
            {
                var def = AssetDatabase.LoadAssetAtPath<AnimalDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && AnimalCosts.TryGetValue(def.SpeciesId, out int cost)) SetIfFree(def, "adoptionCost", cost);
            }
        }

        static void SetIfFree(Object asset, string field, int value)
        {
            var so = new SerializedObject(asset);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning("Missing field " + field + " on " + asset.name); return; }
            if (prop.intValue != 0) return;
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        // ---- Scene -----------------------------------------------------------------------------------------

        static void BuildSceneContent(EconomyConfig config)
        {
            var debugHud = Object.FindAnyObjectByType<DebugHud>(FindObjectsInactive.Include);
            // The layout may change between runs: always rebuild so re-running stays correct.
            var old = Object.FindAnyObjectByType<EconomyHud>(FindObjectsInactive.Include);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var hud = BuildHud(debugHud.transform.parent);

            var binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            var bso = new SerializedObject(binder);
            bso.FindProperty("economyConfig").objectReferenceValue = config;
            bso.FindProperty("economyHud").objectReferenceValue = hud;
            bso.ApplyModifiedPropertiesWithoutUndo();
        }

        static EconomyHud BuildHud(Transform safeArea)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = M0ProjectSetup.NewRect("Economy HUD", safeArea, typeof(EconomyHud));
            M0ProjectSetup.Stretch(root);

            // Top-right corner, clear of the toolbar (right, vertically centred) and the left-hand panels.
            var topRight = new Vector2(1f, 1f);
            var money = M0ProjectSetup.NewText("Money", root, font, 40, TextAnchor.UpperRight);
            M0ProjectSetup.Anchor(money.rectTransform, topRight, topRight, new Vector2(-24f, -24f), new Vector2(620f, 52f));
            var tokens = M0ProjectSetup.NewText("Tokens", root, font, 30, TextAnchor.UpperRight);
            M0ProjectSetup.Anchor(tokens.rectTransform, topRight, topRight, new Vector2(-24f, -78f), new Vector2(620f, 40f));
            var feedback = M0ProjectSetup.NewText("Feedback", root, font, 30, TextAnchor.UpperRight);
            M0ProjectSetup.Anchor(feedback.rectTransform, topRight, topRight, new Vector2(-24f, -120f), new Vector2(760f, 40f));
            feedback.enabled = false;

            var hud = root.GetComponent<EconomyHud>();
            var so = new SerializedObject(hud);
            so.FindProperty("moneyLabel").objectReferenceValue = money;
            so.FindProperty("tokensLabel").objectReferenceValue = tokens;
            so.FindProperty("feedbackLabel").objectReferenceValue = feedback;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }
    }
}
