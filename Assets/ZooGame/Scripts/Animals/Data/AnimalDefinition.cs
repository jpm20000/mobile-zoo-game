using System;
using System.Collections.Generic;
using UnityEngine;
using ZooGame.World;

namespace ZooGame.Animals
{
    /// <summary>The share of an enclosure's cells a species wants covered by one terrain type (inclusive range, 0-1).</summary>
    [Serializable]
    public struct TerrainPreference
    {
        public TerrainType terrain;
        [Range(0f, 1f)] public float minShare;
        [Range(0f, 1f)] public float maxShare;

        public TerrainPreference(TerrainType terrain, float minShare, float maxShare)
        {
            this.terrain = terrain;
            this.minShare = minShare;
            this.maxShare = maxShare;
        }
    }

    public enum AnimalBiome
    {
        Grassland = 0,
        Savanna,
        Forest,
        Desert,
        Arctic,
        Wetland
    }

    /// <summary>
    /// What a species IS: read-only configuration shared by every individual of that species. Nothing here is ever
    /// per-animal (no id, name, sex, age, health, position, enclosure, genetics or AI state) and nothing mutates it at
    /// runtime. Individuals are <see cref="AnimalInstance"/>; what a spawned animal is doing is <see cref="AnimalController"/>.
    /// Looked up by the stable <see cref="SpeciesId"/>, never by asset reference, so saves can stay plain data.
    /// </summary>
    [CreateAssetMenu(menuName = "ZooGame/Animal Definition", fileName = "Animal")]
    public sealed class AnimalDefinition : ScriptableObject
    {
        [Tooltip("Stable key used by saves and lookups. Never change it once shipped.")]
        [SerializeField] string speciesId = "species";
        [SerializeField] string displayName = "Animal";
        [SerializeField, TextArea] string description;
        [SerializeField] GameObject prefab;
        [SerializeField] Sprite icon;
        [SerializeField] AnimalBiome biome;
        [Tooltip("World units per second.")]
        [SerializeField, Min(0.1f)] float baseMoveSpeed = 1.5f;
        [Tooltip("Smallest enclosure (in cells) this species may be placed in.")]
        [SerializeField, Min(1)] int minimumEnclosureArea = 9;
        [SerializeField, Min(1)] int preferredGroupMin = 1;
        [SerializeField, Min(1)] int preferredGroupMax = 4;
        [SerializeField, Min(0f)] float baseAppeal = 1f;

        [Header("Needs (M5) - per minute of simulated time, on the 0-100 scale")]
        [SerializeField, Min(0f)] float hungerDecayPerMinute = 0.4f;
        [SerializeField, Min(0f)] float thirstDecayPerMinute = 0.6f;
        [SerializeField, Min(0f)] float hungerRecoveryPerMinute = 6f;
        [SerializeField, Min(0f)] float thirstRecoveryPerMinute = 8f;

        [Header("Habitat (M5)")]
        [Tooltip("Needs standing water (water terrain) in the enclosure. Separate from drinking water.")]
        [SerializeField] bool requiresHabitatWater;
        [SerializeField] bool requiresShelter;
        [Tooltip("Shelter capacity needed per animal of this species in the enclosure.")]
        [SerializeField, Min(0f)] float shelterPerAnimal = 1f;
        [Tooltip("Total enrichment value the enclosure should offer. 0 = no requirement.")]
        [SerializeField, Min(0f)] float requiredEnrichmentValue;
        [Tooltip("Share of the enclosure's cells (0-1) each listed terrain should cover. Empty = no terrain preference.")]
        [SerializeField] TerrainPreference[] terrainPreferences = new TerrainPreference[0];
        [Tooltip("Distance (as a share of the enclosure, 0-1) outside a preferred range at which that terrain scores 0.")]
        [SerializeField, Range(0.05f, 1f)] float terrainTolerance = 0.5f;

        public string SpeciesId => speciesId;
        public string DisplayName => displayName;
        public string Description => description;
        public GameObject Prefab => prefab;
        public Sprite Icon => icon;
        public AnimalBiome Biome => biome;
        public float BaseMoveSpeed => baseMoveSpeed;
        public int MinimumEnclosureArea => minimumEnclosureArea;
        public int PreferredGroupMin => preferredGroupMin;
        public int PreferredGroupMax => preferredGroupMax;
        public float BaseAppeal => baseAppeal;
        public float HungerDecayPerMinute => hungerDecayPerMinute;
        public float ThirstDecayPerMinute => thirstDecayPerMinute;
        public float HungerRecoveryPerMinute => hungerRecoveryPerMinute;
        public float ThirstRecoveryPerMinute => thirstRecoveryPerMinute;
        public bool RequiresHabitatWater => requiresHabitatWater;
        public bool RequiresShelter => requiresShelter;
        public float ShelterPerAnimal => shelterPerAnimal;
        public float RequiredEnrichmentValue => requiredEnrichmentValue;
        public IReadOnlyList<TerrainPreference> TerrainPreferences => terrainPreferences;
        public float TerrainTolerance => terrainTolerance;

        /// <summary>Tests / tools only: sets the decay and recovery rates (per simulated minute).</summary>
        public AnimalDefinition ConfigureNeeds(float hungerDecay, float thirstDecay, float hungerRecovery, float thirstRecovery)
        {
            hungerDecayPerMinute = hungerDecay;
            thirstDecayPerMinute = thirstDecay;
            hungerRecoveryPerMinute = hungerRecovery;
            thirstRecoveryPerMinute = thirstRecovery;
            return this;
        }

        /// <summary>Tests / tools only: sets group size and habitat requirements.</summary>
        public AnimalDefinition ConfigureHabitat(int groupMin, int groupMax, bool habitatWater = false, bool shelter = false,
            float shelterEach = 1f, float enrichment = 0f, float tolerance = 0.5f, params TerrainPreference[] terrain)
        {
            preferredGroupMin = groupMin;
            preferredGroupMax = groupMax;
            requiresHabitatWater = habitatWater;
            requiresShelter = shelter;
            shelterPerAnimal = shelterEach;
            requiredEnrichmentValue = enrichment;
            terrainTolerance = tolerance;
            terrainPreferences = terrain ?? new TerrainPreference[0];
            return this;
        }

        /// <summary>Builds an in-memory definition (tests, tools). Authored content is created as assets instead.</summary>
        public static AnimalDefinition Create(string speciesId, string displayName, int minimumEnclosureArea,
            float baseMoveSpeed = 1.5f, GameObject prefab = null)
        {
            var d = CreateInstance<AnimalDefinition>();
            d.speciesId = speciesId;
            d.displayName = displayName;
            d.minimumEnclosureArea = minimumEnclosureArea;
            d.baseMoveSpeed = baseMoveSpeed;
            d.prefab = prefab;
            d.name = displayName;
            return d;
        }
    }
}
