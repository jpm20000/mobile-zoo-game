using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZooGame.Construction;
using ZooGame.Data;
using ZooGame.Economy;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Economy
{
    /// <summary>
    /// Unit-based construction pricing and demolition refunds through the real construction model: only committed
    /// valid units are charged, unaffordable batches build and charge nothing, refunds floor per unit.
    /// </summary>
    public class ConstructionPurchaseTests
    {
        readonly List<Object> _created = new List<Object>();
        ConstructionFixture _construction;
        EconomyService _economy;
        PurchaseService _purchases;
        ConstructionCatalog _catalog;
        PathDefinition _path;
        FenceDefinition _fence;
        FenceDefinition _gate;
        ConstructionPurchaser _purchaser;

        [SetUp]
        public void SetUp() => Build(money: 1000, pathCost: 2, fenceCost: 5, gateCost: 8);

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        void Build(long money, int pathCost, int fenceCost, int gateCost, float refund = 0.5f)
        {
            _construction = new ConstructionFixture();
            _economy = new EconomyService(new EconomySettings(money, 0, 10, 6, 4, refund, 100));
            _purchases = new PurchaseService(_economy);

            _path = Track(ScriptableObject.CreateInstance<PathDefinition>()).ConfigureCost(pathCost);
            _fence = Track(ScriptableObject.CreateInstance<FenceDefinition>()).Configure(EdgeKind.Fence, fenceCost);
            _gate = Track(ScriptableObject.CreateInstance<FenceDefinition>()).Configure(EdgeKind.Gate, gateCost);

            _catalog = Track(ScriptableObject.CreateInstance<ConstructionCatalog>());
            var so = new SerializedObject(_catalog);
            var paths = so.FindProperty("paths");
            paths.arraySize = 1;
            paths.GetArrayElementAtIndex(0).objectReferenceValue = _path;
            var fences = so.FindProperty("fences");
            fences.arraySize = 2;
            fences.GetArrayElementAtIndex(0).objectReferenceValue = _fence;
            fences.GetArrayElementAtIndex(1).objectReferenceValue = _gate;
            so.ApplyModifiedPropertiesWithoutUndo();

            _purchaser = new ConstructionPurchaser(_construction.Model, _purchases, _catalog);
        }

        T Track<T>(T o) where T : Object
        {
            _created.Add(o);
            return o;
        }

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        static List<GridCoord> Cells(params (int x, int z)[] cells)
        {
            var list = new List<GridCoord>();
            foreach (var c in cells) list.Add(C(c.x, c.z));
            return list;
        }

        // ---- Paths ----

        [Test]
        public void Paths_CostUnitCostTimesCellsBuilt()
        {
            var commit = _purchaser.TryBuildPaths(_path, Cells((5, 5), (6, 5), (7, 5)));

            Assert.AreEqual(3, commit.Units);
            Assert.AreEqual(6, commit.Money);
            Assert.AreEqual(994, _economy.GetBalance(Currency.Money));
            _economy.TryGetLastTransaction(out var t);
            Assert.AreEqual(TransactionCategory.PathConstruction, t.Category);
            Assert.AreEqual(Currency.Money, t.Currency);
            Assert.AreEqual(6, t.Amount);
            Assert.AreEqual(1, _economy.HistoryCount, "one transaction per committed batch");
        }

        [Test]
        public void Paths_OnlyValidUnbuiltCellsAreCharged()
        {
            _construction.BuildPaths((5, 5));
            // (5,5) already has a path, (0,0) is locked land, the other two build.
            var cells = Cells((5, 5), (0, 0), (6, 5), (7, 5));

            var quote = _purchaser.QuotePaths(_path, cells);
            Assert.AreEqual(2, quote.Units);
            Assert.AreEqual(4, quote.TotalCost);

            var commit = _purchaser.TryBuildPaths(_path, cells);
            Assert.AreEqual(2, commit.Units);
            Assert.AreEqual(996, _economy.GetBalance(Currency.Money));
        }

        [Test]
        public void Paths_WithNothingValid_BuildAndChargeNothing()
        {
            var commit = _purchaser.TryBuildPaths(_path, Cells((0, 0), (1, 1)));
            Assert.AreEqual(0, commit.Units);
            Assert.IsFalse(commit.Unaffordable);
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money));
            Assert.AreEqual(0, _economy.HistoryCount);
        }

        [Test]
        public void Paths_Unaffordable_BuildNothing_AndChargeNothing()
        {
            Build(money: 5, pathCost: 2, fenceCost: 5, gateCost: 8);
            var cells = Cells((5, 5), (6, 5), (7, 5)); // 6 > 5

            var commit = _purchaser.TryBuildPaths(_path, cells);

            Assert.IsTrue(commit.Unaffordable);
            Assert.AreEqual(0, commit.Units);
            Assert.AreEqual(0, _construction.PathCount());
            Assert.AreEqual(5, _economy.GetBalance(Currency.Money));
            Assert.AreEqual(0, _economy.HistoryCount);
        }

        [Test]
        public void Paths_AffordableBatchThatFitsExactly_Builds()
        {
            Build(money: 6, pathCost: 2, fenceCost: 5, gateCost: 8);
            var commit = _purchaser.TryBuildPaths(_path, Cells((5, 5), (6, 5), (7, 5)));
            Assert.AreEqual(3, commit.Units);
            Assert.AreEqual(0, _economy.GetBalance(Currency.Money));
        }

        [Test]
        public void Paths_ThatAreNeverConfirmed_CostNothing()
        {
            // Pending cells live in the tool; only Confirm reaches the purchaser. Quoting is read-only.
            var cells = Cells((5, 5), (6, 5));
            _purchaser.QuotePaths(_path, cells);
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money));
            Assert.AreEqual(0, _construction.PathCount());
        }

        // ---- Fences and gates ----

        static List<FenceEdge> Rect(int x, int z, int w, int d, EdgeKind kind = EdgeKind.Fence) =>
            ConstructionFixture.RectEdges(x, z, w, d, kind);

        [Test]
        public void Fences_CostSegmentCostTimesEdgesBuilt()
        {
            var edges = Rect(5, 5, 2, 2); // 8 edges
            var commit = _purchaser.TryBuildFences(_fence, edges);

            Assert.AreEqual(8, commit.Units);
            Assert.AreEqual(40, commit.Money);
            Assert.AreEqual(960, _economy.GetBalance(Currency.Money));
            _economy.TryGetLastTransaction(out var t);
            Assert.AreEqual(TransactionCategory.FenceConstruction, t.Category);
            Assert.AreEqual(Currency.Money, t.Currency);
        }

        [Test]
        public void Fences_AlreadyBuiltEdges_AreNotChargedAgain()
        {
            _purchaser.TryBuildFences(_fence, Rect(5, 5, 2, 2)); // 8 edges, 40
            // A 3x2 rect sharing the whole west side... overlaps 2 edges with the first? (5,5) 2x2 east side x=7, new rect starts at x=7.
            var overlap = Rect(7, 5, 2, 2); // shares the two edges on x = 7
            var quote = _purchaser.QuoteFences(_fence, overlap);
            Assert.AreEqual(6, quote.Units);
            var commit = _purchaser.TryBuildFences(_fence, overlap);
            Assert.AreEqual(6, commit.Units);
            Assert.AreEqual(1000 - 40 - 30, _economy.GetBalance(Currency.Money));
        }

        [Test]
        public void Fences_Unaffordable_BuildNothing()
        {
            Build(money: 39, pathCost: 2, fenceCost: 5, gateCost: 8);
            var commit = _purchaser.TryBuildFences(_fence, Rect(5, 5, 2, 2));
            Assert.IsTrue(commit.Unaffordable);
            Assert.AreEqual(0, _construction.Model.Fences.Count);
            Assert.AreEqual(39, _economy.GetBalance(Currency.Money));
        }

        [Test]
        public void Gates_UseTheirOwnDefinitionPrice()
        {
            var commit = _purchaser.TryBuildFences(_gate, new List<FenceEdge> { new FenceEdge(new EdgeCoord(EdgeAxis.X, 5, 5), EdgeKind.Gate) });
            Assert.AreEqual(1, commit.Units);
            Assert.AreEqual(992, _economy.GetBalance(Currency.Money));
        }

        [Test]
        public void Fences_OffTheUnlockedLand_AreNotChargedWhenRejected()
        {
            var edges = new List<FenceEdge>
            {
                new FenceEdge(new EdgeCoord(EdgeAxis.X, 5, 5), EdgeKind.Fence),   // fine
                new FenceEdge(new EdgeCoord(EdgeAxis.X, 0, 0), EdgeKind.Fence),   // locked land
            };
            var commit = _purchaser.TryBuildFences(_fence, edges);
            Assert.AreEqual(1, commit.Units);
            Assert.AreEqual(995, _economy.GetBalance(Currency.Money));
        }

        // ---- Demolition refunds ----

        [Test]
        public void DemolishingPaths_RefundsFloorOfHalfPerCell()
        {
            Build(money: 1000, pathCost: 5, fenceCost: 5, gateCost: 8); // floor(5 x 0.5) = 2 per cell
            var cells = Cells((5, 5), (6, 5), (7, 5));
            _purchaser.TryBuildPaths(_path, cells);
            Assert.AreEqual(985, _economy.GetBalance(Currency.Money));

            Assert.AreEqual(6, _purchaser.RefundQuote(cells, new List<EdgeCoord>()));
            var commit = _purchaser.Demolish(cells, new List<EdgeCoord>());

            Assert.AreEqual(3, commit.Units);
            Assert.AreEqual(6, commit.Money);
            Assert.AreEqual(991, _economy.GetBalance(Currency.Money));
            Assert.AreEqual(0, _construction.PathCount());
            _economy.TryGetLastTransaction(out var t);
            Assert.AreEqual(TransactionType.Income, t.Type);
            Assert.AreEqual(TransactionCategory.DemolitionRefund, t.Category);
            Assert.AreEqual(Currency.Money, t.Currency);
        }

        [Test]
        public void DemolishingFences_RefundsByKind()
        {
            var fenceEdge = new EdgeCoord(EdgeAxis.X, 5, 5);
            var gateEdge = new EdgeCoord(EdgeAxis.X, 6, 5);
            _purchaser.TryBuildFences(_fence, new List<FenceEdge> { new FenceEdge(fenceEdge, EdgeKind.Fence) });
            _purchaser.TryBuildFences(_gate, new List<FenceEdge> { new FenceEdge(gateEdge, EdgeKind.Gate) });
            long before = _economy.GetBalance(Currency.Money); // 1000 - 5 - 8

            var commit = _purchaser.Demolish(new List<GridCoord>(), new List<EdgeCoord> { fenceEdge, gateEdge });

            Assert.AreEqual(2, commit.Units);
            Assert.AreEqual(2 + 4, commit.Money, "floor(5 x 0.5) + floor(8 x 0.5)");
            Assert.AreEqual(before + 6, _economy.GetBalance(Currency.Money));
        }

        [Test]
        public void Demolition_NeverRefundsMoreThanWasSpent()
        {
            Build(money: 1000, pathCost: 3, fenceCost: 5, gateCost: 8, refund: 1f);
            var cells = Cells((5, 5), (6, 5));
            _purchaser.TryBuildPaths(_path, cells);
            _purchaser.Demolish(cells, new List<EdgeCoord>());
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money));
            Assert.LessOrEqual(_economy.State.LifetimeMoneyIncome, _economy.State.LifetimeMoneyExpenses);
        }

        [Test]
        public void MarkedButNotConfirmedDemolition_ChangesNothing()
        {
            var cells = Cells((5, 5));
            _purchaser.TryBuildPaths(_path, cells);
            long before = _economy.GetBalance(Currency.Money);
            _purchaser.RefundQuote(cells, new List<EdgeCoord>()); // quoting is read only
            Assert.AreEqual(before, _economy.GetBalance(Currency.Money));
            Assert.AreEqual(1, _construction.PathCount());
        }

        [Test]
        public void DemolishingNothingThatExists_RefundsNothing()
        {
            var commit = _purchaser.Demolish(Cells((5, 5)), new List<EdgeCoord> { new EdgeCoord(EdgeAxis.X, 5, 5) });
            Assert.AreEqual(0, commit.Units);
            Assert.AreEqual(0, commit.Money);
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money));
        }

        // ---- No economy ----

        [Test]
        public void WithoutAPurchaseService_ConstructionIsFree_AndDemolitionRefundsNothing()
        {
            var free = new ConstructionPurchaser(_construction.Model, null, _catalog);
            var cells = Cells((5, 5), (6, 5));
            var built = free.TryBuildPaths(_path, cells);
            Assert.AreEqual(2, built.Units);
            Assert.AreEqual(0, built.Money);
            var removed = free.Demolish(cells, new List<EdgeCoord>());
            Assert.AreEqual(2, removed.Units);
            Assert.AreEqual(0, removed.Money);
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money));
        }
    }
}
