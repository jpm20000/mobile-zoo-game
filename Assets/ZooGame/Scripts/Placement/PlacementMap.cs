using System;
using System.Collections.Generic;
using ZooGame.Data;
using ZooGame.World;

namespace ZooGame.Placement
{
    /// <summary>
    /// Registry of placed objects and the occupancy they create. Marks every footprint cell as
    /// <see cref="CellFlags.Occupied"/> on the <see cref="ZooGrid"/> and records which object owns it, so a cell can
    /// be traced back to its object and released exactly. The map enforces only the invariants that keep the data
    /// consistent (cells inside the grid, no overlap); gameplay rules such as locked land live in
    /// <see cref="PlacementValidator"/>, which callers run first.
    /// </summary>
    public sealed class PlacementMap
    {
        public const int NoOwner = 0;

        readonly ZooGrid _grid;
        readonly int[] _owner; // object id per cell, same indexing as the grid (z * Width + x)
        readonly Dictionary<int, PlacedObject> _objects = new Dictionary<int, PlacedObject>();
        int _nextId = 1;

        public PlacementMap(ZooGrid grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _owner = new int[grid.CellCount];
        }

        public ZooGrid Grid => _grid;
        public int Count => _objects.Count;
        public Dictionary<int, PlacedObject>.ValueCollection Objects => _objects.Values;

        /// <summary>Raised after an object is added / after it moved or rotated / after it is removed (its cells already released).</summary>
        public event Action<PlacedObject> Placed;
        public event Action<PlacedObject> Moved;
        public event Action<PlacedObject> Removed;

        /// <summary>Id of the object owning the cell, or <see cref="NoOwner"/> (also for cells outside the grid).</summary>
        public int OwnerIdAt(GridCoord c) => _grid.IsInsideGrid(c) ? _owner[Index(c)] : NoOwner;

        public PlacedObject GetAt(GridCoord c)
        {
            int id = OwnerIdAt(c);
            return id != NoOwner && _objects.TryGetValue(id, out var obj) ? obj : null;
        }

        public bool TryGet(int id, out PlacedObject obj) => _objects.TryGetValue(id, out obj);

        public bool Contains(PlacedObject obj) => obj != null && _objects.TryGetValue(obj.Id, out var o) && o == obj;

        /// <summary>
        /// Adds an object and occupies its cells. Returns null (and changes nothing) if any cell is outside the grid or
        /// already occupied. Does not check locked land; run <see cref="PlacementValidator"/> first.
        /// </summary>
        public PlacedObject Add(PlaceableDefinition definition, GridCoord origin, Rotation90 rotation)
        {
            if (definition == null) return null;
            Footprint.RotatedSize(definition.FootprintWidth, definition.FootprintHeight, rotation, out int w, out int h);
            if (!CanOccupy(origin, w, h, NoOwner)) return null;

            var obj = new PlacedObject(_nextId++, definition, origin, rotation);
            _objects.Add(obj.Id, obj);
            Occupy(obj);
            Placed?.Invoke(obj);
            return obj;
        }

        /// <summary>
        /// Moves/rotates an object atomically: its old cells are ignored when checking the new ones, then released and
        /// the new ones occupied. Returns false and leaves everything unchanged if the new footprint would leave the
        /// grid or overlap another object.
        /// </summary>
        public bool Move(PlacedObject obj, GridCoord origin, Rotation90 rotation)
        {
            if (!Contains(obj)) return false;
            Footprint.RotatedSize(obj.Definition.FootprintWidth, obj.Definition.FootprintHeight, rotation, out int w, out int h);
            if (!CanOccupy(origin, w, h, obj.Id)) return false;

            Release(obj);
            obj.Set(origin, rotation);
            Occupy(obj);
            Moved?.Invoke(obj);
            return true;
        }

        /// <summary>Removes an object and frees all its cells.</summary>
        public bool Remove(PlacedObject obj)
        {
            if (!Contains(obj)) return false;
            Release(obj);
            _objects.Remove(obj.Id);
            Removed?.Invoke(obj);
            return true;
        }

        /// <summary>True if every cell is inside the grid and unoccupied (cells owned by <paramref name="ignoreId"/> count as free).</summary>
        public bool CanOccupy(GridCoord origin, int width, int height, int ignoreId)
        {
            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                var c = new GridCoord(origin.X + x, origin.Z + z);
                if (!_grid.IsInsideGrid(c)) return false;
                if (!_grid.IsOccupied(c)) continue;
                if (ignoreId == NoOwner || _owner[Index(c)] != ignoreId) return false;
            }
            return true;
        }

        void Occupy(PlacedObject obj)
        {
            var cells = obj.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                _owner[Index(cells[i])] = obj.Id;
                _grid.SetOccupied(cells[i], true);
            }
        }

        void Release(PlacedObject obj)
        {
            var cells = obj.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                _owner[Index(cells[i])] = NoOwner;
                _grid.SetOccupied(cells[i], false);
            }
        }

        int Index(GridCoord c) => c.Z * _grid.Width + c.X;
    }
}
