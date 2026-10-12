using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Data;
using ZooGame.Economy;
using ZooGame.Placement;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Economy
{
    /// <summary>Object placement purchases: charged after success only, blocked when unaffordable, refunded on delete.</summary>
    public class PurchaseTests
    {
        readonly List<Object> _created = new List<Object>();
        ZooGrid _grid;
        PlacementMap _map;
        PlacementSession _session;
        EconomyService _economy;
        PurchaseService _purchases;

        [SetUp]
        public void SetUp() => Build(money: 1000, refund: 0.5f);

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        void Build(long money, float refund)
        {
            _grid = new ZooGrid(GridSettings.Centered(16, 16, 1f, Vector3.zero, 8, 8)); // unlocked 4..11
            _map = new PlacementMap(_grid);
            _economy = new EconomyService(new EconomySettings(money, 0, 10, 6, 4, refund, 100));
            _purchases = new PurchaseService(_economy);
            _session = new PlacementSession(_map, new PlacementValidator(_map), _purchases);
        }

        PlaceableDefinition Define(string id, int cost, PlaceableCategory category = PlaceableCategory.Decoration, int w = 1, int h = 1)
        {
            var def = ScriptableObject.CreateInstance<PlaceableDefinition>();
            def.Configure(id, id, w, h, true, category, null, cost);
            _created.Add(def);
            return def;
        }

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        bool Place(PlaceableDefinition def, int x, int z)
        {
            _session.BeginPlacing(def, C(x, z));
            return _session.Confirm();
        }

        // ---- Success ----

        [Test]
        public void PlacingAnObject_ChargesItsDefinitionCost_AfterItExists()
        {
            var def = Define("bench", 120, PlaceableCategory.Service);
            Assert.IsTrue(Place(def, 5, 5));

            Assert.AreEqual(1, _map.Count);
            Assert.AreEqual(880, _economy.GetBalance(Currency.Money));
            _economy.TryGetLastTransaction(out var t);
            Assert.AreEqual(Currency.Money, t.Currency);
            Assert.AreEqual(TransactionType.Expense, t.Type);
            Assert.AreEqual(TransactionCategory.BuildingPurchase, t.Category);
            Assert.AreEqual(120, t.Amount);
            Assert.AreEqual("bench", t.SourceId);
            Assert.AreEqual(120, _session.Selected.PurchaseCost, "remembered for the refund");
        }

        [Test]
        public void EachPlaceableCategory_MapsToItsOwnExpenseCategory()
        {
            Assert.AreEqual(TransactionCategory.DecorationPurchase, PurchaseService.CategoryFor(PlaceableCategory.Decoration));
            Assert.AreEqual(TransactionCategory.BuildingPurchase, PurchaseService.CategoryFor(PlaceableCategory.Building));
            Assert.AreEqual(TransactionCategory.HabitatObjectPurchase, PurchaseService.CategoryFor(PlaceableCategory.Habitat));
            Assert.AreEqual(TransactionCategory.BuildingPurchase, PurchaseService.CategoryFor(PlaceableCategory.Service));
        }

        [Test]
        public void ADefinitionWithNoCost_PlacesFree_WithoutATransaction()
        {
            Assert.IsTrue(Place(Define("free", 0), 5, 5));
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money));
            Assert.AreEqual(0, _economy.HistoryCount);
        }

        [Test]
        public void PlacementWithoutAPurchaseService_IsFree()
        {
            var session = new PlacementSession(_map, new PlacementValidator(_map));
            session.BeginPlacing(Define("x", 999999), C(5, 5));
            Assert.IsTrue(session.IsAffordable);
            Assert.IsTrue(session.Confirm());
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money));
        }

        // ---- Blocked / failed / cancelled ----

        [Test]
        public void AnUnaffordableObject_IsBlocked_NotPlaced_AndNotCharged()
        {
            var def = Define("castle", 5000, PlaceableCategory.Building);
            _session.BeginPlacing(def, C(5, 5));
            Assert.IsTrue(_session.Result.IsValid, "the spot is fine");
            Assert.IsFalse(_session.IsAffordable);

            InsufficientFundsInfo info = default;
            _economy.InsufficientFunds += i => info = i;
            Assert.IsFalse(_session.Confirm());

            Assert.AreEqual(0, _map.Count);
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money));
            Assert.AreEqual(0, _economy.HistoryCount);
            Assert.IsTrue(_session.IsPlacing, "the preview stays so the player can cancel or earn more");
            Assert.AreEqual(5000, info.Needed);
            Assert.AreEqual(1000, info.Available);
        }

        [Test]
        public void AnInvalidPlacement_IsNotCharged()
        {
            var def = Define("bench", 100);
            Assert.IsFalse(Place(def, 0, 0), "locked land");
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money));
            Assert.AreEqual(0, _economy.HistoryCount);
        }

        [Test]
        public void ACancelledPlacement_IsNotCharged()
        {
            _session.BeginPlacing(Define("bench", 100), C(5, 5));
            _session.Cancel();
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money));
            Assert.AreEqual(0, _economy.HistoryCount);
            Assert.AreEqual(0, _map.Count);
        }

        [Test]
        public void BecomingAffordable_UnblocksTheSameConfirm()
        {
            var def = Define("pricey", 1500);
            _session.BeginPlacing(def, C(5, 5));
            Assert.IsFalse(_session.Confirm());
            _economy.AddIncome(Currency.Money, 600, TransactionCategory.Donation, "gift");
            Assert.IsTrue(_session.IsAffordable);
            Assert.IsTrue(_session.Confirm());
            Assert.AreEqual(100, _economy.GetBalance(Currency.Money));
        }

        [Test]
        public void MovingAndRotating_AreFree()
        {
            var def = Define("bench", 100, PlaceableCategory.Decoration, 2, 1);
            Assert.IsTrue(Place(def, 5, 5));
            long after = _economy.GetBalance(Currency.Money);

            Assert.IsTrue(_session.RotateSelected());
            Assert.IsTrue(_session.BeginMove());
            Assert.AreEqual(0, _session.Cost);
            _session.SetOrigin(C(8, 8));
            Assert.IsTrue(_session.Confirm());

            Assert.AreEqual(after, _economy.GetBalance(Currency.Money));
            Assert.AreEqual(1, _economy.HistoryCount);
        }

        // ---- Refunds ----

        [Test]
        public void DeletingAnObject_RefundsFloorOfHalfWhatWasPaid()
        {
            Assert.IsTrue(Place(Define("odd", 25), 5, 5));
            Assert.AreEqual(975, _economy.GetBalance(Currency.Money));

            Assert.IsTrue(_session.DeleteSelected());

            Assert.AreEqual(0, _map.Count);
            Assert.AreEqual(987, _economy.GetBalance(Currency.Money), "975 + floor(25 x 0.5) = 975 + 12");
            _economy.TryGetLastTransaction(out var t);
            Assert.AreEqual(TransactionType.Income, t.Type);
            Assert.AreEqual(TransactionCategory.DemolitionRefund, t.Category);
            Assert.AreEqual(12, t.Amount);
            Assert.AreEqual(Currency.Money, t.Currency);
        }

        [Test]
        public void ARefund_NeverExceedsTheOriginalCost()
        {
            Build(1000, refund: 1f);
            Assert.IsTrue(Place(Define("x", 40), 5, 5));
            _session.DeleteSelected();
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money), "full refund at 100%, never more");

            Build(1000, refund: 7f); // out-of-range rate is clamped
            Assert.IsTrue(Place(Define("y", 40), 5, 5));
            _session.DeleteSelected();
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money));
        }

        [Test]
        public void AnObjectAddedWithoutAPurchase_RefundsNothing()
        {
            var obj = _map.Add(Define("gift", 500), C(5, 5), Rotation90.Deg0);
            _session.Select(obj);
            Assert.IsTrue(_session.DeleteSelected());
            Assert.AreEqual(1000, _economy.GetBalance(Currency.Money));
            Assert.AreEqual(0, _economy.HistoryCount);
        }

        [Test]
        public void RefundIsBasedOnWhatWasPaid_NotOnALaterDefinitionPrice()
        {
            var def = Define("shifting", 100);
            Assert.IsTrue(Place(def, 5, 5));
            def.Configure("shifting", "shifting", 1, 1, true, PlaceableCategory.Decoration, null, 400); // price changed since
            _session.DeleteSelected();
            Assert.AreEqual(900 + 50, _economy.GetBalance(Currency.Money));
        }

        [Test]
        public void MoneyPurchases_DoNotTouchConservationTokens()
        {
            Assert.IsTrue(Place(Define("bench", 100), 5, 5));
            _session.DeleteSelected();
            Assert.AreEqual(0, _economy.GetBalance(Currency.ConservationTokens));
            Assert.AreEqual(0, _economy.State.LifetimeConservationTokensSpent);
            Assert.AreEqual(0, _economy.State.LifetimeConservationTokensEarned);
        }

        // ---- Quotes ----

        [Test]
        public void Quote_ShowsTotalCostAndAffordability()
        {
            var q = _purchases.Quote(30, 4);
            Assert.AreEqual(120, q.TotalCost);
            Assert.IsTrue(q.CanAfford);
            Assert.AreEqual(0, q.Shortfall);

            var big = _purchases.Quote(300, 4);
            Assert.AreEqual(1200, big.TotalCost);
            Assert.IsFalse(big.CanAfford);
            Assert.AreEqual(200, big.Shortfall);
        }
    }
}
