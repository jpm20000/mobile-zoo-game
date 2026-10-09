using NUnit.Framework;
using UnityEngine;
using ZooGame.Visitors;

namespace ZooGame.Tests.EditMode.Visitors
{
    /// <summary>The visitor formulas, number for number.</summary>
    public class VisitorMathTests
    {
        VisitorConfig _config;

        [SetUp] public void SetUp() => _config = VisitorConfig.CreateDefault();
        [TearDown] public void TearDown() => Object.DestroyImmediate(_config);

        static VisitorInstance Visitor(float hunger = 0f, float thirst = 0f, float toilet = 0f, float energy = 100f, float satisfaction = 60f)
        {
            var v = VisitorInstance.CreateNew(Vector3.zero, 100f);
            v.Hunger = hunger; v.Thirst = thirst; v.ToiletNeed = toilet; v.Energy = energy; v.VisitSatisfaction = satisfaction;
            return v;
        }

        // ---- Need progression ----

        [Test]
        public void DefaultRates_AreTheSuggestedTuning()
        {
            Assert.AreEqual(1.0f, _config.HungerIncreasePerMinute);
            Assert.AreEqual(1.25f, _config.ThirstIncreasePerMinute);
            Assert.AreEqual(0.75f, _config.ToiletIncreasePerMinute);
            Assert.AreEqual(0.5f, _config.EnergyDecreasePerMinute);
            Assert.AreEqual(70f, _config.HungerUrgentAt);
            Assert.AreEqual(70f, _config.ThirstUrgentAt);
            Assert.AreEqual(75f, _config.ToiletUrgentAt);
            Assert.AreEqual(25f, _config.EnergyLowAt);
        }

        [Test]
        public void ProgressNeeds_AppliesRateTimesSimulatedMinutes()
        {
            var v = Visitor(10f, 10f, 10f, 80f);
            VisitorMath.ProgressNeeds(v, _config, 4f);
            Assert.AreEqual(14f, v.Hunger, 1e-4f);
            Assert.AreEqual(15f, v.Thirst, 1e-4f);
            Assert.AreEqual(13f, v.ToiletNeed, 1e-4f);
            Assert.AreEqual(78f, v.Energy, 1e-4f);
        }

        [Test]
        public void ProgressNeeds_UsesConfiguredRates()
        {
            _config.WithRates(2f, 3f, 4f, 5f);
            var v = Visitor(0f, 0f, 0f, 100f);
            VisitorMath.ProgressNeeds(v, _config, 2f);
            Assert.AreEqual(4f, v.Hunger, 1e-4f);
            Assert.AreEqual(6f, v.Thirst, 1e-4f);
            Assert.AreEqual(8f, v.ToiletNeed, 1e-4f);
            Assert.AreEqual(90f, v.Energy, 1e-4f);
        }

        [Test]
        public void ProgressNeeds_ClampsToZeroAndHundred()
        {
            var v = Visitor(99f, 99.5f, 99.9f, 0.2f);
            VisitorMath.ProgressNeeds(v, _config, 50f);
            Assert.AreEqual(100f, v.Hunger);
            Assert.AreEqual(100f, v.Thirst);
            Assert.AreEqual(100f, v.ToiletNeed);
            Assert.AreEqual(0f, v.Energy);
        }

        [Test]
        public void ProgressNeeds_ZeroOrNegativeTime_ChangesNothing()
        {
            var v = Visitor(10f, 20f, 30f, 40f);
            VisitorMath.ProgressNeeds(v, _config, 0f);
            VisitorMath.ProgressNeeds(v, _config, -5f);
            Assert.AreEqual(10f, v.Hunger);
            Assert.AreEqual(20f, v.Thirst);
            Assert.AreEqual(30f, v.ToiletNeed);
            Assert.AreEqual(40f, v.Energy);
        }

        // ---- Urgency ----

        [Test]
        public void Urgency_IsNeedOverHundred_AndEnergyIsInverted()
        {
            var v = Visitor(80f, 50f, 25f, 30f);
            Assert.AreEqual(0.80f, VisitorMath.Urgency(VisitorNeed.Food, v), 1e-5f);
            Assert.AreEqual(0.50f, VisitorMath.Urgency(VisitorNeed.Drink, v), 1e-5f);
            Assert.AreEqual(0.25f, VisitorMath.Urgency(VisitorNeed.Toilet, v), 1e-5f);
            Assert.AreEqual(0.70f, VisitorMath.Urgency(VisitorNeed.Rest, v), 1e-5f);
            Assert.AreEqual(0f, VisitorMath.Urgency(VisitorNeed.None, v));
        }

