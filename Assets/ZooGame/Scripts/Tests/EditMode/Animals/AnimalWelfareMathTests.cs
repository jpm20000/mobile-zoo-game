using NUnit.Framework;
using UnityEngine;
using ZooGame.Animals;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Animals
{
    /// <summary>The M5 formulas as pure functions.</summary>
    public class AnimalWelfareMathTests
    {
        const float Tol = 0.01f;

        static HabitatFactors Factors(float space = 100, float social = 100, float terrain = 100, float water = 100,
            float shelter = 100, float enrichment = 100, bool t = false, bool w = false, bool s = false) =>
            new HabitatFactors
            {
                Space = space, Social = social, Terrain = terrain, Water = water, Shelter = shelter, Enrichment = enrichment,
                TerrainApplicable = t, WaterApplicable = w, ShelterApplicable = s
            };

        // ---- Decay and recovery ----

        [Test]
        public void Decay_SubtractsRatePerMinuteTimesMinutes() =>
            Assert.AreEqual(90f, AnimalWelfareMath.DecayAndRecover(100f, 2f, 10f, false, 5f), Tol);

        [Test]
        public void Recovery_AddsOnTopOfDecay_OnlyWhenUsable() =>
            Assert.AreEqual(100f, AnimalWelfareMath.DecayAndRecover(100f, 2f, 10f, true, 5f), Tol); // 100 - 10 + 50, clamped

        [Test]
        public void Recovery_NetEffect()
        {
            Assert.AreEqual(60f + (10f - 2f) * 3f, AnimalWelfareMath.DecayAndRecover(60f, 2f, 10f, true, 3f), Tol);
            Assert.AreEqual(60f - 2f * 3f, AnimalWelfareMath.DecayAndRecover(60f, 2f, 10f, false, 3f), Tol);
        }

        [Test]
        public void DecayAndRecovery_ClampToZeroAndHundred()
        {
            Assert.AreEqual(0f, AnimalWelfareMath.DecayAndRecover(5f, 100f, 0f, false, 10f));
            Assert.AreEqual(100f, AnimalWelfareMath.DecayAndRecover(95f, 0f, 100f, true, 10f));
            Assert.AreEqual(0f, AnimalWelfareMath.Clamp100(float.NaN));
            Assert.AreEqual(100f, AnimalWelfareMath.Clamp100(1e9f));
            Assert.AreEqual(0f, AnimalWelfareMath.Clamp100(-3f));
        }

        [Test]
        public void MoveToward_StepsGraduallyAndNeverOvershoots()
        {
            Assert.AreEqual(54f, AnimalWelfareMath.MoveToward(50f, 100f, 4f), Tol);
            Assert.AreEqual(46f, AnimalWelfareMath.MoveToward(50f, 0f, 4f), Tol);
            Assert.AreEqual(52f, AnimalWelfareMath.MoveToward(50f, 52f, 10f), Tol);
            Assert.AreEqual(50f, AnimalWelfareMath.MoveToward(50f, 100f, 0f), Tol);
        }

        [Test]
        public void InstanceNeeds_ClampOnAssignment()
        {
            var a = AnimalInstance.CreateNew("rabbit", "R", AnimalSex.Male, null, Vector3.zero);
            a.Hunger = 150f; a.Thirst = -20f; a.Comfort = float.NaN; a.Social = 101f; a.Enrichment = -1f; a.OverallWelfare = 1000f;
            Assert.AreEqual(100f, a.Hunger);
            Assert.AreEqual(0f, a.Thirst);
            Assert.AreEqual(0f, a.Comfort);
            Assert.AreEqual(100f, a.Social);
            Assert.AreEqual(0f, a.Enrichment);
            Assert.AreEqual(100f, a.OverallWelfare);
        }

        // ---- Space ----

        [Test]
        public void Space_IsAreaOverMinimumTimesSameSpeciesCount()
        {
            Assert.AreEqual(100f, AnimalWelfareMath.SpaceScore(16, 4, 2), Tol);   // needs 8
            Assert.AreEqual(100f, AnimalWelfareMath.SpaceScore(16, 4, 4), Tol);   // needs 16, exact
            Assert.AreEqual(50f, AnimalWelfareMath.SpaceScore(16, 4, 8), Tol);    // needs 32
            Assert.AreEqual(25f, AnimalWelfareMath.SpaceScore(4, 16, 1), Tol);
            Assert.AreEqual(100f, AnimalWelfareMath.SpaceScore(1, 0, 3), Tol, "no requirement");
        }

        // ---- Social ----

        [TestCase(2, 100f)]
        [TestCase(3, 100f)]
        [TestCase(4, 100f)]
        public void Social_InsidePreferredGroup_Is100(int count, float expected) =>
            Assert.AreEqual(expected, AnimalWelfareMath.SocialScore(count, 2, 4, 10f), Tol);

        [Test]
        public void Social_BelowMinimum_IsProportional()
        {
            Assert.AreEqual(50f, AnimalWelfareMath.SocialScore(1, 2, 4, 10f), Tol);
            Assert.AreEqual(25f, AnimalWelfareMath.SocialScore(1, 4, 6, 10f), Tol);
            Assert.AreEqual(75f, AnimalWelfareMath.SocialScore(3, 4, 6, 10f), Tol);
        }

        [Test]
        public void Social_AboveMaximum_LosesPenaltyPerAnimal_FlooredAtZero()
        {
            Assert.AreEqual(90f, AnimalWelfareMath.SocialScore(5, 2, 4, 10f), Tol);
            Assert.AreEqual(70f, AnimalWelfareMath.SocialScore(7, 2, 4, 10f), Tol);
            Assert.AreEqual(0f, AnimalWelfareMath.SocialScore(40, 2, 4, 10f), Tol);
            Assert.AreEqual(85f, AnimalWelfareMath.SocialScore(5, 2, 4, 15f), Tol, "penalty is configurable");
        }

        // ---- Terrain ----

        static readonly TerrainPreference[] GrassAndSand =
        {
            new TerrainPreference(TerrainType.Grass, 0.5f, 1f),
            new TerrainPreference(TerrainType.Sand, 0f, 0.3f),
        };

        static int[] Cells(int grass, int dirt, int sand, int water)
        {
            var c = new int[EnclosureHabitat.TerrainTypeCount];
            c[(int)TerrainType.Grass] = grass; c[(int)TerrainType.Dirt] = dirt; c[(int)TerrainType.Sand] = sand; c[(int)TerrainType.Water] = water;
            return c;
        }

        [Test]
        public void Terrain_InsideEveryPreferredRange_IsFull()
        {
            float score = AnimalWelfareMath.TerrainScore(GrassAndSand, Cells(14, 0, 6, 0), 20, 0.5f, out bool applicable);
            Assert.IsTrue(applicable);
            Assert.AreEqual(100f, score, Tol); // grass 0.7, sand 0.3 (boundary is inside)
        }

        [Test]
        public void Terrain_OutsideRange_DropsProgressivelyWithDistance()
        {
            var one = new[] { new TerrainPreference(TerrainType.Sand, 0f, 0.3f) };
            Assert.AreEqual(60f, AnimalWelfareMath.TerrainScore(one, Cells(10, 0, 5, 0), 10, 0.5f, out _) , Tol, "sand share 0.5: 0.2 outside, tolerance 0.5");
            Assert.AreEqual(100f - 0.4f / 0.5f * 100f, AnimalWelfareMath.TerrainScore(one, Cells(0, 0, 7, 0), 10, 0.5f, out _), Tol);
            Assert.AreEqual(0f, AnimalWelfareMath.TerrainScore(one, Cells(0, 0, 10, 0), 10, 0.5f, out _), Tol, "1.0 is 0.7 outside: floored");
        }

        [Test]
        public void Terrain_BelowRange_AlsoDrops()
        {
            var one = new[] { new TerrainPreference(TerrainType.Water, 0.2f, 0.4f) };
            Assert.AreEqual(80f, AnimalWelfareMath.TerrainScore(one, Cells(9, 0, 0, 1), 10, 0.5f, out _), Tol, "0.1 share is 0.1 below the range");
        }

        [Test]
        public void Terrain_CombinesComponentsByAveraging()
        {
            // grass 1.0: in range (100); sand 0.0: in range (100) -> 100. Make sand wrong: wants 0.5-0.6, has 0.0 -> 0.5 away -> 0.
            var prefs = new[]
            {
                new TerrainPreference(TerrainType.Grass, 0f, 1f),
                new TerrainPreference(TerrainType.Sand, 0.5f, 0.6f),
            };
            Assert.AreEqual(50f, AnimalWelfareMath.TerrainScore(prefs, Cells(10, 0, 0, 0), 10, 0.5f, out _), Tol);
        }

        [Test]
        public void Terrain_NotApplicableWithoutPreferencesOrCells()
        {
            AnimalWelfareMath.TerrainScore(null, Cells(1, 0, 0, 0), 1, 0.5f, out bool a1);
            AnimalWelfareMath.TerrainScore(new TerrainPreference[0], Cells(1, 0, 0, 0), 1, 0.5f, out bool a2);
            AnimalWelfareMath.TerrainScore(GrassAndSand, Cells(0, 0, 0, 0), 0, 0.5f, out bool a3);
            Assert.IsFalse(a1 || a2 || a3);
        }

        // ---- Water / shelter / enrichment ----

        [Test]
        public void Water_IsAllOrNothing()
        {
            Assert.AreEqual(100f, AnimalWelfareMath.WaterScore(true));
            Assert.AreEqual(0f, AnimalWelfareMath.WaterScore(false));
        }

        [Test]
        public void Shelter_IsCapacityOverRequirement()
        {
            Assert.AreEqual(50f, AnimalWelfareMath.ShelterScore(2f, 4f), Tol);
            Assert.AreEqual(100f, AnimalWelfareMath.ShelterScore(9f, 4f), Tol);
            Assert.AreEqual(0f, AnimalWelfareMath.ShelterScore(0f, 4f), Tol);
            Assert.AreEqual(100f, AnimalWelfareMath.ShelterScore(0f, 0f), Tol);
        }

        [Test]
        public void Enrichment_IsAvailableOverRequired_And100WhenNothingRequired()
        {
            Assert.AreEqual(25f, AnimalWelfareMath.EnrichmentScore(1f, 4f), Tol);
            Assert.AreEqual(100f, AnimalWelfareMath.EnrichmentScore(10f, 4f), Tol);
            Assert.AreEqual(100f, AnimalWelfareMath.EnrichmentScore(0f, 0f), Tol);
            Assert.AreEqual(0f, AnimalWelfareMath.EnrichmentScore(0f, 4f), Tol);
        }

        // ---- Comfort target ----

        [Test]
        public void ComfortTarget_UsesSpace35_Terrain35_Water15_Shelter15()
        {
            var f = Factors(space: 100, terrain: 0, water: 0, shelter: 0, t: true, w: true, s: true);
            Assert.AreEqual(35f, AnimalWelfareMath.ComfortTarget(f), Tol);
            f = Factors(space: 0, terrain: 100, water: 0, shelter: 0, t: true, w: true, s: true);
            Assert.AreEqual(35f, AnimalWelfareMath.ComfortTarget(f), Tol);
            f = Factors(space: 0, terrain: 0, water: 100, shelter: 0, t: true, w: true, s: true);
            Assert.AreEqual(15f, AnimalWelfareMath.ComfortTarget(f), Tol);
            f = Factors(space: 0, terrain: 0, water: 0, shelter: 100, t: true, w: true, s: true);
            Assert.AreEqual(15f, AnimalWelfareMath.ComfortTarget(f), Tol);
        }

        [Test]
        public void ComfortTarget_NormalisesWhenFactorsAreNotApplicable()
        {
            Assert.AreEqual(80f, AnimalWelfareMath.ComfortTarget(Factors(space: 80, terrain: 0, water: 0, shelter: 0)), Tol, "only space applies");
            Assert.AreEqual(60f, AnimalWelfareMath.ComfortTarget(Factors(space: 80, terrain: 40, t: true)), Tol, "space + terrain, equal weights");
            // space 100, shelter 0: 35 / 50
            Assert.AreEqual(70f, AnimalWelfareMath.ComfortTarget(Factors(space: 100, shelter: 0, s: true)), Tol);
        }

        // ---- Habitat suitability ----

        [Test]
        public void Suitability_WeightsEachFactor()
        {
            var all = Factors(space: 0, social: 0, terrain: 0, water: 0, shelter: 0, enrichment: 0, t: true, w: true, s: true);
            Assert.AreEqual(0f, AnimalWelfareMath.HabitatSuitability(all), Tol);

            var f = all; f.Space = 100;      Assert.AreEqual(30f, AnimalWelfareMath.HabitatSuitability(f), Tol);
            f = all; f.Terrain = 100;        Assert.AreEqual(25f, AnimalWelfareMath.HabitatSuitability(f), Tol);
            f = all; f.Social = 100;         Assert.AreEqual(15f, AnimalWelfareMath.HabitatSuitability(f), Tol);
            f = all; f.Water = 100;          Assert.AreEqual(10f, AnimalWelfareMath.HabitatSuitability(f), Tol);
            f = all; f.Shelter = 100;        Assert.AreEqual(10f, AnimalWelfareMath.HabitatSuitability(f), Tol);
            f = all; f.Enrichment = 100;     Assert.AreEqual(10f, AnimalWelfareMath.HabitatSuitability(f), Tol);
        }

        [Test]
        public void Suitability_NormalisesOverApplicableFactors()
        {
            // terrain, water, shelter not applicable: weights 0.30 + 0.15 + 0.10 = 0.55
            var f = Factors(space: 100, social: 50, enrichment: 0);
            Assert.AreEqual((30f + 7.5f) / 0.55f, AnimalWelfareMath.HabitatSuitability(f), Tol);
            // a not-applicable factor's score is ignored whatever it holds
            f.Terrain = 0; f.Water = 0; f.Shelter = 0;
            Assert.AreEqual((30f + 7.5f) / 0.55f, AnimalWelfareMath.HabitatSuitability(f), Tol);
        }

        // ---- Welfare ----

        [Test]
        public void BasicNeeds_UsesTheDefinedWeights()
        {
            Assert.AreEqual(35f, AnimalWelfareMath.BasicNeeds(100, 0, 0, 0, 0), Tol);
            Assert.AreEqual(30f, AnimalWelfareMath.BasicNeeds(0, 100, 0, 0, 0), Tol);
            Assert.AreEqual(15f, AnimalWelfareMath.BasicNeeds(0, 0, 100, 0, 0), Tol);
            Assert.AreEqual(10f, AnimalWelfareMath.BasicNeeds(0, 0, 0, 100, 0), Tol);
            Assert.AreEqual(10f, AnimalWelfareMath.BasicNeeds(0, 0, 0, 0, 100), Tol);
            Assert.AreEqual(70f, AnimalWelfareMath.BasicNeeds(80, 60, 100, 50, 40), Tol);
        }

        [Test]
        public void Welfare_Is70PercentBasicNeeds_Plus30PercentHabitat()
        {
            Assert.AreEqual(0.7f * 70f + 0.3f * 50f, AnimalWelfareMath.Welfare(70f, 50f, AnimalWelfareMath.NoCap), Tol);
            Assert.AreEqual(100f, AnimalWelfareMath.Welfare(100f, 100f, AnimalWelfareMath.NoCap), Tol);
            Assert.AreEqual(0f, AnimalWelfareMath.Welfare(0f, 0f, AnimalWelfareMath.NoCap), Tol);
        }

        [Test]
        public void Welfare_IsClampedTo0To100()
        {
            Assert.AreEqual(100f, AnimalWelfareMath.Welfare(500f, 500f, AnimalWelfareMath.NoCap));
            Assert.AreEqual(0f, AnimalWelfareMath.Welfare(-500f, -500f, AnimalWelfareMath.NoCap));
        }

        // ---- Critical caps ----

        [Test]
        public void Caps_AppliesTheDocumentedLimits()
        {
            Assert.AreEqual(100f, AnimalWelfareMath.WelfareCap(50, 50, true));
            Assert.AreEqual(100f, AnimalWelfareMath.WelfareCap(20, 20, true), "20 is not below 20");
            Assert.AreEqual(40f, AnimalWelfareMath.WelfareCap(19, 50, true));
            Assert.AreEqual(35f, AnimalWelfareMath.WelfareCap(50, 19, true));
            Assert.AreEqual(20f, AnimalWelfareMath.WelfareCap(0, 50, true));
            Assert.AreEqual(15f, AnimalWelfareMath.WelfareCap(50, 0, true));
        }

        [Test]
        public void Caps_UseTheLowestApplicable()
        {
            Assert.AreEqual(35f, AnimalWelfareMath.WelfareCap(10, 10, true), "thirst low (35) beats hunger low (40)");
            Assert.AreEqual(20f, AnimalWelfareMath.WelfareCap(0, 10, true), "hunger empty 20 beats thirst low 35");
            Assert.AreEqual(15f, AnimalWelfareMath.WelfareCap(0, 0, true));
            Assert.AreEqual(15f, AnimalWelfareMath.WelfareCap(0, 0, false), "empty thirst is lower than the invalid-enclosure cap");
        }

        [Test]
        public void Caps_InvalidEnclosure_LimitsWelfareTo50()
        {
            Assert.AreEqual(50f, AnimalWelfareMath.WelfareCap(100, 100, false));
            Assert.AreEqual(50f, AnimalWelfareMath.Welfare(100f, 100f, AnimalWelfareMath.WelfareCap(100, 100, false)));
            Assert.AreEqual(20f, AnimalWelfareMath.WelfareCap(0, 100, false), "the lowest cap wins");
        }

        [Test]
        public void Caps_OnlyLowerWelfare_NeverRaiseIt() =>
            Assert.AreEqual(10f, AnimalWelfareMath.Welfare(10f, 10f, 40f), Tol);

        // ---- Bands ----

        [TestCase(100f, WelfareBand.Excellent)]
        [TestCase(90f, WelfareBand.Excellent)]
        [TestCase(89.9f, WelfareBand.Good)]
        [TestCase(75f, WelfareBand.Good)]
        [TestCase(74.9f, WelfareBand.Fair)]
        [TestCase(50f, WelfareBand.Fair)]
        [TestCase(49.9f, WelfareBand.Poor)]
        [TestCase(25f, WelfareBand.Poor)]
        [TestCase(24.9f, WelfareBand.Critical)]
        [TestCase(0f, WelfareBand.Critical)]
        public void Bands_FollowTheRanges(float welfare, WelfareBand expected) =>
            Assert.AreEqual(expected, AnimalWelfareMath.Band(welfare));
    }
}
