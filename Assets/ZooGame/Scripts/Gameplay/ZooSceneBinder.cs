using UnityEngine;
using UnityEngine.SceneManagement;
using ZooGame.Animals;
using ZooGame.Cameras;
using ZooGame.Construction;
using ZooGame.Data;
using ZooGame.Input;
using ZooGame.Placement;
using ZooGame.UI;
using ZooGame.Visitors;
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
        [Header("Animals (M4)")]
        [SerializeField] AnimalSpawner animalSpawner;
        [SerializeField] AnimalSelectionController animalSelection;
        [SerializeField] AnimalInfoPanel animalInfoPanel;
        [SerializeField] AnimalDebugSpawnPanel animalDebugPanel;
        [SerializeField] AnimalDefinition[] animalDefinitions;
        [Header("Animal needs (M5)")]
        [Tooltip("Global needs tuning. Optional: built-in defaults are used when empty.")]
        [SerializeField] AnimalNeedsConfig animalNeedsConfig;
        [Header("Visitors (M6)")]
        [Tooltip("Visitor tuning. Optional: built-in defaults are used when empty.")]
        [SerializeField] VisitorConfig visitorConfig;
        [SerializeField] VisitorSpawner visitorSpawner;
        [SerializeField] VisitorSelectionController visitorSelection;
        [SerializeField] VisitorInfoPanel visitorInfoPanel;
        [SerializeField] VisitorDebugPanel visitorDebugPanel;
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

        /// <summary>The authoritative animal records; null if animals are not set up in the scene.</summary>
        public IAnimalRegistry Animals { get; private set; }

        public AnimalSpawner AnimalSpawner => animalSpawner;
        public AnimalSelectionController AnimalSelection => animalSelection;
        public IAnimalEnclosureService AnimalEnclosures { get; private set; }

        /// <summary>Player placement of new animals into enclosures (driven by the build-mode controller).</summary>
        public AnimalPlacementTool AnimalPlacement { get; private set; }

        /// <summary>Cached per-enclosure habitat summaries (area, terrain, resources, residents).</summary>
        public IEnclosureHabitatService AnimalHabitats { get; private set; }

        /// <summary>The welfare simulation, driven by the GameClock.</summary>
        public AnimalNeedsSystem AnimalNeeds { get; private set; }

        public IVisitorRegistry Visitors { get; private set; }
        public VisitorSpawner VisitorSpawner => visitorSpawner;
        public VisitorSelectionController VisitorSelection => visitorSelection;
        public VisitorNavigationService VisitorNavigation { get; private set; }
        public VisitorDestinationService VisitorDestinations { get; private set; }
        public VisitorDecisionService VisitorDecisions { get; private set; }
        public VisitorSimulationService VisitorSimulation { get; private set; }

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

            if (animalSpawner != null && Construction != null)
            {
                Animals = new AnimalRegistry();
                AnimalEnclosures = new AnimalEnclosureService(Grid, Construction.Enclosures);
                var resolver = new AnimalDefinitionResolver(animalDefinitions);
                animalSpawner.Bind(Animals, resolver, AnimalEnclosures, game.Clock);

                AnimalHabitats = new EnclosureHabitatService(Grid, Construction.Enclosures, Placement, Animals);
                AnimalNeeds = new AnimalNeedsSystem(Animals, resolver, AnimalEnclosures, AnimalHabitats,
                    animalNeedsConfig != null ? animalNeedsConfig : AnimalNeedsConfig.CreateDefault());
                var driver = animalSpawner.GetComponent<AnimalNeedsDriver>();
                if (driver == null) driver = animalSpawner.gameObject.AddComponent<AnimalNeedsDriver>();
                driver.Bind(AnimalNeeds, game.Clock);

                if (animalSelection != null)
                {
                    animalSelection.Bind(pointerInput, cameraController.GetComponent<Camera>(), animalSpawner,
                        DpToPixels(28f));
                    // A construction or placement tool owns taps while it is active.
                    animalSelection.CanSelect = () => buildModeController == null || buildModeController.Mode == BuildMode.None;
                    if (animalInfoPanel != null) animalInfoPanel.Bind(animalSelection, AnimalNeeds);
                }
                if (buildModeController != null)
                {
                    AnimalPlacement = new AnimalPlacementTool(cameraController.GetComponent<Camera>(), Grid, animalSpawner);
                    buildModeController.RegisterTool(AnimalPlacement);
                    if (animalDebugPanel != null) animalDebugPanel.Bind(AnimalPlacement, buildModeController, animalDefinitions);
                }

                if (visitorSpawner != null && Placement != null) BindVisitors(game, resolver);
            }

            debugHud.Bind(game, game.Config.ShowDebugHud, gridOverlay.Toggle);
        }

        void BindVisitors(GameManager game, IAnimalDefinitionResolver animalDefinitionResolver)
        {
            var config = visitorConfig != null ? visitorConfig : VisitorConfig.CreateDefault();
            var rng = config.RandomSeed != 0 ? new System.Random(config.RandomSeed) : new System.Random();

            Visitors = new VisitorRegistry();
            VisitorNavigation = new VisitorNavigationService(Grid, Construction);
            VisitorDestinations = new VisitorDestinationService(Grid, Construction, Placement, VisitorNavigation,
                Animals, animalDefinitionResolver);
            VisitorDecisions = new VisitorDecisionService(config, Visitors, VisitorNavigation, VisitorDestinations, rng);
            VisitorSimulation = new VisitorSimulationService(config, Visitors, VisitorDecisions);
            visitorSpawner.Bind(config, Visitors, VisitorDecisions, VisitorDestinations, Grid, rng);

            var driver = visitorSpawner.GetComponent<VisitorSystemDriver>();
            if (driver == null) driver = visitorSpawner.gameObject.AddComponent<VisitorSystemDriver>();
            driver.Bind(game.Clock, VisitorDecisions, VisitorSimulation, visitorSpawner);

            var camera = cameraController.GetComponent<Camera>();
            if (visitorSelection != null)
            {
                visitorSelection.Bind(pointerInput, camera, visitorSpawner, DpToPixels(28f));
                visitorSelection.CanSelect = () => buildModeController == null || buildModeController.Mode == BuildMode.None;
                if (visitorInfoPanel != null) visitorInfoPanel.Bind(visitorSelection, VisitorSimulation, VisitorDecisions);
            }
            if (visitorDebugPanel != null) visitorDebugPanel.Bind(visitorSpawner, VisitorDestinations);
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
