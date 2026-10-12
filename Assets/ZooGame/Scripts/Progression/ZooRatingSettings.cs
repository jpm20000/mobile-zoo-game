namespace ZooGame.Progression
{
    /// <summary>The facility categories that count toward the Facilities rating component.</summary>
    public enum RatingFacility
    {
        Food = 0,
        Drink,
        Toilets,
        Benches
    }

    /// <summary>
    /// Plain-data rating tuning, so the rating maths and service need no ScriptableObject. Built from
    /// <see cref="ZooRatingConfig.ToSettings"/> in the game and from <see cref="CreateDefault"/> in tests.
    /// Every target is configurable; the defaults are the design values.
    /// </summary>
    public sealed class ZooRatingSettings
    {
        public const int FacilityCategoryCount = 4;
        public const int MaxStars = 5;

        // ---- Targets (score = clamp01(value / target) * 100) ----
        /// <summary>Unique species needed for a full Animal Variety score.</summary>
        public float SpeciesCountForMaxScore = 20f;
        /// <summary>Effective beauty needed for a full Zoo Beauty score.</summary>
        public float BeautyForMaxScore = 100f;
        /// <summary>How many recent departing visitors the happiness average covers.</summary>
        public int HappinessSampleCapacity = 50;

        // ---- Facilities: required operational, reachable count per category ----
        public readonly float[] RequiredFacilityCounts = { 2f, 2f, 2f, 4f };
        /// <summary>Disabled categories are ignored and the rest renormalised.</summary>
        public readonly bool[] FacilityCategoryEnabled = { true, true, true, true };

        // ---- Final weights ----
        public double WelfareWeight = 0.30;
        public double VisitorHappinessWeight = 0.25;
        public double VarietyWeight = 0.20;
        public double BeautyWeight = 0.15;
        public double FacilitiesWeight = 0.10;

        // ---- Stars: rating needed for 2, 3, 4 and 5 stars (1 star below the first) ----
        public readonly float[] StarThresholds = { 20f, 40f, 60f, 80f };

        /// <summary>Simulated seconds between automatic recalculations.</summary>
        public float RecalculateIntervalSeconds = 1f;

        public static ZooRatingSettings CreateDefault() => new ZooRatingSettings();

        public float RequiredCount(RatingFacility category) => RequiredFacilityCounts[(int)category];
        public bool IsCategoryEnabled(RatingFacility category) => FacilityCategoryEnabled[(int)category];

        /// <summary>The underlying rating at which <paramref name="stars"/> is reached. 1 star (or less) is 0.</summary>
        public float ThresholdForStars(int stars)
        {
            if (stars <= 1) return 0f;
            if (stars > MaxStars) return float.PositiveInfinity;
            return StarThresholds[stars - 2];
        }

        /// <summary>Repairs invalid values (non-positive targets, decreasing star thresholds).</summary>
        public void Validate()
        {
            if (!(SpeciesCountForMaxScore > 0f)) SpeciesCountForMaxScore = 1f;
            if (!(BeautyForMaxScore > 0f)) BeautyForMaxScore = 1f;
            if (HappinessSampleCapacity < 1) HappinessSampleCapacity = 1;
            if (!(RecalculateIntervalSeconds >= 0f)) RecalculateIntervalSeconds = 0f;
            for (int i = 0; i < RequiredFacilityCounts.Length; i++)
                if (!(RequiredFacilityCounts[i] >= 0f)) RequiredFacilityCounts[i] = 0f;
            // Thresholds must not decrease or a higher star could be reached before a lower one.
            for (int i = 1; i < StarThresholds.Length; i++)
                if (StarThresholds[i] < StarThresholds[i - 1]) StarThresholds[i] = StarThresholds[i - 1];
        }
    }
}
