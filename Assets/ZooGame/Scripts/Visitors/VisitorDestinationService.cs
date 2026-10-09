using System;
using System.Collections.Generic;
using UnityEngine;
using ZooGame.Animals;
using ZooGame.Data;
using ZooGame.Placement;
using ZooGame.World;

namespace ZooGame.Visitors
{
    public enum VisitorDestinationKind
    {
        EnclosureViewing = 0,
        FoodStall,
        DrinkStall,
        Toilet,
        Bench,
        Exit
    }

    /// <summary>
    /// Something a visitor can walk to. The Id is stable: a facility keeps "facility-N" for as long as the placed
    /// object exists, a viewing point keeps "enclosure-N/view-X-Z" for as long as that enclosure and cell qualify, so a
    /// visitor's TargetDestinationId survives unrelated rebuilds. Instances are replaced when the zoo changes; hold the
    /// Id, not the object.
    /// </summary>
    public sealed class VisitorDestination
    {
        internal VisitorDestination(string id, VisitorDestinationKind kind, GridCoord[] interactionCells, int enclosureId,
            int placedObjectId, Vector3 sourcePosition, bool enabled)
        {
            Id = id;
            Kind = kind;
            InteractionCells = interactionCells;
            EnclosureId = enclosureId;
            PlacedObjectId = placedObjectId;
            SourcePosition = sourcePosition;
            Enabled = enabled;
        }

        public string Id { get; }
        /// <summary>The facility type (food stall, toilet...), or EnclosureViewing / Exit.</summary>
        public VisitorDestinationKind Kind { get; }
        /// <summary>Path cells from which the destination can be used. Empty when no walkable path touches it.</summary>
        public IReadOnlyList<GridCoord> InteractionCells { get; }
        /// <summary>The M3 enclosure being viewed (EnclosureViewing only), else 0.</summary>
        public int EnclosureId { get; }
        /// <summary>The placed object behind a facility or the entrance, else 0.</summary>
        public int PlacedObjectId { get; }
        /// <summary>World position of the facility's centre (where the entrance spawns visitors from).</summary>
        public Vector3 SourcePosition { get; }
        public bool Enabled { get; internal set; }
        /// <summary>At least one walkable path cell touches it.</summary>
        public bool IsPathAccessible => InteractionCells.Count > 0;
        /// <summary>The primary interaction point (first interaction cell).</summary>
        public GridCoord InteractionPoint => InteractionCells.Count > 0 ? InteractionCells[0] : default;
    }

    /// <summary>
    /// Exposes everything visitors can walk to, and which of it is reachable from where they stand: the viewing points
    /// of each enclosure, food and drink stalls, toilets, benches and the entrance/exit. Destinations are derived from
    /// the placement map and the M3 enclosures, cached, and rebuilt lazily after a placement, path or fence change.
    /// Reachability is an O(1) check against the navigation service's cached components.
    /// </summary>
    public sealed class VisitorDestinationService : IDisposable
    {
        readonly ZooGrid _grid;
        readonly ConstructionModel _model;
        readonly PlacementMap _placement;
        readonly VisitorNavigationService _navigation;
        readonly IAnimalRegistry _animals;
        readonly IAnimalDefinitionResolver _definitions;

        readonly List<VisitorDestination> _all = new List<VisitorDestination>(32);
        readonly Dictionary<string, VisitorDestination> _byId = new Dictionary<string, VisitorDestination>();
        readonly HashSet<string> _disabled = new HashSet<string>();
        readonly HashSet<GridCoord> _seen = new HashSet<GridCoord>();
        readonly List<GridCoord> _scratchCells = new List<GridCoord>(8);
        bool _dirty = true;

        public VisitorDestinationService(ZooGrid grid, ConstructionModel model, PlacementMap placement,
            VisitorNavigationService navigation, IAnimalRegistry animals, IAnimalDefinitionResolver definitions)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _placement = placement ?? throw new ArgumentNullException(nameof(placement));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _animals = animals;
            _definitions = definitions;
            _placement.Placed += OnPlacementChanged;
            _placement.Moved += OnPlacementChanged;
            _placement.Removed += OnPlacementChanged;
            _model.Enclosures.Changed += MarkDirty;
            _model.FencesChanged += MarkDirty;
            _grid.PathsChanged += MarkDirty;
        }

