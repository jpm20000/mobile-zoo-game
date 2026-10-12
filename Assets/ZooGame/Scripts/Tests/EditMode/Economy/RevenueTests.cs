using System.Collections.Generic;
using NUnit.Framework;
using ZooGame.Data;
using ZooGame.Economy;
using ZooGame.Placement;
using ZooGame.Tests.EditMode.Visitors;
using ZooGame.Visitors;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Economy
{
    /// <summary>Visitor-generated Money: admission, food, drink; free facilities; revenue only after success.</summary>
    public class RevenueTests
    {
        VisitorFixture _f;
        EconomyService _economy;
        RevenueService _revenue;
        PlacedObject _entrance, _food, _drink, _toilet, _bench;

        [SetUp]
        public void SetUp()
        {
            _f = new VisitorFixture();
            _economy = new EconomyService(EconomySettings.Default); // ticket 10, food 6, drink 4
            _revenue = new RevenueService(_economy);
            _f.Decisions.Revenue = _revenue;
        }

        [TearDown] public void TearDown() => _f.Dispose();

        void FullZoo()
        {
            _f.StandardPath();
            _f.Pen();
            _entrance = _f.Place(VisitorFacilityKind.Entrance, 11, 5);
            _food = _f.Place(VisitorFacilityKind.FoodStall, 9, 5);
            _drink = _f.Place(VisitorFacilityKind.DrinkStall, 8, 5);
            _toilet = _f.Place(VisitorFacilityKind.Toilet, 7, 5);
            _bench = _f.Place(VisitorFacilityKind.Bench, 6, 5);
        }

        long Money => _economy.GetBalance(Currency.Money);

        // ---- Entrance ----

        [Test]
        public void ASuccessfulAdmission_EarnsTheTicketPrice()
        {
            FullZoo();
            Assert.IsTrue(_f.Destinations.TryGetEntrance(out var entrance));
            var v = _f.AddVisitor(11, 5);

            Assert.IsTrue(_f.Decisions.BeginEntering(v, entrance));

            Assert.AreEqual(25010, Money);
            _economy.TryGetLastTransaction(out var t);
            Assert.AreEqual(TransactionCategory.EntranceTicket, t.Category);
            Assert.AreEqual(Currency.Money, t.Currency);
            Assert.AreEqual(TransactionType.Income, t.Type);
            Assert.AreEqual(10, t.Amount);
            Assert.AreEqual(entrance.Id, t.SourceId);
        }

        [Test]
        public void AnEntranceWithNoPath_AdmitsNobody_AndEarnsNothing()
        {
            var placed = _f.Place(VisitorFacilityKind.Entrance, 11, 5); // no path anywhere
            Assert.IsTrue(_f.Destinations.TryGet(VisitorFixture.FacilityId(placed), out var entrance));
            Assert.IsFalse(entrance.IsPathAccessible);
            var v = _f.AddVisitor(11, 5);

            Assert.IsFalse(_f.Decisions.BeginEntering(v, entrance));
            Assert.AreEqual(25000, Money);
            Assert.AreEqual(0, _economy.HistoryCount);
        }

        [Test]
        public void EachAdmissionPaysOnce()
        {
            FullZoo();
            _f.Destinations.TryGetEntrance(out var entrance);
            for (int i = 0; i < 3; i++) Assert.IsTrue(_f.Decisions.BeginEntering(_f.AddVisitor(11, 5), entrance));
            Assert.AreEqual(25030, Money);
        }

        // ---- Food and drink ----

        [Test]
        public void EatingAtAFoodStall_EarnsTheFoodPrice_OnlyOnArrival()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 80f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.SeekingFood, v.State);
            Assert.AreEqual(25000, Money, "choosing the destination earns nothing");

            _f.AgentOf(v).Arrive();

            Assert.AreEqual(25006, Money);
            _economy.TryGetLastTransaction(out var t);
            Assert.AreEqual(TransactionCategory.FoodSale, t.Category);
            Assert.AreEqual(6, t.Amount);
            Assert.AreEqual(VisitorFixture.FacilityId(_food), t.SourceId);
        }

        [Test]
        public void DrinkingAtADrinkStall_EarnsTheDrinkPrice()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.Thirst = 85f;
            _f.Decisions.Decide(v);
            _f.AgentOf(v).Arrive();

            Assert.AreEqual(25004, Money);
            _economy.TryGetLastTransaction(out var t);
            Assert.AreEqual(TransactionCategory.DrinkSale, t.Category);
            Assert.AreEqual(VisitorFixture.FacilityId(_drink), t.SourceId);
        }

        [Test]
        public void ToiletsAndBenches_AreFree()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.ToiletNeed = 80f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.SeekingToilet, v.State);
            _f.AgentOf(v).Arrive();
            Assert.AreEqual(0f, v.ToiletNeed);

            var tired = _f.AddVisitor(10, 6);
            tired.Energy = 5f;
            _f.Decisions.Decide(tired);
            _f.AgentOf(tired).Arrive();

            Assert.AreEqual(25000, Money);
            Assert.AreEqual(0, _economy.HistoryCount);
        }

        [Test]
        public void AFacilityThatVanishedBeforeArrival_EarnsNothing()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 80f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.SeekingFood, v.State);

            _f.Placement.Remove(_food); // demolished while the visitor walks
            _f.Decisions.Process(); // the visitor is redirected
            _f.AgentOf(v).Arrive();

            Assert.AreEqual(25000, Money, "no sale without a successful use");
        }

        [Test]
        public void VisitorsWithoutARevenueService_PayNothing()
        {
            FullZoo();
            _f.Decisions.Revenue = null;
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 80f;
            _f.Decisions.Decide(v);
            _f.AgentOf(v).Arrive();
            Assert.AreEqual(25000, Money);
        }

        // ---- Revenue service on its own ----

        [Test]
        public void PricesComeFromSettings()
        {
            var custom = new EconomyService(new EconomySettings(0, 0, 25, 9, 7, 0.5f, 10));
            var revenue = new RevenueService(custom);
            revenue.RecordAdmission("e");
            revenue.RecordFacilityUse(VisitorFacilityKind.FoodStall, "f");
            revenue.RecordFacilityUse(VisitorFacilityKind.DrinkStall, "d");
            Assert.AreEqual(25 + 9 + 7, custom.GetBalance(Currency.Money));
            Assert.AreEqual(0, custom.GetBalance(Currency.ConservationTokens));
        }

        [Test]
        public void ZeroPrices_ProduceNoTransactions()
        {
            var custom = new EconomyService(new EconomySettings(0, 0, 0, 0, 0, 0.5f, 10));
            var revenue = new RevenueService(custom);
            Assert.IsFalse(revenue.RecordAdmission("e"));
            Assert.IsFalse(revenue.RecordFacilityUse(VisitorFacilityKind.FoodStall, "f"));
            Assert.AreEqual(0, custom.HistoryCount);
        }

        [Test]
        public void FreeFacilityKinds_ReturnFalse()
        {
            Assert.IsFalse(_revenue.RecordFacilityUse(VisitorFacilityKind.Toilet, "t"));
            Assert.IsFalse(_revenue.RecordFacilityUse(VisitorFacilityKind.Bench, "b"));
            Assert.IsFalse(_revenue.RecordFacilityUse(VisitorFacilityKind.Entrance, "e"));
            Assert.IsFalse(_revenue.RecordFacilityUse(VisitorFacilityKind.None, "n"));
            Assert.AreEqual(0, _economy.HistoryCount);
        }
    }
}