        [Test]
        public void IsUrgent_UsesTheConfiguredThresholds_Inclusively()
        {
            Assert.IsTrue(VisitorMath.IsUrgent(VisitorNeed.Food, Visitor(hunger: 70f), _config));
            Assert.IsFalse(VisitorMath.IsUrgent(VisitorNeed.Food, Visitor(hunger: 69.9f), _config));
            Assert.IsTrue(VisitorMath.IsUrgent(VisitorNeed.Drink, Visitor(thirst: 70f), _config));
            Assert.IsFalse(VisitorMath.IsUrgent(VisitorNeed.Drink, Visitor(thirst: 69.9f), _config));
            Assert.IsTrue(VisitorMath.IsUrgent(VisitorNeed.Toilet, Visitor(toilet: 75f), _config));
            Assert.IsFalse(VisitorMath.IsUrgent(VisitorNeed.Toilet, Visitor(toilet: 74.9f), _config));
            Assert.IsTrue(VisitorMath.IsUrgent(VisitorNeed.Rest, Visitor(energy: 25f), _config));
            Assert.IsFalse(VisitorMath.IsUrgent(VisitorNeed.Rest, Visitor(energy: 25.1f), _config));
            Assert.IsFalse(VisitorMath.IsUrgent(VisitorNeed.None, Visitor(100f, 100f, 100f, 0f), _config));
        }

        [Test]
        public void MostUrgent_ChoosesTheHighestUrgency()
        {
            Assert.AreEqual(VisitorNeed.None, VisitorMath.MostUrgent(Visitor(), _config));
            Assert.AreEqual(VisitorNeed.Food, VisitorMath.MostUrgent(Visitor(hunger: 80f), _config));
            Assert.AreEqual(VisitorNeed.Drink, VisitorMath.MostUrgent(Visitor(hunger: 80f, thirst: 95f), _config));
            Assert.AreEqual(VisitorNeed.Toilet, VisitorMath.MostUrgent(Visitor(hunger: 72f, toilet: 90f), _config));
            Assert.AreEqual(VisitorNeed.Rest, VisitorMath.MostUrgent(Visitor(hunger: 75f, energy: 10f), _config), "energy urgency is 0.9");
        }

        [Test]
        public void MostUrgent_IgnoresNeedsThatAreNotUrgent_EvenIfMoreUrgentThanNothing()
        {
            // thirst 60 is below its threshold, so food at 71 wins even though both are "high".
            Assert.AreEqual(VisitorNeed.Food, VisitorMath.MostUrgent(Visitor(hunger: 71f, thirst: 60f), _config));
        }

        [Test]
        public void MostUrgent_SkipsExcludedNeeds_ToFindTheNextBestWithAFacility()
        {
            var v = Visitor(hunger: 80f, thirst: 95f);
            Assert.AreEqual(VisitorNeed.Food, VisitorMath.MostUrgent(v, _config, VisitorMath.Mask(VisitorNeed.Drink)));
            Assert.AreEqual(VisitorNeed.None,
                VisitorMath.MostUrgent(v, _config, VisitorMath.Mask(VisitorNeed.Drink) | VisitorMath.Mask(VisitorNeed.Food)));
        }

        // ---- Needs happiness ----

        [Test]
        public void NeedsHappiness_IsHundredMinusWeightedPenalties()
        {
            Assert.AreEqual(100f, VisitorMath.NeedsHappiness(0f, 0f, 0f, 100f), 1e-4f);
            Assert.AreEqual(75f, VisitorMath.NeedsHappiness(100f, 0f, 0f, 100f), 1e-4f, "hunger penalty 0.25");
            Assert.AreEqual(70f, VisitorMath.NeedsHappiness(0f, 100f, 0f, 100f), 1e-4f, "thirst penalty 0.30");
            Assert.AreEqual(75f, VisitorMath.NeedsHappiness(0f, 0f, 100f, 100f), 1e-4f, "toilet penalty 0.25");
            Assert.AreEqual(80f, VisitorMath.NeedsHappiness(0f, 0f, 0f, 0f), 1e-4f, "energy penalty 0.20");
            Assert.AreEqual(0f, VisitorMath.NeedsHappiness(100f, 100f, 100f, 0f), 1e-4f, "every need at its worst");
        }

        [Test]
        public void NeedsHappiness_MixedExample()
        {
            // penalties: 40*.25 + 20*.30 + 10*.25 + (100-60)*.20 = 10 + 6 + 2.5 + 8 = 26.5
            Assert.AreEqual(26.5f, VisitorMath.TotalNeedPenalty(40f, 20f, 10f, 60f), 1e-4f);
            Assert.AreEqual(73.5f, VisitorMath.NeedsHappiness(40f, 20f, 10f, 60f), 1e-4f);
        }

