using System.Collections.Generic;
using UnityEngine;
using ZooGame.Data;
using ZooGame.Input;
using ZooGame.World;

namespace ZooGame.Construction
{
    /// <summary>
    /// Draw straight fence (or gate) runs between grid points. Press on a corner, drag, release: the run along the
    /// dominant axis is added to the pending set (while dragging it is shown live). A tap without dragging picks the
    /// single nearest edge instead. Several runs accumulate, so corners are made with consecutive drags, until
    /// Confirm builds every valid edge and recalculates enclosures once. Cancel discards the lot.
    /// Which kind is drawn (fence or gate) comes from <see cref="Definition"/>.
    /// </summary>
    public sealed class FenceTool : StrokeToolBase
    {
        readonly List<FenceEdge> _pending = new List<FenceEdge>(32);
        readonly Dictionary<EdgeCoord, int> _pendingIndex = new Dictionary<EdgeCoord, int>();
        readonly List<EdgeCoord> _run = new List<EdgeCoord>(16);
        GridCoord _startPoint, _endPoint, _squareMin;
        Vector3 _pressWorld;

        public FenceTool(BuildContext ctx) : base(ctx) { }

        public FenceDefinition Definition { get; set; }
        /// <summary>Side length in cells of the one-touch square outline, or 0 for freehand runs.</summary>
        public int SquareSize => Definition != null ? Definition.SquareSize : 0;
        public EdgeKind Kind => Definition != null ? Definition.Kind : EdgeKind.Fence;
        public override BuildMode Mode => BuildMode.FencePlacement;
        public override bool HasPending => _pending.Count > 0;
        public IReadOnlyList<FenceEdge> Pending => _pending;

        protected override void BeginStroke(Vector3 ground)
        {
            _pressWorld = ground;
            _startPoint = _endPoint = EdgeMath.NearestPoint(Ctx.Grid, ground);
            if (SquareSize > 0) _squareMin = SquareMinFor(_endPoint);
            Refresh(includeLiveRun: true);
        }

        // The live run is never part of the pending set until the finger lifts, so aborting just hides it.
        protected override void OnStrokeAborted(bool rollBack)
        {
            _endPoint = _startPoint;
            Refresh(includeLiveRun: false);
        }

        protected override void MoveStroke(Vector3 ground)
        {
            var point = EdgeMath.NearestPoint(Ctx.Grid, ground);
            if (point == _endPoint) return;
            _endPoint = point;
            if (SquareSize > 0) _squareMin = SquareMinFor(point);
            Refresh(includeLiveRun: true);
        }

        protected override void EndStroke()
        {
            if (SquareSize > 0)
            {
                // A square follows the finger; a new touch repositions it instead of stamping another.
                _pending.Clear();
                _pendingIndex.Clear();
                _run.Clear();
                EdgeMath.SquareOutline(_squareMin, SquareSize, _run);
                for (int i = 0; i < _run.Count; i++) AddEdge(_run[i]);
            }
            else if (_endPoint != _startPoint)
            {
                _run.Clear();
                EdgeMath.StraightRun(_startPoint, _endPoint, _run);
                for (int i = 0; i < _run.Count; i++) AddEdge(_run[i]);
            }
            else
            {
                AddEdge(EdgeMath.NearestEdge(Ctx.Grid, _pressWorld)); // a tap picks one edge
            }
            Refresh(includeLiveRun: false);
        }

        GridCoord SquareMinFor(GridCoord centrePoint) => new GridCoord(centrePoint.X - SquareSize / 2, centrePoint.Z - SquareSize / 2);

        void AddEdge(EdgeCoord edge)
        {
            if (!Ctx.Model.Fences.IsValidEdge(edge) || Ctx.Model.Fences.Get(edge) == Kind) return; // off-map, or already built
            var fe = new FenceEdge(edge, Kind);
            if (_pendingIndex.TryGetValue(edge, out int i)) _pending[i] = fe;
            else
            {
                _pendingIndex[edge] = _pending.Count;
                _pending.Add(fe);
            }
        }

        public override bool Confirm()
        {
            if (_pending.Count == 0) return false;
            int built = Ctx.Model.BuildFences(_pending);
            Clear();
            return built > 0;
        }

        public override void Cancel()
        {
            Pointer = NoPointer;
            Clear();
        }

        void Clear()
        {
            _pending.Clear();
            _pendingIndex.Clear();
            Refresh(includeLiveRun: false);
        }

        void Refresh(bool includeLiveRun)
        {
            var p = Ctx.Preview;
            p.Clear();
            bool squareLive = includeLiveRun && SquareSize > 0; // the live square replaces the previous one
            if (!squareLive)
                for (int i = 0; i < _pending.Count; i++) AddPreview(_pending[i].Edge, _pending[i].Kind);
            if (includeLiveRun && (SquareSize > 0 || _endPoint != _startPoint))
            {
                _run.Clear();
                if (SquareSize > 0) EdgeMath.SquareOutline(_squareMin, SquareSize, _run);
                else EdgeMath.StraightRun(_startPoint, _endPoint, _run);
                for (int i = 0; i < _run.Count; i++)
                    if (Ctx.Model.Fences.IsValidEdge(_run[i])) AddPreview(_run[i], Kind);
            }
            p.Apply();
        }

        void AddPreview(EdgeCoord edge, EdgeKind kind) =>
            Ctx.Preview.AddEdge(edge, Ctx.Model.CanBuildFence(edge, kind).IsValid ? Ctx.Config.ValidPreview : Ctx.Config.InvalidPreview);
    }
}
