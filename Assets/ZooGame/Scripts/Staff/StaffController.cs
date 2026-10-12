using System;
using System.Collections.Generic;
using UnityEngine;
using ZooGame.World;

namespace ZooGame.Staff
{
    /// <summary>
    /// The scene representation of one staff member: a body that walks the cell route it is given and reports arrival.
    /// It decides nothing (that is <see cref="StaffWorkService"/>) and draws no UI. There is no Update:
    /// <see cref="StaffSpawner"/> steps every body once per frame, and walking time is GameClock time, so pause stops
    /// staff and 2x/3x speeds them up.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StaffController : MonoBehaviour, IStaffAgent
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("Degrees per second the body turns towards its heading.")]
        [SerializeField, Min(1f)] float turnSpeed = 540f;

        readonly List<Vector3> _waypoints = new List<Vector3>(32);
        Transform _transform;
        ZooGrid _grid;
        float _speed;
        int _next;
        bool _arrivePending;
        MaterialPropertyBlock _block;

        public StaffInstance Instance { get; private set; }
        public bool IsMoving => _next < _waypoints.Count;

        /// <summary>Raised when the end of the route is reached (or next frame for an empty route).</summary>
        public event Action<StaffController> Arrived;

        void Awake() => _transform = transform;

        /// <param name="speedCellsPerSecond">Walking speed in grid cells per simulation second.</param>
        public void Bind(StaffInstance instance, ZooGrid grid, float speedCellsPerSecond)
        {
            if (_transform == null) _transform = transform;
            Instance = instance;
            _grid = grid;
            _speed = Mathf.Max(0.01f, speedCellsPerSecond) * grid.CellSize;
            Stop();
            _transform.position = instance.Position;
            Tint(RoleColor(instance.Role));
        }

        public void Unbind()
        {
            Stop();
            Instance = null;
            Arrived = null;
        }

        public void Follow(IReadOnlyList<GridCoord> route)
        {
            _waypoints.Clear();
            _next = 0;
            for (int i = 0; i < route.Count; i++) _waypoints.Add(_grid.GridToWorld(route[i]));
            _arrivePending = route.Count == 0;
        }

        public void Stop()
        {
            _waypoints.Clear();
            _next = 0;
            _arrivePending = false;
        }

        /// <summary>Advances the walk by simulation seconds. Called once per frame by the spawner.</summary>
        public void Step(float dt)
        {
            if (Instance == null) return;
            if (_arrivePending)
            {
                _arrivePending = false;
                Arrived?.Invoke(this);
                return;
            }
            if (_next >= _waypoints.Count || !(dt > 0f)) return;

            var pos = _transform.position;
            var heading = Vector3.zero;
            float budget = _speed * dt;
            while (budget > 0f && _next < _waypoints.Count)
            {
                var target = _waypoints[_next];
                var delta = target - pos;
                float dist = delta.magnitude;
                if (dist > 1e-5f) heading = delta / dist;
                if (dist <= budget)
                {
                    pos = target;
                    budget -= dist;
                    _next++;
                }
                else
                {
                    pos += heading * budget;
                    budget = 0f;
                }
            }

            _transform.position = pos;
            Instance.Position = pos;
            if (heading.sqrMagnitude > 1e-6f)
                _transform.rotation = Quaternion.RotateTowards(_transform.rotation, Quaternion.LookRotation(heading), turnSpeed * dt);

            if (_next >= _waypoints.Count)
            {
                _waypoints.Clear();
                _next = 0;
                Arrived?.Invoke(this); // last: the handler may start a new route
            }
        }

        // ---- Look ------------------------------------------------------------------------------------------

        public static Color RoleColor(StaffRole role)
        {
            switch (role)
            {
                case StaffRole.Vendor: return new Color(0.95f, 0.45f, 0.20f);
                case StaffRole.Keeper: return new Color(0.25f, 0.70f, 0.35f);
                case StaffRole.MaintenanceWorker: return new Color(0.95f, 0.85f, 0.20f);
                case StaffRole.Veterinarian: return new Color(0.90f, 0.95f, 0.98f);
                default: return Color.gray;
            }
        }

        void Tint(Color color)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) renderers[i].SetPropertyBlock(_block);
        }
    }
}
