using System;
using UnityEngine;
using ZooGame.Input;

namespace ZooGame.Visitors
{
    /// <summary>
    /// Tap-to-select for visitors, mirroring the animal selection: it listens to the shared gesture recogniser's Tapped
    /// event (a drag is never a tap, and a finger owned by UI or a build tool never arrives here) and picks the nearest
    /// visitor whose projected pick circle contains the tap. A tap on empty ground clears the selection; because the
    /// animal selection does the same, selecting one kind of thing clears the other.
    /// </summary>
    public sealed class VisitorSelectionController : MonoBehaviour
    {
        PointerInputSource _input;
        Camera _camera;
        VisitorSpawner _spawner;
        float _minPickPixels;
        bool _subscribed;

        public VisitorController Selected { get; private set; }

        /// <summary>Extra gate, e.g. false while a construction tool is active. Null means always allowed.</summary>
        public Func<bool> CanSelect { get; set; }

        /// <summary>Raised when the selection changes; the argument is null when cleared.</summary>
        public event Action<VisitorController> SelectionChanged;

        public void Bind(PointerInputSource input, Camera camera, VisitorSpawner spawner, float minPickPixels = 48f)
        {
            Unbind();
            _input = input;
            _camera = camera;
            _spawner = spawner;
            _minPickPixels = minPickPixels;
            _spawner.Despawning += OnDespawning;
            if (isActiveAndEnabled) Subscribe();
        }

        void Unbind()
        {
            if (_spawner == null) return;
            Unsubscribe();
            _spawner.Despawning -= OnDespawning;
            _spawner = null;
            _input = null;
        }

        void OnEnable() => Subscribe();
        void OnDisable() => Unsubscribe();
        void OnDestroy() => Unbind();

        void Subscribe()
        {
            if (_input == null || _subscribed) return;
            _subscribed = true;
            _input.Gestures.Tapped += OnTapped;
        }

        void Unsubscribe()
        {
            if (_input == null || !_subscribed) return;
            _subscribed = false;
            _input.Gestures.Tapped -= OnTapped;
        }

        void OnTapped(int pointerId, Vector2 screenPosition)
        {
            if (_spawner == null || (CanSelect != null && !CanSelect())) return;
            Select(Pick(screenPosition));
        }

        /// <summary>The visitor under a screen position, or null.</summary>
        public VisitorController Pick(Vector2 screenPosition)
        {
            var active = _spawner.Active;
            VisitorController best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < active.Count; i++)
            {
                var v = active[i];
                var point = v.PickPoint;
                var screen = _camera.WorldToScreenPoint(point);
                if (screen.z <= 0f) continue; // behind the camera

                var edge = _camera.WorldToScreenPoint(point + _camera.transform.right * v.PickRadius);
                float radius = Mathf.Max(_minPickPixels, Mathf.Abs(edge.x - screen.x));
                float sqr = ((Vector2)screen - screenPosition).sqrMagnitude;
                if (sqr <= radius * radius && sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = v;
                }
            }
            return best;
        }

        public void Select(VisitorController visitor)
        {
            if (visitor == Selected) return;
            if (Selected != null) Selected.SetSelected(false);
            Selected = visitor;
            if (visitor != null) visitor.SetSelected(true);
            SelectionChanged?.Invoke(visitor);
        }

        public void Clear() => Select(null);

        void OnDespawning(VisitorController visitor)
        {
            if (visitor == Selected) Select(null);
        }
    }
}
