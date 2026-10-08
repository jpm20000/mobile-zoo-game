using System;
using System.Collections.Generic;
using ZooGame.Data;
using ZooGame.Placement;
using ZooGame.World;

namespace ZooGame.Animals
{
    /// <summary>
    /// Builds and caches <see cref="EnclosureHabitat"/> summaries from the zoo grid (terrain), the enclosure map
    /// (cells), the placement map (food, water, shelter and enrichment objects) and the animal registry (residents).
    ///
    /// Invalidation is event-driven and O(1): structural changes (enclosure map, grid, placed objects) bump one version
    /// number, a resident change marks only that enclosure dirty. A summary is rebuilt lazily, on the next
    /// <see cref="TryGet"/>, in one pass over the enclosure's own cells. Nothing here runs per frame.
    /// </summary>
    public sealed class EnclosureHabitatService : IEnclosureHabitatService, IDisposable
    {
        sealed class Entry
        {
            public readonly EnclosureHabitat Habitat = new EnclosureHabitat();
            public int BuiltVersion = -1;
            public bool Dirty = true;
        }

        readonly ZooGrid _grid;
        readonly EnclosureMap _enclosures;
        readonly PlacementMap _placement; // may be null: no resources then
        readonly IAnimalRegistry _registry;
        readonly Dictionary<int, Entry> _cache = new Dictionary<int, Entry>();
        readonly HashSet<int> _seenObjects = new HashSet<int>();
        int _structureVersion;

        public EnclosureHabitatService(ZooGrid grid, EnclosureMap enclosures, PlacementMap placement, IAnimalRegistry registry)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _enclosures = enclosures ?? throw new ArgumentNullException(nameof(enclosures));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _placement = placement;

            _enclosures.Changed += OnStructureChanged;
            _grid.Changed += OnStructureChanged; // terrain edits; also fires for paths/occupancy, which is harmless (lazy rebuild)
            if (_placement != null)
            {
                _placement.Placed += OnObjectChanged;
                _placement.Moved += OnObjectChanged;
                _placement.Removed += OnObjectChanged;
            }
            _registry.MembershipChanged += OnMembershipChanged;
        }

        public event Action Changed;

        /// <summary>Number of summaries rebuilt so far (diagnostics and tests: proves caching).</summary>
        public int RebuildCount { get; private set; }

        public void Dispose()
        {
            _enclosures.Changed -= OnStructureChanged;
            _grid.Changed -= OnStructureChanged;
            if (_placement != null)
            {
                _placement.Placed -= OnObjectChanged;
                _placement.Moved -= OnObjectChanged;
                _placement.Removed -= OnObjectChanged;
            }
            _registry.MembershipChanged -= OnMembershipChanged;
        }

        void OnStructureChanged()
        {
            _structureVersion++;
            Changed?.Invoke();
        }

        void OnObjectChanged(PlacedObject _) => OnStructureChanged();

        void OnMembershipChanged(string enclosureId)
        {
            if (AnimalEnclosureService.TryParseId(enclosureId, out int id) && _cache.TryGetValue(id, out var entry)) entry.Dirty = true;
            Changed?.Invoke();
        }

        public bool TryGet(string enclosureId, out EnclosureHabitat habitat)
        {
            habitat = null;
            if (!AnimalEnclosureService.TryParseId(enclosureId, out int id)) return false;
            if (!_enclosures.TryGet(id, out var enclosure))
            {
                _cache.Remove(id);
                return false;
            }
            if (!_cache.TryGetValue(id, out var entry)) _cache[id] = entry = new Entry();
            if (entry.Dirty || entry.BuiltVersion != _structureVersion) Rebuild(entry, enclosure, enclosureId);
            habitat = entry.Habitat;
            return true;
        }

        void Rebuild(Entry entry, Enclosure enclosure, string enclosureId)
        {
            var h = entry.Habitat;
            h.Clear();
            h.EnclosureId = enclosureId;
            h.Area = enclosure.Area;
            _seenObjects.Clear();

            var cells = enclosure.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                int terrain = (int)_grid.GetCell(cell).Terrain;
                if ((uint)terrain < (uint)h.TerrainCells.Length) h.TerrainCells[terrain]++;

                if (_placement == null) continue;
                var obj = _placement.GetAt(cell);
                if (obj == null || !_seenObjects.Add(obj.Id)) continue; // a multi-cell object counts once
                var res = obj.Definition.HabitatResource;
                switch (res.Kind)
                {
                    case HabitatResourceKind.Food: h.FoodSources++; break;
                    case HabitatResourceKind.DrinkingWater: h.DrinkingWaterSources++; break;
                    case HabitatResourceKind.Shelter: h.ShelterCapacity += res.Amount; break;
                    case HabitatResourceKind.Enrichment: h.EnrichmentValue += res.Amount; break;
                }
            }

            foreach (var animal in _registry.GetByEnclosure(enclosureId)) h.AddAnimal(animal.SpeciesId);

            h.Version++;
            entry.BuiltVersion = _structureVersion;
            entry.Dirty = false;
            RebuildCount++;
        }
    }
}
