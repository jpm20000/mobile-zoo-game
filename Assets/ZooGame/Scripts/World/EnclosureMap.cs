using System;
using System.Collections.Generic;

namespace ZooGame.World
{
    /// <summary>A closed region bounded by fences and gates.</summary>
    public sealed class Enclosure
    {
        internal Enclosure(int id, GridCoord[] cells, EdgeCoord[] boundary, FenceEdge[] gates)
        {
            Id = id;
            Cells = cells;
            BoundaryEdges = boundary;
            Gates = gates;
        }

        public int Id { get; }
        /// <summary>Every cell inside the boundary.</summary>
        public IReadOnlyList<GridCoord> Cells { get; }
        /// <summary>Fence and gate edges that separate the interior from the outside (interior walls are not included).</summary>
        public IReadOnlyList<EdgeCoord> BoundaryEdges { get; }
        /// <summary>The boundary edges that are gates (for future keeper/visitor access logic).</summary>
        public IReadOnlyList<FenceEdge> Gates { get; }
        /// <summary>Number of cells.</summary>
        public int Area => Cells.Count;
    }

    /// <summary>
    /// Detects closed fence boundaries and records the enclosures they form. Recalculation is incremental: only the
    /// cells next to changed edges are used as flood-fill seeds, and only the enclosures those seeds belong to are
    /// dissolved and rebuilt. Call <see cref="Recalculate"/> when construction is committed, not during preview.
    ///
    /// A region is an enclosure only if flood fill from a seed (stepping across any edge without a fence or gate)
    /// never leaves the grid; the grid border is not a wall. Ids stay stable: a rebuilt region keeps the id of the old
    /// enclosure it overlaps most (adding a gate, extending, or splitting keep one of the old ids), and only genuinely
    /// new regions get a fresh id. Ids are never reused after an enclosure disappears.
    /// </summary>
    public sealed class EnclosureMap
    {
        struct Region
        {
            public GridCoord[] Cells;
            public int BestId;
            public int BestOverlap;
        }

        readonly ZooGrid _grid;
        readonly FenceMap _fences;
        readonly Dictionary<int, Enclosure> _enclosures = new Dictionary<int, Enclosure>();
        readonly Dictionary<EdgeCoord, int> _edgeOwner = new Dictionary<EdgeCoord, int>();
        int _nextId = 1;

        // Reusable scratch (no per-call allocation beyond results).
        readonly int[] _stamp;
        int _stampValue;
        readonly int[] _stack;
        readonly List<GridCoord> _fill = new List<GridCoord>(256);
        readonly Dictionary<int, int> _overlap = new Dictionary<int, int>();

        public EnclosureMap(ZooGrid grid, FenceMap fences)
        {
            _grid = grid;
            _fences = fences;
            _stamp = new int[grid.CellCount];
            _stack = new int[grid.CellCount];
        }

        public int Count => _enclosures.Count;
        public Dictionary<int, Enclosure>.ValueCollection Enclosures => _enclosures.Values;

        /// <summary>Raised after a recalculation that created, removed or rebuilt at least one enclosure.</summary>
        public event Action Changed;

        public bool TryGet(int id, out Enclosure enclosure) => _enclosures.TryGetValue(id, out enclosure);

        /// <summary>The enclosure containing a cell, or null.</summary>
        public Enclosure GetAt(GridCoord cell) =>
            _enclosures.TryGetValue(_grid.GetCell(cell).EnclosureId, out var e) ? e : null;

        /// <summary>
        /// Is this fence/gate part of a closed boundary? False for fences that only belong to an open layout
        /// (and for empty edges).
        /// </summary>
        public bool IsClosedBoundary(EdgeCoord edge, out int enclosureId) => _edgeOwner.TryGetValue(edge, out enclosureId);

        /// <summary>Re-evaluates the enclosures touched by the given changed edges.</summary>
        public void Recalculate(IReadOnlyList<EdgeCoord> changedEdges)
        {
            if (changedEdges == null || changedEdges.Count == 0) return;

            // 1. Seed cells and the enclosures they currently belong to.
            var seeds = new List<GridCoord>(changedEdges.Count * 2);
            var dissolved = new HashSet<int>();
            for (int i = 0; i < changedEdges.Count; i++)
            {
                AddSeed(changedEdges[i].CellA, seeds, dissolved);
                AddSeed(changedEdges[i].CellB, seeds, dissolved);
            }
            if (seeds.Count == 0) return;

            // 2. Flood from each seed; keep closed regions (old ids are still on the grid for overlap counting).
            NewStamp();
            var regions = new List<Region>();
            for (int i = 0; i < seeds.Count; i++)
            {
                if (_stamp[Index(seeds[i])] == _stampValue) continue;
                if (!Flood(seeds[i])) continue;
                regions.Add(MakeRegion());
            }

            bool changed = dissolved.Count > 0 || regions.Count > 0;
            if (!changed) return;

            // 3. Dissolve the affected old enclosures.
            foreach (int id in dissolved) RemoveEnclosure(id);

            // 4. Give each region an id: the most-overlapping old id wins, others get fresh ids.
            var order = new List<int>(regions.Count);
            for (int i = 0; i < regions.Count; i++) order.Add(i);
            order.Sort((a, b) => regions[b].BestOverlap.CompareTo(regions[a].BestOverlap));
            var taken = new HashSet<int>();
            for (int k = 0; k < order.Count; k++)
            {
                var r = regions[order[k]];
                int id = r.BestId != 0 && dissolved.Contains(r.BestId) && taken.Add(r.BestId) ? r.BestId : _nextId++;
                if (id >= _nextId) _nextId = id + 1;
                AddEnclosure(id, r.Cells);
            }

            Changed?.Invoke();
        }