        [Test]
        public void NeedsHappiness_IsClamped()
        {
            Assert.AreEqual(100f, VisitorMath.NeedsHappiness(-50f, -50f, -50f, 200f), 1e-4f);
            Assert.AreEqual(0f, VisitorMath.NeedsHappiness(1000f, 1000f, 1000f, -1000f), 1e-4f);
        }

        // ---- Final happiness ----

        [Test]
        public void FinalHappiness_BlendsNeedsAndSatisfaction_65_35()
        {
            Assert.AreEqual(0.65f * 80f + 0.35f * 60f, VisitorMath.FinalHappiness(80f, 60f, VisitorMath.NoCap), 1e-4f);
            Assert.AreEqual(73f, VisitorMath.FinalHappiness(80f, 60f, VisitorMath.NoCap), 1e-4f);
            Assert.AreEqual(100f, VisitorMath.FinalHappiness(100f, 100f, VisitorMath.NoCap), 1e-4f);
            Assert.AreEqual(0f, VisitorMath.FinalHappiness(0f, 0f, VisitorMath.NoCap), 1e-4f);
            Assert.AreEqual(65f, VisitorMath.FinalHappiness(100f, 0f, VisitorMath.NoCap), 1e-4f);
            Assert.AreEqual(35f, VisitorMath.FinalHappiness(0f, 100f, VisitorMath.NoCap), 1e-4f);
        }

        [Test]
        public void RecalculateHappiness_SetsNeedsHappinessAndHappiness()
        {
            var v = Visitor(40f, 20f, 10f, 60f, 60f);
            VisitorMath.RecalculateHappiness(v);
            Assert.AreEqual(73.5f, v.NeedsHappiness, 1e-3f);
            Assert.AreEqual(0.65f * 73.5f + 0.35f * 60f, v.Happiness, 1e-3f);
        }

        // ---- Critical caps ----

        [Test]
        public void CriticalCaps_ApplyAtTheirThresholds()
        {
            Assert.AreEqual(VisitorMath.NoCap, VisitorMath.CriticalCap(89.9f, 89.9f, 89.9f, 10.1f));
            Assert.AreEqual(35f, VisitorMath.CriticalCap(0f, 90f, 0f, 100f), "thirst >= 90");
            Assert.AreEqual(40f, VisitorMath.CriticalCap(0f, 0f, 90f, 100f), "toilet >= 90");
            Assert.AreEqual(45f, VisitorMath.CriticalCap(90f, 0f, 0f, 100f), "hunger >= 90");
            Assert.AreEqual(40f, VisitorMath.CriticalCap(0f, 0f, 0f, 10f), "energy <= 10");
        }

        [Test]
        public void CriticalCaps_TheLowestApplicableCapWins()
        {
            Assert.AreEqual(35f, VisitorMath.CriticalCap(95f, 95f, 95f, 5f));
            Assert.AreEqual(40f, VisitorMath.CriticalCap(95f, 0f, 95f, 100f), "toilet 40 beats hunger 45");
            Assert.AreEqual(40f, VisitorMath.CriticalCap(95f, 0f, 0f, 5f), "energy 40 beats hunger 45");
        }

        [Test]
        public void CriticalCaps_StopAttractionQualityMaskingAnUrgentNeed()
        {
            // Best possible needs elsewhere and full satisfaction, but thirst is critical.
            var v = Visitor(0f, 90f, 0f, 100f, 100f);
            VisitorMath.RecalculateHappiness(v);
            Assert.Greater(0.65f * v.NeedsHappiness + 0.35f * 100f, 35f, "the uncapped value would be higher");
            Assert.AreEqual(35f, v.Happiness, 1e-4f);

            v = Visitor(0f, 0f, 0f, 5f, 100f);
            VisitorMath.RecalculateHappiness(v);
            Assert.AreEqual(40f, v.Happiness, 1e-4f);
        }

        [Test]
        public void ACapDoesNotRaiseAnAlreadyLowerHappiness()
        {
            Assert.AreEqual(10f, VisitorMath.FinalHappiness(10f, 10f, 35f), 1e-4f);
        }

        // ---- Viewing ----

