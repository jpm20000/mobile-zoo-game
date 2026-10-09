using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ZooGame.Animals;
using ZooGame.Cameras;
using ZooGame.Core;
using ZooGame.Data;
using ZooGame.Gameplay;
using ZooGame.Placement;
using ZooGame.Visitors;
using ZooGame.World;

namespace ZooGame.Tests.PlayMode.Visitors
{
    /// <summary>
    /// Visitors in the real Zoo scene: entrance spawn, walking only on paths, viewing animals, using facilities, leaving,
    /// pause and speed, unreachable destinations, selection and the debug panel.
    /// Layout (all inside the unlocked land): path z = 34 (x 30..41), a 6x4 pen at (30,35) touching it, facilities under
    /// the path at z = 33 and a 2x2 entrance at (40,32).
    /// </summary>
    public class VisitorPlayModeTests
    {
        ZooSceneBinder _binder;
        ConstructionModel _model;
        VisitorSpawner _spawner;
        GameClock _clock;
        readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        PlacedObject _entrance, _food, _drink, _toilet, _bench;
        int _penId;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Bootstrap");
            float timeout = Time.realtimeSinceStartup + 10f;
            while ((GameManager.Instance == null || GameManager.Instance.State.Current != GameState.Playing)
                   && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State.Current);

