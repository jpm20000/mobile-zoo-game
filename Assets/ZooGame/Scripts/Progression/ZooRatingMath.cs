namespace ZooGame.Progression
{
    /// <summary>The result of one rating calculation. Components are 0-100 and unrounded; round only for display.</summary>
    public readonly struct ZooRatingBreakdown
    {
        public ZooRatingBreakdown(double welfare, double happiness, double variety, double beauty, double facilities,
            double rating, int stars)
        {
            AnimalWelfare = welfare;
            VisitorHappiness = happiness;
            AnimalVariety = variety;
            ZooBeauty = beauty;
            Facilities = facilities;
            Rating = rating;
            Stars = stars;
        }

        public double AnimalWelfare { get; }
        public double VisitorHappiness { get; }
        public double AnimalVariety { get; }
        public double ZooBeauty { get; }
        public double Facilities { get; }
        /// <summary>The weighted total, clamped to 0-100. Use this, not a rounded value, for unlocks.</summary>
        public double Rating { get; }
        public int Stars { get; }
    }

    /// <summary>
    /// The rating formulas as pure functions (no Unity, no scene state). Everything resolves to 0-100 before
    /// weighting; negative, NaN and missing inputs contribute 0 and values past the target clamp to 100. Doubles are
    /// used throughout so nothing is rounded until the UI formats it.
    /// </summary>
    public static class ZooRatingMath
    {
        public const double Max = 100.0;

        /// <summary>
        /// Tolerance for the star lookup only, so a rating that is mathematically on a boundary (for example 80 built
        /// from several weighted terms) is not pushed a star down by floating point error.
        /// </summary>
        public const double StarEpsilon = 1e-9;

        /// <summary>clamp01(current / target) * 100. A non-positive or NaN target is invalid and gives 0.</summary>
        public static double Normalize(double current, double target)
        {
            if (!(current > 0.0) || !(target > 0.0)) return 0.0; // also catches NaN
            if (current >= target) return Max;
            return current / target * Max;
        }

        public static double Clamp100(double value) => !(value > 0.0) ? 0.0 : value > Max ? Max : value;

        /// <summary>Average of already 0-100 values; 0 when there are none.</summary>
        public static double AverageScore(double sum, int count) => count <= 0 ? 0.0 : Clamp100(sum / count);

        /// <summary>Rolling happiness average when there is history, otherwise the active visitor average, otherwise 0.</summary>
        public static double VisitorHappiness(double historySum, int historyCount, double activeSum, int activeCount)
        {
            if (historyCount > 0) return AverageScore(historySum, historyCount);
            if (activeCount > 0) return AverageScore(activeSum, activeCount);
            return 0.0;
        }

        public static double AnimalVariety(int uniqueSpecies, ZooRatingSettings s) =>
            Normalize(uniqueSpecies, s.SpeciesCountForMaxScore);

        public static double ZooBeauty(double effectiveBeauty, ZooRatingSettings s) =>
            Normalize(effectiveBeauty, s.BeautyForMaxScore);

        /// <summary>
        /// Average of the applicable category scores, each clamp01(available / required) * 100. Categories that are
        /// disabled or require nothing are ignored and the rest renormalised; with none applicable the score is 0.
        /// <paramref name="available"/> is indexed by <see cref="RatingFacility"/>.
        /// </summary>
        public static double Facilities(int[] available, ZooRatingSettings s)
        {
            double sum = 0.0;
            int applicable = 0;
            for (int i = 0; i < ZooRatingSettings.FacilityCategoryCount; i++)
            {
                var category = (RatingFacility)i;
                if (!s.IsCategoryEnabled(category) || !(s.RequiredCount(category) > 0f)) continue;
                int have = available != null && i < available.Length ? available[i] : 0;
                sum += Normalize(have, s.RequiredCount(category));
                applicable++;
            }
            return applicable == 0 ? 0.0 : sum / applicable;
        }

        public static double Rating(double welfare, double happiness, double variety, double beauty, double facilities,
            ZooRatingSettings s)
        {
            double total = s.WelfareWeight * Clamp100(welfare)
                         + s.VisitorHappinessWeight * Clamp100(happiness)
                         + s.VarietyWeight * Clamp100(variety)
                         + s.BeautyWeight * Clamp100(beauty)
                         + s.FacilitiesWeight * Clamp100(facilities);
            return Clamp100(total);
        }

        /// <summary>1-5 stars from the underlying (unrounded) rating: 0-19.999 = 1, 20-39.999 = 2, ... 80-100 = 5.</summary>
        public static int Stars(double rating, ZooRatingSettings s)
        {
            int stars = 1;
            for (int i = 0; i < s.StarThresholds.Length && stars < ZooRatingSettings.MaxStars; i++)
            {
                if (rating + StarEpsilon >= s.StarThresholds[i]) stars++;
                else break;
            }
            return stars;
        }

        /// <summary>True once the underlying rating has reached the threshold of <paramref name="stars"/>.</summary>
        public static bool IsStarReached(double rating, int stars, ZooRatingSettings s)
        {
            if (stars <= 1) return true;
            if (stars > ZooRatingSettings.MaxStars) return false;
            return rating + StarEpsilon >= s.ThresholdForStars(stars);
        }

        public static ZooRatingBreakdown Compose(double welfare, double happiness, double variety, double beauty,
            double facilities, ZooRatingSettings s)
        {
            welfare = Clamp100(welfare);
            happiness = Clamp100(happiness);
            variety = Clamp100(variety);
            beauty = Clamp100(beauty);
            facilities = Clamp100(facilities);
            double rating = Rating(welfare, happiness, variety, beauty, facilities, s);
            return new ZooRatingBreakdown(welfare, happiness, variety, beauty, facilities, rating, Stars(rating, s));
        }
    }
}