        /// <summary>Raised when the destination set may have changed (including enable/disable).</summary>
        public event Action Changed;

        /// <summary>How many times the destination set was rebuilt (diagnostics and tests).</summary>
        public int RebuildCount { get; private set; }

        public void Dispose()
        {
            _placement.Placed -= OnPlacementChanged;
            _placement.Moved -= OnPlacementChanged;
            _placement.Removed -= OnPlacementChanged;
            _model.Enclosures.Changed -= MarkDirty;
            _model.FencesChanged -= MarkDirty;
            _grid.PathsChanged -= MarkDirty;
        }

        void OnPlacementChanged(PlacedObject _) => MarkDirty();

        void MarkDirty()
        {
            _dirty = true;
            Changed?.Invoke();
        }

        // ---- Lookups ---------------------------------------------------------------------------------------

        public int Count
        {
            get { EnsureBuilt(); return _all.Count; }
        }

        public IReadOnlyList<VisitorDestination> All
        {
            get { EnsureBuilt(); return _all; }
        }

        public bool TryGet(string destinationId, out VisitorDestination destination)
        {
            if (destinationId == null) { destination = null; return false; }
            EnsureBuilt();
            return _byId.TryGetValue(destinationId, out destination);
        }

        /// <summary>Enables or disables a destination (a closed stall, say). Unknown ids return false.</summary>
        public bool SetEnabled(string destinationId, bool enabled)
        {
            if (!TryGet(destinationId, out var d)) return false;
            if (d.Enabled == enabled) return true;
            if (enabled) _disabled.Remove(destinationId); else _disabled.Add(destinationId);
            d.Enabled = enabled;
            Changed?.Invoke();
            return true;
        }

        // ---- Reachability ----------------------------------------------------------------------------------

        /// <summary>Enabled, path-accessible and connected to the visitor's cell by the path network.</summary>
        public bool IsReachable(VisitorDestination d, GridCoord from) =>
            d != null && d.Enabled && d.IsPathAccessible && _navigation.IsConnectedToAny(from, d.InteractionCells);

        public bool IsReachable(string destinationId, GridCoord from) =>
            TryGet(destinationId, out var d) && IsReachable(d, from);

        /// <summary>Appends every reachable destination of a kind; returns how many were added.</summary>
        public int GetReachable(VisitorDestinationKind kind, GridCoord from, List<VisitorDestination> results)
        {
            EnsureBuilt();
            int added = 0;
            for (int i = 0; i < _all.Count; i++)
            {
                var d = _all[i];
                if (d.Kind != kind || !IsReachable(d, from)) continue;
                results.Add(d);
                added++;
            }
            return added;
        }

        /// <summary>Appends the id of every enclosure with at least one reachable viewing point.</summary>
        public int GetReachableEnclosures(GridCoord from, List<int> enclosureIds)
        {
            EnsureBuilt();
            int added = 0;
            for (int i = 0; i < _all.Count; i++)
            {
                var d = _all[i];
                if (d.Kind != VisitorDestinationKind.EnclosureViewing || enclosureIds.Contains(d.EnclosureId) || !IsReachable(d, from)) continue;
                enclosureIds.Add(d.EnclosureId);
                added++;
            }
            return added;
        }

        public int GetReachableViewpoints(int enclosureId, GridCoord from, List<VisitorDestination> results)
        {
            EnsureBuilt();
            int added = 0;
            for (int i = 0; i < _all.Count; i++)
            {
                var d = _all[i];
                if (d.Kind != VisitorDestinationKind.EnclosureViewing || d.EnclosureId != enclosureId || !IsReachable(d, from)) continue;
                results.Add(d);
                added++;
            }
            return added;
        }

        /// <summary>The first enabled, path-accessible entrance/exit (visitors spawn here and leave through it).</summary>
        public bool TryGetEntrance(out VisitorDestination entrance)
        {
            EnsureBuilt();
            for (int i = 0; i < _all.Count; i++)
            {
                var d = _all[i];
                if (d.Kind != VisitorDestinationKind.Exit || !d.Enabled || !d.IsPathAccessible) continue;
                entrance = d;
                return true;
            }
            entrance = null;
            return false;
        }

