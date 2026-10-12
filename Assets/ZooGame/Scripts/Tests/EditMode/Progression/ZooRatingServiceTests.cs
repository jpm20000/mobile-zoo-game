using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Animals;
using ZooGame.Data;
using ZooGame.Placement;
using ZooGame.Progression;
using ZooGame.Staff;
using ZooGame.Tests.EditMode.Staff;
using ZooGame.Visitors;

namespace ZooGame.Tests.EditMode.Progression
{
    /// <summary>
    /// The rating service against the real animal, visitor, placement and staff systems: welfare, variety, beauty,
    /// happiness history and facility availability (placed, reachable, enabled, staffed).
    /// </summary>
    public class ZooRatingServiceTests
    {
        StaffFixture _f;
        ZooRatingSettings _settings;
        ZooRatingService _rating;
        readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _f = new StaffFixture();
            _settings = ZooRatingSettings.CreateDefault();
            _rating = NewService();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++) Object.DestroyImmediate(_created[i]);
            _created.Clear();
            _f.Dispose();
        }

        ZooRatingService NewService() =>
            new ZooRatingService(_settings, _f.V.Animals.Registry, _f.V.Registry, _f.Placement, _f.V.Destinations);

        // ---- Helpers ----

        AnimalInstance AddAnimal(string species, float welfare, string enclosureId = null)
        {
            var a = AnimalInstance.CreateNew(species, species, AnimalSex.Female, enclosureId, Vector3.zero);
            a.OverallWelfare = welfare;
            Assert.IsTrue(_f.V.Animals.Registry.Register(a));
            return a;
        }

        PlacedObject AddBeauty(int x, int z, float beauty)
        {
            var def = ScriptableObject.CreateInstance<PlaceableDefinition>();
            def.Configure("deco-" + _created.Count, "Deco", 1, 1);
            def.ConfigureBeauty(beauty);
            _created.Add(def);
            var obj = _f.Placement.Add(def, StaffFixture.C(x, z), ZooGame.World.Rotation90.Deg0);
            Assert.IsNotNull(obj);
            return obj;
        }

        void WithEntrance()
        {
            _f.V.StandardPath();
            _f.V.Place(VisitorFacilityKind.Entrance, 11, 5);
        }

        StaffInstance Staff(PlacedObject stall)
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            Assert.AreEqual(StaffAssignmentResult.Success, _f.Assignments.Assign(vendor.StaffId, StaffFixture.IdOf(stall)));
            _f.Settle(vendor);
            Assert.AreEqual(StaffState.Working, vendor.State);
            return vendor;
        }

        void OnlyCategory(RatingFacility keep)
        {
            for (int i = 0; i < ZooRatingSettings.FacilityCategoryCount; i++)
                _settings.FacilityCategoryEnabled[i] = i == (int)keep;
        }

        // ---- Empty zoo ----

        [Test]
        public void EmptyZoo_RatesZero_WithOneStar()
        {
            var b = _rating.Recalculate();
            Assert.AreEqual(0.0, b.AnimalWelfare, 1e-12);
            Assert.AreEqual(0.0, b.VisitorHappiness, 1e-12);
            Assert.AreEqual(0.0, b.AnimalVariety, 1e-12);
            Assert.AreEqual(0.0, b.ZooBeauty, 1e-12);
            Assert.AreEqual(0.0, b.Facilities, 1e-12);
            Assert.AreEqual(0.0, b.Rating, 1e-12);
            Assert.AreEqual(1, b.Stars);
        }

        [Test]
        public void ServiceWorksWithNoCollaboratorsAtAll()
        {
            var bare = new ZooRatingService(ZooRatingSettings.CreateDefault());
            Assert.AreEqual(0.0, bare.Rating, 1e-12);
            Assert.AreEqual(1, bare.Stars);
        }

        // ---- Animal welfare ----

        [Test]
        public void Welfare_IsTheAverageOverAllLivingAnimals()
        {
            AddAnimal("rabbit", 40);
            AddAnimal("zebra", 80);
            Assert.AreEqual(60.0, _rating.Recalculate().AnimalWelfare, 1e-9);
        }

        [Test]
        public void Welfare_CountsAnimalsInOpenOrMissingEnclosures()
        {
            AddAnimal("rabbit", 100, null);          // no enclosure
            AddAnimal("lion", 0, "enclosure-999");   // enclosure that does not exist
            Assert.AreEqual(50.0, _rating.Recalculate().AnimalWelfare, 1e-9);
        }

        [Test]
        public void Welfare_DropsAnimalsThatNoLongerLive()
        {
            AddAnimal("rabbit", 100);
            var dead = AddAnimal("lion", 0);
            Assert.AreEqual(50.0, _rating.Recalculate().AnimalWelfare, 1e-9);

            _f.V.Animals.Registry.Unregister(dead.AnimalId);
            Assert.AreEqual(100.0, _rating.Recalculate().AnimalWelfare, 1e-9);
        }

        // ---- Variety ----

        [Test]
        public void Variety_CountsEachSpeciesOnce()
        {
            AddAnimal("rabbit", 50);
            AddAnimal("rabbit", 50);
            AddAnimal("rabbit", 50);
            AddAnimal("zebra", 50);
            // 2 species of the 20 needed.
            Assert.AreEqual(10.0, _rating.Recalculate().AnimalVariety, 1e-9);
        }

        [Test]
        public void Variety_ReachesFullScoreAtTheTarget()
        {
            for (int i = 0; i < 20; i++) AddAnimal("species-" + i, 50);
            Assert.AreEqual(100.0, _rating.Recalculate().AnimalVariety, 1e-9);
            AddAnimal("species-21", 50);
            Assert.AreEqual(100.0, _rating.Recalculate().AnimalVariety, 1e-9);
        }

        // ---- Beauty ----

        [Test]
        public void Beauty_SumsPlacedObjectValues()
        {
            AddBeauty(5, 10, 10f);
            AddBeauty(6, 10, 15f);
            Assert.AreEqual(25.0, _rating.Recalculate().ZooBeauty, 1e-9);
        }

        [Test]
        public void Beauty_RemovedObjectsContributeNothing()
        {
            var a = AddBeauty(5, 10, 40f);
            AddBeauty(6, 10, 10f);
            Assert.AreEqual(50.0, _rating.Recalculate().ZooBeauty, 1e-9);

            Assert.IsTrue(_f.Placement.Remove(a));
            Assert.AreEqual(10.0, _rating.Recalculate().ZooBeauty, 1e-9);
        }

        [Test]
        public void Beauty_NegativeValueContributesZero_AndTotalClampsAtTarget()
        {
            AddBeauty(5, 10, -30f);
            AddBeauty(6, 10, 80f);
            Assert.AreEqual(80.0, _rating.Recalculate().ZooBeauty, 1e-9);
            AddBeauty(7, 10, 80f);
            Assert.AreEqual(100.0, _rating.Recalculate().ZooBeauty, 1e-9);
        }

        // ---- Visitor happiness ----

        [Test]
        public void Happiness_NoVisitorData_IsZero()
        {
            Assert.AreEqual(0.0, _rating.Recalculate().VisitorHappiness, 1e-12);
        }

        [Test]
        public void Happiness_WithoutHistory_UsesTheActiveVisitorAverage()
        {
            var a = _f.V.AddVisitor(5, 6);
            var b = _f.V.AddVisitor(6, 6);
            a.Happiness = 20f;
            b.Happiness = 60f;
            Assert.AreEqual(40.0, _rating.Recalculate().VisitorHappiness, 1e-9);
        }

        [Test]
        public void Happiness_HistoryTakesPrecedenceOverActiveVisitors()
        {
            var v = _f.V.AddVisitor(5, 6);
            v.Happiness = 10f;
            _rating.RecordVisitorHappiness(90f);
            Assert.AreEqual(90.0, _rating.Recalculate().VisitorHappiness, 1e-9);
        }

        [Test]
        public void Happiness_RollingWindowKeepsOnlyTheLatest50()
        {
            for (int i = 0; i < 10; i++) _rating.RecordVisitorHappiness(0f);
            for (int i = 0; i < 50; i++) _rating.RecordVisitorHappiness(100f);

            Assert.AreEqual(50, _rating.HappinessSampleCount);
            Assert.AreEqual(100.0, _rating.Recalculate().VisitorHappiness, 1e-9);
        }

        [Test]
        public void Happiness_PartialWindowAveragesWhatItHas()
        {
            _rating.RecordVisitorHappiness(30f);
            _rating.RecordVisitorHappiness(70f);
            Assert.AreEqual(50.0, _rating.Recalculate().VisitorHappiness, 1e-9);
        }

        [Test]
        public void Happiness_DepartureRecordsTheVisitorsFinalHappiness()
        {
            var v = _f.V.AddVisitor(5, 6);
            v.Happiness = 80f;
            _rating.RecordVisitorDeparture(v);
            _f.V.Registry.Unregister(v.VisitorId);
            Assert.AreEqual(80.0, _rating.Recalculate().VisitorHappiness, 1e-9);
        }

        [Test]
        public void Happiness_SamplesAreClamped()
        {
            _rating.RecordVisitorHappiness(-40f);
            _rating.RecordVisitorHappiness(float.NaN);
            _rating.RecordVisitorHappiness(500f);
            Assert.AreEqual(100.0 / 3.0, _rating.Recalculate().VisitorHappiness, 1e-9);
        }

        // ---- Facilities ----

        [Test]
        public void Facilities_NoEntrance_NothingIsReachable()
        {
            _f.V.StandardPath();
            Staff(_f.Stall(9, 5, VisitorFacilityKind.FoodStall));
            Assert.AreEqual(0.0, _rating.Recalculate().Facilities, 1e-12);
        }

        [Test]
        public void Facilities_UnstaffedVendorStallDoesNotCount()
        {
            WithEntrance();
            _f.Stall(9, 5, VisitorFacilityKind.FoodStall);
            OnlyCategory(RatingFacility.Food);

            Assert.AreEqual(0.0, _rating.Recalculate().Facilities, 1e-12);
            Assert.AreEqual(0, _rating.FacilityCount(RatingFacility.Food));
        }

        [Test]
        public void Facilities_StaffedVendorStallCounts_AndStopsCountingWhenTheVendorIsUnassigned()
        {
            WithEntrance();
            var stall = _f.Stall(9, 5, VisitorFacilityKind.FoodStall);
            OnlyCategory(RatingFacility.Food);
            var vendor = Staff(stall);

            Assert.AreEqual(50.0, _rating.Recalculate().Facilities, 1e-9); // 1 of 2
            Assert.AreEqual(1, _rating.FacilityCount(RatingFacility.Food));

            _f.Assignments.Unassign(vendor.StaffId);
            _f.Work.Process();
            Assert.AreEqual(0.0, _rating.Recalculate().Facilities, 1e-12);
        }

        [Test]
        public void Facilities_ToiletsAndBenchesCountWithoutStaff()
        {
            WithEntrance();
            _f.V.Place(VisitorFacilityKind.Toilet, 8, 5);
            _f.V.Place(VisitorFacilityKind.Toilet, 9, 5);
            for (int x = 4; x <= 7; x++) _f.V.Place(VisitorFacilityKind.Bench, x, 5);
            _settings.FacilityCategoryEnabled[(int)RatingFacility.Food] = false;
            _settings.FacilityCategoryEnabled[(int)RatingFacility.Drink] = false;

            Assert.AreEqual(100.0, _rating.Recalculate().Facilities, 1e-9);
        }

        [Test]
        public void Facilities_UnreachableFacilityDoesNotCount()
        {
            WithEntrance();
            _f.V.Place(VisitorFacilityKind.Toilet, 9, 9); // no path touches it
            OnlyCategory(RatingFacility.Toilets);
            Assert.AreEqual(0.0, _rating.Recalculate().Facilities, 1e-12);
        }

        [Test]
        public void Facilities_FacilityOnAnIsolatedPathDoesNotCount()
        {
            WithEntrance();
            _f.V.BuildPath(4, 9, 6);                       // separate path, not joined to the entrance path
            _f.V.Place(VisitorFacilityKind.Toilet, 5, 10);
            OnlyCategory(RatingFacility.Toilets);
            Assert.AreEqual(0.0, _rating.Recalculate().Facilities, 1e-12);
        }

        [Test]
        public void Facilities_ADisabledDestinationDoesNotCount()
        {
            WithEntrance();
            var toilet = _f.V.Place(VisitorFacilityKind.Toilet, 8, 5);
            OnlyCategory(RatingFacility.Toilets);
            Assert.AreEqual(50.0, _rating.Recalculate().Facilities, 1e-9);

            _f.V.Destinations.SetEnabled(StaffFixture.IdOf(toilet), false);
            Assert.AreEqual(0.0, _rating.Recalculate().Facilities, 1e-12);
        }

        [Test]
        public void Facilities_IgnoredCategoriesDoNotDragTheScoreDown()
        {
            WithEntrance();
            _f.V.Place(VisitorFacilityKind.Toilet, 8, 5);
            _f.V.Place(VisitorFacilityKind.Toilet, 7, 5);
            OnlyCategory(RatingFacility.Toilets);
            Assert.AreEqual(100.0, _rating.Recalculate().Facilities, 1e-9);
        }

        // ---- Whole rating ----

        [Test]
        public void Rating_CombinesComponentsWithTheDesignWeights()
        {
            AddAnimal("rabbit", 100);       // welfare 100, variety 1/20 = 5
            AddBeauty(5, 10, 100f);         // beauty 100
            _rating.RecordVisitorHappiness(50f);

            var b = _rating.Recalculate();
            double expected = 0.30 * 100 + 0.25 * 50 + 0.20 * 5 + 0.15 * 100 + 0.10 * 0;
            Assert.AreEqual(expected, b.Rating, 1e-6);
            Assert.AreEqual(expected, _rating.Rating, 1e-6);
            Assert.AreEqual(3, b.Stars); // 58.5
        }

        [Test]
        public void Rating_StaysWithinZeroTo100_WithExtremeData()
        {
            for (int i = 0; i < 30; i++) AddAnimal("species-" + i, 100f);
            AddBeauty(5, 10, 100000f);
            _rating.RecordVisitorHappiness(100f);
            WithEntrance();
            for (int x = 4; x <= 7; x++) _f.V.Place(VisitorFacilityKind.Bench, x, 5);
            _f.V.Place(VisitorFacilityKind.Toilet, 8, 5);
            _f.V.Place(VisitorFacilityKind.Toilet, 9, 5);

            var b = _rating.Recalculate();
            Assert.That(b.Rating, Is.InRange(0.0, 100.0));
            Assert.That(b.Stars, Is.InRange(1, 5));
        }

        [Test]
        public void IsStarUnlocked_UsesTheUnderlyingRating()
        {
            _settings.StarThresholds[0] = 30f; // 2 stars at 30
            var svc = NewService();
            svc.RecordVisitorHappiness(100f);  // 0.25 * 100 = 25
            svc.Recalculate();
            Assert.IsFalse(svc.IsStarUnlocked(2));

            AddAnimal("rabbit", 100);          // +30
            svc.Recalculate();
            Assert.IsTrue(svc.IsStarUnlocked(2));
            Assert.IsTrue(svc.IsStarUnlocked(1));
            Assert.IsFalse(svc.IsStarUnlocked(5));
        }

        // ---- Events and timing ----

        [Test]
        public void Changed_RaisedOnlyWhenTheRatingActuallyChanges()
        {
            int changed = 0;
            _rating.Changed += _ => changed++;

            _rating.Recalculate();
            Assert.AreEqual(0, changed, "nothing changed");

            AddAnimal("rabbit", 80);
            _rating.Recalculate();
            Assert.AreEqual(1, changed);

            _rating.Recalculate();
            Assert.AreEqual(1, changed, "same result, no event");
        }

        [Test]
        public void StarsChanged_ReportsOldAndNewStars()
        {
            int from = 0, to = 0, calls = 0;
            _rating.StarsChanged += (a, b) => { from = a; to = b; calls++; };

            _rating.RecordVisitorHappiness(100f);
            AddAnimal("rabbit", 100); // 25 + 30 + 1 = 56
            _rating.Recalculate();

            Assert.AreEqual(1, calls);
            Assert.AreEqual(1, from);
            Assert.AreEqual(3, to);
        }

        [Test]
        public void Tick_RecalculatesOnlyOncePerInterval()
        {
            _settings.RecalculateIntervalSeconds = 2f;
            var svc = NewService();
            AddAnimal("rabbit", 100);

            svc.Tick(1f);
            Assert.AreEqual(0.0, svc.Current.AnimalWelfare, 1e-12, "interval not reached");
            svc.Tick(1f);
            Assert.AreEqual(100.0, svc.Current.AnimalWelfare, 1e-9);
        }

        [Test]
        public void Tick_IgnoresZeroAndNegativeTime_SoPausingFreezesTheRating()
        {
            _settings.RecalculateIntervalSeconds = 0f;
            var svc = NewService();
            AddAnimal("rabbit", 100);

            svc.Tick(0f);
            svc.Tick(-1f);
            Assert.AreEqual(0.0, svc.Current.AnimalWelfare, 1e-12);
        }
    }
}