            _binder = UnityEngine.Object.FindAnyObjectByType<ZooSceneBinder>();
            _spawner = _binder.VisitorSpawner;
            Assert.IsNotNull(_spawner, "run ZooGame > M6 > Create Or Update Project Assets");
            Assert.IsNotNull(_binder.VisitorDecisions, "visitors not bound");
            _model = _binder.Construction;
            _clock = GameManager.Instance.Clock;
            _spawner.AutoSpawn = false; // tests spawn explicitly
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameManager.Instance != null) UnityEngine.Object.Destroy(GameManager.Instance.gameObject);
            foreach (var o in _created) if (o != null) UnityEngine.Object.Destroy(o);
            _created.Clear();
            yield return null;
        }

        // ---- Helpers ----

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        PlacedObject Place(VisitorFacilityKind kind, int x, int z, int w = 1, int h = 1)
        {
            var def = ScriptableObject.CreateInstance<PlaceableDefinition>();
            def.Configure(kind + "-" + x + "-" + z, kind.ToString(), w, h, false, PlaceableCategory.Service);
            def.ConfigureVisitorFacility(kind);
            _created.Add(def);
            var obj = _binder.Placement.Add(def, C(x, z), Rotation90.Deg0);
            Assert.IsNotNull(obj, "placement failed at " + x + "," + z);
            return obj;
        }

        int BuildPen(int minX, int minZ, int w, int d)
        {
            var edges = new List<FenceEdge>();
            for (int x = minX; x < minX + w; x++)
            {
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.X, x, minZ), EdgeKind.Fence));
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.X, x, minZ + d), EdgeKind.Fence));
            }
            for (int z = minZ; z < minZ + d; z++)
            {
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.Z, minX, z), EdgeKind.Fence));
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.Z, minX + w, z), EdgeKind.Fence));
            }
            _model.BuildFences(edges);
            var e = _model.Enclosures.GetAt(C(minX, minZ));
            Assert.IsNotNull(e, "pen should be closed");
            return e.Id;
        }

        void BuildWalkway(int x0, int x1, int z)
        {
            var cells = new List<GridCoord>();
            for (int x = x0; x <= x1; x++) cells.Add(C(x, z));
            _model.BuildPaths(cells);
        }

        /// <summary>The standard zoo: walkway, pen with a rabbit, entrance and the four facilities.</summary>
        void BuildZoo(bool facilities = true, bool animal = true)
        {
            BuildWalkway(30, 41, 34);
            _penId = BuildPen(30, 35, 6, 4);
            _entrance = Place(VisitorFacilityKind.Entrance, 40, 32, 2, 2);
            if (facilities)
            {
                _food = Place(VisitorFacilityKind.FoodStall, 33, 33);
                _drink = Place(VisitorFacilityKind.DrinkStall, 34, 33);
                _toilet = Place(VisitorFacilityKind.Toilet, 35, 33);
                _bench = Place(VisitorFacilityKind.Bench, 36, 33, 2, 1);
            }
            if (animal)
            {
                var r = _binder.AnimalSpawner.SpawnNew("rabbit", AnimalEnclosureService.ToId(_penId));
                Assert.IsTrue(r.Success, r.Message);
            }
        }

        VisitorController SpawnVisitor()
        {
            var failure = _spawner.TrySpawn(out var c);
            Assert.AreEqual(VisitorSpawnFailure.None, failure);
            return c;
        }

        IEnumerator WaitUntil(Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        }

        string AllText(GameObject root)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in root.GetComponentsInChildren<Text>(true)) sb.AppendLine(t.text);
            return sb.ToString();
        }

        // ---- Tests ----

        [UnityTest]
        public IEnumerator TheSceneBindsEveryVisitorService()
        {
            Assert.IsNotNull(_binder.Visitors);
            Assert.IsNotNull(_binder.VisitorNavigation);
            Assert.IsNotNull(_binder.VisitorDestinations);
            Assert.IsNotNull(_binder.VisitorSimulation);
            Assert.IsNotNull(_binder.VisitorSelection);
            Assert.AreEqual(0, _binder.Visitors.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NoEntrance_MeansNoVisitors()
        {
            BuildWalkway(30, 41, 34);
            Assert.AreEqual(VisitorSpawnFailure.NoEntrance, _spawner.TrySpawn(out _));
            _spawner.AutoSpawn = true;
            _clock.SetSpeed(SimulationSpeed.X3);
            yield return WaitUntil(() => false, 1f);
            Assert.AreEqual(0, _binder.Visitors.Count);
        }

        [UnityTest]
        public IEnumerator Visitors_EnterThroughTheEntrance_AndAreRegistered()
        {
            BuildZoo();
            var c = SpawnVisitor();
            Assert.AreEqual(1, _binder.Visitors.Count);
            Assert.IsTrue(_binder.Visitors.Contains(c.Instance.VisitorId));
            Assert.AreEqual(VisitorState.Entering, c.Instance.State);
            Assert.IsTrue(_binder.Grid.TryWorldToGrid(c.transform.position, out var cell));
            Assert.AreSame(_entrance, _binder.Placement.GetAt(cell), "starts on the entrance");

            _clock.SetSpeed(SimulationSpeed.X3);
            yield return WaitUntil(() => c.Instance.State != VisitorState.Entering, 10f);
            Assert.AreNotEqual(VisitorState.Entering, c.Instance.State, "stepped onto the path and chose a destination");
            Assert.IsNotNull(c.Instance.TargetDestinationId);
        }

        [UnityTest]
        public IEnumerator Visitors_WalkOnlyOnConnectedPaths_AndNeverEnterEnclosures()
        {
            BuildZoo();
            var visitors = new List<VisitorController>();
            for (int i = 0; i < 4; i++) visitors.Add(SpawnVisitor());
            _clock.SetSpeed(SimulationSpeed.X3);

            int samples = 0;
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until)
            {
                foreach (var v in visitors)
                {
                    if (v == null || !v.gameObject.activeSelf) continue;
                    Assert.IsTrue(_binder.Grid.TryWorldToGrid(v.transform.position, out var cell), "left the grid");
                    var gc = _binder.Grid.GetCell(cell);
                    bool onEntrance = _binder.Placement.GetAt(cell) == _entrance;
                    Assert.IsTrue(gc.HasPath || onEntrance, "visitor off the path network at " + cell);
                    Assert.AreEqual(GridCell.NoEnclosure, gc.EnclosureId, "visitor inside an enclosure at " + cell);
                    samples++;
                }
                yield return null;
            }
            Assert.Greater(samples, 50, "visitors were observed");
        }

        [UnityTest]
        public IEnumerator Visitors_ViewAnimals_AndTheirSatisfactionChanges()
        {
            BuildZoo(facilities: false);
            var c = SpawnVisitor();
            c.Instance.VisitSatisfaction = 60f;
            _clock.SetSpeed(SimulationSpeed.X3);

            bool viewed = false;
            float until = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < until && !c.Instance.RecentlyViewed(_penId))
            {
                if (c.Instance.State == VisitorState.ViewingAnimal)
                {
                    viewed = true;
                    Assert.AreEqual(_penId, c.Instance.CurrentEnclosureId);
                    Assert.IsFalse(c.IsMoving, "stands still while viewing");
                }
                yield return null;
            }
            Assert.IsTrue(viewed, "reached a viewing point and watched");
            Assert.IsTrue(c.Instance.RecentlyViewed(_penId), "finished viewing");
            Assert.AreNotEqual(60f, c.Instance.VisitSatisfaction, "viewing moved satisfaction towards the viewing score");
        }

        [UnityTest]
        public IEnumerator HungryVisitors_UseAFoodStall()
        {
            BuildZoo();
            var c = SpawnVisitor();
            yield return null;
            c.Instance.Hunger = 92f;
            _clock.SetSpeed(SimulationSpeed.X3);

            bool sought = false;
            float until = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < until && c.Instance.Hunger > 50f)
            {
                if (c.Instance.State == VisitorState.SeekingFood)
                {
                    sought = true;
                    Assert.AreEqual("facility-" + _food.Id, c.Instance.TargetDestinationId);
                }
                yield return null;
            }
            Assert.IsTrue(sought, "headed for the food stall");
            Assert.Less(c.Instance.Hunger, 50f, "ate: hunger fell by 70");
        }

        [UnityTest]
        public IEnumerator TiredVisitors_RestOnABench()
        {
            BuildZoo();
            var c = SpawnVisitor();
            yield return null;
            c.Instance.Energy = 15f;
            _clock.SetSpeed(SimulationSpeed.X3);

            bool rested = false;
            float until = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < until && c.Instance.Energy < 50f)
            {
                if (c.Instance.State == VisitorState.Resting) rested = true;
                yield return null;
            }
            Assert.IsTrue(rested);
            Assert.GreaterOrEqual(c.Instance.Energy, 50f, "energy came back");
        }

        [UnityTest]
        public IEnumerator Visitors_EventuallyLeaveThroughTheExit()
        {
            BuildZoo();
            var c = SpawnVisitor();
            string id = c.Instance.VisitorId;
            yield return null;
            c.Instance.MaximumVisitTime = 3f; // a very short visit
            _clock.SetSpeed(SimulationSpeed.X3);

            yield return WaitUntil(() => _binder.Visitors.Count == 0, 40f);
            Assert.AreEqual(0, _binder.Visitors.Count);
            Assert.IsFalse(_binder.Visitors.Contains(id));
            Assert.AreEqual(0, _spawner.Active.Count);
            Assert.IsFalse(c.gameObject.activeSelf, "returned to the pool");
            Assert.AreEqual(0, _binder.VisitorDecisions.ForcedLeaveCount, "left through the exit, not by teleport");
        }

        [UnityTest]
        public IEnumerator Pause_FreezesVisitors_AndSpeedScalesTheSimulation()
        {
            BuildZoo();
            var c = SpawnVisitor();
            _clock.SetSpeed(SimulationSpeed.X3);
            yield return WaitUntil(() => c.Instance.VisitTime > 4f, 15f);

            _clock.SetSpeed(SimulationSpeed.Paused);
            yield return null;
            var pos = c.transform.position;
            float time = c.Instance.VisitTime, hunger = c.Instance.Hunger;
            int ticks = _binder.VisitorSimulation.TicksRun;
            yield return WaitUntil(() => false, 1f);
            Assert.AreEqual(ticks, _binder.VisitorSimulation.TicksRun, "no ticks while paused");
            Assert.AreEqual(time, c.Instance.VisitTime);
            Assert.AreEqual(hunger, c.Instance.Hunger);
            Assert.AreEqual(pos, c.transform.position, "nobody walks while paused");

            _clock.SetSpeed(SimulationSpeed.X1);
            int t0 = _binder.VisitorSimulation.TicksRun;
            yield return WaitUntil(() => false, 3f);
            int slow = _binder.VisitorSimulation.TicksRun - t0;

            _clock.SetSpeed(SimulationSpeed.X3);
            t0 = _binder.VisitorSimulation.TicksRun;
            yield return WaitUntil(() => false, 3f);
            int fast = _binder.VisitorSimulation.TicksRun - t0;
            Assert.Greater(slow, 0, "1x runs ticks");
            Assert.Greater(fast, slow, "3x runs more ticks in the same real time: " + fast + " vs " + slow);
        }

        [UnityTest]
        public IEnumerator CuttingTheWalkway_SendsTheVisitorToAnotherDestinationOrTheExit()
        {
            BuildZoo(facilities: false);
            var c = SpawnVisitor();
            _clock.SetSpeed(SimulationSpeed.X3);
            yield return WaitUntil(() => c.Instance.State == VisitorState.Walking, 10f);
            Assert.AreEqual(VisitorState.Walking, c.Instance.State);
            string firstTarget = c.Instance.TargetDestinationId;

            var instance = c.Instance;
            bool sawExiting = false;
            Action<VisitorInstance> watch = v => { if (ReferenceEquals(v, instance) && v.State == VisitorState.Exiting) sawExiting = true; };
            _binder.VisitorDecisions.StateChanged += watch;

            // Remove the path beside the pen: every viewing point disappears, so the target cannot be reached.
            var cut = new List<GridCoord>();
            for (int x = 30; x <= 35; x++) cut.Add(C(x, 34));
            _model.RemovePaths(cut);

            yield return WaitUntil(() => _binder.Visitors.Count == 0, 30f);
            _binder.VisitorDecisions.StateChanged -= watch;
            Assert.IsTrue(sawExiting, "nothing useful was left, so the visitor headed out");
            Assert.AreEqual(0, _binder.Visitors.Count, "and left safely");
            Assert.AreEqual(0, _binder.VisitorDecisions.ForcedLeaveCount, "through the exit");
            Assert.AreNotEqual(firstTarget, instance.TargetDestinationId);
        }

        [UnityTest]
        public IEnumerator Pathfinding_DoesNotRunEveryFrame()
        {
            BuildZoo();
            for (int i = 0; i < 8; i++) SpawnVisitor();
            _clock.SetSpeed(SimulationSpeed.X3);
            yield return WaitUntil(() => false, 3f);

            int frames = 0;
            int routes0 = _binder.VisitorNavigation.RouteRequestCount;
            int graph0 = _binder.VisitorNavigation.GraphRebuildCount;
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until) { frames++; yield return null; }
            int routes = _binder.VisitorNavigation.RouteRequestCount - routes0;
            Assert.Greater(frames, 20);
            Assert.Less(routes, frames, "far fewer route searches than frames: " + routes + " in " + frames);
            Assert.AreEqual(graph0, _binder.VisitorNavigation.GraphRebuildCount, "the graph is cached while nothing is built");

            _clock.SetSpeed(SimulationSpeed.Paused);
            yield return null;
            routes0 = _binder.VisitorNavigation.RouteRequestCount;
            yield return WaitUntil(() => false, 1f);
            Assert.AreEqual(routes0, _binder.VisitorNavigation.RouteRequestCount, "no searches while paused");
        }

        [UnityTest]
        public IEnumerator SelectingAVisitor_ShowsItsStateNeedsAndHappiness()
        {
            BuildZoo();
            var c = SpawnVisitor();
            _clock.SetSpeed(SimulationSpeed.X3);
            yield return WaitUntil(() => c.Instance.VisitTime > 2f, 10f);

            _binder.VisitorSelection.Select(c);
            yield return null;
            var panel = UnityEngine.Object.FindAnyObjectByType<VisitorInfoPanel>(FindObjectsInactive.Include);
            Assert.IsNotNull(panel);
            string all = AllText(panel.gameObject);
            foreach (string needle in new[] { "State", "Target", "Hunger", "Thirst", "Toilet", "Energy", "Needs Happiness", "Visit Satisfaction", "Happiness", "Visit time" })
                StringAssert.Contains(needle, all);
            StringAssert.Contains(c.Instance.State.ToString(), all);

            _binder.VisitorSelection.Clear();
            yield return null;
            Assert.IsFalse(panel.transform.Find("Content").gameObject.activeSelf, "the panel hides when nothing is selected");
        }

        [UnityTest]
        public IEnumerator TappingAVisitor_PicksIt()
        {
            BuildZoo();
            var c = SpawnVisitor();
            yield return null;
            var camera = UnityEngine.Object.FindAnyObjectByType<ZooCameraController>().GetComponent<Camera>();
            var screen = camera.WorldToScreenPoint(c.PickPoint);
            Assert.Greater(screen.z, 0f);
            Assert.AreSame(c, _binder.VisitorSelection.Pick(new Vector2(screen.x, screen.y)), "tap on the visitor");
            Assert.IsNull(_binder.VisitorSelection.Pick(new Vector2(screen.x + 600f, screen.y + 600f)), "tap far away");
        }

        [UnityTest]
        public IEnumerator TheDebugPanel_SpawnsAndClearsVisitors()
        {
            BuildZoo();
            var panel = UnityEngine.Object.FindAnyObjectByType<VisitorDebugPanel>(FindObjectsInactive.Include);
            Assert.IsNotNull(panel);
            Button Find(string name)
            {
                foreach (var b in panel.GetComponentsInChildren<Button>(true)) if (b.name == name) return b;
                Assert.Fail("no button " + name);
                return null;
            }
            Find("Spawn Five").onClick.Invoke();
            Assert.AreEqual(5, _binder.Visitors.Count);
            Find("Spawn One").onClick.Invoke();
            Assert.AreEqual(6, _binder.Visitors.Count);
            StringAssert.Contains("Visitors 6/", AllText(panel.gameObject));

            Find("Auto Spawn").onClick.Invoke();
            Assert.IsTrue(_spawner.AutoSpawn);
            StringAssert.Contains("ON", AllText(panel.gameObject));

            Find("Clear Visitors").onClick.Invoke();
            Assert.AreEqual(0, _binder.Visitors.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AutoSpawn_FillsTheZoo_UpToTheCap()
        {
            BuildZoo();
            _spawner.AutoSpawn = true;
            _clock.SetSpeed(SimulationSpeed.X3);
            yield return WaitUntil(() => _binder.Visitors.Count >= 3, 20f);
            Assert.GreaterOrEqual(_binder.Visitors.Count, 3, "visitors keep arriving");
            Assert.LessOrEqual(_binder.Visitors.Count, _spawner.MaxVisitors);
        }
    }
}
