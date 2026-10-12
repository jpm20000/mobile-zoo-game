using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZooGame.Core;
using ZooGame.Data;
using ZooGame.Economy;
using ZooGame.Gameplay;
using ZooGame.Placement;
using ZooGame.Staff;
using ZooGame.Visitors;
using ZooGame.World;

namespace ZooGame.Tests.PlayMode.Staff
{
    /// <summary>
    /// Staff in the real Zoo scene: hiring through the purchase flow, a vendor walking to and opening a stall, a task
    /// being carried out by a keeper, demolition cleanup and the facility panel following the placement selection.
    /// Layout: path z = 34 (x 30..41), a 6x4 pen at (30,35) touching it, a 2x2 entrance at (40,32), stalls at z = 33.
    /// </summary>
    public class StaffPlayModeTests
    {
        ZooSceneBinder _binder;
        GameClock _clock;
        readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        PlacedObject _entrance, _food;
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
            Assert.IsNotNull(_binder.StaffHiring, "staff not bound: run ZooGame > M8 > Create Or Update Project Assets");
            _clock = GameManager.Instance.Clock;
            _binder.VisitorSpawner.AutoSpawn = false;
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

        void BuildZoo()
        {
            var cells = new List<GridCoord>();
            for (int x = 30; x <= 41; x++) cells.Add(C(x, 34));
            _binder.Construction.BuildPaths(cells);

            var edges = new List<FenceEdge>();
            for (int x = 30; x < 36; x++)
            {
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.X, x, 35), EdgeKind.Fence));
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.X, x, 39), EdgeKind.Fence));
            }
            for (int z = 35; z < 39; z++)
            {
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.Z, 30, z), EdgeKind.Fence));
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.Z, 36, z), EdgeKind.Fence));
            }
            _binder.Construction.BuildFences(edges);
            var pen = _binder.Construction.Enclosures.GetAt(C(30, 35));
            Assert.IsNotNull(pen, "pen should be closed");
            _penId = pen.Id;

            _entrance = Place(VisitorFacilityKind.Entrance, 40, 32, 2, 2);
            _food = Place(VisitorFacilityKind.FoodStall, 33, 33);
        }

        IEnumerator WaitUntil(Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        }

        StaffInstance Hire(StaffRole role)
        {
            Assert.AreEqual(StaffHireResult.Success, _binder.StaffHiring.Hire(role, out var s));
            return s;
        }

        string FoodId => VisitorDestinationService.FacilityIdFor(_food.Id);

        // ---- Tests ----

        [UnityTest]
        public IEnumerator SceneBindsTheStaffSystems()
        {
            Assert.IsNotNull(_binder.Staff);
            Assert.IsNotNull(_binder.StaffAssignments);
            Assert.IsNotNull(_binder.StaffTasks);
            Assert.IsNotNull(_binder.StaffWork);
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<StaffPanel>(FindObjectsInactive.Include));
            yield return null;
        }

        [UnityTest]
        public IEnumerator HiringChargesMoney_AndGivesTheMemberABody()
        {
            BuildZoo();
            long before = _binder.Economy.GetBalance(Currency.Money);

            var keeper = Hire(StaffRole.Keeper);
            yield return null;

            Assert.AreEqual(before - 300, _binder.Economy.GetBalance(Currency.Money));
            Assert.IsTrue(_binder.StaffSpawner.TryGetController(keeper.StaffId, out var body));
            Assert.IsTrue(body.gameObject.activeInHierarchy);

            _binder.StaffHiring.Dismiss(keeper.StaffId);
            yield return null;
            Assert.IsFalse(body.gameObject.activeInHierarchy, "body returned to the pool");
        }

        [UnityTest]
        public IEnumerator Vendor_WalksToTheStall_AndVisitorsCanThenUseIt()
        {
            BuildZoo();
            Assert.IsTrue(_binder.VisitorDestinations.TryGet(FoodId, out var dest));
            Assert.IsFalse(dest.Enabled, "closed without a vendor");

            var vendor = Hire(StaffRole.Vendor);
            Assert.AreEqual(StaffAssignmentResult.Success, _binder.StaffAssignments.Assign(vendor.StaffId, FoodId));
            _clock.SetSpeed(SimulationSpeed.X3);
            yield return WaitUntil(() => vendor.State == StaffState.Working, 20f);

            Assert.AreEqual(StaffState.Working, vendor.State);
            Assert.IsTrue(_binder.VisitorDestinations.TryGet(FoodId, out dest));
            Assert.IsTrue(dest.Enabled, "open while the vendor works");

            long incomeBefore = _binder.Economy.GetLifetimeIncome(Currency.Money);
            var failure = _binder.VisitorSpawner.TrySpawn(out var visitor);
            Assert.AreEqual(VisitorSpawnFailure.None, failure);
            yield return null;
            visitor.Instance.Hunger = 95f;
            yield return WaitUntil(() => visitor.Instance == null || visitor.Instance.Hunger < 50f, 25f);

            Assert.IsNotNull(visitor.Instance);
            Assert.Less(visitor.Instance.Hunger, 50f, "ate at the staffed stall");
            long ticket = _binder.Economy.Settings.EntranceTicketPrice;
            Assert.Greater(_binder.Economy.GetLifetimeIncome(Currency.Money) - incomeBefore, ticket, "a food sale on top of the ticket");
        }

        [UnityTest]
        public IEnumerator DemolishingTheStall_ReleasesTheVendor()
        {
            BuildZoo();
            var vendor = Hire(StaffRole.Vendor);
            _binder.StaffAssignments.Assign(vendor.StaffId, FoodId);
            _clock.SetSpeed(SimulationSpeed.X3);
            yield return WaitUntil(() => vendor.State == StaffState.Working, 20f);

            Assert.IsTrue(_binder.Placement.Remove(_food));
            yield return null;

            Assert.IsNull(vendor.AssignedFacilityId);
            Assert.AreEqual(StaffState.Idle, vendor.State);
            Assert.IsTrue(_binder.Staff.Contains(vendor.StaffId));
        }

        [UnityTest]
        public IEnumerator Keeper_CarriesOutAPostedTask()
        {
            BuildZoo();
            var keeper = Hire(StaffRole.Keeper);
            var task = _binder.StaffTasks.PostForEnclosure(StaffRole.Keeper, _penId, 2f, "tend");
            Assert.IsNotNull(task);
            _clock.SetSpeed(SimulationSpeed.X3);

            yield return WaitUntil(() => task.State == StaffTaskState.Completed, 25f);

            Assert.AreEqual(StaffTaskState.Completed, task.State);
            Assert.AreEqual(StaffState.Idle, keeper.State);
            Assert.IsNull(keeper.AssignedFacilityId);
            Assert.IsNull(keeper.ActiveTaskId);
        }

        [UnityTest]
        public IEnumerator PausedClock_FreezesStaffWalking()
        {
            BuildZoo();
            var vendor = Hire(StaffRole.Vendor);
            _binder.StaffAssignments.Assign(vendor.StaffId, FoodId);
            _clock.SetSpeed(SimulationSpeed.Paused);
            var at = vendor.Position;

            for (int i = 0; i < 20; i++) yield return null;

            Assert.AreEqual(at, vendor.Position);
            Assert.AreEqual(StaffState.Travelling, vendor.State);
        }

        [UnityTest]
        public IEnumerator FacilityPanel_AppearsForASelectedStall()
        {
            BuildZoo();
            var panel = UnityEngine.Object.FindAnyObjectByType<StaffPanel>(FindObjectsInactive.Include);
            var section = panel.transform.Find("Content/Facility Section");
            Assert.IsNotNull(section, "panel layout");
            yield return null;
            Assert.IsFalse(section.gameObject.activeSelf);

            var session = _binder.PlacementController.Session;
            Assert.IsTrue(session.Select(_food));
            yield return null;
            Assert.IsTrue(section.gameObject.activeSelf, "shown for a staffable facility");

            session.ClearSelection();
            yield return null;
            Assert.IsFalse(section.gameObject.activeSelf);
        }
    }
}
