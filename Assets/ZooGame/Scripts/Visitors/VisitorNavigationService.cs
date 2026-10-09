using System;
using System.Collections.Generic;
using ZooGame.World;

namespace ZooGame.Visitors
{
    /// <summary>
    /// The visitor path network. Visitors may stand only on path cells that are not inside an enclosure, and may not
    /// step across a fence or gate edge. The graph is never stored explicitly: walkability is read from the M3 grid, and
    /// the only cached data is a connected-component label per cell, rebuilt lazily (one flood pass over the grid) the
    /// first time it is queried after a path or fence change. That makes "can A reach B?" an O(1) comparison, so
    /// destination choice never pathfinds; a real route (breadth-first, reusing scratch arrays) is computed only when a
    /// visitor's destination changes, or when the network changed under a walking visitor.
    /// </summary>
    public sealed class VisitorNavigationService : IDisposable
    {
        readonly ZooGrid _grid;
        readonly ConstructionModel _model;
        readonly int[] _component;   // 0 = not walkable; otherwise the connected-component label
        readonly int[] _stamp;       // BFS visited marks
        readonly int[] _targetMark;  // BFS target marks
        readonly int[] _parent;
        readonly int[] _queue;
        readonly List<GridCoord> _single = new List<GridCoord>(1);
        int _stampValue;
        bool _dirty = true;

        public VisitorNavigationService(ZooGrid grid, ConstructionModel model)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            int n = grid.CellCount;
            _component = new int[n];
            _stamp = new int[n];
            _targetMark = new int[n];
            _parent = new int[n];
            _queue = new int[n];
            _grid.PathsChanged += OnNetworkChanged;
            _model.FencesChanged += OnNetworkChanged;
        }

        /// <summary>Raised when a path or fence change may have altered what is reachable (routes should be re-checked).</summary>
        public event Action GraphChanged;

        /// <summary>How many times the component labels were rebuilt (diagnostics and tests).</summary>
        public int GraphRebuildCount { get; private set; }

        /// <summary>How many routes were searched for (diagnostics and tests): proves nothing pathfinds per frame.</summary>
        public int RouteRequestCount { get; private set; }

        public int RouteFailureCount { get; private set; }

        public ZooGrid Grid => _grid;

        public void Dispose()
        {
            _grid.PathsChanged -= OnNetworkChanged;
            _model.FencesChanged -= OnNetworkChanged;
        }

        void OnNetworkChanged()
        {
            _dirty = true;
            GraphChanged?.Invoke();
        }

        // ---- Graph queries ---------------------------------------------------------------------------------

        /// <summary>A path cell outside every enclosure.</summary>
        public bool IsWalkable(GridCoord c)
        {
            if (!_grid.IsInsideGrid(c)) return false;
            var cell = _grid.GetCell(c);
            return cell.HasPath && cell.EnclosureId == GridCell.NoEnclosure;
        }

        /// <summary>Connected-component label of a cell: 0 when it is not walkable.</summary>
        public int ComponentOf(GridCoord c)
        {
            if (!_grid.IsInsideGrid(c)) return 0;
            EnsureBuilt();
            return _component[Index(c)];
        }

        public bool AreConnected(GridCoord a, GridCoord b)
        {
            int ca = ComponentOf(a);
            return ca != 0 && ca == ComponentOf(b);
        }

        /// <summary>Is any of the target cells on the same connected network as <paramref name="from"/>?</summary>
        public bool IsConnectedToAny(GridCoord from, IReadOnlyList<GridCoord> targets)
        {
            int c = ComponentOf(from);
            if (c == 0 || targets == null) return false;
            for (int i = 0; i < targets.Count; i++)
                if (ComponentOf(targets[i]) == c) return true;
            return false;
        }

        // ---- Routes ----------------------------------------------------------------------------------------

        public bool TryFindRoute(GridCoord from, GridCoord to, List<GridCoord> route)
        {
            _single.Clear();
            _single.Add(to);
            return TryFindRoute(from, _single, out _, route);
        }

