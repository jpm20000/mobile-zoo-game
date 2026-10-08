using System;
using UnityEngine;

namespace ZooGame.World
{
    /// <summary>
    /// Authoritative logical map: a Width x Depth grid on the world X/Z plane stored in flat arrays
    /// (one flag byte, one terrain byte and one enclosure id per cell). No GameObject or MonoBehaviour per cell.
    ///
    /// Cell (x, z) covers world X in [Origin.x + x*CellSize, Origin.x + (x+1)*CellSize) and likewise for Z, so
    /// conversion uses floor and a point exactly on a shared edge belongs to the higher-index cell.
    /// Everything out of range is handled safely: queries return false / an invalid cell, mutators return false.
    /// Pure data; not thread-safe; deterministic.
    /// </summary>
    public sealed class ZooGrid
    {
        const float CoordLimit = 1e9f;

        readonly CellFlags[] _flags;
        readonly TerrainType[] _terrain;
        readonly ushort[] _enclosure;
        int _unlockedCount;

        public int Width { get; }
        public int Depth { get; }
        public float CellSize { get; }
        /// <summary>World position of the grid's minimum (x=0, z=0) corner.</summary>
        public Vector3 Origin { get; }
        public int CellCount => _flags.Length;
        public int UnlockedCount => _unlockedCount;

        /// <summary>Minimum world corner of the whole grid (alias of <see cref="Origin"/>).</summary>
        public Vector3 WorldMin => Origin;
        /// <summary>Maximum world corner of the whole grid.</summary>
        public Vector3 WorldMax => new Vector3(Origin.x + Width * CellSize, Origin.y, Origin.z + Depth * CellSize);
        public Vector3 WorldCenter => (WorldMin + WorldMax) * 0.5f;

        /// <summary>Raised after any mutation that actually changed a cell. Subscribe in OnEnable, unsubscribe in OnDisable.</summary>
        public event Action Changed;

        public ZooGrid(GridSettings settings)
        {
            if (settings.Width <= 0 || settings.Depth <= 0)
                throw new ArgumentOutOfRangeException(nameof(settings), "Grid dimensions must be positive.");
            if (!(settings.CellSize > 0f))
                throw new ArgumentOutOfRangeException(nameof(settings), "Cell size must be positive.");

            Width = settings.Width;
            Depth = settings.Depth;
            CellSize = settings.CellSize;
            Origin = settings.Origin;

            int count = Width * Depth;
            _flags = new CellFlags[count];
            _terrain = new TerrainType[count];
            _enclosure = new ushort[count];

            UnlockRect(settings.UnlockedMinX, settings.UnlockedMinZ, settings.UnlockedWidth, settings.UnlockedDepth);
        }

        // ---- Bounds ----------------------------------------------------------------------------------------

        public bool IsInsideGrid(GridCoord c) => IsInsideGrid(c.X, c.Z);

        public bool IsInsideGrid(int x, int z) => (uint)x < (uint)Width && (uint)z < (uint)Depth;

        // ---- Conversion ------------------------------------------------------------------------------------

        /// <summary>Cell containing the world point (Y ignored). The result may be outside the grid.</summary>
        public GridCoord WorldToGrid(Vector3 world) =>
            new GridCoord(FloorToCell((world.x - Origin.x) / CellSize), FloorToCell((world.z - Origin.z) / CellSize));

        /// <summary>Like <see cref="WorldToGrid"/> but false when the point is outside the grid.</summary>
        public bool TryWorldToGrid(Vector3 world, out GridCoord coord)
        {
            coord = WorldToGrid(world);
            return IsInsideGrid(coord);
        }

        /// <summary>World position of the cell centre, at the grid's ground height (Origin.y). Works for any coordinate.</summary>
        public Vector3 GridToWorld(GridCoord c) =>
            new Vector3(Origin.x + (c.X + 0.5f) * CellSize, Origin.y, Origin.z + (c.Z + 0.5f) * CellSize);

        /// <summary>World position of the cell's minimum corner. Works for any coordinate.</summary>
        public Vector3 GridToWorldCorner(GridCoord c) =>
            new Vector3(Origin.x + c.X * CellSize, Origin.y, Origin.z + c.Z * CellSize);

        static int FloorToCell(float v)
        {
            // Clamp first: casting NaN/huge floats to int is undefined; this keeps every input deterministic
            // and guarantees out-of-range values land outside the grid.
            if (!(v > -CoordLimit)) return -(int)CoordLimit; // also catches NaN
            if (v > CoordLimit) return (int)CoordLimit;
            return (int)Math.Floor(v);
        }

