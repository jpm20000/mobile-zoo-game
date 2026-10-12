using UnityEngine;
using UnityEngine.SceneManagement;
using ZooGame.Animals;
using ZooGame.Cameras;
using ZooGame.Construction;
using ZooGame.Data;
using ZooGame.Economy;
using ZooGame.Input;
using ZooGame.Placement;
using ZooGame.Progression;
using ZooGame.Quests;
using ZooGame.Staff;
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
        [Header("Economy (M7)")]
        [Tooltip("Starting balances, prices and refund rate. Optional: built-in defaults are used when empty.")]
        [SerializeField] EconomyConfig economyConfig;
        [SerializeField] EconomyHud economyHud;
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
        [Header("Staff (M8)")]
        [Tooltip("Hire costs, staff cap and walking speed. Optional: built-in defaults are used when empty.")]
        [SerializeField] StaffConfig staffConfig;
        [SerializeField] StaffSpawner staffSpawner;
        [SerializeField] StaffPanel staffPanel;
        [Header("Zoo rating (M9)")]
        [Tooltip("Rating targets, weights and star thresholds. Optional: built-in defaults are used when empty.")]
        [SerializeField] ZooRatingConfig zooRatingConfig;
        [SerializeField] ZooRatingHud zooRatingHud;
        [Header("Quests (M10)")]
        [Tooltip("The quests in this game. Optional: without a catalogue there are no quests.")]
        [SerializeField] QuestCatalog questCatalog;
        [SerializeField] QuestPanel questPanel;
        [SerializeField] QuestTrackerHud questTracker;
        [Tooltip("Editor convenience: if this scene is entered directly, load the Bootstrap scene first.")]
        [SerializeField] string bootstrapSceneName = "Bootstrap";

        /// <summary>The authoritative zoo map for this scene. Hand it to systems from here; do not search for it.</summary>
        public ZooGrid Grid { get; private set; }

        /// <summary>Placed objects and their occupancy; null until the Zoo scene is bound or if placement is not set up.</summary>
        public PlacementMap Placement => placementController != null ? placementController.Map : null;

        public PlacementController PlacementController => placementController;

        /// <summary>The authoritative currency balances and transaction history (Money and Conservation Tokens).</summary>
        public EconomyService Economy { get; private set; }

        /// <summary>Money purchase validation and refunds for construction, placement and adoption.</summary>
        public PurchaseService Purchases { get; private set; }

        /// <summary>Visitor-generated Money income.</summary>
        public RevenueService Revenue { get; private set; }

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

        public IStaffRegistry Staff { get; private set; }
        public StaffSpawner StaffSpawner => staffSpawner;
        public StaffFacilityRegistry StaffFacilities { get; private set; }
        /// <summary>The only way to assign staff to facilities (UI must not write the ids itself).</summary>
        public StaffAssignmentService StaffAssignments { get; private set; }
        public StaffTaskPool StaffTasks { get; private set; }
        public StaffHiringService StaffHiring { get; private set; }
        public StaffWorkService StaffWork { get; private set; }

        /// <summary>The zoo rating and star level (null if visitors/animals are not set up in the scene).</summary>
        public ZooRatingService Rating { get; private set; }

        /// <summary>Zoo XP and level. Always present once the scene is bound.</summary>
        public ProgressionService Progression { get; private set; }

        /// <summary>Quest lifecycle and progress; null when no quest catalogue is assigned.</summary>
        public QuestService Quests { get; private set; }

        QuestEventBridge _questBridge;

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

            // The economy exists before anything that charges or earns is bound.
            var economySettings = (economyConfig != null ? economyConfig : EconomyConfig.CreateDefault()).ToSettings();
            Economy = new EconomyService(economySettings, () => game.Clock.ElapsedTime);
            Purchases = new PurchaseService(Economy);
            Revenue = new RevenueService(Economy);
            Progression = new ProgressionService();
            if (economyHud != null) economyHud.Bind(Economy);

            Grid = new ZooGrid(worldConfig.ToGridSettings());
            terrainView.Bind(Grid, worldConfig.ToTerrainPalette());

            bool debugBuild = Application.isEditor || Debug.isDebugBuild;
            gridOverlay.Bind(Grid, worldConfig.OverlayMajorLineEvery, debugBuild && worldConfig.ShowGridOverlay);

            pointerInput.Gestures.Settings = new GestureSettings(
                DpToPixels(cameraConfig.DragThresholdDp), cameraConfig.MaxTapSeconds);
            cameraController.Bind(pointerInput, Grid, cameraConfig, UnlockedAreaCentre(worldConfig.ToGridSettings()));

            if (placementController != null)
            {
                placementController.Bind(cameraController.GetComponent<Camera>(), Grid, placementConfig, placementCatalog, Purchases);
                if (placementPanel != null) placementPanel.Bind(placementController, placementCatalog, Economy);
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
                    Config = constructionConfig,
                    Purchaser = new ConstructionPurchaser(Construction, Purchases, constructionCatalog)
                };
                buildModeController.Bind(pointerInput, context, placementController, constructionCatalog);
                if (constructionPanel != null) constructionPanel.Bind(buildModeController, Construction, Economy);
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
                    AnimalPlacement = new AnimalPlacementTool(cameraController.GetComponent<Camera>(), Grid, animalSpawner, Purchases);
                    buildModeController.RegisterTool(AnimalPlacement);
                    if (animalDebugPanel != null) animalDebugPanel.Bind(AnimalPlacement, buildModeController, animalDefinitions, Economy);
                }

                if (visitorSpawner != null && Placement != null) BindVisitors(game, resolver);
                if (staffSpawner != null && VisitorDestinations != null) BindStaff(game);
                if (VisitorDestinations != null) BindRating(game);
            }

            if (questCatalog != null) BindQuests();

            debugHud.Bind(game, game.Config.ShowDebugHud, gridOverlay.Toggle);
        }

        void OnDestroy()
        {
            if (_questBridge != null) _questBridge.Dispose();
        }

        // Quests listen to the existing services' events through the bridge; nothing here runs per frame.
        void BindQuests()
        {
            var library = questCatalog.BuildLibrary();
            foreach (var problem in library.Problems) Debug.LogWarning("Quest catalogue: " + problem);

            var world = new QuestWorldQuery(Placement, Grid, Construction, Animals, Visitors, Staff, Rating, Progression);
            var rewards = new QuestRewardService(Economy, Progression);
            Quests = new QuestService(library, world, rewards);

            _questBridge = new QuestEventBridge(Quests, new QuestEventSources
            {
                Placement = Placement,
                Grid = Grid,
                Construction = Construction,
                Animals = Animals,
                AnimalPlacement = AnimalPlacement,
                Visitors = Visitors,
                Staff = Staff,
                Rating = Rating,
                Progression = Progression,
                Economy = Economy
            });

            if (questPanel != null) questPanel.Bind(Quests);
            if (questTracker != null) questTracker.Bind(Quests);
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
            VisitorDecisions.Revenue = Revenue;
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

        void BindStaff(GameManager game)
        {
            var config = staffConfig != null ? staffConfig : StaffConfig.CreateDefault();

            Staff = new StaffRegistry();
            StaffFacilities = new StaffFacilityRegistry(Placement);
            StaffAssignments = new StaffAssignmentService(Staff, StaffFacilities);
            StaffTasks = new StaffTaskPool(Staff);
            StaffWork = new StaffWorkService(Staff, StaffFacilities, StaffAssignments, StaffTasks, VisitorNavigation,
                VisitorDestinations, Animals);
            StaffHiring = new StaffHiringService(config, Staff, VisitorDestinations, Purchases);

            staffSpawner.Bind(Staff, StaffWork, Grid, config.WalkSpeedCellsPerSecond);
            var driver = staffSpawner.GetComponent<StaffSystemDriver>();
            if (driver == null) driver = staffSpawner.gameObject.AddComponent<StaffSystemDriver>();
            driver.Bind(game.Clock, StaffWork, staffSpawner);

            if (staffPanel != null)
                staffPanel.Bind(StaffHiring, Staff, StaffFacilities, StaffAssignments, StaffTasks, StaffWork,
                    placementController != null ? placementController.Session : null, VisitorDestinations, Animals, config);
        }

        void BindRating(GameManager game)
        {
            var settings = (zooRatingConfig != null ? zooRatingConfig : ZooRatingConfig.CreateDefault()).ToSettings();
            Rating = new ZooRatingService(settings, Animals, Visitors, Placement, VisitorDestinations);
            // Leaving visitors feed the rolling happiness average.
            VisitorDecisions.Left += Rating.RecordVisitorDeparture;

            var driver = gameObject.GetComponent<ZooRatingDriver>();
            if (driver == null) driver = gameObject.AddComponent<ZooRatingDriver>();
            driver.Bind(game.Clock, Rating);

            if (zooRatingHud != null) zooRatingHud.Bind(Rating);
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
