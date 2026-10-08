using System;
using UnityEngine;
using ZooGame.Input;

namespace ZooGame.Animals
{
    /// <summary>
    /// Tap-to-select for animals. It listens to the shared gesture recogniser's Tapped event, so a drag is never a tap
    /// (camera panning is unaffected) and a finger owned by UI or a build tool never arrives here. The nearest animal
    /// whose projected pick circle contains the tap is selected; tapping empty ground clears the selection.
    /// Selection is only allowed while <see cref="CanSelect"/> says so (the scene wires it to "no build tool active").
    /// </summary>
    public sealed class AnimalSelectionController : MonoBehaviour
    {
        PointerInputSource _input;
        Camera _camera;
        AnimalSpawner _spawner;
        float _minPickPixels;
        bool _subscribed;

        public AnimalController Selected { get; private set; }

        /// <summary>Extra gate, e.g. false while a construction tool is active. Null means always allowed.</summary>
        public Func<bool> CanSelect { get; set; }

        /// <summary>Raised when the selection changes; the argument is null when cleared.</summary>
        public event Action<AnimalController> SelectionChanged;

        public void Bind(PointerInputSource input, Camera camera, AnimalSpawner spawner, float minPickPixels = 48f)
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

        /// <summary>The animal under a screen position, or null.</summary>
        public AnimalController Pick(Vector2 screenPosition)
        {
            var active = _spawner.Active;
            AnimalController best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < active.Count; i++)
            {
                var sel = active[i].Selectable;
                if (sel == null) continue;
                var point = sel.PickPoint;
                var screen = _camera.WorldToScreenPoint(point);
                if (screen.z <= 0f) continue; // behind the camera

                var edge = _camera.WorldToScreenPoint(point + _camera.transform.right * sel.PickRadius);
                float radius = Mathf.Max(_minPickPixels, Mathf.Abs(edge.x - screen.x));
                float sqr = ((Vector2)screen - screenPosition).sqrMagnitude;
                if (sqr <= radius * radius && sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = active[i];
                }
            }
            return best;
        }

        public void Select(AnimalController animal)
        {
            if (animal == Selected) return;
            if (Selected != null && Selected.Selectable != null) Selected.Selectable.SetSelected(false);
            Selected = animal;
            if (animal != null && animal.Selectable != null) animal.Selectable.SetSelected(true);
            SelectionChanged?.Invoke(animal);
        }

        public void Clear() => Select(null);

        void OnDespawning(AnimalController animal)
        {
            if (animal == Selected) Select(null);
        }
    }
}