        /// <summary>
        /// Shortest route (in steps) from <paramref name="from"/> to the nearest reachable cell of
        /// <paramref name="targets"/>. <paramref name="route"/> is cleared and receives every cell to walk through, in
        /// order, excluding the start and including the reached cell (empty when already there). Returns false, with an
        /// empty route, when there is none.
        /// </summary>
        public bool TryFindRoute(GridCoord from, IReadOnlyList<GridCoord> targets, out GridCoord reached, List<GridCoord> route)
        {
            route.Clear();
            reached = from;
            RouteRequestCount++;
            if (targets == null || targets.Count == 0 || !IsConnectedToAny(from, targets))
            {
                RouteFailureCount++;
                return false;
            }

            NewStamp();
            int comp = ComponentOf(from);
            for (int i = 0; i < targets.Count; i++)
                if (_grid.IsInsideGrid(targets[i]) && ComponentOf(targets[i]) == comp) _targetMark[Index(targets[i])] = _stampValue;

            int start = Index(from);
            if (_targetMark[start] == _stampValue) return true;

            int head = 0, tail = 0;
            _queue[tail++] = start;
            _stamp[start] = _stampValue;
            _parent[start] = -1;
            while (head < tail)
            {
                int ci = _queue[head++];
                var c = new GridCoord(ci % _grid.Width, ci / _grid.Width);
                for (int d = 0; d < 4; d++)
                {
                    var dir = (Direction4)d;
                    var n = dir.Step(c);
                    if (!CanStep(c, dir, n)) continue;
                    int ni = Index(n);
                    if (_stamp[ni] == _stampValue) continue;
                    _stamp[ni] = _stampValue;
                    _parent[ni] = ci;
                    if (_targetMark[ni] == _stampValue)
                    {
                        reached = n;
                        for (int at = ni; at != start; at = _parent[at])
                            route.Add(new GridCoord(at % _grid.Width, at / _grid.Width));
                        route.Reverse();
                        return true;
                    }
                    _queue[tail++] = ni;
                }
            }
            RouteFailureCount++;
            return false; // unreachable in practice: the component check above already agreed
        }

        // ---- Internals -------------------------------------------------------------------------------------

        bool CanStep(GridCoord from, Direction4 dir, GridCoord to) =>
            IsWalkable(to) && !_model.Fences.GetSide(from, dir).IsBoundary();

        void EnsureBuilt()
        {
            if (!_dirty) return;
            _dirty = false;
            GraphRebuildCount++;
            Array.Clear(_component, 0, _component.Length);
            int label = 0;
            for (int z = 0; z < _grid.Depth; z++)
            for (int x = 0; x < _grid.Width; x++)
            {
                var start = new GridCoord(x, z);
                int si = Index(start);
                if (_component[si] != 0 || !IsWalkable(start)) continue;
                label++;
                Label(si, label);
            }
        }

        void Label(int start, int label)
        {
            int head = 0, tail = 0;
            _queue[tail++] = start;
            _component[start] = label;
            while (head < tail)
            {
                int ci = _queue[head++];
                var c = new GridCoord(ci % _grid.Width, ci / _grid.Width);
                for (int d = 0; d < 4; d++)
                {
                    var dir = (Direction4)d;
                    var n = dir.Step(c);
                    if (!CanStep(c, dir, n)) continue;
                    int ni = Index(n);
                    if (_component[ni] != 0) continue;
                    _component[ni] = label;
                    _queue[tail++] = ni;
                }
            }
        }

        void NewStamp()
        {
            if (++_stampValue == int.MaxValue)
            {
                Array.Clear(_stamp, 0, _stamp.Length);
                Array.Clear(_targetMark, 0, _targetMark.Length);
                _stampValue = 1;
            }
        }

        int Index(GridCoord c) => c.Z * _grid.Width + c.X;
    }
}
