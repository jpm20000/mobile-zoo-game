using UnityEngine;

namespace ZooGame.Animals
{
    /// <summary>
    /// Global tuning for the needs simulation (not species-specific; species numbers live in <see cref="AnimalDefinition"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "ZooGame/Animal Needs Config", fileName = "AnimalNeedsConfig")]
    public sealed class AnimalNeedsConfig : ScriptableObject
    {
        [Tooltip("Simulation seconds (GameClock time, so it scales with 1x/2x/3x and stops on pause) between need updates.")]
        [SerializeField, Min(0.05f)] float tickIntervalSeconds = 2f;
        [Tooltip("Simulated minutes that pass per simulation second. Decay/recovery rates are per simulated minute.")]
        [SerializeField, Min(0f)] float simulatedMinutesPerSecond = 1f;
        [Tooltip("Social score lost per animal above the species' preferred maximum.")]
        [SerializeField, Min(0f)] float overcrowdingPenaltyPerAnimal = 10f;
        [Tooltip("How fast Comfort moves towards its target, points per simulated minute.")]
        [SerializeField, Min(0f)] float comfortChangePerMinute = 4f;
        [Tooltip("How fast Enrichment moves towards its score, points per simulated minute.")]
        [SerializeField, Min(0f)] float enrichmentChangePerMinute = 4f;
        [Tooltip("Safety cap on ticks processed in one frame after a long hitch; leftover time is dropped.")]
        [SerializeField, Min(1)] int maxTicksPerAdvance = 8;

        public float TickIntervalSeconds => tickIntervalSeconds;
        public float SimulatedMinutesPerSecond => simulatedMinutesPerSecond;
        public float OvercrowdingPenaltyPerAnimal => overcrowdingPenaltyPerAnimal;
        public float ComfortChangePerMinute => comfortChangePerMinute;
        public float EnrichmentChangePerMinute => enrichmentChangePerMinute;
        public int MaxTicksPerAdvance => maxTicksPerAdvance;

        /// <summary>Simulated minutes covered by one scheduled tick.</summary>
        public float MinutesPerTick => tickIntervalSeconds * simulatedMinutesPerSecond;

        /// <summary>In-memory config (tests, or a scene that has no asset assigned). Authored values live in an asset.</summary>
        public static AnimalNeedsConfig CreateDefault(float tickIntervalSeconds = 2f, float simulatedMinutesPerSecond = 1f,
            float overcrowdingPenalty = 10f, float comfortPerMinute = 4f, float enrichmentPerMinute = 4f)
        {
            var c = CreateInstance<AnimalNeedsConfig>();
            c.tickIntervalSeconds = tickIntervalSeconds;
            c.simulatedMinutesPerSecond = simulatedMinutesPerSecond;
            c.overcrowdingPenaltyPerAnimal = overcrowdingPenalty;
            c.comfortChangePerMinute = comfortPerMinute;
            c.enrichmentChangePerMinute = enrichmentPerMinute;
            c.name = "AnimalNeedsConfig (default)";
            return c;
        }
    }
}
