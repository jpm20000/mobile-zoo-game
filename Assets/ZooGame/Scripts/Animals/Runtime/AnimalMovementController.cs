using UnityEngine;

namespace ZooGame.Animals
{
    /// <summary>
    /// Idle / Moving roaming for one animal: pick a spot inside its enclosure, walk there in a straight line, pause,
    /// repeat. Plain C#, ticked by <see cref="AnimalController"/> (which the spawner ticks), so there is no per-animal
    /// Update. A destination is only accepted if sampling the straight path shows it stays inside the enclosure, which
    /// keeps animals in even for L-shaped or notched enclosures. Knows nothing about persistence or the scene.
    /// </summary>
    public sealed class AnimalMovementController
    {
        const int DestinationAttempts = 6;
        const float PathSampleFraction = 0.2f; // of a cell

        readonly IAnimalEnclosureService _enclosures;
        readonly System.Random _rng;

        string _enclosureId;
        float _speed;
        float _minPause, _maxPause;
        float _pauseLeft;
        Vector3 _destination;

        public AnimalMovementController(IAnimalEnclosureService enclosures, System.Random rng)
        {
            _enclosures = enclosures;
            _rng = rng;
        }

        public Vector3 Position { get; private set; }
        public Vector3 Destination => _destination;
        public bool IsMoving { get; private set; }

        /// <summary>Raised when the movement switches between idle and moving.</summary>
        public event System.Action ModeChanged;

        public void Begin(string enclosureId, Vector3 position, float speed, float minPause, float maxPause)
        {
            _enclosureId = enclosureId;
            Position = position;
            _speed = speed;
            _minPause = minPause;
            _maxPause = Mathf.Max(minPause, maxPause);
            SetMoving(false);
            _pauseLeft = RandomPause();
        }

        /// <summary>Stops walking and starts a fresh pause (used when the surroundings changed).</summary>
        public void Halt()
        {
            _pauseLeft = RandomPause();
            SetMoving(false);
        }

        /// <summary>Advances by <paramref name="dt"/>. Returns true if the position changed.</summary>
        public bool Tick(float dt)
        {
            if (string.IsNullOrEmpty(_enclosureId) || dt <= 0f) return false;

            if (!IsMoving)
            {
                _pauseLeft -= dt;
                if (_pauseLeft > 0f) return false;
                if (TryChooseDestination()) SetMoving(true);
                else _pauseLeft = RandomPause();
                return false;
            }

            var to = _destination - Position;
            float step = _speed * dt;
            float distance = to.magnitude;
            if (distance <= step)
            {
                Position = _destination;
                Halt();
                return true;
            }
            // The route was sampled when chosen; this single cell lookup is the hard guarantee at a concave corner.
            var next = Position + to * (step / distance);
            if (!_enclosures.ContainsPosition(_enclosureId, next))
            {
                Halt();
                return false;
            }
            Position = next;
            return true;
        }

        /// <summary>The direction of the current leg, flattened to the ground plane (zero when idle).</summary>
        public Vector3 Heading
        {
            get
            {
                if (!IsMoving) return Vector3.zero;
                var h = _destination - Position;
                h.y = 0f;
                return h;
            }
        }

        /// <summary>True if walking the current leg still stays inside the enclosure (used after enclosures change).</summary>
        public bool IsCurrentLegClear() => IsMoving && IsSegmentInside(Position, _destination);

        bool TryChooseDestination()
        {
            for (int i = 0; i < DestinationAttempts; i++)
            {
                if (!_enclosures.TryGetRandomValidPosition(_enclosureId, out var candidate)) return false;
                if (!IsSegmentInside(Position, candidate)) continue;
                _destination = candidate;
                return true;
            }
            return false;
        }

        bool IsSegmentInside(Vector3 from, Vector3 to)
        {
            float length = Vector3.Distance(from, to);
            float spacing = Mathf.Max(0.05f, _enclosures.CellSize * PathSampleFraction);
            int samples = Mathf.CeilToInt(length / spacing);
            for (int i = 1; i <= samples; i++)
                if (!_enclosures.ContainsPosition(_enclosureId, Vector3.Lerp(from, to, (float)i / samples))) return false;
            return _enclosures.ContainsPosition(_enclosureId, from);
        }

        float RandomPause() => _minPause + (float)_rng.NextDouble() * (_maxPause - _minPause);

        void SetMoving(bool moving)
        {
            if (IsMoving == moving) return;
            IsMoving = moving;
            ModeChanged?.Invoke();
        }
    }
}
