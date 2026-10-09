using System;
using System.Collections.Generic;
using UnityEngine;
using ZooGame.World;

namespace ZooGame.Visitors
{
    /// <summary>
    /// The scene representation of one visitor: a body that walks the cell route it is given and reports arrival.
    /// It decides nothing (that is <see cref="VisitorDecisionService"/>), simulates nothing and draws no UI. There is no
    /// Update: <see cref="VisitorSpawner"/> steps every active visitor once per frame, and walking time is GameClock
    /// time, so pause stops visitors and 2x/3x speeds them up. Instances are pooled; <see cref="Bind"/> reuses one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VisitorController : MonoBehaviour, IVisitorAgent
    {
        [SerializeField, Min(0.05f)] float pickRadius = 0.5f;
        [SerializeField, Min(0f)] float pickHeight = 0.5f;
        [Tooltip("Degrees per second the body turns towards its heading.")]
        [SerializeField, Min(1f)] float turnSpeed = 540f;
        [Tooltip("Optional child shown while selected.")]
        [SerializeField] GameObject selectionMarker;

        readonly List<Vector3> _waypoints = new List<Vector3>(32);
        Transform _transform;
        ZooGrid _grid;
        float _speed;
        int _next;
        bool _arrivePending;

        public VisitorInstance Instance { get; private set; }
        public float PickRadius => pickRadius;
        public Vector3 PickPoint => transform.position + Vector3.up * pickHeight;
        public bool IsMoving => _next < _waypoints.Count;

        /// <summary>Raised when the end of the route is reached (or immediately-next-frame for an empty route).</summary>
        public event Action<VisitorController> Arrived;

        void Awake() => _transform = transform;

        /// <param name="speedCellsPerSecond">Walking speed in grid cells per simulation second.</param>
        public void Bind(VisitorInstance instance, ZooGrid grid, float speedCellsPerSecond)
        {
            if (_transform == null) _transform = transform;
            Instance = instance;
            _grid = grid;
            _speed = Mathf.Max(0.01f, speedCellsPerSecond) * grid.CellSize;
            Stop();
            _transform.position = instance.Position;
            SetSelected(false);
        }

        /// <summary>Releases the visitor so the controller can be pooled.</summary>
        public void Unbind()
        {
            Stop();
            Instance = null;
            Arrived = null;
            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (selectionMarker != null) selectionMarker.SetActive(selected);
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
    }
}
