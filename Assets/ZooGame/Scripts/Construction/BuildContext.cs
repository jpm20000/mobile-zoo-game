using UnityEngine;
using ZooGame.Data;
using ZooGame.Input;
using ZooGame.World;

namespace ZooGame.Construction
{
    /// <summary>What every construction tool needs; created once by <see cref="BuildModeController"/>.</summary>
    public sealed class BuildContext
    {
        public Camera Camera;
        public ZooGrid Grid;
        public ConstructionModel Model;
        public ConstructionPreview Preview;
        public ConstructionConfig Config;

        public bool TryGround(Vector2 screenPosition, out Vector3 point) =>
            GroundProbe.TryGroundPoint(Camera, Grid, screenPosition, out point);
    }

    /// <summary>Behaviour shared by the stroke-based tools: one claimed finger at a time, nothing happens on taps.</summary>
    public abstract class StrokeToolBase : IBuildTool
    {
        protected const int NoPointer = int.MinValue;

        protected readonly BuildContext Ctx;
        protected int Pointer = NoPointer;

        protected StrokeToolBase(BuildContext ctx) => Ctx = ctx;

        public abstract BuildMode Mode { get; }
        public InputOwner Owner => InputOwner.Construction;
        public abstract bool HasPending { get; }

        /// <summary>A finger that has moved less than this (screen pixels) since pressing has not really started drawing.</summary>
        const float StrokeMovedPixels = 24f;

        Vector2 _pressScreen;
        bool _strokeMoved;

        public bool IsStroking => Pointer != NoPointer;
        public int StrokePointer => Pointer;

        public virtual void Enter() { }

        public void Exit() => Cancel();

        public bool WantsPointer(in PointerSample s)
        {
            if (Pointer != NoPointer || !Ctx.TryGround(s.ScreenPosition, out var ground)) return false;
            Pointer = s.PointerId;
            _pressScreen = s.ScreenPosition;
            _strokeMoved = false;
            BeginStroke(ground);
            return true;
        }

        public void OnPointer(in PointerSample s)
        {
            if (s.PointerId != Pointer) return;
            switch (s.Phase)
            {
                case PointerPhase.Moved:
                    if ((s.ScreenPosition - _pressScreen).sqrMagnitude > StrokeMovedPixels * StrokeMovedPixels) _strokeMoved = true;
                    if (Ctx.TryGround(s.ScreenPosition, out var ground)) MoveStroke(ground);
                    break;
                case PointerPhase.Ended:
                case PointerPhase.Canceled:
                    Pointer = NoPointer;
                    EndStroke();
                    break;
            }
        }

        /// <summary>
        /// A second finger arrived, so the gesture is a camera move, not drawing. The stroke stops without committing.
        /// If the finger had barely moved (an accidental first contact of a two-finger gesture) whatever its press added
        /// is rolled back; a stroke that was really drawing keeps what it has already added to the pending set.
        /// </summary>
        public void AbortStroke()
        {
            if (Pointer == NoPointer) return;
            Pointer = NoPointer;
            OnStrokeAborted(rollBack: !_strokeMoved);
        }

        protected virtual void OnStrokeAborted(bool rollBack) { }

        public void OnTapped(int pointerId, Vector2 screenPosition) { }

        public abstract bool Confirm();
        public abstract void Cancel();

        protected abstract void BeginStroke(Vector3 ground);
        protected abstract void MoveStroke(Vector3 ground);
        protected virtual void EndStroke() { }
    }
}
