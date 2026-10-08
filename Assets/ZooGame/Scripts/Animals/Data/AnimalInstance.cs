using System;
using UnityEngine;

namespace ZooGame.Animals
{
    /// <summary>
    /// One individual animal's persistent state: plain serializable data, not a MonoBehaviour and with no scene
    /// references. The <see cref="AnimalRegistry"/> owns these records for the whole life of the animal; the id never
    /// changes and is never derived from a GameObject, prefab, Transform or list position. Species values live in
    /// <see cref="AnimalDefinition"/> and are looked up by <see cref="SpeciesId"/>, not copied here.
    /// </summary>
    [Serializable]
    public sealed class AnimalInstance
    {
        [SerializeField] string animalId;
        [SerializeField] string speciesId;
        [SerializeField] string displayName;
        [SerializeField] AnimalSex sex;
        [SerializeField] string enclosureId;
        [SerializeField] Vector3 position;
        // Needs (M5), 0-100. Per-individual, so they live here; the species-level tuning lives in AnimalDefinition.
        [SerializeField] float hunger = FullNeed;
        [SerializeField] float thirst = FullNeed;
        [SerializeField] float comfort = StartingNeed;
        [SerializeField] float social = StartingNeed;
        [SerializeField] float enrichment = StartingNeed;
        [SerializeField] float overallWelfare = StartingNeed;

        public const float MinNeed = 0f;
        public const float MaxNeed = 100f;
        /// <summary>Hunger and thirst start full; comfort, social, enrichment and welfare start neutral until first evaluated.</summary>
        public const float FullNeed = 100f;
        public const float StartingNeed = 50f;

        /// <summary>Use for animals that already have an id (reconstruction from saved data). New animals use <see cref="CreateNew"/>.</summary>
        public AnimalInstance(string animalId, string speciesId, string displayName, AnimalSex sex, string enclosureId, Vector3 position)
        {
            if (string.IsNullOrEmpty(animalId)) throw new ArgumentException("An animal needs an id.", nameof(animalId));
            if (string.IsNullOrEmpty(speciesId)) throw new ArgumentException("An animal needs a species.", nameof(speciesId));
            this.animalId = animalId;
            this.speciesId = speciesId;
            this.displayName = displayName ?? string.Empty;
            this.sex = sex;
            this.enclosureId = Normalize(enclosureId);
            this.position = position;
            hunger = thirst = FullNeed;
            comfort = social = enrichment = overallWelfare = StartingNeed;
        }

        /// <summary>Creates a brand-new individual with a freshly generated id.</summary>
        public static AnimalInstance CreateNew(string speciesId, string displayName, AnimalSex sex, string enclosureId, Vector3 position) =>
            new AnimalInstance(NewId(), speciesId, displayName, sex, enclosureId, position);

        public static string NewId() => "animal-" + Guid.NewGuid().ToString("N");

        public string AnimalId => animalId;
        public string SpeciesId => speciesId;
        public string DisplayName { get => displayName; set => displayName = value ?? string.Empty; }
        public AnimalSex Sex => sex;
        public Vector3 Position { get => position; set => position = value; }

        // Every setter clamps to 0-100, so no system can push a need out of range.
        public float Hunger { get => hunger; set => hunger = Clamp(value); }
        public float Thirst { get => thirst; set => thirst = Clamp(value); }
        public float Comfort { get => comfort; set => comfort = Clamp(value); }
        public float Social { get => social; set => social = Clamp(value); }
        public float Enrichment { get => enrichment; set => enrichment = Clamp(value); }
        public float OverallWelfare { get => overallWelfare; set => overallWelfare = Clamp(value); }

        static float Clamp(float v) => float.IsNaN(v) ? MinNeed : Mathf.Clamp(v, MinNeed, MaxNeed);

        /// <summary>The enclosure this animal belongs to, or null. Changed through <see cref="IAnimalRegistry.SetEnclosure"/> so the registry's index stays correct.</summary>
        public string EnclosureId => enclosureId;
        public bool HasEnclosure => enclosureId != null;

        internal void SetEnclosure(string id) => enclosureId = Normalize(id);

        static string Normalize(string id) => string.IsNullOrEmpty(id) ? null : id;
    }
}
