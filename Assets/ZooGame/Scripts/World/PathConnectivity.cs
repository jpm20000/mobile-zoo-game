namespace ZooGame.World
{
    /// <summary>How a path cell joins its neighbours.</summary>
    public enum PathShape : byte
    {
        /// <summary>No path neighbours.</summary>
        Isolated = 0,
        /// <summary>One neighbour.</summary>
        DeadEnd,
        /// <summary>Two opposite neighbours.</summary>
        Straight,
        /// <summary>Two neighbours at a right angle.</summary>
        Corner,
        /// <summary>Three neighbours.</summary>
        TJunction,
        /// <summary>Four neighbours.</summary>
        Cross
    }

    /// <summary>
    /// A path cell's connection state, derived from the grid on demand. Mask bits: 1 = north, 2 = east, 4 = south,
    /// 8 = west (bit index = <see cref="Direction4"/>). <see cref="Rotation"/> is the number of clockwise quarter turns
    /// from the canonical orientation of the shape, for art that is authored once per shape:
    /// DeadEnd north, Straight north-south, Corner north-east, TJunction open to the west (north, east, south).
    /// </summary>
    public readonly struct PathNode
    {
        public readonly int Mask;
        public readonly PathShape Shape;
        public readonly int Rotation;

        public PathNode(int mask, PathShape shape, int rotation)
        {
            Mask = mask;
            Shape = shape;
            Rotation = rotation;
        }

        public bool Connects(Direction4 d) => (Mask & (1 << (int)d)) != 0;
    }

    /// <summary>Neighbour-connection logic for path cells. Connectivity is never stored, so it cannot go stale.</summary>
    public static class PathConnectivity
    {
        const int N = 1, E = 2, S = 4, W = 8;

        /// <summary>Bit mask of the neighbouring cells that are paths.</summary>
        public static int GetMask(ZooGrid grid, GridCoord cell)
        {
            int mask = 0;
            if (grid.HasPath(Direction4.North.Step(cell))) mask |= N;
            if (grid.HasPath(Direction4.East.Step(cell))) mask |= E;
            if (grid.HasPath(Direction4.South.Step(cell))) mask |= S;
            if (grid.HasPath(Direction4.West.Step(cell))) mask |= W;
            return mask;
        }

        public static PathNode Describe(ZooGrid grid, GridCoord cell) => Classify(GetMask(grid, cell));

        public static PathNode Classify(int mask)
        {
            mask &= 15;
            switch (CountBits(mask))
            {
                case 0: return new PathNode(mask, PathShape.Isolated, 0);
                case 1: return new PathNode(mask, PathShape.DeadEnd, RotationFor(mask, N));
                case 2:
                    if (mask == (N | S) || mask == (E | W)) return new PathNode(mask, PathShape.Straight, RotationFor(mask, N | S));
                    return new PathNode(mask, PathShape.Corner, RotationFor(mask, N | E));
                case 3: return new PathNode(mask, PathShape.TJunction, RotationFor(mask, N | E | S));
                default: return new PathNode(mask, PathShape.Cross, 0);
            }
        }

        /// <summary>Mask rotated clockwise by quarter turns (north becomes east, and so on).</summary>
        public static int RotateMask(int mask, int quarterTurns)
        {
            quarterTurns &= 3;
            mask &= 15;
            return ((mask << quarterTurns) | (mask >> (4 - quarterTurns))) & 15;
        }

        static int RotationFor(int mask, int canonical)
        {
            for (int r = 0; r < 4; r++)
                if (RotateMask(canonical, r) == mask) return r;
            return 0;
        }

        static int CountBits(int v)
        {
            int n = 0;
            for (; v != 0; v &= v - 1) n++;
            return n;
        }
    }
}
