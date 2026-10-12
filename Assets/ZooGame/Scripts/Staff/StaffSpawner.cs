using System;
using System.Collections.Generic;
using UnityEngine;
using ZooGame.World;

namespace ZooGame.Staff
{
    /// <summary>
    /// Gives every registered staff member a body and returns it to a pool when they are dismissed. It follows the
    /// <see cref="IStaffRegistry"/> (hire and dismissal go through <see cref="StaffHiringService"/>, never through
    /// here), connects each body to the <see cref="StaffWorkService"/> and steps the walk once per frame.
    /// </summary>
    public sealed class StaffSpawner : MonoBehaviour
    {
        [Tooltip("Staff body prefab with a StaffController. Optional: a plain capsule is built when empty.")]
        [SerializeField] StaffController prefab;

        IStaffRegistry _registry;
        StaffWorkService _work;
        ZooGrid _grid;
        float _walkSpeed;

        readonly Stack<StaffController> _pool = new Stack<StaffController>(8);
        readonly Dictionary<string, StaffController> _byId = new Dictionary<string, StaffController>();
        readonly List<StaffController> _active = new List<StaffController>(16);
        readonly Action<StaffController> _onArrived;

        public StaffSpawner() => _onArrived = c => _work.OnArrived(c.Instance);

        public bool IsBound => _registry != null;
        public IReadOnlyList<StaffController> Active => _active;

        /// <summary>Raised after a body was created for a member / just before it is returned to the pool.</summary>
        public event Action<StaffController> Spawned;
        public event Action<StaffController> Despawning;

        public void Bind(IStaffRegistry registry, StaffWorkService work, ZooGrid grid, float walkSpeedCellsPerSecond)
        {
            Unbind();
            _registry = registry;
            _work = work;
            _grid = grid;
            _walkSpeed = walkSpeedCellsPerSecond;
            _registry.Registered += OnRegistered;
            _registry.Unregistered += OnUnregistered;
            for (int i = 0; i < _registry.All.Count; i++) OnRegistered(_registry.All[i]);
        }

        void Unbind()
        {
            if (_registry == null) return;
            _registry.Registered -= OnRegistered;
            _registry.Unregistered -= OnUnregistered;
            _registry = null;
        }

        void OnDestroy() => Unbind();

        public bool TryGetController(string staffId, out StaffController controller)
        {
            if (staffId == null) { controller = null; return false; }
            return _byId.TryGetValue(staffId, out controller);
        }

        /// <summary>Walks every body by simulation seconds. Called once per frame by the driver.</summary>
        public void StepAgents(float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (i >= _active.Count) continue;
                _active[i].Step(dt);
            }
        }

        void OnRegistered(StaffInstance staff)
        {
            if (_byId.ContainsKey(staff.StaffId)) return;
            var controller = Rent();
            controller.name = staff.DisplayName;
            controller.Bind(staff, _grid, _walkSpeed);
            controller.Arrived += _onArrived;
            controller.gameObject.SetActive(true);
            _byId.Add(staff.StaffId, controller);
            _active.Add(controller);
            _work.Attach(controller);
            Spawned?.Invoke(controller);
        }

        void OnUnregistered(StaffInstance staff)
        {
            if (!_byId.TryGetValue(staff.StaffId, out var controller)) return;
            Despawning?.Invoke(controller);
            _byId.Remove(staff.StaffId);
            _active.Remove(controller);
            controller.Arrived -= _onArrived;
            controller.Unbind();
            controller.gameObject.SetActive(false);
            _pool.Push(controller);
        }

        StaffController Rent()
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                if (pooled != null) return pooled;
            }
            GameObject go = prefab != null ? Instantiate(prefab.gameObject, transform) : BuildFallbackBody();
            var controller = go.GetComponent<StaffController>();
            if (controller == null) controller = go.AddComponent<StaffController>();
            return controller;
        }

        GameObject BuildFallbackBody()
        {
            var root = new GameObject("Staff");
            root.transform.SetParent(transform, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            var collider = body.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.4f, 0.5f, 0.4f);
            body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            return root;
        }
    }
}
