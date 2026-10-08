using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ZooGame.Input
{
    public enum PointerPhase
    {
        Began,
        Moved,
        Ended,
        Canceled
    }

    public readonly struct PointerSample
    {
        public readonly int PointerId;
        public readonly PointerPhase Phase;
        public readonly Vector2 ScreenPosition;
        public readonly Vector2 Delta;

        public PointerSample(int pointerId, PointerPhase phase, Vector2 screenPosition, Vector2 delta)
        {
            PointerId = pointerId;
            Phase = phase;
            ScreenPosition = screenPosition;
            Delta = delta;
        }
    }

    /// <summary>Answers whether a pointer is over interactive UI. Implemented by the UI layer.</summary>
    public interface IPointerUiBlocker
    {
        bool IsOverUi(int pointerId);
    }

    /// <summary>
    /// The single place that samples touch (and, in Editor/desktop only, mouse) each frame and republishes raw
    /// pointer samples. Gesture recognition (tap, drag, pan, pinch) is built on top of this in later milestones.
    /// Consumers must claim a pointer via <see cref="Ownership"/> before acting on it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PointerInputSource : MonoBehaviour
    {
        public const int MousePointerId = -1;

        public PointerOwnershipTracker Ownership { get; } = new PointerOwnershipTracker();

        /// <summary>Raised during Update, once per pointer change. Subscribe/unsubscribe in OnEnable/OnDisable.</summary>
        public event Action<PointerSample> PointerChanged;

        IPointerUiBlocker _uiBlocker;

        public void SetUiBlocker(IPointerUiBlocker blocker) => _uiBlocker = blocker;

        void OnEnable() => EnhancedTouchSupport.Enable();

        void OnDisable() => EnhancedTouchSupport.Disable();

        void Update()
        {
            var touches = Touch.activeTouches;
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                PointerPhase phase;
                switch (t.phase)
                {
                    case TouchPhase.Began: phase = PointerPhase.Began; break;
                    case TouchPhase.Moved: phase = PointerPhase.Moved; break;
                    case TouchPhase.Ended: phase = PointerPhase.Ended; break;
                    case TouchPhase.Canceled: phase = PointerPhase.Canceled; break;
                    default: continue; // Stationary
                }
                Dispatch(new PointerSample(t.touchId, phase, t.screenPosition, t.delta));
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            // Development convenience only; gameplay must never require a mouse.
            if (touches.Count == 0) PollMouse();
#endif
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        void PollMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            var pos = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame)
                Dispatch(new PointerSample(MousePointerId, PointerPhase.Began, pos, Vector2.zero));
            else if (mouse.leftButton.wasReleasedThisFrame)
                Dispatch(new PointerSample(MousePointerId, PointerPhase.Ended, pos, mouse.delta.ReadValue()));
            else if (mouse.leftButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                if (delta != Vector2.zero)
                    Dispatch(new PointerSample(MousePointerId, PointerPhase.Moved, pos, delta));
            }
        }
#endif

        void Dispatch(in PointerSample sample)
        {
            if (sample.Phase == PointerPhase.Began && _uiBlocker != null && _uiBlocker.IsOverUi(sample.PointerId))
                Ownership.TryClaim(sample.PointerId, InputOwner.Ui);

            PointerChanged?.Invoke(sample);

            if (sample.Phase == PointerPhase.Ended || sample.Phase == PointerPhase.Canceled)
                Ownership.ReleaseAll(sample.PointerId);
        }
    }
}
