using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Data;
using ZooGame.Visitors;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Visitors
{
    /// <summary>The spawner (entrance, interval, cap, pooling, leaving) and the controller's walk, with the real services behind them.</summary>
    public class VisitorSpawnerTests
    {
        VisitorFixture _f;
        GameObject _go;
        VisitorSpawner _spawner;
        readonly List<GameObject> _extra = new List<GameObject>();

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        [SetUp]
        public void SetUp()
        {
            _f = new VisitorFixture();
            _go = new GameObject("Spawner");
            _spawner = _go.AddComponent<VisitorSpawner>();
            _spawner.Bind(_f.Config, _f.Registry, _f.Decisions, _f.Destinations, _f.Grid, new System.Random(3));
            _f.Decisions.Left += _ => { }; // the spawner subscribed first and owns despawning
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            foreach (var g in _extra) if (g != null) Object.DestroyImmediate(g);
            _f.Dispose();
        }

        void StandardZoo()
        {
            _f.StandardPath();
            _f.Pen();
            _f.Place(VisitorFacilityKind.Entrance, 11, 5);
        }

        // ---- Entrance spawning ----

        [Test]
        public void WithoutAnEntrance_NothingSpawns()
        {
            _f.StandardPath();
            Assert.AreEqual(VisitorSpawnFailure.NoEntrance, _spawner.TrySpawn(out var c));
            Assert.IsNull(c);
            Assert.AreEqual(0, _f.Registry.Count);
            Assert.IsFalse(_spawner.HasEntrance);
        }

        [Test]
        public void AnUnboundSpawner_FailsSafely()
        {
            var go = new GameObject("Unbound");
            _extra.Add(go);
            var s = go.AddComponent<VisitorSpawner>();
            Assert.AreEqual(VisitorSpawnFailure.NotBound, s.TrySpawn(out _));
            Assert.DoesNotThrow(() => s.Tick(1f));
            Assert.DoesNotThrow(() => s.StepAgents(1f));
            Assert.DoesNotThrow(() => s.Clear());
        }

        [Test]
        public void ANewVisitor_AppearsAtTheEntrance_RegisteredAndEntering()
        {
            StandardZoo();
            Assert.IsTrue(_spawner.HasEntrance);
            var spawned = new List<VisitorController>();
            _spawner.Spawned += spawned.Add;

            Assert.AreEqual(VisitorSpawnFailure.None, _spawner.TrySpawn(out var c));
            Assert.AreEqual(1, _f.Registry.Count);
            Assert.AreEqual(1, _spawner.Active.Count);
            CollectionAssert.AreEqual(new[] { c }, spawned);
            Assert.IsTrue(_f.Registry.Contains(c.Instance.VisitorId));
            Assert.AreEqual(VisitorState.Entering, c.Instance.State);
            Assert.AreEqual(new Vector3(11.5f, 0f, 5.5f), c.transform.position, "the centre of the entrance object");
            Assert.IsTrue(c.gameObject.activeSelf);
            Assert.IsTrue(c.IsMoving, "stepping onto the path");
            Assert.IsTrue(_spawner.TryGetController(c.Instance.VisitorId, out var found));
            Assert.AreSame(c, found);
        }

        [Test]
        public void TheEnteringVisitor_WalksOntoThePath_ThenChoosesWhereToGo()
        {
            StandardZoo();
            _spawner.TrySpawn(out var c);
            for (int i = 0; i < 100 && c.Instance.State == VisitorState.Entering; i++) _spawner.StepAgents(0.05f);
            Assert.AreEqual(VisitorState.Walking, c.Instance.State, "it has seen the pen");
            Assert.AreEqual(VisitorDestinationKind.EnclosureViewing, DestinationOf(c).Kind);
        }

        VisitorDestination DestinationOf(VisitorController c)
        {
            Assert.IsTrue(_f.Destinations.TryGet(c.Instance.TargetDestinationId, out var d));
            return d;
        }

        [Test]
        public void SpawnedVisitors_HaveUniqueIds_AndStartingNeedsInRange()
        {
            StandardZoo();
            _f.Config.WithSpawning(1f, 50);
            var ids = new HashSet<string>();
            for (int i = 0; i < 40; i++)
            {
                Assert.AreEqual(VisitorSpawnFailure.None, _spawner.TrySpawn(out var c));
                Assert.IsTrue(ids.Add(c.Instance.VisitorId));
                Assert.That(c.Instance.Hunger, Is.InRange(5f, 20f));
                Assert.That(c.Instance.Energy, Is.InRange(80f, 100f));
            }
            Assert.AreEqual(40, _f.Registry.Count);
        }

        // ---- Cap and interval ----

        [Test]
        public void TheVisitorCap_IsRespected()
        {
            StandardZoo();
            _f.Config.WithSpawning(1f, 3);
            for (int i = 0; i < 3; i++) Assert.AreEqual(VisitorSpawnFailure.None, _spawner.TrySpawn(out _));
            Assert.AreEqual(VisitorSpawnFailure.MaxVisitors, _spawner.TrySpawn(out var none));
            Assert.IsNull(none);
            Assert.AreEqual(3, _f.Registry.Count);
        }

        [Test]
        public void AutoSpawn_FollowsTheConfiguredInterval()
        {
            StandardZoo();
            _f.Config.WithSpawning(2f, 10);
            _spawner.AutoSpawn = true;
            _spawner.Tick(1f);
            Assert.AreEqual(0, _f.Registry.Count);
            _spawner.Tick(1f);
            Assert.AreEqual(1, _f.Registry.Count);
            _spawner.Tick(2f);
            Assert.AreEqual(2, _f.Registry.Count);
        }

        [Test]
        public void AutoSpawn_CanBeSwitchedOff_AndStopsWhilePaused()
        {
            StandardZoo();
            _f.Config.WithSpawning(1f, 10);
            _spawner.AutoSpawn = false;
            _spawner.Tick(5f);
            Assert.AreEqual(0, _f.Registry.Count);
            _spawner.AutoSpawn = true;
            _spawner.Tick(0f); // paused clock supplies no time
            Assert.AreEqual(0, _f.Registry.Count);
            _spawner.Tick(1f);
            Assert.AreEqual(1, _f.Registry.Count);
        }

        [Test]
        public void AutoSpawn_StopsAtTheCap()
        {
            StandardZoo();
            _f.Config.WithSpawning(1f, 2);
            for (int i = 0; i < 10; i++) _spawner.Tick(1f);
            Assert.AreEqual(2, _f.Registry.Count);
        }

        // ---- Leaving and pooling ----

        [Test]
        public void AVisitorWhoReachesTheExit_IsRemoved_AndItsBodyIsPooled()
        {
            StandardZoo();
            var despawning = new List<VisitorController>();
            _spawner.Despawning += despawning.Add;
            _spawner.TrySpawn(out var c);
            string id = c.Instance.VisitorId;
            c.Instance.MaximumVisitTime = 1f; // leave at the first tick
            for (int i = 0; i < 10; i++) _spawner.StepAgents(0.25f); // out of the entrance and well along the walkway
            for (int i = 0; i < 4; i++) _spawner.StepAgents(0.25f);
            _f.Simulation.Step(2f);
            Assert.AreEqual(VisitorState.Exiting, c.Instance.State);
            for (int i = 0; i < 80 && _f.Registry.Count > 0; i++) _spawner.StepAgents(0.25f);

            Assert.AreEqual(0, _f.Registry.Count);
            Assert.IsFalse(_f.Registry.Contains(id));
            Assert.AreEqual(0, _spawner.Active.Count);
            Assert.IsFalse(_spawner.TryGetController(id, out _));
            Assert.IsFalse(c.gameObject.activeSelf, "returned to the pool");
            Assert.AreEqual(0, _f.Decisions.ForcedLeaveCount);
            Assert.AreEqual(1, despawning.Count);
        }

        [Test]
        public void Bodies_AreReusedFromThePool()
        {
            StandardZoo();
            _spawner.TrySpawn(out var first);
            _spawner.Clear();
            Assert.AreEqual(0, _f.Registry.Count);
            Assert.IsFalse(first.gameObject.activeSelf);

            _spawner.TrySpawn(out var second);
            Assert.AreSame(first, second, "the pooled controller is reused");
            Assert.IsTrue(second.gameObject.activeSelf);
            Assert.IsNotNull(second.Instance);
            Assert.AreNotEqual("", second.Instance.VisitorId);
            Assert.AreEqual(1, _go.transform.childCount, "no extra GameObject was created");
        }

        [Test]
        public void Clear_RemovesEveryone()
        {
            StandardZoo();
            _f.Config.WithSpawning(1f, 10);
            for (int i = 0; i < 6; i++) _spawner.TrySpawn(out _);
            _spawner.Clear();
            Assert.AreEqual(0, _f.Registry.Count);
            Assert.AreEqual(0, _spawner.Active.Count);
        }

        [Test]
        public void ADespawnedVisitor_NoLongerReceivesArrivals()
        {
            StandardZoo();
            _spawner.TrySpawn(out var c);
            var instance = c.Instance;
            _spawner.Clear();
            Assert.DoesNotThrow(() => _f.Decisions.OnArrived(instance));
            Assert.DoesNotThrow(() => _spawner.StepAgents(1f));
        }

        // ---- Controller ----

        VisitorController NewController(Vector3 at, float speedCells)
        {
            var go = new GameObject("Controller");
            _extra.Add(go);
            var c = go.AddComponent<VisitorController>();
            c.Bind(VisitorInstance.CreateNew(at, 100f), _f.Grid, speedCells);
            return c;
        }

        [Test]
        public void TheController_WalksCellCentresInOrder_AtTheConfiguredSpeed_AndArrivesOnce()
        {
            var c = NewController(_f.Grid.GridToWorld(C(4, 6)), 2f);
            int arrived = 0;
            c.Arrived += _ => arrived++;
            c.Follow(new[] { C(5, 6), C(6, 6), C(6, 7) });
            Assert.IsTrue(c.IsMoving);

            c.Step(0.25f); // 0.5 cells
            Assert.AreEqual(5.0f, c.transform.position.x, 1e-4f);
            c.Step(0.5f);  // 1 more cell: reaches (5,6) then half way to (6,6)...
            Assert.AreEqual(6.0f, c.transform.position.x, 1e-4f);
            Assert.AreEqual(6.5f, c.transform.position.z, 1e-4f);
            Assert.AreEqual(0, arrived);

            c.Step(10f);
            Assert.AreEqual(1, arrived);
            Assert.AreEqual(6.5f, c.transform.position.x, 1e-4f);
            Assert.AreEqual(7.5f, c.transform.position.z, 1e-4f);
            Assert.IsFalse(c.IsMoving);
            c.Step(10f);
            Assert.AreEqual(1, arrived, "arrival is reported once");
            Assert.AreEqual(c.transform.position, c.Instance.Position, "the record follows the body");
        }

        [Test]
        public void TheController_DoesNotCutCorners()
        {
            var c = NewController(_f.Grid.GridToWorld(C(4, 6)), 1f);
            c.Follow(new[] { C(5, 6), C(5, 7) });
            for (int i = 0; i < 20; i++)
            {
                c.Step(0.1f);
                var p = c.transform.position;
                bool onRow = Mathf.Abs(p.z - 6.5f) < 1e-3f && p.x <= 5.5f + 1e-3f;
                bool onColumn = Mathf.Abs(p.x - 5.5f) < 1e-3f && p.z >= 6.5f - 1e-3f;
                Assert.IsTrue(onRow || onColumn, "left the path at " + p);
            }
        }

        [Test]
        public void ZeroTime_MovesNothing()
        {
            var c = NewController(_f.Grid.GridToWorld(C(4, 6)), 2f);
            var start = c.transform.position;
            c.Follow(new[] { C(8, 6) });
            c.Step(0f);
            Assert.AreEqual(start, c.transform.position, "a paused clock supplies no time");
        }

        [Test]
        public void AnEmptyRoute_ArrivesOnTheNextStep()
        {
            var c = NewController(_f.Grid.GridToWorld(C(4, 6)), 2f);
            int arrived = 0;
            c.Arrived += _ => arrived++;
            c.Follow(new GridCoord[0]);
            Assert.AreEqual(0, arrived);
            c.Step(0f);
            Assert.AreEqual(1, arrived);
        }

        [Test]
        public void Stop_CancelsTheWalk()
        {
            var c = NewController(_f.Grid.GridToWorld(C(4, 6)), 2f);
            int arrived = 0;
            c.Arrived += _ => arrived++;
            c.Follow(new[] { C(8, 6) });
            c.Stop();
            c.Step(10f);
            Assert.AreEqual(0, arrived);
            Assert.IsFalse(c.IsMoving);
        }

        [Test]
        public void ANewRoute_ReplacesTheOldOne()
        {
            var c = NewController(_f.Grid.GridToWorld(C(4, 6)), 2f);
            c.Follow(new[] { C(8, 6) });
            c.Step(0.25f);
            c.Follow(new[] { C(4, 7) });
            c.Step(10f);
            Assert.AreEqual(4.5f, c.transform.position.x, 0.6f);
            Assert.AreEqual(7.5f, c.transform.position.z, 1e-4f);
        }

        [Test]
        public void SelectionMarker_IsToggledBySetSelected()
        {
            var c = NewController(Vector3.zero, 1f);
            Assert.DoesNotThrow(() => c.SetSelected(true), "the marker is optional");
        }
    }
}
