using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Animals;
using ZooGame.Economy;
using ZooGame.Progression;
using ZooGame.Quests;
using ZooGame.Staff;
using ZooGame.Visitors;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Quests
{
    /// <summary>The real systems feeding the quest service: events in, objective progress out, no polling.</summary>
    public class QuestBridgeTests : QuestTestBase
    {
        PlacementFixture _placement;
        ConstructionModel _construction;
        AnimalRegistry _animals;
        VisitorRegistry _visitors;
        StaffRegistry _staff;
        QuestEventBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            _placement = new PlacementFixture();
            _construction = new ConstructionModel(_placement.Grid);
            _animals = new AnimalRegistry();
            _visitors = new VisitorRegistry();
            _staff = new StaffRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            _bridge?.Dispose();
            _placement.Dispose();
        }

        QuestService Wire(params QuestDefinition[] definitions)
        {
            var world = new QuestWorldQuery(_placement.Map, _placement.Grid, _construction, _animals, _visitors, _staff,
                null, Progression);
            var service = new QuestService(new QuestLibrary(definitions), world, new QuestRewardService(Economy, Progression));
            Quests = service;
            _bridge = new QuestEventBridge(service, new QuestEventSources
            {
                Placement = _placement.Map,
                Grid = _placement.Grid,
                Construction = _construction,
                Animals = _animals,
                Visitors = _visitors,
                Staff = _staff,
                Progression = Progression,
                Economy = Economy
            });
            return service;
        }

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        [Test]
        public void PlacingAnObject_ProgressesPlaceObject_ById()
        {
            var stall = _placement.Define("food_stall", 1, 1);
            Wire(Def("q", new[] { Obj("o", QuestObjectiveType.PlaceObject, 1, "food_stall") }));

            _placement.Map.Add(_placement.Define("bench", 1, 1), C(5, 5), Rotation90.Deg0);
            Assert.AreEqual(QuestState.Active, StateOf("q"));

            _placement.Map.Add(stall, C(6, 5), Rotation90.Deg0);
            Assert.AreEqual(QuestState.Completed, StateOf("q"));
        }

        [Test]
        public void BuildingPaths_ProgressesBuildPath_ThroughTheGridPathCount()
        {
            Wire(Def("q", new[] { Obj("o", QuestObjectiveType.BuildPath, 3) }));

            _construction.BuildPaths(new[] { C(5, 5), C(6, 5) });
            Assert.AreEqual(2, Get("q").Objectives[0].CurrentValue);
            Assert.AreEqual(2, _placement.Grid.PathCount);

            _construction.BuildPaths(new[] { C(7, 5) });
            Assert.AreEqual(QuestState.Completed, StateOf("q"));
        }

        [Test]
        public void GridPathCount_TracksBuildAndRemove_WithoutDoubleCounting()
        {
            var grid = _placement.Grid;
            Assert.AreEqual(0, grid.PathCount);
            grid.SetPath(C(5, 5), true);
            grid.SetPath(C(5, 5), true); // repeat: no change
            grid.SetPath(C(6, 5), true);
            Assert.AreEqual(2, grid.PathCount);
            grid.SetPath(C(5, 5), false);
            grid.SetPath(C(5, 5), false);
            Assert.AreEqual(1, grid.PathCount);
        }

        [Test]
        public void RegisteringAnimals_ProgressesOwnSpeciesAndAnimalCounts()
        {
            Wire(Def("q", new[]
            {
                Obj("species", QuestObjectiveType.OwnSpeciesCount, 2),
                Obj("animals", QuestObjectiveType.OwnAnimalCount, 3),
            }));

            _animals.Register(new AnimalInstance("a1", "lion", "Leo", AnimalSex.Male, null, Vector3.zero));
            _animals.Register(new AnimalInstance("a2", "lion", "Lea", AnimalSex.Female, null, Vector3.zero));
            Assert.AreEqual(1, Get("q").Objectives[0].CurrentValue);
            Assert.AreEqual(2, Get("q").Objectives[1].CurrentValue);

            _animals.Register(new AnimalInstance("a3", "zebra", "Zed", AnimalSex.Male, null, Vector3.zero));
            Assert.AreEqual(QuestState.Completed, StateOf("q"));
        }

        [Test]
        public void VisitorsEnteringAndStaffHiring_ProgressTheirObjectives()
        {
            Wire(Def("q", new[]
            {
                Obj("visitors", QuestObjectiveType.ReachVisitorCount, 2),
                Obj("keeper", QuestObjectiveType.HireStaff, 1, "Keeper"),
            }));

            _visitors.Register(new VisitorInstance("v1", Vector3.zero, 100f));
            _visitors.Register(new VisitorInstance("v2", Vector3.zero, 100f));
            Assert.IsTrue(Get("q").Objectives[0].IsComplete);

            _staff.Register(StaffInstance.CreateNew(StaffRole.Vendor, "V", Vector3.zero));
            Assert.IsFalse(Get("q").Objectives[1].IsComplete);
            _staff.Register(StaffInstance.CreateNew(StaffRole.Keeper, "K", Vector3.zero));
            Assert.AreEqual(QuestState.Completed, StateOf("q"));
        }

        [Test]
        public void VisitorRevenue_ProgressesEarnMoney_ThroughTransactions()
        {
            Wire(Def("q", new[] { Obj("o", QuestObjectiveType.EarnMoney, 20) }));
            Economy.AddIncome(Currency.Money, 12, TransactionCategory.EntranceTicket, "entrance");
            Assert.AreEqual(12, Get("q").Objectives[0].CurrentValue);
            Economy.TrySpend(Currency.Money, 100, TransactionCategory.BuildingPurchase, "x"); // spending is not earning
            Economy.AddIncome(Currency.Money, 8, TransactionCategory.FoodSale, "stall");
            Assert.AreEqual(QuestState.Completed, StateOf("q"));
        }

        [Test]
        public void XpFromProgression_ProgressesReachZooLevel()
        {
            Wire(Def("q", new[] { Obj("o", QuestObjectiveType.ReachZooLevel, 2) }));
            Progression.TryAddXp(50, "test");
            Assert.AreEqual(QuestState.Active, StateOf("q"));
            Progression.TryAddXp(60, "test"); // 110 XP = level 2
            Assert.AreEqual(QuestState.Completed, StateOf("q"));
        }

        [Test]
        public void Dispose_StopsListening()
        {
            var service = Wire(Def("q", new[] { Obj("o", QuestObjectiveType.BuildPath, 1) }));
            _bridge.Dispose();
            _bridge = null;
            _construction.BuildPaths(new[] { C(5, 5) });
            Assert.AreEqual(0, Get("q").Objectives[0].CurrentValue);
            Assert.AreEqual(QuestState.Active, StateOf("q"));
            Assert.IsNotNull(service);
        }
    }

    /// <summary>The XP and level service quests grant into.</summary>
    public class ProgressionServiceTests
    {
        [Test]
        public void Xp_AccumulatesAndMapsToLevels()
        {
            var p = new ProgressionService(new ProgressionSettings(new long[] { 0, 100, 300 }));
            Assert.AreEqual(1, p.Level);
            Assert.IsTrue(p.TryAddXp(99, "a"));
            Assert.AreEqual(1, p.Level);
            Assert.AreEqual(1, p.XpToNextLevel);
            p.TryAddXp(1, "a");
            Assert.AreEqual(2, p.Level);
            p.TryAddXp(10000, "a");
            Assert.AreEqual(3, p.Level, "capped at the highest level");
            Assert.AreEqual(0, p.XpToNextLevel);
        }

        [Test]
        public void InvalidAmounts_AreRejected()
        {
            var p = new ProgressionService();
            Assert.IsFalse(p.TryAddXp(0, "a"));
            Assert.IsFalse(p.TryAddXp(-5, "a"));
            Assert.AreEqual(0, p.TotalXp);
            p.TryAddXp(long.MaxValue, "a");
            Assert.IsFalse(p.TryAddXp(1, "a"), "overflow is refused");
        }

        [Test]
        public void LevelChanged_IsRaisedWithOldAndNew()
        {
            var p = new ProgressionService(new ProgressionSettings(new long[] { 0, 10 }));
            var seen = new List<(int, int)>();
            p.LevelChanged += (o, n) => seen.Add((o, n));
            p.TryAddXp(5, "a");
            p.TryAddXp(5, "a");
            p.TryAddXp(5, "a");
            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual((1, 2), seen[0]);
        }

        [Test]
        public void SnapshotAndRestore_RoundTripThroughJson()
        {
            var p = new ProgressionService();
            p.TryAddXp(260, "a");
            string json = JsonUtility.ToJson(p.Snapshot());

            var q = new ProgressionService();
            Assert.IsTrue(q.Restore(JsonUtility.FromJson<ProgressionState>(json)));
            Assert.AreEqual(260, q.TotalXp);
            Assert.AreEqual(p.Level, q.Level);
            Assert.IsFalse(q.Restore(null));
        }
    }
}