        // ---- Internals -------------------------------------------------------------------------------------

        void AddSeed(GridCoord cell, List<GridCoord> seeds, HashSet<int> dissolved)
        {
            if (!_grid.IsInsideGrid(cell)) return;
            seeds.Add(cell);
            int id = _grid.GetCell(cell).EnclosureId;
            if (id != GridCell.NoEnclosure) dissolved.Add(id);
        }

        /// <summary>Fills <see cref="_fill"/> from a seed. Returns true if the region is closed.</summary>
        bool Flood(GridCoord seed)
        {
            _fill.Clear();
            int sp = 0;
            int si = Index(seed);
            _stamp[si] = _stampValue;
            _stack[sp++] = si;
            bool closed = true;
            while (sp > 0)
            {
                int ci = _stack[--sp];
                var c = new GridCoord(ci % _grid.Width, ci / _grid.Width);
                _fill.Add(c);
                for (int d = 0; d < 4; d++)
                {
                    var dir = (Direction4)d;
                    if (_fences.GetSide(c, dir).IsBoundary()) continue;
                    var n = dir.Step(c);
                    if (!_grid.IsInsideGrid(n)) { closed = false; continue; } // keep marking cells so other seeds skip them
                    int ni = Index(n);
                    if (_stamp[ni] == _stampValue) continue;
                    _stamp[ni] = _stampValue;
                    _stack[sp++] = ni;
                }
            }
            return closed;
        }

        Region MakeRegion()
        {
            var cells = _fill.ToArray();
            // Which old enclosure does this region overlap most? (old ids are still on the grid at this point)
            _overlap.Clear();
            int bestId = 0, bestCount = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                int id = _grid.GetCell(cells[i]).EnclosureId;
                if (id == GridCell.NoEnclosure) continue;
                _overlap.TryGetValue(id, out int n);
                _overlap[id] = ++n;
                if (n > bestCount) { bestCount = n; bestId = id; }
            }
            return new Region { Cells = cells, BestId = bestId, BestOverlap = bestCount };
        }

        void AddEnclosure(int id, GridCoord[] cells)
        {
            for (int i = 0; i < cells.Length; i++) _grid.SetEnclosure(cells[i], id);

            var boundary = new List<EdgeCoord>();
            var gates = new List<FenceEdge>();
            for (int i = 0; i < cells.Length; i++)
            for (int d = 0; d < 4; d++)
            {
                var dir = (Direction4)d;
                var kind = _fences.GetSide(cells[i], dir);
                if (!kind.IsBoundary()) continue;
                var other = dir.Step(cells[i]);
                if (_grid.IsInsideGrid(other) && _grid.GetCell(other).EnclosureId == id) continue; // interior wall
                var edge = EdgeCoord.OfCell(cells[i], dir);
                boundary.Add(edge);
                _edgeOwner[edge] = id;
                if (kind == EdgeKind.Gate) gates.Add(new FenceEdge(edge, kind));
            }
            _enclosures[id] = new Enclosure(id, cells, boundary.ToArray(), gates.ToArray());
        }

        void RemoveEnclosure(int id)
        {
            if (!_enclosures.TryGetValue(id, out var e)) return;
            for (int i = 0; i < e.Cells.Count; i++) _grid.SetEnclosure(e.Cells[i], GridCell.NoEnclosure);
            for (int i = 0; i < e.BoundaryEdges.Count; i++) _edgeOwner.Remove(e.BoundaryEdges[i]);
            _enclosures.Remove(id);
        }

        void NewStamp()
        {
            if (++_stampValue == int.MaxValue)
            {
                Array.Clear(_stamp, 0, _stamp.Length);
                _stampValue = 1;
            }
        }

        int Index(GridCoord c) => c.Z * _grid.Width + c.X;
    }
}
