using UnityEngine;

namespace ZooGame.Visitors
{
    /// <summary>
    /// All visitor tuning in one asset. Rates are per simulated minute; times are simulated minutes unless a name says
    /// seconds. The happiness weights and critical caps are part of the formula and live in <see cref="VisitorMath"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "ZooGame/Visitor Config", fileName = "VisitorConfig")]
    public sealed class VisitorConfig : ScriptableObject
    {
        [Header("Scheduling")]
        [Tooltip("Simulation seconds (GameClock time: stops on pause, scales with 1x/2x/3x) between need updates.")]
        [SerializeField, Min(0.05f)] float tickIntervalSeconds = 2f;
        [Tooltip("Simulated minutes that pass per simulation second.")]
        [SerializeField, Min(0f)] float simulatedMinutesPerSecond = 1f;
        [Tooltip("Safety cap on ticks processed in one frame after a hitch; leftover time is dropped.")]
        [SerializeField, Min(1)] int maxTicksPerAdvance = 8;

        [Header("Need rates (per simulated minute)")]
        [SerializeField, Min(0f)] float hungerIncreasePerMinute = 1f;
        [SerializeField, Min(0f)] float thirstIncreasePerMinute = 1.25f;
        [SerializeField, Min(0f)] float toiletIncreasePerMinute = 0.75f;
        [SerializeField, Min(0f)] float energyDecreasePerMinute = 0.5f;

        [Header("Urgency thresholds")]
        [SerializeField, Range(0f, 100f)] float hungerUrgentAt = 70f;
        [SerializeField, Range(0f, 100f)] float thirstUrgentAt = 70f;
        [SerializeField, Range(0f, 100f)] float toiletUrgentAt = 75f;
        [Tooltip("Energy at or below this sends the visitor to a bench.")]
        [SerializeField, Range(0f, 100f)] float energyLowAt = 25f;

        [Header("Facility effects")]
        [SerializeField, Min(0f)] float foodHungerReduction = 70f;
        [SerializeField, Min(0f)] float drinkThirstReduction = 75f;
        [Tooltip("Toilet need after using a toilet.")]
        [SerializeField, Range(0f, 100f)] float toiletNeedAfterUse = 0f;
        [SerializeField, Min(0f)] float benchEnergyGain = 50f;
        [Tooltip("Added to visit satisfaction after each successful facility use.")]
        [SerializeField, Min(0f)] float facilitySatisfactionBonus = 5f;
        [Tooltip("How long a bench rest lasts; the energy is gained when it ends.")]
        [SerializeField, Min(0f)] float restDurationMinutes = 5f;

        [Header("Viewing")]
        [SerializeField, Min(0f)] float viewDurationMinutes = 8f;
        [Tooltip("Total animal appeal that gives a full viewing score (100).")]
        [SerializeField, Min(0.01f)] float appealForMaxViewingScore = 100f;
        [Tooltip("How far one viewing moves visit satisfaction towards the viewing score.")]
        [SerializeField, Min(0f)] float viewingSatisfactionAdjustment = 20f;
        [Tooltip("Attraction weight multiplier for an enclosure the visitor recently viewed.")]
        [SerializeField, Range(0f, 1f)] float recentViewingMultiplier = 0.25f;

        [Header("Entering")]
        [SerializeField, Range(0f, 100f)] float hungerMin = 5f;
        [SerializeField, Range(0f, 100f)] float hungerMax = 20f;
        [SerializeField, Range(0f, 100f)] float thirstMin = 5f;
        [SerializeField, Range(0f, 100f)] float thirstMax = 20f;
        [SerializeField, Range(0f, 100f)] float toiletMin = 0f;
        [SerializeField, Range(0f, 100f)] float toiletMax = 15f;
        [SerializeField, Range(0f, 100f)] float energyMin = 80f;
        [SerializeField, Range(0f, 100f)] float energyMax = 100f;
        [SerializeField, Range(0f, 100f)] float startingVisitSatisfaction = 60f;

        [Header("Leaving")]
        [Tooltip("Each visitor stays for a random time in this range (simulated minutes).")]
        [SerializeField, Min(1f)] float minVisitMinutes = 120f;
        [SerializeField, Min(1f)] float maxVisitMinutes = 180f;
        [SerializeField, Range(0f, 100f)] float unhappyAtOrBelow = 20f;
        [Tooltip("Happiness must stay at or below the threshold for this long before the visitor leaves.")]
        [SerializeField, Min(0f)] float unhappyDurationMinutes = 2f;

        [Header("Spawning (development controls; not tied to rating or marketing yet)")]
        [SerializeField, Min(0.1f)] float spawnIntervalSeconds = 6f;
        [SerializeField, Min(0)] int maxVisitors = 20;
        [Tooltip("Seed for visitor randomness; 0 uses a time-based seed.")]
        [SerializeField, Min(0)] int randomSeed;

        [Header("Movement")]
        [Tooltip("Walking speed in grid cells per simulation second.")]
        [SerializeField, Min(0.1f)] float walkSpeedCellsPerSecond = 1.6f;