        [Test]
        public void ViewingScore_IsAppealOverMaxAppeal_Clamped_TimesHundred()
        {
            Assert.AreEqual(100f, VisitorMath.ViewingScore(100f, 100f), 1e-4f, "100 appeal = 100 score by default");
            Assert.AreEqual(50f, VisitorMath.ViewingScore(50f, 100f), 1e-4f);
            Assert.AreEqual(25f, VisitorMath.ViewingScore(5f, 20f), 1e-4f);
            Assert.AreEqual(100f, VisitorMath.ViewingScore(250f, 100f), 1e-4f);
            Assert.AreEqual(0f, VisitorMath.ViewingScore(0f, 100f), 1e-4f);
            Assert.AreEqual(0f, VisitorMath.ViewingScore(-5f, 100f), 1e-4f);
        }

        [Test]
        public void ViewingSatisfaction_MovesTowardsTheScore_WithoutOvershoot()
        {
            Assert.AreEqual(80f, VisitorMath.ViewingSatisfaction(60f, 100f, 20f), 1e-4f);
            Assert.AreEqual(100f, VisitorMath.ViewingSatisfaction(95f, 100f, 20f), 1e-4f);
            Assert.AreEqual(40f, VisitorMath.ViewingSatisfaction(60f, 0f, 20f), 1e-4f);
            Assert.AreEqual(0f, VisitorMath.ViewingSatisfaction(10f, 0f, 20f), 1e-4f);
            Assert.AreEqual(60f, VisitorMath.ViewingSatisfaction(60f, 60f, 20f), 1e-4f);
            Assert.AreEqual(60f, VisitorMath.ViewingSatisfaction(60f, 100f, 0f), 1e-4f);
        }

        // ---- Attraction choice ----

        [Test]
        public void AttractionWeight_IsAtLeastOne()
        {
            Assert.AreEqual(1f, VisitorMath.AttractionWeight(0f, false, 0.25f));
            Assert.AreEqual(1f, VisitorMath.AttractionWeight(0.4f, false, 0.25f));
            Assert.AreEqual(1f, VisitorMath.AttractionWeight(1f, false, 0.25f));
            Assert.AreEqual(7.5f, VisitorMath.AttractionWeight(7.5f, false, 0.25f));
        }

        [Test]
        public void AttractionWeight_RecentlyViewedIsReduced()
        {
            Assert.AreEqual(2f, VisitorMath.AttractionWeight(8f, true, 0.25f), 1e-5f);
            Assert.AreEqual(0.25f, VisitorMath.AttractionWeight(0f, true, 0.25f), 1e-5f, "max(1, appeal) first, then x0.25");
        }

        [Test]
        public void PickWeighted_FollowsTheCumulativeWeights()
        {
            var w = new[] { 1f, 3f };
            Assert.AreEqual(0, VisitorMath.PickWeighted(w, 2, 0.0));
            Assert.AreEqual(0, VisitorMath.PickWeighted(w, 2, 0.24));
            Assert.AreEqual(1, VisitorMath.PickWeighted(w, 2, 0.25));
            Assert.AreEqual(1, VisitorMath.PickWeighted(w, 2, 0.999999));
        }

        [Test]
        public void PickWeighted_IgnoresZeroAndNegativeWeights_AndHandlesEmpty()
        {
            Assert.AreEqual(2, VisitorMath.PickWeighted(new[] { 0f, -3f, 5f }, 3, 0.0));
            Assert.AreEqual(-1, VisitorMath.PickWeighted(new[] { 0f, 0f }, 2, 0.5));
            Assert.AreEqual(-1, VisitorMath.PickWeighted(new float[0], 0, 0.5));
            Assert.AreEqual(0, VisitorMath.PickWeighted(new[] { 4f, 9f }, 1, 0.9), "only the first `count` weights are considered");
        }

        // ---- Leaving ----

        [Test]
        public void ShouldExit_WhenVisitTimeIsUp()
        {
            var v = Visitor();
            v.MaximumVisitTime = 100f;
            v.VisitTime = 99.9f;
            Assert.IsFalse(VisitorMath.ShouldExit(v, _config));
            v.VisitTime = 100f;
            Assert.IsTrue(VisitorMath.ShouldExit(v, _config));
        }

        [Test]
        public void ShouldExit_OnlyAfterHappinessStaysLowForTheConfiguredPeriod()
        {
            _config.WithUnhappyExit(20f, 2f);
            var v = Visitor();
            v.Happiness = 15f;
            v.UnhappyTime = 1.9f;
            Assert.IsFalse(VisitorMath.ShouldExit(v, _config), "one temporary low tick is not enough");
            v.UnhappyTime = 2f;
            Assert.IsTrue(VisitorMath.ShouldExit(v, _config));
            v.Happiness = 21f;
            Assert.IsFalse(VisitorMath.ShouldExit(v, _config), "recovered above the threshold");
        }
    }
}
