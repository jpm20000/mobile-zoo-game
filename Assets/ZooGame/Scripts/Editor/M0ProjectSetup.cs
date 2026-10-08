using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ZooGame.Data;
using ZooGame.Gameplay;
using ZooGame.Input;
using ZooGame.UI;

namespace ZooGame.Editor
{
    /// <summary>
    /// Idempotent generator for M0 assets (GameConfig, Bootstrap and Zoo scenes, ground material),
    /// plus mobile Player Settings. Run via menu ZooGame > M0 > Create Or Update Project Assets.
    /// Placeholder identifiers below are NOT real; change them in Project Settings before shipping.
    /// </summary>
    public static class M0ProjectSetup
    {
        const string Root = "Assets/ZooGame";
        const string ConfigPath = Root + "/ScriptableObjects/GameConfig.asset";
        const string GroundMaterialPath = Root + "/Materials/Ground.mat";
        const string BootstrapScenePath = Root + "/Scenes/Bootstrap.unity";
        const string ZooScenePath = Root + "/Scenes/Zoo.unity";

        const string PlaceholderBundleId = "com.example.mobilezoo";

        [MenuItem("ZooGame/M0/Create Or Update Project Assets")]
        public static void Run()
        {
            var config = CreateConfig();
            ConfigurePlayerSettings();
            var groundMaterial = CreateGroundMaterial();
            CreateBootstrapScene(config);
            CreateZooScene(groundMaterial);
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("M0 project assets created/updated.");
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static GameConfig CreateConfig()
        {
            EnsureFolder(Root + "/ScriptableObjects");
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<GameConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        static void ConfigurePlayerSettings()
        {
            // Landscape only, both landscape directions allowed so devices can flip.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // Render into notch/cutout regions; SafeAreaFitter keeps the UI clear of them.
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.iOS.hideHomeButton = false;

            PlayerSettings.productName = "Mobile Zoo";
            PlayerSettings.bundleVersion = "0.1.0";

            // Placeholder identifiers. Only replace the untouched Unity-template defaults.
            ReplaceTemplateId(NamedBuildTarget.Android);
            ReplaceTemplateId(NamedBuildTarget.iOS);

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        }

        static void ReplaceTemplateId(NamedBuildTarget target)
        {
            var current = PlayerSettings.GetApplicationIdentifier(target);
            if (string.IsNullOrEmpty(current) || current.Contains("unity.template") || current.Contains("Unity-Technologies"))
                PlayerSettings.SetApplicationIdentifier(target, PlaceholderBundleId);
        }

        static Material CreateGroundMaterial()
        {
            EnsureFolder(Root + "/Materials");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (mat != null) return mat;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(shader);
            var green = new Color(0.36f, 0.62f, 0.33f);
            mat.SetColor("_BaseColor", green);
            mat.SetFloat("_Smoothness", 0f);
            AssetDatabase.CreateAsset(mat, GroundMaterialPath);
            return mat;
        }

        static Scene NewScene()
        {
            EnsureFolder(Root + "/Scenes");
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void CreateBootstrapScene(GameConfig config)
        {
            var scene = NewScene();

            var cam = new GameObject("Boot Camera", typeof(Camera));
            var camera = cam.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;

            var go = new GameObject("GameManager", typeof(GameManager));
            var so = new SerializedObject(go.GetComponent<GameManager>());
            // Reload by path: NewScene may have unloaded the in-memory instance.
            so.FindProperty("config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, BootstrapScenePath);
        }

        static void CreateZooScene(Material groundMaterial)
        {
            var scene = NewScene();

            // Isometric-style orthographic camera (placeholder; real camera controls arrive in M1).
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var camera = camGo.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.nearClipPlane = -50f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.75f, 0.9f);
            var camRotation = Quaternion.Euler(30f, 45f, 0f);
            camGo.transform.SetPositionAndRotation(camRotation * Vector3.back * 30f, camRotation); // looks at world origin

            var lightGo = new GameObject("Directional Light", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(5f, 1f, 5f); // 50 x 50 units
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            Object.DestroyImmediate(ground.GetComponent<MeshCollider>());

            // Input
            var input = new GameObject("Input", typeof(PointerInputSource));
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            // UI: Canvas scales to a 1920x1080 reference so layouts adapt to any aspect ratio / DPI.
            var canvasGo = new GameObject("UI Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var safe = NewRect("Safe Area", canvasGo.transform, typeof(SafeAreaFitter));
            Stretch(safe);

            var hud = BuildDebugHud(safe);

            var binderGo = new GameObject("ZooSceneBinder", typeof(ZooSceneBinder));
            var binder = new SerializedObject(binderGo.GetComponent<ZooSceneBinder>());
            binder.FindProperty("debugHud").objectReferenceValue = hud;
            binder.FindProperty("pointerInput").objectReferenceValue = input.GetComponent<PointerInputSource>();
            binder.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ZooScenePath);
        }

        static DebugHud BuildDebugHud(RectTransform parent)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var root = NewRect("Debug HUD", parent, typeof(DebugHud));
            Stretch(root);

            // Info readout: top-left.
            var infoLabel = NewText("Info", root, font, 36, TextAnchor.UpperLeft);
            Anchor(infoLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(520f, 140f));

            // Controls: bottom-left row of large touch targets (>= 48dp at typical densities).
            var pause = NewButton("Pause", root, font, "Pause", out var pauseLabel);
            var sp = NewButton("Speed Paused", root, font, "0x", out _);
            var s1 = NewButton("Speed 1x", root, font, "1x", out _);
            var s2 = NewButton("Speed 2x", root, font, "2x", out _);
            var s3 = NewButton("Speed 3x", root, font, "3x", out _);
            var buttons = new[] { pause, sp, s1, s2, s3 };
            for (int i = 0; i < buttons.Length; i++)
            {
                var size = i == 0 ? new Vector2(220f, 110f) : new Vector2(130f, 110f);
                float x = 24f + (i == 0 ? 0f : 220f + 16f + (i - 1) * (130f + 16f));
                Anchor((RectTransform)buttons[i].transform, Vector2.zero, Vector2.zero, new Vector2(x, 24f), size);
            }

            var hud = root.GetComponent<DebugHud>();
            var so = new SerializedObject(hud);
            so.FindProperty("root").objectReferenceValue = root.gameObject;
            so.FindProperty("infoLabel").objectReferenceValue = infoLabel;
            so.FindProperty("pauseLabel").objectReferenceValue = pauseLabel;
            so.FindProperty("pauseButton").objectReferenceValue = pause;
            so.FindProperty("speedPausedButton").objectReferenceValue = sp;
            so.FindProperty("speed1xButton").objectReferenceValue = s1;
            so.FindProperty("speed2xButton").objectReferenceValue = s2;
            so.FindProperty("speed3xButton").objectReferenceValue = s3;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }

        internal static RectTransform NewRect(string name, Transform parent, params System.Type[] components)
        {
            var types = new System.Type[components.Length + 1];
            types[0] = typeof(RectTransform);
            components.CopyTo(types, 1);
            var go = new GameObject(name, types);
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        internal static void Anchor(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = anchorMin; // corner-anchored
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        internal static Text NewText(string name, Transform parent, Font font, int size, TextAnchor alignment)
        {
            var rt = NewRect(name, parent, typeof(CanvasRenderer), typeof(Text));
            var text = rt.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // Outline keeps the debug text readable on any background.
            rt.gameObject.AddComponent<Shadow>();
            return text;
        }

        internal static Button NewButton(string name, Transform parent, Font font, string label, out Text labelText)
        {
            var rt = NewRect(name, parent, typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var image = rt.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.6f);
            var button = rt.GetComponent<Button>();
            button.targetGraphic = image;

            labelText = NewText("Label", rt, font, 40, TextAnchor.MiddleCenter);
            Stretch(labelText.rectTransform);
            labelText.text = label;
            return button;
        }

        static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScenePath, true),
                new EditorBuildSettingsScene(ZooScenePath, true)
            };
        }
    }
}
