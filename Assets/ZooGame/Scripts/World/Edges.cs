using System;
using UnityEngine;

namespace ZooGame.World
{
    /// <summary>Compass direction on the grid. North is +Z, East is +X.</summary>
    public enum Direction4 : byte
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }

    public static class Direction4Extensions
    {
        public static Direction4 Opposite(this Direction4 d) => (Direction4)(((int)d + 2) & 3);
        public static Direction4 Clockwise(this Direction4 d) => (Direction4)(((int)d + 1) & 3);

        public static GridCoord Step(this Direction4 d, GridCoord c)
        {
            switch (d)
            {
                case Direction4.North: return new GridCoord(c.X, c.Z + 1);
                case Direction4.East: return new GridCoord(c.X + 1, c.Z);
                case Direction4.South: return new GridCoord(c.X, c.Z - 1);
                default: return new GridCoord(c.X - 1, c.Z);
            }
        }
    }

    /// <summary>Which way a cell edge runs. <see cref="X"/> edges run along the X axis (they separate a cell from its north/south neighbour).</summary>
    public enum EdgeAxis : byte
    {
        X = 0,
        Z = 1
    }

    /// <summary>What occupies a cell edge. Gates are boundaries too but are passable (reserved for keeper/visitor access).</summary>
    public enum EdgeKind : byte
    {
        None = 0,
        Fence = 1,
        Gate = 2
    }

    public static class EdgeKindExtensions
    {
        /// <summary>Does this kind close the boundary of an enclosure?</summary>
        public static bool IsBoundary(this EdgeKind k) => k != EdgeKind.None;

        /// <summary>Can something walk through it? Only gates. Nothing navigates yet; this is the hook for later access logic.</summary>
        public static bool IsPassable(this EdgeKind k) => k == EdgeKind.None || k == EdgeKind.Gate;
    }

    /// <summary>
    /// One unit-length edge between grid points, identified canonically so a shared edge has exactly one coordinate
    /// no matter which neighbouring cell it is reached from.
    /// <para><c>Axis.X</c> edge (x, z) runs from grid point (x, z) to (x+1, z): the south side of cell (x, z) and the
    /// north side of cell (x, z-1).</para>
    /// <para><c>Axis.Z</c> edge (x, z) runs from grid point (x, z) to (x, z+1): the west side of cell (x, z) and the
    /// east side of cell (x-1, z).</para>
    /// </summary>
    public readonly struct EdgeCoord : IEquatable<EdgeCoord>
    {
        public readonly EdgeAxis Axis;
        public readonly int X;
        public readonly int Z;

        public EdgeCoord(EdgeAxis axis, int x, int z)
        {
            Axis = axis;
            X = x;
            Z = z;
        }

        /// <summary>The edge on the given side of a cell. North of (x,z) and South of (x,z+1) are the same edge.</summary>
        public static EdgeCoord OfCell(GridCoord c, Direction4 side)
        {
            switch (side)
            {
                case Direction4.North: return new EdgeCoord(EdgeAxis.X, c.X, c.Z + 1);
                case Direction4.South: return new EdgeCoord(EdgeAxis.X, c.X, c.Z);
                case Direction4.East: return new EdgeCoord(EdgeAxis.Z, c.X + 1, c.Z);
                default: return new EdgeCoord(EdgeAxis.Z, c.X, c.Z);
            }
        }

        /// <summary>The cell on the south (X axis) or west (Z axis) side. May be outside the grid.</summary>
        public GridCoord CellA => Axis == EdgeAxis.X ? new GridCoord(X, Z - 1) : new GridCoord(X - 1, Z);

        /// <summary>The cell on the north (X axis) or east (Z axis) side. May be outside the grid.</summary>
        public GridCoord CellB => new GridCoord(X, Z);

        /// <summary>Grid point at the low end of the edge.</summary>
        public GridCoord PointStart => new GridCoord(X, Z);

        /// <summary>Grid point at the high end of the edge.</summary>
        public GridCoord PointEnd => Axis == EdgeAxis.X ? new GridCoord(X + 1, Z) : new GridCoord(X, Z + 1);

        public bool Equals(EdgeCoord o) => Axis == o.Axis && X == o.X && Z == o.Z;
        public override bool Equals(object obj) => obj is EdgeCoord o && Equals(o);
        public override int GetHashCode() => unchecked(((int)Axis * 397 ^ X) * 397 ^ Z);
        public static bool operator ==(EdgeCoord a, EdgeCoord b) => a.Equals(b);
        public static bool operator !=(EdgeCoord a, EdgeCoord b) => !a.Equals(b);
        public override string ToString() => (Axis == EdgeAxis.X ? "X-edge(" : "Z-edge(") + X + ", " + Z + ")";
    }

    /// <summary>An edge plus what is built on it.</summary>
    public readonly struct FenceEdge
    {
        public readonly EdgeCoord Edge;
        public readonly EdgeKind Kind;

        public FenceEdge(EdgeCoord edge, EdgeKind kind)
        {
            Edge = edge;
            Kind = kind;
        }
    }

    /// <summary>Screen-independent helpers that turn world positions into grid points, edges and straight runs.</summary>
    public static class EdgeMath
    {
        /// <summary>The nearest grid point (cell corner) to a world position. May lie outside the grid.</summary>
        public static GridCoord NearestPoint(ZooGrid grid, Vector3 world) =>
            new GridCoord(RoundClamped((world.x - grid.Origin.x) / grid.CellSize),
                          RoundClamped((world.z - grid.Origin.z) / grid.CellSize));

        /// <summary>The edge closest to a world position (by distance to its line). May lie outside the grid.</summary>
        public static EdgeCoord NearestEdge(ZooGrid grid, Vector3 world)
        {
            float u = (world.x - grid.Origin.x) / grid.CellSize;
            float v = (world.z - grid.Origin.z) / grid.CellSize;
            int rz = RoundClamped(v), rx = RoundClamped(u);
            float dzLine = Mathf.Abs(v - rz); // distance to the nearest X-axis line
            float dxLine = Mathf.Abs(u - rx);
            if (dzLine <= dxLine) return new EdgeCoord(EdgeAxis.X, FloorClamped(u), rz);
            return new EdgeCoord(EdgeAxis.Z, rx, FloorClamped(v));
        }

        /// <summary>Distance, in cells, from a world position to the nearest cell-edge line.</summary>
        public static float DistanceToNearestEdgeLine(ZooGrid grid, Vector3 world)
        {
            float u = (world.x - grid.Origin.x) / grid.CellSize;
            float v = (world.z - grid.Origin.z) / grid.CellSize;
            return Mathf.Min(Mathf.Abs(v - Mathf.Round(v)), Mathf.Abs(u - Mathf.Round(u)));
        }

        /// <summary>
        /// Appends the straight orthogonal run of edges from grid point <paramref name="a"/> to <paramref name="b"/>,
        /// along whichever axis has the larger distance (ties go to X). Nothing is added if the points are equal.
        /// The run is perpendicular-free: a drag never produces a diagonal.
        /// </summary>
        public static void StraightRun(GridCoord a, GridCoord b, System.Collections.Generic.List<EdgeCoord> output)
        {
            int dx = b.X - a.X, dz = b.Z - a.Z;
            if (dx == 0 && dz == 0) return;
            if (Math.Abs(dx) >= Math.Abs(dz))
            {
                int from = Math.Min(a.X, b.X), to = Math.Max(a.X, b.X);
                for (int x = from; x < to; x++) output.Add(new EdgeCoord(EdgeAxis.X, x, a.Z));
            }
            else
            {
                int from = Math.Min(a.Z, b.Z), to = Math.Max(a.Z, b.Z);
                for (int z = from; z < to; z++) output.Add(new EdgeCoord(EdgeAxis.Z, a.X, z));
            }
        }

        /// <summary>Appends the 4 x size edges outlining a size x size cell square whose minimum corner point is <paramref name="minPoint"/>.</summary>
        public static void SquareOutline(GridCoord minPoint, int size, System.Collections.Generic.List<EdgeCoord> output)
        {
            for (int i = 0; i < size; i++)
            {
                output.Add(new EdgeCoord(EdgeAxis.X, minPoint.X + i, minPoint.Z));
                output.Add(new EdgeCoord(EdgeAxis.X, minPoint.X + i, minPoint.Z + size));
                output.Add(new EdgeCoord(EdgeAxis.Z, minPoint.X, minPoint.Z + i));
                output.Add(new EdgeCoord(EdgeAxis.Z, minPoint.X + size, minPoint.Z + i));
            }
        }

        /// <summary>
        /// Appends the orthogonal cell route after <paramref name="from"/> up to and including <paramref name="to"/>,
        /// stepping along the longer axis first. Used to join successive finger samples while painting a path.
        /// </summary>
        public static void OrthogonalRoute(GridCoord from, GridCoord to, System.Collections.Generic.List<GridCoord> output)
        {
            int x = from.X, z = from.Z;
            bool xFirst = Math.Abs(to.X - from.X) >= Math.Abs(to.Z - from.Z);
            for (int pass = 0; pass < 2; pass++)
            {
                bool doX = (pass == 0) == xFirst;
                if (doX)
                    while (x != to.X) { x += Math.Sign(to.X - x); output.Add(new GridCoord(x, z)); }
                else
                    while (z != to.Z) { z += Math.Sign(to.Z - z); output.Add(new GridCoord(x, z)); }
            }
        }

        static int RoundClamped(float v) => FloorClamped(v + 0.5f);

        static int FloorClamped(float v)
        {
            const float limit = 1_000_000f;
            if (!(v > -limit)) return -(int)limit; // also catches NaN
            if (v > limit) return (int)limit;
            return Mathf.FloorToInt(v);
        }
    }
}
