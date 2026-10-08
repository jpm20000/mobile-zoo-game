using System;

namespace ZooGame.World
{
    /// <summary>
    /// Fences and gates stored as cell-edge data, separate from the cell grid. One byte per edge in two flat arrays
    /// (X-axis edges and Z-axis edges), so a shared edge is one logical segment with a single owner: reading it from
    /// either neighbouring cell yields the same <see cref="EdgeCoord"/>. No GameObject per edge. Edges on the map
    /// border are valid. Invalid coordinates are handled safely (queries return None / false).
    /// </summary>
    public sealed class FenceMap
    {
        readonly int _width, _depth;
        readonly EdgeKind[] _xEdges; // (Width) x (Depth + 1), index z * Width + x
        readonly EdgeKind[] _zEdges; // (Width + 1) x (Depth), index z * (Width + 1) + x
        int _count;

        public FenceMap(int width, int depth)
        {
            if (width <= 0 || depth <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            _width = width;
            _depth = depth;
            _xEdges = new EdgeKind[width * (depth + 1)];
            _zEdges = new EdgeKind[(width + 1) * depth];
        }

        public int Width => _width;
        public int Depth => _depth;
        /// <summary>Number of edges currently holding a fence or gate.</summary>
        public int Count => _count;

        /// <summary>Raised after an edge actually changed kind. Args: edge, previous kind, new kind.</summary>
        public event Action<EdgeCoord, EdgeKind, EdgeKind> EdgeChanged;

        public bool IsValidEdge(EdgeCoord e) => e.Axis == EdgeAxis.X
            ? (uint)e.X < (uint)_width && (uint)e.Z <= (uint)_depth
            : (uint)e.X <= (uint)_width && (uint)e.Z < (uint)_depth;

        public EdgeKind Get(EdgeCoord e) => IsValidEdge(e) ? Array(e)[Index(e)] : EdgeKind.None;

        public bool HasBoundary(EdgeCoord e) => Get(e).IsBoundary();

        public bool IsGate(EdgeCoord e) => Get(e) == EdgeKind.Gate;

        /// <summary>The edge on a cell's side, valid for any cell coordinate (even outside the grid).</summary>
        public EdgeKind GetSide(GridCoord cell, Direction4 side) => Get(EdgeCoord.OfCell(cell, side));

        /// <summary>
        /// Sets an edge's kind (<see cref="EdgeKind.None"/> removes). Returns false only for an invalid edge;
        /// setting the same kind again is a successful no-op that raises no event. Placing a gate on a fence replaces it.
        /// </summary>
        public bool Set(EdgeCoord e, EdgeKind kind)
        {
            if (!IsValidEdge(e)) return false;
            var array = Array(e);
            int i = Index(e);
            var before = array[i];
            if (before == kind) return true;
            array[i] = kind;
            if (before == EdgeKind.None) _count++;
            else if (kind == EdgeKind.None) _count--;
            EdgeChanged?.Invoke(e, before, kind);
            return true;
        }

        public bool Remove(EdgeCoord e) => Get(e) != EdgeKind.None && Set(e, EdgeKind.None);

        /// <summary>Visits every edge holding a fence or gate (row-major per axis). Allocation-free.</summary>
        public void ForEach(Action<EdgeCoord, EdgeKind> visit)
        {
            for (int z = 0; z <= _depth; z++)
            for (int x = 0; x < _width; x++)
            {
                var k = _xEdges[z * _width + x];
                if (k != EdgeKind.None) visit(new EdgeCoord(EdgeAxis.X, x, z), k);
            }
            for (int z = 0; z < _depth; z++)
            for (int x = 0; x <= _width; x++)
            {
                var k = _zEdges[z * (_width + 1) + x];
                if (k != EdgeKind.None) visit(new EdgeCoord(EdgeAxis.Z, x, z), k);
            }
        }

        EdgeKind[] Array(EdgeCoord e) => e.Axis == EdgeAxis.X ? _xEdges : _zEdges;

        int Index(EdgeCoord e) => e.Axis == EdgeAxis.X ? e.Z * _width + e.X : e.Z * (_width + 1) + e.X;
    }
}
