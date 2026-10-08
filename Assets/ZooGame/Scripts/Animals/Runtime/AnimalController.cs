using System;
using UnityEngine;

namespace ZooGame.Animals
{
    /// <summary>
    /// The scene-side representation of one spawned animal: what it is doing right now. It is bound to an
    /// <see cref="AnimalInstance"/> (identity and persistent state) and an <see cref="AnimalDefinition"/> (species
    /// config) and mirrors its position back into the instance. It does not own the animal list, create ids, change the
    /// definition or draw any UI. Ticked by <see cref="AnimalSpawner"/>; there is no Update here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimalController : MonoBehaviour
    {
        [SerializeField, Min(0f)] float minPauseSeconds = 1f;
        [SerializeField, Min(0f)] float maxPauseSeconds = 4f;
        [Tooltip("Degrees per second the body turns towards its heading.")]
        [SerializeField, Min(1f)] float turnSpeed = 360f;

        Transform _transform;
        IAnimalEnclosureService _enclosures;
        AnimalMovementController _movement;
        AnimalState _state;

        public AnimalInstance Instance { get; private set; }
        public AnimalDefinition Definition { get; private set; }
        public AnimalSelectable Selectable { get; private set; }
        public AnimalMovementController Movement => _movement;

        public AnimalState State => _state;

        /// <summary>False when the animal belongs to an enclosure that is open/gone or no longer contains it. Animals without an enclosure are not flagged.</summary>
        public bool EnclosureValid { get; private set; } = true;

        /// <summary>Raised when <see cref="State"/> or <see cref="EnclosureValid"/> changes.</summary>
        public event Action<AnimalController> StateChanged;

        void Awake()
        {
            _transform = transform;
            Selectable = GetComponent<AnimalSelectable>();
            if (Selectable != null) Selectable.Controller = this;
        }

        public void Bind(AnimalInstance instance, AnimalDefinition definition, IAnimalEnclosureService enclosures, System.Random rng)
        {
            if (_transform == null) Awake();
            Instance = instance;
            Definition = definition;
            _enclosures = enclosures;
            _movement = new AnimalMovementController(enclosures, rng);
            _movement.ModeChanged += OnModeChanged;
            _transform.position = instance.Position;

            _movement.Begin(instance.EnclosureId, instance.Position, definition.BaseMoveSpeed, minPauseSeconds, maxPauseSeconds);
            EnclosureValid = ComputeEnclosureValid();
            _state = EnclosureValid ? AnimalState.Idle : AnimalState.Stranded;
        }

        /// <summary>Called once per frame by the spawner.</summary>
        public void Tick(float dt)
        {
            if (_state == AnimalState.Stranded || _movement == null) return;
            if (!_movement.Tick(dt)) return;

            var p = _movement.Position;
            _transform.position = p;
            Instance.Position = p;
            var heading = _movement.Heading;
            if (heading.sqrMagnitude > 1e-6f)
                _transform.rotation = Quaternion.RotateTowards(_transform.rotation, Quaternion.LookRotation(heading), turnSpeed * dt);
        }

        /// <summary>Re-checks the enclosure after construction changed (or after the animal was reassigned). Never deletes the animal.</summary>
        public void RefreshEnclosure()
        {
            if (Instance == null) return;
            bool valid = ComputeEnclosureValid();
            bool changed = valid != EnclosureValid;
            EnclosureValid = valid;

            if (!valid)
            {
                _movement.Halt();
                SetState(AnimalState.Stranded, changed);
                return;
            }

            if (_state == AnimalState.Stranded)
            {
                _movement.Begin(Instance.EnclosureId, Instance.Position, Definition.BaseMoveSpeed, minPauseSeconds, maxPauseSeconds);
                SetState(AnimalState.Idle, true);
            }
            else if (_movement.IsMoving && !_movement.IsCurrentLegClear())
            {
                _movement.Halt(); // the route is no longer inside; ModeChanged updates the state
            }
            else if (changed) StateChanged?.Invoke(this);
        }

        bool ComputeEnclosureValid() =>
            !Instance.HasEnclosure
            || (_enclosures.IsValidEnclosure(Instance.EnclosureId) && _enclosures.ContainsPosition(Instance.EnclosureId, Instance.Position));

        void OnModeChanged()
        {
            if (_state == AnimalState.Stranded) return;
            SetState(_movement.IsMoving ? AnimalState.Moving : AnimalState.Idle, true);
        }

        void SetState(AnimalState state, bool notify)
        {
            if (_state == state)
            {
                if (notify) StateChanged?.Invoke(this);
                return;
            }
            _state = state;
            StateChanged?.Invoke(this);
        }

        void OnDestroy()
        {
            if (_movement != null) _movement.ModeChanged -= OnModeChanged;
        }
    }
}
