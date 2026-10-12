using UnityEngine;

namespace ZooGame.Progression
{
    /// <summary>
    /// Zoo rating tuning in one asset. Create via Assets > Create > ZooGame > Zoo Rating Config. Optional: defaults
    /// apply when absent. See <see cref="ZooRatingMath"/> for how each target is used.
    /// </summary>
    [CreateAssetMenu(menuName = "ZooGame/Zoo Rating Config", fileName = "ZooRatingConfig")]
    public sealed class ZooRatingConfig : ScriptableObject
    {
        [Header("Targets (value at which a component reaches 100)")]
        [SerializeField, Min(1f)] float speciesCountForMaxScore = 20f;
        [SerializeField, Min(1f)] float beautyForMaxScore = 100f;
        [Tooltip("Recent departing visitors averaged for the Visitor Happiness component.")]
        [SerializeField, Min(1)] int happinessSampleCapacity = 50;

        [Header("Facilities: operational, reachable facilities required per category")]
        [SerializeField] bool foodEnabled = true;
        [SerializeField, Min(0)] int foodRequired = 2;
        [SerializeField] bool drinkEnabled = true;
        [SerializeField, Min(0)] int drinkRequired = 2;
        [SerializeField] bool toiletsEnabled = true;
        [SerializeField, Min(0)] int toiletsRequired = 2;
        [SerializeField] bool benchesEnabled = true;
        [SerializeField, Min(0)] int benchesRequired = 4;

        [Header("Weights (design: 0.30 / 0.25 / 0.20 / 0.15 / 0.10)")]
        [SerializeField, Range(0f, 1f)] float welfareWeight = 0.30f;
        [SerializeField, Range(0f, 1f)] float visitorHappinessWeight = 0.25f;
        [SerializeField, Range(0f, 1f)] float varietyWeight = 0.20f;
        [SerializeField, Range(0f, 1f)] float beautyWeight = 0.15f;
        [SerializeField, Range(0f, 1f)] float facilitiesWeight = 0.10f;

        [Header("Stars: rating needed for 2, 3, 4 and 5 stars")]
        [SerializeField, Range(0f, 100f)] float twoStars = 20f;
        [SerializeField, Range(0f, 100f)] float threeStars = 40f;
        [SerializeField, Range(0f, 100f)] float fourStars = 60f;
        [SerializeField, Range(0f, 100f)] float fiveStars = 80f;

        [Header("Update")]
        [Tooltip("Simulated seconds between automatic recalculations.")]
        [SerializeField, Min(0f)] float recalculateIntervalSeconds = 1f;

        public ZooRatingSettings ToSettings()
        {
            var s = new ZooRatingSettings
            {
                SpeciesCountForMaxScore = speciesCountForMaxScore,
                BeautyForMaxScore = beautyForMaxScore,
                HappinessSampleCapacity = happinessSampleCapacity,
                WelfareWeight = Exact(welfareWeight),
                VisitorHappinessWeight = Exact(visitorHappinessWeight),
                VarietyWeight = Exact(varietyWeight),
                BeautyWeight = Exact(beautyWeight),
                FacilitiesWeight = Exact(facilitiesWeight),
                RecalculateIntervalSeconds = recalculateIntervalSeconds
            };
            Set(s, RatingFacility.Food, foodEnabled, foodRequired);
            Set(s, RatingFacility.Drink, drinkEnabled, drinkRequired);
            Set(s, RatingFacility.Toilets, toiletsEnabled, toiletsRequired);
            Set(s, RatingFacility.Benches, benchesEnabled, benchesRequired);
            s.StarThresholds[0] = twoStars;
            s.StarThresholds[1] = threeStars;
            s.StarThresholds[2] = fourStars;
            s.StarThresholds[3] = fiveStars;
            s.Validate();
            return s;
        }

        // 0.3f widened to double is 0.30000001192...; going through decimal gives the value the designer typed.
        static double Exact(float v) => (double)(decimal)v;

        static void Set(ZooRatingSettings s, RatingFacility c, bool enabled, int required)
        {
            s.FacilityCategoryEnabled[(int)c] = enabled;
            s.RequiredFacilityCounts[(int)c] = required;
        }

        public static ZooRatingConfig CreateDefault() => CreateInstance<ZooRatingConfig>();
    }
}