        public float TickIntervalSeconds => tickIntervalSeconds;
        public float SimulatedMinutesPerSecond => simulatedMinutesPerSecond;
        public int MaxTicksPerAdvance => maxTicksPerAdvance;
        public float MinutesPerTick => tickIntervalSeconds * simulatedMinutesPerSecond;

        public float HungerIncreasePerMinute => hungerIncreasePerMinute;
        public float ThirstIncreasePerMinute => thirstIncreasePerMinute;
        public float ToiletIncreasePerMinute => toiletIncreasePerMinute;
        public float EnergyDecreasePerMinute => energyDecreasePerMinute;

        public float HungerUrgentAt => hungerUrgentAt;
        public float ThirstUrgentAt => thirstUrgentAt;
        public float ToiletUrgentAt => toiletUrgentAt;
        public float EnergyLowAt => energyLowAt;

        public float FoodHungerReduction => foodHungerReduction;
        public float DrinkThirstReduction => drinkThirstReduction;
        public float ToiletNeedAfterUse => toiletNeedAfterUse;
        public float BenchEnergyGain => benchEnergyGain;
        public float FacilitySatisfactionBonus => facilitySatisfactionBonus;
        public float RestDurationMinutes => restDurationMinutes;

        public float ViewDurationMinutes => viewDurationMinutes;
        public float AppealForMaxViewingScore => appealForMaxViewingScore;
        public float ViewingSatisfactionAdjustment => viewingSatisfactionAdjustment;
        public float RecentViewingMultiplier => recentViewingMultiplier;

        public float HungerMin => hungerMin;
        public float HungerMax => hungerMax;
        public float ThirstMin => thirstMin;
        public float ThirstMax => thirstMax;
        public float ToiletMin => toiletMin;
        public float ToiletMax => toiletMax;
        public float EnergyMin => energyMin;
        public float EnergyMax => energyMax;
        public float StartingVisitSatisfaction => startingVisitSatisfaction;

        public float MinVisitMinutes => minVisitMinutes;
        public float MaxVisitMinutes => maxVisitMinutes;
        public float UnhappyAtOrBelow => unhappyAtOrBelow;
        public float UnhappyDurationMinutes => unhappyDurationMinutes;

        public float SpawnIntervalSeconds => spawnIntervalSeconds;
        public int MaxVisitors => maxVisitors;
        public int RandomSeed => randomSeed;
        public float WalkSpeedCellsPerSecond => walkSpeedCellsPerSecond;

        void OnValidate()
        {
            if (maxVisitMinutes < minVisitMinutes) maxVisitMinutes = minVisitMinutes;
            if (hungerMax < hungerMin) hungerMax = hungerMin;
            if (thirstMax < thirstMin) thirstMax = thirstMin;
            if (toiletMax < toiletMin) toiletMax = toiletMin;
            if (energyMax < energyMin) energyMax = energyMin;
        }

        /// <summary>In-memory config with the suggested defaults (tests, or a scene with no asset assigned).</summary>
        public static VisitorConfig CreateDefault()
        {
            var c = CreateInstance<VisitorConfig>();
            c.name = "VisitorConfig (default)";
            return c;
        }

        // ---- Test / tool helpers (fluent) ----

        public VisitorConfig WithScheduling(float tickSeconds, float minutesPerSecond)
        {
            tickIntervalSeconds = Mathf.Max(0.05f, tickSeconds);
            simulatedMinutesPerSecond = Mathf.Max(0f, minutesPerSecond);
            return this;
        }

        public VisitorConfig WithRates(float hunger, float thirst, float toilet, float energy)
        {
            hungerIncreasePerMinute = hunger; thirstIncreasePerMinute = thirst;
            toiletIncreasePerMinute = toilet; energyDecreasePerMinute = energy;
            return this;
        }

        public VisitorConfig WithVisitLength(float minMinutes, float maxMinutes)
        {
            minVisitMinutes = minMinutes; maxVisitMinutes = Mathf.Max(minMinutes, maxMinutes);
            return this;
        }

        public VisitorConfig WithViewing(float durationMinutes, float appealForMax, float adjustment)
        {
            viewDurationMinutes = durationMinutes; appealForMaxViewingScore = Mathf.Max(0.01f, appealForMax);
            viewingSatisfactionAdjustment = adjustment;
            return this;
        }

        public VisitorConfig WithUnhappyExit(float atOrBelow, float durationMinutes)
        {
            unhappyAtOrBelow = atOrBelow; unhappyDurationMinutes = durationMinutes;
            return this;
        }

        public VisitorConfig WithSpawning(float intervalSeconds, int max)
        {
            spawnIntervalSeconds = Mathf.Max(0.1f, intervalSeconds); maxVisitors = Mathf.Max(0, max);
            return this;
        }

        public VisitorConfig WithFacilityEffects(float foodHunger, float drinkThirst, float toiletAfterUse, float benchEnergy, float satisfactionBonus)
        {
            foodHungerReduction = foodHunger; drinkThirstReduction = drinkThirst;
            toiletNeedAfterUse = toiletAfterUse; benchEnergyGain = benchEnergy;
            facilitySatisfactionBonus = satisfactionBonus;
            return this;
        }

        public VisitorConfig WithRestDuration(float minutes)
        {
            restDurationMinutes = minutes;
            return this;
        }
    }
}
