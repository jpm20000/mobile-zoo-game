using System.Collections.Generic;
using UnityEngine;
using ZooGame.Data;
using ZooGame.Input;
using ZooGame.World;

namespace ZooGame.Construction
{
    /// <summary>
    /// Paint paths by dragging. The finger's cells are joined by simple orthogonal routes so fast drags never leave
    /// gaps or go diagonal. Every touched cell without a path becomes a pending cell shown green (buildable) or red
    /// (rejected by validation); nothing is built until Confirm, which builds the valid ones. Cancel discards all.
    /// </summary>
    public sealed class PathTool : StrokeToolBase
    {
        readonly List<GridCoord> _pending = new List<GridCoord>(64);
        readonly HashSet<GridCoord> _pendingSet = new HashSet<GridCoord>();
        readonly List<GridCoord> _route = new List<GridCoord>(16);
        GridCoord _last;
        int _strokeStart;

        public PathTool(BuildContext ctx) : base(ctx) { }

        public PathDefinition Definition { get; set; }
        public override BuildMode Mode => BuildMode.PathPlacement;
        public override bool HasPending => _pending.Count > 0;
        public IReadOnlyList<GridCoord> Pending => _pending;

        protected override void OnStrokeAborted(bool rollBack)
        {
            if (!rollBack) return;
            while (_pending.Count > _strokeStart)
            {
                _pendingSet.Remove(_pending[_pending.Count - 1]);
                _pending.RemoveAt(_pending.Count - 1);
            }
            Refresh();
        }

        protected override void BeginStroke(Vector3 ground)
        {
            _strokeStart = _pending.Count;
            _last = Ctx.Grid.WorldToGrid(ground);
            AddCell(_last);
            Refresh();
        }

        protected override void MoveStroke(Vector3 ground)
        {
            var cell = Ctx.Grid.WorldToGrid(ground);
            if (cell == _last) return;
            _route.Clear();
            EdgeMath.OrthogonalRoute(_last, cell, _route);
            for (int i = 0; i < _route.Count; i++) AddCell(_route[i]);
            _last = cell;
            Refresh();
        }

        void AddCell(GridCoord cell)
        {
            if (!Ctx.Grid.IsInsideGrid(cell) || Ctx.Grid.HasPath(cell)) return;
            if (_pendingSet.Add(cell)) _pending.Add(cell);
        }

        public override bool Confirm()
        {
            if (_pending.Count == 0) return false;
            int built = Ctx.Model.BuildPaths(_pending);
            ClearPending();
            return built > 0;
        }

        public override void Cancel()
        {
            Pointer = NoPointer;
            ClearPending();
        }

        void ClearPending()
        {
            _pending.Clear();
            _pendingSet.Clear();
            Refresh();
        }

        void Refresh()
        {
            var p = Ctx.Preview;
            p.Clear();
            for (int i = 0; i < _pending.Count; i++)
                p.AddCell(_pending[i], Ctx.Model.CanBuildPath(_pending[i]).IsValid ? Ctx.Config.ValidPreview : Ctx.Config.InvalidPreview);
            p.Apply();
        }
    }
}
