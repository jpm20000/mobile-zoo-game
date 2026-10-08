using UnityEngine;

namespace ZooGame.Animals
{
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
