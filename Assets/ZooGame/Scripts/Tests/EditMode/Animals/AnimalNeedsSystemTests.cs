using NUnit.Framework;
using UnityEngine;
using ZooGame.Animals;
using ZooGame.Core;
using ZooGame.Data;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Animals
{
    /// <summary>The needs simulation against real enclosures, placed resources and the GameClock.</summary>
    public class AnimalNeedsSystemTests
    {
        const float Tol = 0.01f;
        NeedsFixture _f;

        [SetUp] public void SetUp() => _f = new NeedsFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        // ---- Decay ----

        [Test]
        public void HungerAndThirst_DecayWithSimulatedTime()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            _f.Needs.Advance(16f); // 8 ticks (the per-frame cap) x 2 minutes = 16 minutes
            Assert.AreEqual(8, _f.Needs.TicksRun);
            Assert.AreEqual(100f - 1f * 16f, a.Hunger, Tol);
            Assert.AreEqual(100f - 2f * 16f, a.Thirst, Tol);
        }

        [Test]
        public void Ticks_AreScheduled_NotPerCall()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            _f.Needs.Advance(1.9f);
            Assert.AreEqual(0, _f.Needs.TicksRun);
            Assert.AreEqual(100f, a.Hunger);
            _f.Needs.Advance(0.1f);
            Assert.AreEqual(1, _f.Needs.TicksRun);
            Assert.AreEqual(98f, a.Hunger, Tol);
        }

        [Test]
        public void ALongHitch_IsCapped_AndLeftoverTimeIsDropped()
        {
            _f.Add(_f.Rabbit, _f.Pen());
            _f.Needs.Advance(1000f);
            Assert.AreEqual(_f.Config.MaxTicksPerAdvance, _f.Needs.TicksRun);
            _f.Needs.Advance(1.9f);
            Assert.AreEqual(_f.Config.MaxTicksPerAdvance, _f.Needs.TicksRun, "no backlog was kept");
        }

        [Test]
        public void Needs_NeverLeaveZeroTo100()
        {
            string pen = _f.Pen();
            var starving = _f.Add(_f.Rabbit, pen);
            _f.Needs.Step(100000f);
            Assert.AreEqual(0f, starving.Hunger);
            Assert.AreEqual(0f, starving.Thirst);

            _f.Place(HabitatResourceKind.Food, 1f, 6, 6);
            _f.Place(HabitatResourceKind.DrinkingWater, 1f, 7, 7);
            _f.Needs.Step(100000f);
            Assert.AreEqual(100f, starving.Hunger);
            Assert.AreEqual(100f, starving.Thirst);
            Assert.That(starving.OverallWelfare, Is.InRange(0f, 100f));
        }

        // ---- Recovery ----

        [Test]
        public void Food_RecoversHunger_EvenWithoutReachingIt()
        {
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen, 5, 5);
            _f.Place(HabitatResourceKind.Food, 1f, 7, 7);
            a.Hunger = 50f;
            _f.Needs.Advance(2f); // one tick: 50 - 1x2 + 10x2
            Assert.AreEqual(68f, a.Hunger, Tol);
            Assert.AreEqual(96f, a.Thirst, Tol, "food does not touch thirst");
        }

        [Test]
        public void DrinkingWater_RecoversThirst()
        {
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen);
            _f.Place(HabitatResourceKind.DrinkingWater, 1f, 6, 6);
            a.Thirst = 50f;
            _f.Needs.Advance(2f); // 50 - 2x2 + 20x2
            Assert.AreEqual(86f, a.Thirst, Tol);
            Assert.AreEqual(98f, a.Hunger, Tol, "water does not touch hunger");
        }

        [Test]
        public void Food_OutsideTheEnclosure_DoesNothing()
        {
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen);
            _f.Place(HabitatResourceKind.Food, 1f, 10, 10);
            a.Hunger = 50f;
            _f.Needs.Advance(2f);
            Assert.AreEqual(48f, a.Hunger, Tol);
        }

        [Test]
        public void RemovingTheFood_StopsRecovery()
        {
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen);
            var food = _f.Place(HabitatResourceKind.Food, 1f, 6, 6);
            a.Hunger = 50f;
            _f.Needs.Advance(2f);
            Assert.AreEqual(68f, a.Hunger, Tol);
            _f.Placement.Remove(food);
            _f.Needs.Advance(2f);
            Assert.AreEqual(66f, a.Hunger, Tol);
        }

        [Test]
        public void StandingWaterTerrain_IsNotDrinkingWater()
        {
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen);
            _f.Grid.SetTerrain(new GridCoord(6, 6), TerrainType.Water);
            a.Thirst = 50f;
            _f.Needs.Advance(2f);
            Assert.AreEqual(46f, a.Thirst, Tol);
        }

        [Test]
        public void DrinkingWaterObject_DoesNotSatisfyHabitatWater_AndTerrainDoes()
        {
            _f.Rabbit.ConfigureHabitat(1, 4, habitatWater: true);
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen);
            _f.Place(HabitatResourceKind.DrinkingWater, 1f, 6, 6);

            var r = _f.Report(a);
            Assert.IsTrue(r.Factors.WaterApplicable);
            Assert.AreEqual(0f, r.Factors.Water, Tol);

            _f.Grid.SetTerrain(new GridCoord(5, 5), TerrainType.Water);
            r = _f.Report(a);
            Assert.AreEqual(100f, r.Factors.Water, Tol);
        }

        // ---- Clock: pause and speed ----

        [Test]
        public void Pause_StopsTheSimulation()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            _f.Clock.SetSpeed(SimulationSpeed.Paused);
            _f.RunRealSeconds(10f);
            Assert.AreEqual(0, _f.Needs.TicksRun);
            Assert.AreEqual(100f, a.Hunger);
            Assert.AreEqual(100f, a.Thirst);
        }

        [Test]
        public void Resuming_AfterPause_ContinuesFromWhereItStopped()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            _f.RunRealSeconds(4f);
            Assert.AreEqual(2, _f.Needs.TicksRun);
            _f.Clock.SetSpeed(SimulationSpeed.Paused);
            _f.RunRealSeconds(10f);
            Assert.AreEqual(2, _f.Needs.TicksRun);
            _f.Clock.SetSpeed(SimulationSpeed.X1);
            _f.RunRealSeconds(2f);
            Assert.AreEqual(3, _f.Needs.TicksRun);
            Assert.AreEqual(94f, a.Hunger, Tol);
        }

        [TestCase(SimulationSpeed.X1, 5)]
        [TestCase(SimulationSpeed.X2, 10)]
        [TestCase(SimulationSpeed.X3, 15)]
        public void Speed_ScalesTheSimulationConsistently(SimulationSpeed speed, int expectedTicks)
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            _f.Clock.SetSpeed(speed);
            _f.RunRealSeconds(10f);
            Assert.AreEqual(expectedTicks, _f.Needs.TicksRun);
            Assert.AreEqual(100f - 2f * expectedTicks, a.Hunger, Tol);
        }

        // ---- Social ----

        [Test]
        public void Social_FollowsSameSpeciesGroupSize()
        {
            _f.Rabbit.ConfigureHabitat(2, 4);
            string pen = _f.Pen();
            var first = _f.Add(_f.Rabbit, pen);
            Assert.AreEqual(50f, _f.Report(first).Factors.Social, Tol, "1 of a minimum 2");
            Assert.AreEqual(50f, first.Social, Tol);

            var second = _f.Add(_f.Rabbit, pen);
            _f.Report(first);
            Assert.AreEqual(100f, first.Social, Tol);
            Assert.AreEqual(100f, second.Social, Tol);

            for (int i = 0; i < 4; i++) _f.Add(_f.Rabbit, pen); // 6 rabbits, 2 above the maximum
            Assert.AreEqual(80f, _f.Report(first).Factors.Social, Tol);
            Assert.AreEqual(6, _f.Report(first).SameSpeciesCount);
        }

        [Test]
        public void Social_IgnoresOtherSpecies()
        {
            _f.Rabbit.ConfigureHabitat(2, 4);
            string pen = _f.Pen();
            var rabbit = _f.Add(_f.Rabbit, pen);
            _f.Add(_f.Zebra, pen);
            _f.Add(_f.Zebra, pen);
            Assert.AreEqual(50f, _f.Report(rabbit).Factors.Social, Tol);
        }

        [Test]
        public void Social_UsesTheConfiguredOvercrowdingPenalty()
        {
            var config = AnimalNeedsConfig.CreateDefault(overcrowdingPenalty: 25f);
            var needs = new AnimalNeedsSystem(_f.Animals.Registry, _f.Animals.Resolver, _f.Animals.Enclosures, _f.Habitats, config);
            _f.Rabbit.ConfigureHabitat(1, 2);
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen);
            _f.Add(_f.Rabbit, pen);
            _f.Add(_f.Rabbit, pen);
            needs.Advance(0f);
            Assert.IsTrue(needs.TryGetReport(a.AnimalId, out var r));
            Assert.AreEqual(75f, r.Factors.Social, Tol);
            needs.Dispose();
            Object.DestroyImmediate(config);
        }

        // ---- Space / terrain / shelter / enrichment ----

        [Test]
        public void Space_ComparesAreaToMinimumTimesGroup()
        {
            _f.Zebra.ConfigureHabitat(1, 8);
            string pen = _f.Pen(); // 16 cells, zebra minimum 16
            var z1 = _f.Add(_f.Zebra, pen);
            Assert.AreEqual(100f, _f.Report(z1).Factors.Space, Tol);
            _f.Add(_f.Zebra, pen);
            Assert.AreEqual(50f, _f.Report(z1).Factors.Space, Tol);
            _f.Add(_f.Zebra, pen);
            _f.Add(_f.Zebra, pen);
            Assert.AreEqual(25f, _f.Report(z1).Factors.Space, Tol);
        }

        [Test]
        public void Terrain_ReadsTheEnclosureComposition_AndReactsToTerrainEdits()
        {
            _f.Rabbit.ConfigureHabitat(1, 4, terrain: new[] { new TerrainPreference(TerrainType.Sand, 0f, 0.1f) });
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen);
            Assert.AreEqual(100f, _f.Report(a).Factors.Terrain, Tol, "no sand");

            for (int i = 0; i < 4; i++) _f.Grid.SetTerrain(new GridCoord(4 + i, 4), TerrainType.Sand); // 4 of 16 = 0.25
            var r = _f.Report(a);
            Assert.IsTrue(r.Factors.TerrainApplicable);
            Assert.AreEqual(70f, r.Factors.Terrain, Tol); // 0.15 outside / 0.5 tolerance
        }

        [Test]
        public void Terrain_NotApplicable_WithoutPreferences()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            Assert.IsFalse(_f.Report(a).Factors.TerrainApplicable);
        }

        [Test]
        public void Shelter_ScoresCapacityAgainstAnimalCount()
        {
            _f.Rabbit.ConfigureHabitat(1, 8, shelter: true, shelterEach: 1f);
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen);
            _f.Add(_f.Rabbit, pen);
            Assert.IsTrue(_f.Report(a).Factors.ShelterApplicable);
            Assert.AreEqual(0f, _f.Report(a).Factors.Shelter, Tol);

            _f.Place(HabitatResourceKind.Shelter, 1f, 6, 6);
            Assert.AreEqual(50f, _f.Report(a).Factors.Shelter, Tol, "capacity 1 for 2 animals");
            _f.Place(HabitatResourceKind.Shelter, 1f, 4, 6);
            Assert.AreEqual(100f, _f.Report(a).Factors.Shelter, Tol);
        }

        [Test]
        public void Shelter_NotApplicable_WhenNotRequired()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            Assert.IsFalse(_f.Report(a).Factors.ShelterApplicable);
        }

        [Test]
        public void Enrichment_ScoreComesFromObjects_AndTheNeedMovesGradually()
        {
            _f.Rabbit.ConfigureHabitat(1, 4, enrichment: 4f);
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen);
            _f.Place(HabitatResourceKind.Enrichment, 2f, 6, 6);
            Assert.AreEqual(50f, _f.Report(a).Factors.Enrichment, Tol);

            a.Enrichment = 100f;
            _f.Needs.Advance(2f); // one tick = 2 minutes x 4 per minute
            Assert.AreEqual(92f, a.Enrichment, Tol);
            _f.Needs.Advance(2f);
            Assert.AreEqual(84f, a.Enrichment, Tol);
        }

        [Test]
        public void Enrichment_IsFull_WhenNothingIsRequired()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            Assert.AreEqual(100f, _f.Report(a).Factors.Enrichment, Tol);
        }

        // ---- Comfort ----

        [Test]
        public void Comfort_MovesGraduallyToTheTarget()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            var r = _f.Report(a);
            Assert.AreEqual(100f, r.ComfortTarget, Tol, "plenty of space, nothing else applies");
            Assert.AreEqual(50f, a.Comfort, Tol, "evaluating without time does not move it");
            _f.Needs.Advance(2f);
            Assert.AreEqual(58f, a.Comfort, Tol);
            _f.Needs.Advance(2f);
            Assert.AreEqual(66f, a.Comfort, Tol);
            _f.Needs.Step(1000f);
            Assert.AreEqual(100f, a.Comfort, Tol, "stops at the target");
        }

        [Test]
        public void Comfort_FallsTowardsALowerTarget()
        {
            _f.Zebra.ConfigureHabitat(1, 8);
            string pen = _f.Pen();
            var a = _f.Add(_f.Zebra, pen);
            for (int i = 0; i < 3; i++) _f.Add(_f.Zebra, pen); // 4 zebras need 64 cells, pen has 16 -> space 25
            a.Comfort = 90f;
            var r = _f.Report(a);
            Assert.AreEqual(25f, r.ComfortTarget, Tol);
            _f.Needs.Advance(2f);
            Assert.AreEqual(82f, a.Comfort, Tol);
        }

        // ---- Welfare ----

        [Test]
        public void Welfare_EndToEnd_MatchesTheFormula()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            var r = _f.Report(a);
            // hunger 100, thirst 100, social 100, enrichment 50, comfort 50 -> basic 90; habitat 100 -> 0.7x90 + 0.3x100
            Assert.AreEqual(90f, r.BasicNeeds, Tol);
            Assert.AreEqual(100f, r.HabitatSuitability, Tol);
            Assert.AreEqual(93f, r.Welfare, Tol);
            Assert.AreEqual(93f, a.OverallWelfare, Tol, "stored on the animal");
            Assert.AreEqual(WelfareBand.Excellent, r.Band);
            Assert.AreEqual(AnimalWelfareMath.NoCap, r.Cap);
        }

        [Test]
        public void Welfare_LowHunger_CapsAt40_AndEmptyAt20()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            a.Hunger = 15f;
            var r = _f.Report(a);
            Assert.AreEqual(40f, r.Cap);
            Assert.AreEqual(40f, r.Welfare, Tol);
            Assert.Greater(r.UncappedWelfare, 40f);
            Assert.AreEqual(WelfareBand.Poor, r.Band);

            a.Hunger = 0f;
            Assert.AreEqual(20f, _f.Report(a).Welfare, Tol);
            Assert.AreEqual(WelfareBand.Critical, _f.Report(a).Band);
        }

        [Test]
        public void Welfare_LowThirst_CapsAt35_AndEmptyAt15()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            a.Thirst = 10f;
            Assert.AreEqual(35f, _f.Report(a).Welfare, Tol);
            a.Thirst = 0f;
            Assert.AreEqual(15f, _f.Report(a).Welfare, Tol);
        }

        [Test]
        public void Welfare_UsesTheLowestApplicableCap()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            a.Hunger = 0f;
            a.Thirst = 10f;
            Assert.AreEqual(20f, _f.Report(a).Welfare, Tol); // 40, 35 and 20 apply: lowest wins
        }

        [Test]
        public void Welfare_FallsAsAnimalsStarve()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            float before = _f.Report(a).Welfare;
            _f.Needs.Step(40f);
            Assert.Less(a.OverallWelfare, before);
            _f.Needs.Step(5000f);
            Assert.AreEqual(15f, a.OverallWelfare, Tol, "starved: hunger and thirst both empty");
        }

        // ---- Invalid / open enclosures ----

        [Test]
        public void MissingEnclosure_CapsWelfareAt50_AndHasNoHabitatScore()
        {
            var a = _f.Add(_f.Rabbit, "enclosure-99");
            var r = _f.Report(a);
            Assert.IsFalse(r.EnclosureValid);
            Assert.AreEqual(50f, r.Cap);
            Assert.LessOrEqual(r.Welfare, 50f);
            Assert.AreEqual(0f, r.HabitatSuitability, Tol);
            Assert.AreEqual(0f, r.Factors.Space);
        }

        [Test]
        public void AnimalWithoutAnEnclosure_IsCappedToo()
        {
            var a = _f.Add(_f.Rabbit, null);
            var r = _f.Report(a);
            Assert.IsFalse(r.EnclosureValid);
            Assert.LessOrEqual(r.Welfare, 50f);
        }

        [Test]
        public void OpeningTheFence_CapsWelfare_AndRepairingItLiftsTheCap()
        {
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen);
            Assert.AreEqual(93f, _f.Report(a).Welfare, Tol);

            var gap = new EdgeCoord(EdgeAxis.X, 5, 4);
            _f.Animals.Construction.Model.RemoveFence(gap); // re-evaluation is requested by the enclosure change itself
            _f.Needs.Advance(0f);
            Assert.IsTrue(_f.Needs.TryGetReport(a.AnimalId, out var open));
            Assert.IsFalse(open.EnclosureValid);
            Assert.LessOrEqual(a.OverallWelfare, 50f);

            _f.Needs.Advance(2f);
            Assert.AreEqual(98f, a.Hunger, Tol, "an open pen has no usable food either; decay carries on");
        }

        [Test]
        public void ARepairedFence_ButANewEnclosureId_StillLeavesTheAnimalUnhoused_UntilReassigned()
        {
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen);
            var gap = new EdgeCoord(EdgeAxis.X, 5, 4);
            _f.Animals.Construction.Model.RemoveFence(gap);
            _f.Animals.Construction.Model.BuildFences(new[] { new FenceEdge(gap, EdgeKind.Fence) });
            Assert.IsFalse(_f.Report(a).EnclosureValid, "closing it again creates a new enclosure id (M3)");

            Assert.IsTrue(_f.Animals.Enclosures.TryGetEnclosureAt(a.Position, out string fresh));
            _f.Animals.Registry.SetEnclosure(a.AnimalId, fresh);
            Assert.IsTrue(_f.Report(a).EnclosureValid);
        }

        // ---- Re-evaluation without time ----

        [Test]
        public void ChangingTheHabitat_ReevaluatesImmediately_EvenWhilePaused()
        {
            _f.Rabbit.ConfigureHabitat(1, 4, shelter: true);
            string pen = _f.Pen();
            var a = _f.Add(_f.Rabbit, pen);
            _f.Clock.SetSpeed(SimulationSpeed.Paused);
            _f.RunRealSeconds(0.25f);
            Assert.IsTrue(_f.Needs.TryGetReport(a.AnimalId, out var r));
            Assert.AreEqual(0f, r.Factors.Shelter, Tol);

            _f.Place(HabitatResourceKind.Shelter, 1f, 6, 6);
            _f.RunRealSeconds(0.25f);
            Assert.AreEqual(100f, r.Factors.Shelter, Tol);
            Assert.AreEqual(0, _f.Needs.TicksRun, "recalculation is not simulated time");
            Assert.AreEqual(100f, a.Hunger);
        }

        [Test]
        public void UnregisteredAnimals_AreForgotten()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            _f.Report(a);
            _f.Animals.Registry.Unregister(a.AnimalId);
            Assert.IsFalse(_f.Needs.TryGetReport(a.AnimalId, out _));
        }

        [Test]
        public void Evaluated_IsRaisedPerAnimalPerTick()
        {
            _f.Add(_f.Rabbit, _f.Pen());
            _f.Add(_f.Rabbit, _f.Pen(8, 8, 3, 3));
            _f.Needs.Advance(0f);
            int seen = 0;
            _f.Needs.Evaluated += _ => seen++;
            _f.Needs.Advance(2f);
            Assert.AreEqual(2, seen);
        }

        // ---- Persistence ----

        [Test]
        public void Needs_RoundTripThroughJson()
        {
            var a = _f.Add(_f.Rabbit, _f.Pen());
            a.Hunger = 12f; a.Thirst = 34f; a.Comfort = 56f; a.Social = 78f; a.Enrichment = 90f; a.OverallWelfare = 23f;
            var copy = JsonUtility.FromJson<AnimalInstance>(JsonUtility.ToJson(a));
            Assert.AreEqual(12f, copy.Hunger);
            Assert.AreEqual(34f, copy.Thirst);
            Assert.AreEqual(56f, copy.Comfort);
            Assert.AreEqual(78f, copy.Social);
            Assert.AreEqual(90f, copy.Enrichment);
            Assert.AreEqual(23f, copy.OverallWelfare);
        }

        [Test]
        public void NewAnimals_StartWithFullHungerAndThirst()
        {
            var a = AnimalInstance.CreateNew("rabbit", "R", AnimalSex.Female, null, Vector3.zero);
            Assert.AreEqual(100f, a.Hunger);
            Assert.AreEqual(100f, a.Thirst);
            Assert.That(a.OverallWelfare, Is.InRange(0f, 100f));
        }
    }
}
