using System;
using System.Collections.Generic;
using UnityEngine;
using ZooGame.World;

namespace ZooGame.Animals
{
    /// <summary>
    /// Adapts the M3 enclosure map to the animal systems. It holds no geometry of its own: every answer is read from
    /// the grid and <see cref="EnclosureMap"/> at call time, so it can never disagree with construction.
    /// </summary>
    public sealed class AnimalEnclosureService : IAnimalEnclosureService, IDisposable
    {
        const string Prefix = "enclosure-";
        /// <summary>Spawn points stay this fraction of a cell away from the cell edge, so animals do not start inside a fence.</summary>
        const float EdgeMargin = 0.25f;

        readonly ZooGrid _grid;
        readonly EnclosureMap _enclosures;
        readonly System.Random _rng;

        public AnimalEnclosureService(ZooGrid grid, EnclosureMap enclosures, System.Random rng = null)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _enclosures = enclosures ?? throw new ArgumentNullException(nameof(enclosures));
            _rng = rng ?? new System.Random();
            _enclosures.Changed += OnMapChanged;
        }

        public float CellSize => _grid.CellSize;

        public event Action EnclosuresChanged;

        public void Dispose() => _enclosures.Changed -= OnMapChanged;

        void OnMapChanged() => EnclosuresChanged?.Invoke();

        // ---- Ids -------------------------------------------------------------------------------------------

        public static string ToId(int mapId) => Prefix + mapId;

        public static bool TryParseId(string enclosureId, out int mapId)
        {
            mapId = 0;
            return enclosureId != null && enclosureId.StartsWith(Prefix, StringComparison.Ordinal)
                   && int.TryParse(enclosureId.Substring(Prefix.Length), out mapId);
        }

        bool TryGet(string enclosureId, out Enclosure enclosure)
        {
            enclosure = null;
            return TryParseId(enclosureId, out int id) && _enclosures.TryGet(id, out enclosure);
        }

        // ---- Queries ---------------------------------------------------------------------------------------

        public bool IsValidEnclosure(string enclosureId) => TryGet(enclosureId, out _);

        public bool ContainsPosition(string enclosureId, Vector3 worldPosition)
        {
            if (!TryParseId(enclosureId, out int id) || !_grid.TryWorldToGrid(worldPosition, out var cell)) return false;
            return _grid.GetCell(cell).EnclosureId == id && _enclosures.TryGet(id, out _);
        }

        public bool TryGetEnclosureAt(Vector3 worldPosition, out string enclosureId)
        {
            enclosureId = null;
            if (!_grid.TryWorldToGrid(worldPosition, out var cell)) return false;
            int id = _grid.GetCell(cell).EnclosureId;
            if (id == GridCell.NoEnclosure || !_enclosures.TryGet(id, out _)) return false;
            enclosureId = ToId(id);
            return true;
        }

        public bool TryGetArea(string enclosureId, out int area)
        {
            if (TryGet(enclosureId, out var e)) { area = e.Area; return true; }
            area = 0;
            return false;
        }

        public bool MeetsMinimumArea(string enclosureId, int minimumArea) =>
            TryGet(enclosureId, out var e) && e.Area >= minimumArea;

        public bool TryGetRandomValidPosition(string enclosureId, out Vector3 position)
        {
            position = default;
            if (!TryGet(enclosureId, out var e) || e.Cells.Count == 0) return false;
            var cell = e.Cells[_rng.Next(e.Cells.Count)];
            var corner = _grid.GridToWorldCorner(cell);
            float span = 1f - 2f * EdgeMargin;
            position = new Vector3(
                corner.x + (EdgeMargin + (float)_rng.NextDouble() * span) * _grid.CellSize,
                corner.y,
                corner.z + (EdgeMargin + (float)_rng.NextDouble() * span) * _grid.CellSize);
            return true;
        }

        public void GetEnclosureIds(List<string> results)
        {
            foreach (var e in _enclosures.Enclosures) results.Add(ToId(e.Id));
        }
    }
}
