using System;
using System.Collections.Generic;
using ZooGame.World;

namespace ZooGame.Animals
{
    /// <summary>
    /// A cached, aggregated summary of one enclosure: everything the needs simulation asks about habitat, so animals
    /// never scan cells, objects or each other. Rebuilt by <see cref="EnclosureHabitatService"/> only when the
    /// enclosure's contents change. Plain data; the setters are public so tests and tools can build one directly.
    /// </summary>
    public sealed class EnclosureHabitat
    {
        public static readonly int TerrainTypeCount = Enum.GetValues(typeof(TerrainType)).Length;

        readonly Dictionary<string, int> _speciesCounts = new Dictionary<string, int>();

        public string EnclosureId { get; set; }
        /// <summary>Number of cells.</summary>
        public int Area { get; set; }
        /// <summary>Cells of each <see cref="TerrainType"/>, indexed by the enum value.</summary>
        public int[] TerrainCells { get; } = new int[TerrainTypeCount];
        /// <summary>Food sources inside the enclosure. Unlimited in M5: any source means food is usable.</summary>
        public int FoodSources { get; set; }
        /// <summary>Drinking-water sources (troughs). Separate from standing <see cref="HabitatWaterCells"/>.</summary>
        public int DrinkingWaterSources { get; set; }
        /// <summary>Total animals the shelters in the enclosure cover.</summary>
        public float ShelterCapacity { get; set; }
        public float EnrichmentValue { get; set; }
        /// <summary>Residents of every species.</summary>
        public int AnimalCount { get; private set; }
        /// <summary>Incremented on every rebuild, so consumers can cheaply detect change.</summary>
        public int Version { get; set; }

        public bool HasFood => FoodSources > 0;
        public bool HasDrinkingWater => DrinkingWaterSources > 0;
        /// <summary>Standing water: cells of water terrain.</summary>
        public int HabitatWaterCells => TerrainCells[(int)TerrainType.Water];
        public bool HasHabitatWater => HabitatWaterCells > 0;

        public int CountOf(string speciesId) =>
            speciesId != null && _speciesCounts.TryGetValue(speciesId, out int n) ? n : 0;

        public void SetCount(string speciesId, int count)
        {
            if (string.IsNullOrEmpty(speciesId)) return;
            _speciesCounts.TryGetValue(speciesId, out int before);
            AnimalCount += count - before;
            if (count > 0) _speciesCounts[speciesId] = count;
            else _speciesCounts.Remove(speciesId);
        }

        public void AddAnimal(string speciesId)
        {
            _speciesCounts.TryGetValue(speciesId, out int n);
            _speciesCounts[speciesId] = n + 1;
            AnimalCount++;
        }

        /// <summary>Zeroes the aggregates before a rebuild (keeps the allocated storage).</summary>
        public void Clear()
        {
            Area = 0;
            Array.Clear(TerrainCells, 0, TerrainCells.Length);
            FoodSources = 0;
            DrinkingWaterSources = 0;
            ShelterCapacity = 0f;
            EnrichmentValue = 0f;
            AnimalCount = 0;
            _speciesCounts.Clear();
        }
    }
}
