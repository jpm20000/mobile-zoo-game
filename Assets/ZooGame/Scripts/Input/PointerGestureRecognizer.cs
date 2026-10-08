using System;
using UnityEngine;

namespace ZooGame.Input
{
    public readonly struct GestureSettings
    {
        /// <summary>Screen pixels a pointer must travel from its press position before it counts as a drag.</summary>
        public readonly float DragThresholdPixels;
        /// <summary>A press released within this many seconds, without becoming a drag, is a tap.</summary>
        public readonly float MaxTapSeconds;

        public GestureSettings(float dragThresholdPixels, float maxTapSeconds)
        {
            DragThresholdPixels = dragThresholdPixels;
            MaxTapSeconds = maxTapSeconds;
        }

        public static GestureSettings Default => new GestureSettings(20f, 0.5f);
    }

    /// <summary>
    /// Turns raw <see cref="PointerSample"/>s into tap / drag / pinch events. Gestures are recognised only; they are
    /// not acted on. Consumers (camera now; selection and placement later) must call
    /// <see cref="PointerOwnershipTracker.TryClaim"/> for the pointer id(s) in the *Started event and ignore the
    /// gesture if the claim fails, so one finger never drives two systems.
    ///
    /// Pointers that are already owned when they begin (UI) are never tracked, so UI touches produce no gestures.
    /// At most two pointers are tracked; extra fingers are ignored. A second finger turns a press or drag into a
    /// pinch (the drag is ended first). When a pinch ends, the finger still down is suppressed until it lifts, so
    /// the view never jumps from pinch into pan. Pure C#, allocation-free after construction; time is passed in
    /// so it is testable.
    /// </summary>
    public sealed class PointerGestureRecognizer
    {
        struct Slot
        {
            public bool Active;
            public int Id;
            public Vector2 StartPos;
            public Vector2 Pos;
            public Vector2 LastDragPos;
            public float StartTime;
            public bool Dragging;
            public bool Suppressed;
        }

        const float MinPinchDistance = 1e-3f;

        readonly PointerOwnershipTracker _ownership;
        readonly Slot[] _slots = new Slot[2];
        bool _pinching;
        float _lastPinchDistance;

        public GestureSettings Settings { get; set; } = GestureSettings.Default;

        /// <summary>A short press released without dragging. Args: pointerId, screen position.</summary>
        public event Action<int, Vector2> Tapped;
        /// <summary>Press moved past the drag threshold. Args: pointerId, press position, current position.</summary>
        public event Action<int, Vector2, Vector2> DragStarted;
        /// <summary>Args: pointerId, current position, movement since the previous DragMoved/DragStarted.</summary>
        public event Action<int, Vector2, Vector2> DragMoved;
        /// <summary>Drag finished or was cancelled (also raised when a second finger converts the drag to a pinch).</summary>
        public event Action<int> DragEnded;
        /// <summary>Two fingers down. Args: first id, second id, centre.</summary>
        public event Action<int, int, Vector2> PinchStarted;
        /// <summary>Args: centre, ratio of current finger distance to the previous one (&gt;1 = fingers spreading).</summary>
        public event Action<Vector2, float> PinchChanged;
        public event Action PinchEnded;

        public bool IsPinching => _pinching;

        public PointerGestureRecognizer(PointerOwnershipTracker ownership)
        {
            _ownership = ownership;
        }

        public void Process(in PointerSample s, float time)
        {
            switch (s.Phase)
            {
                case PointerPhase.Began: OnBegan(s, time); break;
                case PointerPhase.Moved: OnMoved(s); break;
                case PointerPhase.Ended: OnEnded(s, time, false); break;
                case PointerPhase.Canceled: OnEnded(s, time, true); break;
            }
        }

        void OnBegan(in PointerSample s, float time)
        {
            if (_ownership.GetOwner(s.PointerId) != InputOwner.None) return; // e.g. UI
            if (IndexOf(s.PointerId) >= 0) return;

            int free = !_slots[0].Active ? 0 : !_slots[1].Active ? 1 : -1;
            if (free < 0) return; // third finger
            int other = 1 - free;
            if (_slots[other].Active && _slots[other].Suppressed) return; // leftover finger from a pinch

            _slots[free] = new Slot
            {
                Active = true,
                Id = s.PointerId,
                StartPos = s.ScreenPosition,
                Pos = s.ScreenPosition,
                LastDragPos = s.ScreenPosition,
                StartTime = time
            };

            if (_slots[other].Active) BeginPinch(other, free);
        }

        void BeginPinch(int first, int second)
        {
            if (_slots[first].Dragging)
            {
                _slots[first].Dragging = false;
                DragEnded?.Invoke(_slots[first].Id);
            }
            _pinching = true;
            _lastPinchDistance = Mathf.Max(Vector2.Distance(_slots[first].Pos, _slots[second].Pos), MinPinchDistance);
            PinchStarted?.Invoke(_slots[first].Id, _slots[second].Id, (_slots[first].Pos + _slots[second].Pos) * 0.5f);
        }

        void OnMoved(in PointerSample s)
        {
            int i = IndexOf(s.PointerId);
            if (i < 0) return;
            _slots[i].Pos = s.ScreenPosition;

            if (_pinching)
            {
                var a = _slots[0].Pos;
                var b = _slots[1].Pos;
                float dist = Mathf.Max(Vector2.Distance(a, b), MinPinchDistance);
                float ratio = dist / _lastPinchDistance;
                _lastPinchDistance = dist;
                if (ratio != 1f) PinchChanged?.Invoke((a + b) * 0.5f, ratio);
                return;
            }

            ref Slot slot = ref _slots[i];
            if (slot.Suppressed) return;

            if (!slot.Dragging)
            {
                float t = Settings.DragThresholdPixels;
                if ((slot.Pos - slot.StartPos).sqrMagnitude <= t * t) return;
                slot.Dragging = true;
                slot.LastDragPos = slot.Pos; // swallow the dead zone so the view does not jump
                DragStarted?.Invoke(slot.Id, slot.StartPos, slot.Pos);
                return;
            }

            var delta = slot.Pos - slot.LastDragPos;
            slot.LastDragPos = slot.Pos;
            if (delta != Vector2.zero) DragMoved?.Invoke(slot.Id, slot.Pos, delta);
        }

        void OnEnded(in PointerSample s, float time, bool canceled)
        {
            int i = IndexOf(s.PointerId);
            if (i < 0) return;
            var slot = _slots[i];
            _slots[i] = default;

            if (_pinching)
            {
                _pinching = false;
                _slots[1 - i].Suppressed = true; // must lift before it can drag or tap again
                PinchEnded?.Invoke();
                return;
            }

            if (slot.Suppressed) return;
            if (slot.Dragging) DragEnded?.Invoke(slot.Id);
            else if (!canceled && time - slot.StartTime <= Settings.MaxTapSeconds) Tapped?.Invoke(slot.Id, s.ScreenPosition);
        }

        int IndexOf(int id)
        {
            if (_slots[0].Active && _slots[0].Id == id) return 0;
            if (_slots[1].Active && _slots[1].Id == id) return 1;
            return -1;
        }
    }
}