        // ---- Appeal ----------------------------------------------------------------------------------------

        /// <summary>Sum of the base appeal of every animal in the enclosure (no line of sight yet).</summary>
        public float TotalAnimalAppeal(int enclosureId)
        {
            if (_animals == null || _definitions == null) return 0f;
            float total = 0f;
            foreach (var a in _animals.GetByEnclosure(AnimalEnclosureService.ToId(enclosureId)))
                if (_definitions.TryGet(a.SpeciesId, out var def)) total += def.BaseAppeal;
            return total;
        }

        // ---- Building --------------------------------------------------------------------------------------

        void EnsureBuilt()
        {
            if (!_dirty) return;
            _dirty = false;
            RebuildCount++;
            _all.Clear();
            _byId.Clear();
            BuildFacilities();
            BuildViewingPoints();
        }

        void Add(VisitorDestination d)
        {
            if (_byId.ContainsKey(d.Id)) return;
            _all.Add(d);
            _byId.Add(d.Id, d);
        }

        void BuildFacilities()
        {
            foreach (var obj in _placement.Objects)
            {
                var kind = obj.Definition.VisitorFacility.Kind;
                if (kind == VisitorFacilityKind.None) continue;
                string id = "facility-" + obj.Id;
                CollectInteractionCells(obj, _scratchCells);
                Add(new VisitorDestination(id, Map(kind), _scratchCells.ToArray(), 0, obj.Id, Centre(obj), !_disabled.Contains(id)));
            }
        }

        static VisitorDestinationKind Map(VisitorFacilityKind kind)
        {
            switch (kind)
            {
                case VisitorFacilityKind.FoodStall: return VisitorDestinationKind.FoodStall;
                case VisitorFacilityKind.DrinkStall: return VisitorDestinationKind.DrinkStall;
                case VisitorFacilityKind.Toilet: return VisitorDestinationKind.Toilet;
                case VisitorFacilityKind.Bench: return VisitorDestinationKind.Bench;
                default: return VisitorDestinationKind.Exit; // Entrance doubles as the zoo exit
            }
        }

        /// <summary>Walkable path cells edge-adjacent to the footprint with no fence between (a path is never under an object).</summary>
        void CollectInteractionCells(PlacedObject obj, List<GridCoord> results)
        {
            results.Clear();
            var cells = obj.Cells;
            for (int i = 0; i < cells.Count; i++)
            for (int d = 0; d < 4; d++)
            {
                var dir = (Direction4)d;
                var n = dir.Step(cells[i]);
                if (_placement.OwnerIdAt(n) == obj.Id || !_navigation.IsWalkable(n)) continue;
                if (_model.Fences.GetSide(cells[i], dir).IsBoundary() || results.Contains(n)) continue;
                results.Add(n);
            }
        }

        Vector3 Centre(PlacedObject obj)
        {
            var cells = obj.Cells;
            var sum = Vector3.zero;
            for (int i = 0; i < cells.Count; i++) sum += _grid.GridToWorld(cells[i]);
            return cells.Count > 0 ? sum / cells.Count : Vector3.zero;
        }

        void BuildViewingPoints()
        {
            foreach (var enclosure in _model.Enclosures.Enclosures)
            {
                _seen.Clear();
                var edges = enclosure.BoundaryEdges;
                for (int i = 0; i < edges.Count; i++)
                {
                    // The side of the boundary that is not inside the enclosure is where a visitor stands.
                    var a = edges[i].CellA;
                    var b = edges[i].CellB;
                    var outside = _grid.IsInsideGrid(a) && _grid.GetCell(a).EnclosureId == enclosure.Id ? b : a;
                    if (!_navigation.IsWalkable(outside) || !_seen.Add(outside)) continue;
                    string id = "enclosure-" + enclosure.Id + "/view-" + outside.X + "-" + outside.Z;
                    Add(new VisitorDestination(id, VisitorDestinationKind.EnclosureViewing, new[] { outside }, enclosure.Id, 0,
                        _grid.GridToWorld(outside), !_disabled.Contains(id)));
                }
            }
        }
    }
}
