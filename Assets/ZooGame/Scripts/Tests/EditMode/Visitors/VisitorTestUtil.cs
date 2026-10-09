using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Animals;
using ZooGame.Data;
using ZooGame.Placement;
using ZooGame.Tests.EditMode.Animals;
using ZooGame.Visitors;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Visitors
{
    /// <summary>A stand-in for the scene body: records what it was told and "walks" instantly when asked to arrive.</summary>
    public sealed class FakeAgent : IVisitorAgent
    {
        readonly VisitorDecisionService _decisions;
        readonly ZooGrid _grid;

        public FakeAgent(VisitorInstance instance, VisitorDecisionService decisions, ZooGrid grid)
        {
            Instance = instance;
            _decisions = decisions;
            _grid = grid;
        }

        public VisitorInstance Instance { get; }
        public readonly List<GridCoord> Route = new List<GridCoord>();
        public int FollowCalls;
        public int StopCalls;

        public bool HasRoute => Route.Count > 0;
        public GridCoord Destination => Route[Route.Count - 1];

        public void Follow(IReadOnlyList<GridCoord> route)
        {
            FollowCalls++;
            Route.Clear();
            for (int i = 0; i < route.Count; i++) Route.Add(route[i]);
        }

        public void Stop()
        {
            StopCalls++;
            Route.Clear();
        }

        /// <summary>Teleports to the end of the current route and reports arrival, as the controller would.</summary>
        public void Arrive()
        {
            if (Route.Count > 0) Instance.Position = _grid.GridToWorld(Route[Route.Count - 1]);
            Route.Clear();
            _decisions.OnArrived(Instance);
        }
    }

    /// <summary>
    /// Real M3 construction (16x16 grid, unlocked 4..11), real placement map, real animal registry and the real
    /// visitor services, with fake agents. Layout helpers: a path along z = 6, a 4x3 pen at (4,7) whose south fence
    /// touches that path, and facilities placed under the path at z = 5.
    /// </summary>
    public sealed class VisitorFixture
    {
        readonly List<Object> _created = new List<Object>();

        public readonly AnimalFixture Animals = new AnimalFixture();
        public readonly PlacementMap Placement;
        public readonly VisitorConfig Config;
        public readonly VisitorRegistry Registry = new VisitorRegistry();
        public readonly VisitorNavigationService Nav;
        public readonly VisitorDestinationService Destinations;
        public readonly VisitorDecisionService Decisions;
        public readonly VisitorSimulationService Simulation;
        public readonly System.Random Rng;
        public readonly Dictionary<string, FakeAgent> Agents = new Dictionary<string, FakeAgent>();
        public readonly List<VisitorInstance> LeftVisitors = new List<VisitorInstance>();

        public ZooGrid Grid => Animals.Construction.Grid;
        public ConstructionModel Model => Animals.Construction.Model;

        public VisitorFixture(int seed = 11)
        {
            Rng = new System.Random(seed);
            Placement = new PlacementMap(Grid);
            Config = VisitorConfig.CreateDefault();
            Nav = new VisitorNavigationService(Grid, Model);
            Destinations = new VisitorDestinationService(Grid, Model, Placement, Nav, Animals.Registry, Animals.Resolver);
            Decisions = new VisitorDecisionService(Config, Registry, Nav, Destinations, Rng);
            Simulation = new VisitorSimulationService(Config, Registry, Decisions);
            Decisions.Left += v => LeftVisitors.Add(v);
            Animals.Rabbit.ConfigureAppeal(1f);
            Animals.Zebra.ConfigureAppeal(2f);
            Animals.Lion.ConfigureAppeal(4f);
        }

        // ---- Layout ----

        public static GridCoord C(int x, int z) => new GridCoord(x, z);

        public void BuildPath(int x0, int z, int x1)
        {
            var cells = new List<GridCoord>();
            for (int x = x0; x <= x1; x++) cells.Add(C(x, z));
            Assert.AreEqual(cells.Count, Model.BuildPaths(cells), "path cells should all build");
        }

        public void BuildPathColumn(int x, int z0, int z1)
        {
            var cells = new List<GridCoord>();
            for (int z = z0; z <= z1; z++) cells.Add(C(x, z));
            Model.BuildPaths(cells);
        }

        /// <summary>The standard walkway: path z = 6, x 4..11.</summary>
        public void StandardPath() => BuildPath(4, 6, 11);

        /// <summary>Builds a closed w x d pen and returns its M3 enclosure id (default: 4x3 at (4,7), south fence touching path z = 6).</summary>
        public int Pen(int minX = 4, int minZ = 7, int w = 4, int d = 3)
        {
            Animals.Construction.BuildRect(minX, minZ, w, d);
            int id = Animals.Construction.EnclosureIdAt(minX, minZ);
            Assert.AreNotEqual(0, id, "pen should be closed");
            return id;
        }

        public PlaceableDefinition Define(VisitorFacilityKind kind, int w = 1, int h = 1)
        {
            var def = ScriptableObject.CreateInstance<PlaceableDefinition>();
            def.Configure(kind + "-" + _created.Count, kind.ToString(), w, h, false, PlaceableCategory.Service);
            def.ConfigureVisitorFacility(kind);
            _created.Add(def);
            return def;
        }

        public PlacedObject Place(VisitorFacilityKind kind, int x, int z, int w = 1, int h = 1)
        {
            var obj = Placement.Add(Define(kind, w, h), C(x, z), Rotation90.Deg0);
            Assert.IsNotNull(obj, "placement failed at " + x + "," + z);
            return obj;
        }

        public static string FacilityId(PlacedObject o) => "facility-" + o.Id;

        public void AddAnimal(AnimalDefinition def, int enclosureId)
        {
            var a = AnimalInstance.CreateNew(def.SpeciesId, def.DisplayName, AnimalSex.Female, AnimalEnclosureService.ToId(enclosureId), Vector3.zero);
            Animals.Registry.Register(a);
        }

        // ---- Visitors ----

        /// <summary>A registered visitor standing on a cell with all needs satisfied (so nothing is urgent), state ChoosingDestination.</summary>
        public VisitorInstance AddVisitor(int x, int z, float maxVisitMinutes = 10000f)
        {
            var v = VisitorInstance.CreateNew(Grid.GridToWorld(C(x, z)), maxVisitMinutes);
            v.Hunger = 0f; v.Thirst = 0f; v.ToiletNeed = 0f; v.Energy = 100f; v.VisitSatisfaction = 60f;
            v.State = VisitorState.ChoosingDestination;
            VisitorMath.RecalculateHappiness(v);
            Assert.IsTrue(Registry.Register(v));
            var agent = new FakeAgent(v, Decisions, Grid);
            Agents[v.VisitorId] = agent;
            Decisions.Attach(agent);
            return v;
        }

        public FakeAgent AgentOf(VisitorInstance v) => Agents[v.VisitorId];

        public void Dispose()
        {
            Decisions.Dispose();
            Destinations.Dispose();
            Nav.Dispose();
            for (int i = 0; i < _created.Count; i++) Object.DestroyImmediate(_created[i]);
            Object.DestroyImmediate(Config);
            Animals.Dispose();
        }
    }
}
