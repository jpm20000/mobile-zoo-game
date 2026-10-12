using NUnit.Framework;
using ZooGame.Progression;

namespace ZooGame.Tests.EditMode.Progression
{
    /// <summary>The rating formulas: normalisation, per-component rules, the weighted total and the star bands.</summary>
    public class ZooRatingMathTests
    {
        ZooRatingSettings _s;

        [SetUp]
        public void SetUp() => _s = ZooRatingSettings.CreateDefault();

        // ---- Normalisation ----

        [Test]
        public void Normalize_ZeroIsNoContribution()
        {
            Assert.AreEqual(0.0, ZooRatingMath.Normalize(0, 20), 1e-12);
        }

        [Test]
        public void Normalize_ScalesLinearlyToTheTarget()
        {
            Assert.AreEqual(50.0, ZooRatingMath.Normalize(10, 20), 1e-12);
            Assert.AreEqual(100.0, ZooRatingMath.Normalize(20, 20), 1e-12);
        }

        [Test]
        public void Normalize_AboveTargetClampsTo100()
        {
            Assert.AreEqual(100.0, ZooRatingMath.Normalize(25, 20), 1e-12);
            Assert.AreEqual(100.0, ZooRatingMath.Normalize(double.PositiveInfinity, 20), 1e-12);
        }

        [Test]
        public void Normalize_NegativeAndNaNInputsClampToZero()
        {
            Assert.AreEqual(0.0, ZooRatingMath.Normalize(-5, 20), 1e-12);
            Assert.AreEqual(0.0, ZooRatingMath.Normalize(double.NaN, 20), 1e-12);
        }

        [Test]
        public void Normalize_KeepsFullPrecision()
        {
            Assert.AreEqual(100.0 / 3.0, ZooRatingMath.Normalize(1, 3), 1e-12);
        }

        [Test]
        public void Normalize_InvalidTargetGivesZeroInsteadOfDividingByZero()
        {
            Assert.AreEqual(0.0, ZooRatingMath.Normalize(5, 0), 1e-12);
            Assert.AreEqual(0.0, ZooRatingMath.Normalize(5, -1), 1e-12);
            Assert.AreEqual(0.0, ZooRatingMath.Normalize(5, double.NaN), 1e-12);
        }

        [Test]
        public void Variety_UsesTheConfiguredSpeciesTarget()
        {
            Assert.AreEqual(10.0, ZooRatingMath.AnimalVariety(2, _s), 1e-12);
            _s.SpeciesCountForMaxScore = 4f;
            Assert.AreEqual(50.0, ZooRatingMath.AnimalVariety(2, _s), 1e-12);
            Assert.AreEqual(100.0, ZooRatingMath.AnimalVariety(40, _s), 1e-12);
        }

        [Test]
        public void Beauty_UsesTheConfiguredBeautyTarget()
        {
            Assert.AreEqual(25.0, ZooRatingMath.ZooBeauty(25, _s), 1e-12);
            _s.BeautyForMaxScore = 50f;
            Assert.AreEqual(100.0, ZooRatingMath.ZooBeauty(80, _s), 1e-12);
        }

        // ---- Visitor happiness ----

        [Test]
        public void Happiness_PrefersHistoryOverActiveVisitors()
        {
            Assert.AreEqual(80.0, ZooRatingMath.VisitorHappiness(160, 2, 20, 1), 1e-12);
        }

        [Test]
        public void Happiness_FallsBackToActiveVisitorAverage()
        {
            Assert.AreEqual(30.0, ZooRatingMath.VisitorHappiness(0, 0, 90, 3), 1e-12);
        }

        [Test]
        public void Happiness_IsZeroWithNoData()
        {
            Assert.AreEqual(0.0, ZooRatingMath.VisitorHappiness(0, 0, 0, 0), 1e-12);
        }

        // ---- Facilities ----

        [Test]
        public void Facilities_AveragesTheCategoryScores()
        {
            // Food 1/2 = 50, Drink 2/2 = 100, Toilets 0/2 = 0, Benches 4/4 = 100.
            Assert.AreEqual(62.5, ZooRatingMath.Facilities(new[] { 1, 2, 0, 4 }, _s), 1e-12);
        }

        [Test]
        public void Facilities_CountsAboveRequiredAreClamped()
        {
            Assert.AreEqual(100.0, ZooRatingMath.Facilities(new[] { 10, 10, 10, 10 }, _s), 1e-12);
        }

        [Test]
        public void Facilities_DisabledCategoriesAreIgnoredAndTheRestRenormalised()
        {
            _s.FacilityCategoryEnabled[(int)RatingFacility.Toilets] = false;
            _s.FacilityCategoryEnabled[(int)RatingFacility.Benches] = false;
            // Only Food (1/2 = 50) and Drink (2/2 = 100) apply: average 75, not (50 + 100 + 0 + 0) / 4.
            Assert.AreEqual(75.0, ZooRatingMath.Facilities(new[] { 1, 2, 0, 0 }, _s), 1e-12);
        }

        [Test]
        public void Facilities_ACategoryRequiringNothingIsNonApplicable()
        {
            _s.RequiredFacilityCounts[(int)RatingFacility.Benches] = 0f;
            Assert.AreEqual(100.0, ZooRatingMath.Facilities(new[] { 2, 2, 2, 0 }, _s), 1e-12);
        }

