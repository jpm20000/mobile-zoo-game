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
    /// The single place that samples touch (and, in Editor/desktop only, mouse) each frame, republishes raw
    /// pointer samples and feeds the shared <see cref="Gestures"/> recognizer. Consumers must claim a pointer via
    /// <see cref="Ownership"/> before acting on it. Runs after EventSystem (order -1000) so the UI module has
    /// already processed this frame's touches when UI ownership is decided.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class PointerInputSource : MonoBehaviour
    {
        public const int MousePointerId = -1;

        public PointerOwnershipTracker Ownership { get; }

        /// <summary>Shared tap/drag/pinch recognition; subscribe here instead of interpreting raw samples.</summary>
        public PointerGestureRecognizer Gestures { get; }

        /// <summary>Mouse wheel (Editor/desktop only). Args: screen position, notches (positive = scroll up). Never raised over UI.</summary>
        public event Action<Vector2, float> Scrolled;

        public PointerInputSource()
        {
            Ownership = new PointerOwnershipTracker();
            Gestures = new PointerGestureRecognizer(Ownership);
        }

        /// <summary>Raised during Update, once per pointer change. Subscribe/unsubscribe in OnEnable/OnDisable.</summary>
        public event Action<PointerSample> PointerChanged;

        IPointerUiBlocker _uiBlocker;
        readonly System.Collections.Generic.List<IPointerClaimant> _claimants = new System.Collections.Generic.List<IPointerClaimant>(2);

        public void SetUiBlocker(IPointerUiBlocker blocker) => _uiBlocker = blocker;

        /// <summary>Registers a system that may claim a pointer on press, before gestures are recognised. Earlier registrations win.</summary>
        public void AddClaimant(IPointerClaimant claimant)
        {
            if (claimant != null && !_claimants.Contains(claimant)) _claimants.Add(claimant);
        }

        public void RemoveClaimant(IPointerClaimant claimant) => _claimants.Remove(claimant);

        /// <summary>
        /// Takes back a pointer a claimant was using and lets the gesture recogniser see it as if it had just pressed at
        /// <paramref name="screenPosition"/>. Call during a Began claim check for a second finger so the pair becomes a
        /// pinch for the camera.
        /// </summary>
        public void ReturnToGestures(int pointerId, Vector2 screenPosition)
        {
            Ownership.ReleaseAll(pointerId);
            Gestures.Process(new PointerSample(pointerId, PointerPhase.Began, screenPosition, Vector2.zero), Time.unscaledTime);
        }

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

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f && (_uiBlocker == null || !_uiBlocker.IsOverUi(MousePointerId)))
                Scrolled?.Invoke(pos, NormalizeScroll(scroll));

            // Press and release can land in the same frame (a fast click); deliver both, in order.
            bool pressed = mouse.leftButton.wasPressedThisFrame;
            bool released = mouse.leftButton.wasReleasedThisFrame;
            if (pressed)
                Dispatch(new PointerSample(MousePointerId, PointerPhase.Began, pos, Vector2.zero));
            if (released)
                Dispatch(new PointerSample(MousePointerId, PointerPhase.Ended, pos, mouse.delta.ReadValue()));
            else if (!pressed && mouse.leftButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                if (delta != Vector2.zero)
                    Dispatch(new PointerSample(MousePointerId, PointerPhase.Moved, pos, delta));
            }
        }

        // Windows reports 120 units per wheel notch; other platforms report small fractions or whole notches.
        static float NormalizeScroll(float raw) => Mathf.Abs(raw) >= 20f ? raw / 120f : raw;
#endif

        void Dispatch(in PointerSample sample)
        {
            if (sample.Phase == PointerPhase.Began && _uiBlocker != null && _uiBlocker.IsOverUi(sample.PointerId))
                Ownership.TryClaim(sample.PointerId, InputOwner.Ui);

            if (sample.Phase == PointerPhase.Began && Ownership.GetOwner(sample.PointerId) == InputOwner.None)
            {
                for (int i = 0; i < _claimants.Count; i++)
                {
                    var c = _claimants[i];
                    if (c.WantsPointer(sample) && Ownership.TryClaim(sample.PointerId, c.Owner)) break;
                }
            }

            Gestures.Process(sample, Time.unscaledTime);
            PointerChanged?.Invoke(sample);

            if (sample.Phase == PointerPhase.Ended || sample.Phase == PointerPhase.Canceled)
                Ownership.ReleaseAll(sample.PointerId);
        }
    }
}
