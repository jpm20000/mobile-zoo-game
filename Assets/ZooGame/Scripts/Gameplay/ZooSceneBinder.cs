using UnityEngine;
using UnityEngine.SceneManagement;
using ZooGame.Cameras;
using ZooGame.Construction;
using ZooGame.Data;
using ZooGame.Input;
using ZooGame.Placement;
using ZooGame.UI;
using ZooGame.World;

namespace ZooGame.Gameplay
{
    /// <summary>
    /// Composition point for the Zoo scene: hands the persistent game context to scene-level components once, at Awake.
    /// Add future scene-level systems here (or to a sibling binder) rather than letting them look up GameManager.
    /// </summary>
    public sealed class ZooSceneBinder : MonoBehaviour
    {
        [SerializeField] DebugHud debugHud;
        [SerializeField] PointerInputSource pointerInput;
        [SerializeField] WorldConfig worldConfig;
        [SerializeField] CameraConfig cameraConfig;
        [SerializeField] TerrainView terrainView;
        [SerializeField] GridDebugOverlay gridOverlay;
        [SerializeField] ZooCameraController cameraController;
        [Header("Placement (M2)")]
        [SerializeField] PlacementController placementController;
        [SerializeField] PlaceableCatalog placementCatalog;
        [SerializeField] PlacementConfig placementConfig;
        [SerializeField] PlacementTestPanel placementPanel;
        [Header("Construction (M3)")]
        [SerializeField] BuildModeController buildModeController;
        [SerializeField] ConstructionCatalog constructionCatalog;
        [SerializeField] ConstructionConfig constructionConfig;
        [SerializeField] PathRenderer pathRenderer;
        [SerializeField] FenceRenderer fenceRenderer;
        [SerializeField] ConstructionPreview constructionPreview;
        [SerializeField] ConstructionTestPanel constructionPanel;
        [Tooltip("Editor convenience: if this scene is entered directly, load the Bootstrap scene first.")]
        [SerializeField] string bootstrapSceneName = "Bootstrap";

        /// <summary>The authoritative zoo map for this scene. Hand it to systems from here; do not search for it.</summary>
        public ZooGrid Grid { get; private set; }

        /// <summary>Placed objects and their occupancy; null until the Zoo scene is bound or if placement is not set up.</summary>
        public PlacementMap Placement => placementController != null ? placementController.Map : null;

        public PlacementController PlacementController => placementController;

        /// <summary>Paths, fences, gates and enclosures; null if construction is not set up in the scene.</summary>
        public ConstructionModel Construction { get; private set; }

        public BuildModeController Builder => buildModeController;

        void Awake()
        {
            var game = GameManager.Instance;
            if (game == null)
            {
#if UNITY_EDITOR
                SceneManager.LoadScene(bootstrapSceneName);
#else
                Debug.LogError("Zoo scene started without the Bootstrap scene.");
#endif
                return;
            }

            pointerInput.SetUiBlocker(new EventSystemPointerBlocker());

            Grid = new ZooGrid(worldConfig.ToGridSettings());
            terrainView.Bind(Grid, worldConfig.ToTerrainPalette());

            bool debugBuild = Application.isEditor || Debug.isDebugBuild;
            gridOverlay.Bind(Grid, worldConfig.OverlayMajorLineEvery, debugBuild && worldConfig.ShowGridOverlay);

            pointerInput.Gestures.Settings = new GestureSettings(
                DpToPixels(cameraConfig.DragThresholdDp), cameraConfig.MaxTapSeconds);
            cameraController.Bind(pointerInput, Grid, cameraConfig, UnlockedAreaCentre(worldConfig.ToGridSettings()));

            if (placementController != null)
            {
                placementController.Bind(cameraController.GetComponent<Camera>(), Grid, placementConfig, placementCatalog);
                if (placementPanel != null) placementPanel.Bind(placementController, placementCatalog);
            }

            if (buildModeController != null && placementController != null)
            {
                Construction = new ConstructionModel(Grid);
                constructionPreview.Bind(Grid, constructionConfig);
                pathRenderer.Bind(Grid, constructionConfig, constructionCatalog.DefaultPath);
                fenceRenderer.Bind(Construction, constructionConfig,
                    constructionCatalog.FirstOfKind(EdgeKind.Fence), constructionCatalog.FirstOfKind(EdgeKind.Gate));
                var context = new BuildContext
                {
                    Camera = cameraController.GetComponent<Camera>(),
                    Grid = Grid,
                    Model = Construction,
                    Preview = constructionPreview,
                    Config = constructionConfig
                };
                buildModeController.Bind(pointerInput, context, placementController, constructionCatalog);
                if (constructionPanel != null) constructionPanel.Bind(buildModeController, Construction);
            }

            debugHud.Bind(game, game.Config.ShowDebugHud, gridOverlay.Toggle);
        }

        static float DpToPixels(float dp)
        {
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f; // some platforms/Editor report 0
            return dp * dpi / 160f;
        }

        static Vector3 UnlockedAreaCentre(GridSettings s)
        {
            float x = s.Origin.x + (s.UnlockedMinX + s.UnlockedWidth * 0.5f) * s.CellSize;
            float z = s.Origin.z + (s.UnlockedMinZ + s.UnlockedDepth * 0.5f) * s.CellSize;
            return new Vector3(x, s.Origin.y, z);
        }
    }
}