        // ---- Queries ---------------------------------------------------------------------------------------

        /// <summary>Snapshot of a cell. Outside the grid this returns a cell with <see cref="GridCell.IsValid"/> false.</summary>
        public GridCell GetCell(GridCoord c)
        {
            if (!IsInsideGrid(c)) return new GridCell(c, false, CellFlags.None, TerrainType.Grass, GridCell.NoEnclosure);
            int i = Index(c);
            return new GridCell(c, true, _flags[i], _terrain[i], _enclosure[i]);
        }

        public bool TryGetCell(GridCoord c, out GridCell cell)
        {
            cell = GetCell(c);
            return cell.IsValid;
        }

        public bool IsUnlocked(GridCoord c) => IsInsideGrid(c) && (_flags[Index(c)] & CellFlags.Unlocked) != 0;

        public bool IsOccupied(GridCoord c) => IsInsideGrid(c) && (_flags[Index(c)] & CellFlags.Occupied) != 0;

        public bool HasPath(GridCoord c) => IsInsideGrid(c) && (_flags[Index(c)] & CellFlags.HasPath) != 0;

        /// <summary>Inside the grid, unlocked and not occupied.</summary>
        public bool IsAvailable(GridCoord c)
        {
            if (!IsInsideGrid(c)) return false;
            var f = _flags[Index(c)];
            return (f & CellFlags.Unlocked) != 0 && (f & CellFlags.Occupied) == 0;
        }

        // ---- Mutation (each returns false for out-of-range cells and raises Changed only on real change) ----

        public bool SetUnlocked(GridCoord c, bool unlocked)
        {
            if (!IsInsideGrid(c)) return false;
            if (!SetFlag(Index(c), CellFlags.Unlocked, unlocked, out bool changed)) return false;
            if (changed)
            {
                _unlockedCount += unlocked ? 1 : -1;
                Changed?.Invoke();
            }
            return true;
        }

        /// <summary>Unlocks every in-grid cell of the rectangle. Returns how many cells changed. Raises Changed once.</summary>
        public int UnlockRect(int minX, int minZ, int width, int depth)
        {
            int x0 = Math.Max(minX, 0), z0 = Math.Max(minZ, 0);
            int x1 = (int)Math.Min((long)minX + Math.Max(width, 0), Width);
            int z1 = (int)Math.Min((long)minZ + Math.Max(depth, 0), Depth);
            int changedCount = 0;
            for (int z = z0; z < z1; z++)
            {
                int row = z * Width;
                for (int x = x0; x < x1; x++)
                {
                    if ((_flags[row + x] & CellFlags.Unlocked) != 0) continue;
                    _flags[row + x] |= CellFlags.Unlocked;
                    changedCount++;
                }
            }
            if (changedCount > 0)
            {
                _unlockedCount += changedCount;
                Changed?.Invoke();
            }
            return changedCount;
        }

        public bool SetOccupied(GridCoord c, bool occupied)
        {
            if (!IsInsideGrid(c)) return false;
            if (SetFlag(Index(c), CellFlags.Occupied, occupied, out bool changed) && changed) Changed?.Invoke();
            return true;
        }

        public bool SetPath(GridCoord c, bool hasPath)
        {
            if (!IsInsideGrid(c)) return false;
            if (SetFlag(Index(c), CellFlags.HasPath, hasPath, out bool changed) && changed) Changed?.Invoke();
            return true;
        }

        public bool SetTerrain(GridCoord c, TerrainType terrain)
        {
            if (!IsInsideGrid(c)) return false;
            int i = Index(c);
            if (_terrain[i] != terrain)
            {
                _terrain[i] = terrain;
                Changed?.Invoke();
            }
            return true;
        }

        /// <summary>Associates the cell with an enclosure id (<see cref="GridCell.NoEnclosure"/> clears it).</summary>
        public bool SetEnclosure(GridCoord c, int enclosureId)
        {
            if (!IsInsideGrid(c) || enclosureId < 0 || enclosureId > ushort.MaxValue) return false;
            int i = Index(c);
            if (_enclosure[i] != enclosureId)
            {
                _enclosure[i] = (ushort)enclosureId;
                Changed?.Invoke();
            }
            return true;
        }

        // ---- Internals -------------------------------------------------------------------------------------

        int Index(GridCoord c) => c.Z * Width + c.X;

        bool SetFlag(int index, CellFlags flag, bool on, out bool changed)
        {
            var before = _flags[index];
            var after = on ? before | flag : before & ~flag;
            changed = after != before;
            _flags[index] = after;
            return true;
        }
    }
}
