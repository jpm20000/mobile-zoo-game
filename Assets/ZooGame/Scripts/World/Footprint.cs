using UnityEngine;

namespace ZooGame.World
{
    /// <summary>Clockwise quarter-turn rotation of a placed object, in 90 degree steps.</summary>
    public enum Rotation90 : byte
    {
        Deg0 = 0,
        Deg90 = 1,
        Deg180 = 2,
        Deg270 = 3
    }

    /// <summary>
    /// Pure footprint maths. A footprint is a width x height rectangle of cells (X by Z). Its <i>origin</i> is the
    /// minimum-corner cell of the rectangle <b>after</b> rotation, so the occupied cells are always
    /// <c>[origin.X, origin.X + rotatedWidth) x [origin.Z, origin.Z + rotatedHeight)</c>. A 90/270 degree rotation swaps
    /// width and height (2x3 becomes 3x2). Rotating keeps the origin cell fixed.
    /// </summary>
    public static class Footprint
    {
        public static Rotation90 Next(Rotation90 r) => (Rotation90)(((int)r + 1) & 3);

        public static Rotation90 Previous(Rotation90 r) => (Rotation90)(((int)r + 3) & 3);

        /// <summary>True for 90 and 270 degrees, where width and height are swapped.</summary>
        public static bool SwapsAxes(Rotation90 r) => ((int)r & 1) != 0;

        public static float Degrees(Rotation90 r) => (int)r * 90f;

        public static void RotatedSize(int width, int height, Rotation90 rotation, out int rotatedWidth, out int rotatedHeight)
        {
            if (SwapsAxes(rotation))
            {
                rotatedWidth = height;
                rotatedHeight = width;
            }
            else
            {
                rotatedWidth = width;
                rotatedHeight = height;
            }
        }

        /// <summary>The <paramref name="index"/>th cell (row-major, X fastest) of a rotated-size rectangle.</summary>
        public static GridCoord CellAt(GridCoord origin, int rotatedWidth, int index) =>
            new GridCoord(origin.X + index % rotatedWidth, origin.Z + index / rotatedWidth);

        /// <summary>Writes every cell of the rectangle into <paramref name="buffer"/> and returns the count written.</summary>
        public static int Fill(GridCoord origin, int rotatedWidth, int rotatedHeight, GridCoord[] buffer)
        {
            int count = Mathf.Min(rotatedWidth * rotatedHeight, buffer.Length);
            for (int i = 0; i < count; i++) buffer[i] = CellAt(origin, rotatedWidth, i);
            return count;
        }

        public static bool Contains(GridCoord origin, int rotatedWidth, int rotatedHeight, GridCoord cell) =>
            cell.X >= origin.X && cell.X < origin.X + rotatedWidth &&
            cell.Z >= origin.Z && cell.Z < origin.Z + rotatedHeight;

        /// <summary>World position of the rectangle's centre at ground height.</summary>
        public static Vector3 CentreWorld(ZooGrid grid, GridCoord origin, int rotatedWidth, int rotatedHeight)
        {
            var corner = grid.GridToWorldCorner(origin);
            return new Vector3(corner.x + rotatedWidth * 0.5f * grid.CellSize, corner.y,
                corner.z + rotatedHeight * 0.5f * grid.CellSize);
        }

        /// <summary>
        /// The origin that puts the rectangle's centre as close as possible to a world point (snaps to whole cells,
        /// so even-sized footprints centre on a cell corner and odd-sized ones on a cell centre). Never throws; NaN
        /// and huge values give a deterministic far-away origin that validation rejects.
        /// </summary>
        public static GridCoord OriginForCentre(ZooGrid grid, Vector3 worldCentre, int rotatedWidth, int rotatedHeight)
        {
            float lx = (worldCentre.x - grid.Origin.x) / grid.CellSize - rotatedWidth * 0.5f + 0.5f;
            float lz = (worldCentre.z - grid.Origin.z) / grid.CellSize - rotatedHeight * 0.5f + 0.5f;
            return new GridCoord(FloorClamped(lx), FloorClamped(lz));
        }

        static int FloorClamped(float v)
        {
            const float limit = 1_000_000f;
            if (!(v > -limit)) return -(int)limit; // also catches NaN
            if (v > limit) return (int)limit;
            return Mathf.FloorToInt(v);
        }
    }
}
