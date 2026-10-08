using System;
using UnityEngine;

namespace ZooGame.World
{
    /// <summary>Integer cell coordinate on the X/Z grid. May lie outside the grid; use <see cref="ZooGrid.IsInsideGrid(GridCoord)"/>.</summary>
    public readonly struct GridCoord : IEquatable<GridCoord>
    {
        public readonly int X;
        public readonly int Z;

        public GridCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(GridCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is GridCoord other && Equals(other);
        public override int GetHashCode() => unchecked(X * 397 ^ Z);
        public static bool operator ==(GridCoord a, GridCoord b) => a.Equals(b);
        public static bool operator !=(GridCoord a, GridCoord b) => !a.Equals(b);
        public override string ToString() => "(" + X + ", " + Z + ")";
    }

    /// <summary>Ground type of a cell. Placeholder set; extend (append only) as terrain art arrives.</summary>
    public enum TerrainType : byte
    {
        Grass = 0,
        Dirt,
        Sand,
        Water,
        Rock
    }

    [Flags]
    public enum CellFlags : byte
    {
        None = 0,
        Unlocked = 1 << 0,
        Occupied = 1 << 1,
        HasPath = 1 << 2
    }

    /// <summary>
    /// Read-only snapshot of one cell, returned by value. The grid itself stores cells in flat arrays, so
    /// no object exists per cell. <see cref="IsValid"/> is false for coordinates outside the grid.
    /// </summary>
    public readonly struct GridCell
    {
        /// <summary>Enclosure id meaning "not part of any enclosure".</summary>
        public const int NoEnclosure = 0;

        public readonly GridCoord Coord;
        public readonly bool IsValid;
        public readonly CellFlags Flags;
        public readonly TerrainType Terrain;
        public readonly int EnclosureId;

        public GridCell(GridCoord coord, bool isValid, CellFlags flags, TerrainType terrain, int enclosureId)
        {
            Coord = coord;
            IsValid = isValid;
            Flags = flags;
            Terrain = terrain;
            EnclosureId = enclosureId;
        }

        public bool IsUnlocked => IsValid && (Flags & CellFlags.Unlocked) != 0;
        public bool IsOccupied => IsValid && (Flags & CellFlags.Occupied) != 0;
        public bool HasPath => IsValid && (Flags & CellFlags.HasPath) != 0;
        public bool HasEnclosure => IsValid && EnclosureId != NoEnclosure;

        /// <summary>Unlocked and not occupied. Path and enclosure membership are separate facts and do not affect this.</summary>
        public bool IsAvailable => IsUnlocked && !IsOccupied;
    }

    /// <summary>Static layout of the grid in world space. The grid's minimum corner sits at <see cref="Origin"/>.</summary>
    public readonly struct GridSettings
    {
        public readonly int Width;
        public readonly int Depth;
        public readonly float CellSize;
        public readonly Vector3 Origin;
        public readonly int UnlockedMinX;
        public readonly int UnlockedMinZ;
        public readonly int UnlockedWidth;
        public readonly int UnlockedDepth;

        public GridSettings(int width, int depth, float cellSize, Vector3 origin,
            int unlockedMinX, int unlockedMinZ, int unlockedWidth, int unlockedDepth)
        {
            Width = width;
            Depth = depth;
            CellSize = cellSize;
            Origin = origin;
            UnlockedMinX = unlockedMinX;
            UnlockedMinZ = unlockedMinZ;
            UnlockedWidth = unlockedWidth;
            UnlockedDepth = unlockedDepth;
        }

        /// <summary>Layout with the initially unlocked block centred in the grid.</summary>
        public static GridSettings Centered(int width, int depth, float cellSize, Vector3 origin, int unlockedWidth, int unlockedDepth)
        {
            unlockedWidth = Mathf.Clamp(unlockedWidth, 0, width);
            unlockedDepth = Mathf.Clamp(unlockedDepth, 0, depth);
            return new GridSettings(width, depth, cellSize, origin,
                (width - unlockedWidth) / 2, (depth - unlockedDepth) / 2, unlockedWidth, unlockedDepth);
        }
    }
}
