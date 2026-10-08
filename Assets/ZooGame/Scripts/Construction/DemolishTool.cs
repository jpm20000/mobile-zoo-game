using System.Collections.Generic;
using UnityEngine;
using ZooGame.Input;
using ZooGame.World;

namespace ZooGame.Construction
{
    /// <summary>
    /// Mark paths, fences and gates for removal by touching or dragging over them (orange preview); Confirm removes
    /// them, Cancel keeps everything. A pointer close to a fence line targets that fence/gate; otherwise it targets
    /// the path in the cell under it. Placed objects are not demolished here (use the placement Delete control), and
    /// removal never touches occupancy.
    /// </summary>
    public sealed class DemolishTool : StrokeToolBase
    {
        const float SampleSpacingCells = 0.25f;

        readonly List<GridCoord> _paths = new List<GridCoord>(32);
        readonly HashSet<GridCoord> _pathSet = new HashSet<GridCoord>();
        readonly List<EdgeCoord> _edges = new List<EdgeCoord>(32);
        readonly HashSet<EdgeCoord> _edgeSet = new HashSet<EdgeCoord>();
        Vector3 _lastGround;
        int _pathsAtStart, _edgesAtStart;

        public DemolishTool(BuildContext ctx) : base(ctx) { }

        public override BuildMode Mode => BuildMode.Demolition;
        public override bool HasPending => _paths.Count + _edges.Count > 0;
        public IReadOnlyList<GridCoord> PendingPaths => _paths;
        public IReadOnlyList<EdgeCoord> PendingEdges => _edges;

        protected override void OnStrokeAborted(bool rollBack)
        {
            if (!rollBack) return;
            while (_paths.Count > _pathsAtStart)
            {
                _pathSet.Remove(_paths[_paths.Count - 1]);
                _paths.RemoveAt(_paths.Count - 1);
            }
            while (_edges.Count > _edgesAtStart)
            {
                _edgeSet.Remove(_edges[_edges.Count - 1]);
                _edges.RemoveAt(_edges.Count - 1);
            }
            Refresh();
        }

        protected override void BeginStroke(Vector3 ground)
        {
            _pathsAtStart = _paths.Count;
            _edgesAtStart = _edges.Count;
            _lastGround = ground;
            Mark(ground);
            Refresh();
        }

        protected override void MoveStroke(Vector3 ground)
        {
            // Finger samples can be far apart; walk between them so nothing is skipped.
            float spacing = SampleSpacingCells * Ctx.Grid.CellSize;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(_lastGround, ground) / spacing));
            for (int i = 1; i <= steps; i++) Mark(Vector3.Lerp(_lastGround, ground, i / (float)steps));
            _lastGround = ground;
            Refresh();
        }

        void Mark(Vector3 world)
        {
            var grid = Ctx.Grid;
            if (EdgeMath.DistanceToNearestEdgeLine(grid, world) <= Ctx.Config.FencePickRadius)
            {
                var edge = EdgeMath.NearestEdge(grid, world);
                if (Ctx.Model.Fences.HasBoundary(edge) && _edgeSet.Add(edge)) _edges.Add(edge);
                return;
            }
            var cell = grid.WorldToGrid(world);
            if (grid.HasPath(cell) && _pathSet.Add(cell)) _paths.Add(cell);
        }

        public override bool Confirm()
        {
            if (!HasPending) return false;
            int removed = Ctx.Model.RemovePaths(_paths) + Ctx.Model.RemoveFences(_edges);
            Clear();
            return removed > 0;
        }

        public override void Cancel()
        {
            Pointer = NoPointer;
            Clear();
        }

        void Clear()
        {
            _paths.Clear();
            _pathSet.Clear();
            _edges.Clear();
            _edgeSet.Clear();
            Refresh();
        }

        void Refresh()
        {
            var p = Ctx.Preview;
            p.Clear();
            var color = Ctx.Config.DemolishPreview;
            for (int i = 0; i < _paths.Count; i++) p.AddCell(_paths[i], color);
            for (int i = 0; i < _edges.Count; i++) p.AddEdge(_edges[i], color);
            p.Apply();
        }
    }
}