        [Test]
        public void Facilities_WithNothingApplicableIsZero()
        {
            for (int i = 0; i < ZooRatingSettings.FacilityCategoryCount; i++) _s.FacilityCategoryEnabled[i] = false;
            Assert.AreEqual(0.0, ZooRatingMath.Facilities(new[] { 9, 9, 9, 9 }, _s), 1e-12);
        }

        [Test]
        public void Facilities_NegativeCountsClampToZero()
        {
            Assert.AreEqual(0.0, ZooRatingMath.Facilities(new[] { -3, -1, -2, -9 }, _s), 1e-12);
        }

        // ---- Final rating ----

        [Test]
        public void Rating_AppliesTheDesignWeights()
        {
            // 0.30*100 + 0.25*0 + 0.20*0 + 0.15*0 + 0.10*0
            Assert.AreEqual(30.0, ZooRatingMath.Rating(100, 0, 0, 0, 0, _s), 1e-9);
            Assert.AreEqual(25.0, ZooRatingMath.Rating(0, 100, 0, 0, 0, _s), 1e-9);
            Assert.AreEqual(20.0, ZooRatingMath.Rating(0, 0, 100, 0, 0, _s), 1e-9);
            Assert.AreEqual(15.0, ZooRatingMath.Rating(0, 0, 0, 100, 0, _s), 1e-9);
            Assert.AreEqual(10.0, ZooRatingMath.Rating(0, 0, 0, 0, 100, _s), 1e-9);
        }

        [Test]
        public void Rating_AllMaxIs100_AllZeroIs0()
        {
            Assert.AreEqual(100.0, ZooRatingMath.Rating(100, 100, 100, 100, 100, _s), 1e-9);
            Assert.AreEqual(0.0, ZooRatingMath.Rating(0, 0, 0, 0, 0, _s), 1e-12);
        }

        [Test]
        public void Rating_OutOfRangeComponentsStayInsideZeroTo100()
        {
            double high = ZooRatingMath.Rating(1000, 1000, 1000, 1000, 1000, _s);
            double low = ZooRatingMath.Rating(-1000, -1000, double.NaN, -1, -50, _s);
            Assert.AreEqual(100.0, high, 1e-9);
            Assert.AreEqual(0.0, low, 1e-12);
        }

        [Test]
        public void Rating_StaysClampedEvenIfWeightsAreMisconfiguredAboveOne()
        {
            _s.WelfareWeight = 1f;
            _s.VisitorHappinessWeight = 1f;
            Assert.AreEqual(100.0, ZooRatingMath.Rating(100, 100, 100, 100, 100, _s), 1e-9);
        }

        // ---- Stars ----

        [TestCase(0.0, 1)]
        [TestCase(19.999, 1)]
        [TestCase(20.0, 2)]
        [TestCase(39.999, 2)]
        [TestCase(40.0, 3)]
        [TestCase(59.999, 3)]
        [TestCase(60.0, 4)]
        [TestCase(79.999, 4)]
        [TestCase(80.0, 5)]
        [TestCase(100.0, 5)]
        public void Stars_FollowTheBands(double rating, int expected)
        {
            Assert.AreEqual(expected, ZooRatingMath.Stars(rating, _s));
        }

        [Test]
        public void Stars_UseTheUnderlyingValueNotARoundedOne()
        {
            // Displays as "20.0" but has not reached 2 stars.
            Assert.AreEqual("20.0", (19.96).ToString("0.0"));
            Assert.AreEqual(1, ZooRatingMath.Stars(19.96, _s));
            Assert.IsFalse(ZooRatingMath.IsStarReached(19.96, 2, _s));
            Assert.IsTrue(ZooRatingMath.IsStarReached(20.0, 2, _s));
        }

        [Test]
        public void Stars_ABoundaryBuiltFromWeightedTermsIsNotLostToFloatingPointError()
        {
            // 80 in every component gives exactly the 80 boundary mathematically.
            var b = ZooRatingMath.Compose(80, 80, 80, 80, 80, _s);
            Assert.AreEqual(5, b.Stars);
            Assert.AreEqual(80.0, b.Rating, 1e-9);
        }

        [Test]
        public void Stars_FollowConfiguredThresholds()
        {
            _s.StarThresholds[0] = 10f;
            Assert.AreEqual(2, ZooRatingMath.Stars(10.0, _s));
            Assert.AreEqual(1, ZooRatingMath.Stars(9.999, _s));
        }

        [Test]
        public void IsStarReached_OneStarIsAlwaysUnlocked_BeyondMaxNever()
        {
            Assert.IsTrue(ZooRatingMath.IsStarReached(0.0, 1, _s));
            Assert.IsFalse(ZooRatingMath.IsStarReached(100.0, 6, _s));
        }

        [Test]
        public void Compose_ClampsEveryComponentAndTheTotal()
        {
            var b = ZooRatingMath.Compose(500, -20, double.NaN, 100, 100, _s);
            Assert.AreEqual(100.0, b.AnimalWelfare, 1e-12);
            Assert.AreEqual(0.0, b.VisitorHappiness, 1e-12);
            Assert.AreEqual(0.0, b.AnimalVariety, 1e-12);
            Assert.That(b.Rating, Is.InRange(0.0, 100.0));
            Assert.That(b.Stars, Is.InRange(1, 5));
        }
    }
}
