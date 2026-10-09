using System;
using System.Collections.Generic;
using UnityEngine;
using ZooGame.World;

namespace ZooGame.Visitors
{
    public enum VisitorSpawnFailure
    {
        None = 0,
        NotBound,
        /// <summary>The visitor cap is reached.</summary>
        MaxVisitors,
        /// <summary>No enabled entrance with a path to step onto.</summary>
        NoEntrance,
        Rejected
    }

    /// <summary>
    /// Creates visitors at the zoo entrance and returns them to a pool when they leave. It owns the spawn schedule
    /// (interval and cap from the config, in GameClock seconds so pause and speed apply), the list of active
    /// controllers and the per-frame walk step; it holds no needs, decision or pathfinding logic. Spawning is not
    /// connected to rating or marketing; <see cref="AutoSpawn"/> and <see cref="TrySpawn"/> are the development controls.
    /// </summary>
    public sealed class VisitorSpawner : MonoBehaviour
    {
        [Tooltip("Visitor body prefab with a VisitorController. Optional: a plain capsule is built when empty.")]
        [SerializeField] VisitorController prefab;
        [Tooltip("Spawn visitors automatically on the configured interval while an entrance exists.")]
        [SerializeField] bool autoSpawn = true;

        VisitorConfig _config;
        IVisitorRegistry _registry;
        VisitorDecisionService _decisions;
        VisitorDestinationService _destinations;
        ZooGrid _grid;
        System.Random _rng;
        float _spawnTimer;

        readonly Stack<VisitorController> _pool = new Stack<VisitorController>(16);
        readonly Dictionary<string, VisitorController> _byId = new Dictionary<string, VisitorController>();
        readonly List<VisitorController> _active = new List<VisitorController>(32);
        readonly Action<VisitorController> _onArrived;

        public VisitorSpawner() => _onArrived = c => _decisions.OnArrived(c.Instance);

        public bool IsBound => _registry != null;
        public IReadOnlyList<VisitorController> Active => _active;
        public IVisitorRegistry Registry => _registry;

        /// <summary>Development toggle: spawn on the configured interval.</summary>
        public bool AutoSpawn { get => autoSpawn; set => autoSpawn = value; }

        public bool HasEntrance => IsBound && _destinations.TryGetEntrance(out _);
        public int MaxVisitors => _config != null ? _config.MaxVisitors : 0;

        /// <summary>Raised after a visitor's controller is bound and registered / just before it is returned to the pool.</summary>
        public event Action<VisitorController> Spawned;
        public event Action<VisitorController> Despawning;

        public void Bind(VisitorConfig config, IVisitorRegistry registry, VisitorDecisionService decisions,
            VisitorDestinationService destinations, ZooGrid grid, System.Random rng = null)
        {
            Unbind();
            _config = config;
            _registry = registry;
            _decisions = decisions;
            _destinations = destinations;
            _grid = grid;
            _rng = rng ?? (config.RandomSeed != 0 ? new System.Random(config.RandomSeed) : new System.Random());
            _decisions.Left += OnLeft;
        }

        void Unbind()
        {
            if (_decisions != null) _decisions.Left -= OnLeft;
        }

        void OnDestroy() => Unbind();

        // ---- Spawning --------------------------------------------------------------------------------------

        /// <summary>Spawns one visitor at the entrance if there is room and an entrance. Returns the failure, or None with the controller.</summary>
        public VisitorSpawnFailure TrySpawn(out VisitorController controller)
        {
            controller = null;
            if (!IsBound) return VisitorSpawnFailure.NotBound;
            if (_registry.Count >= _config.MaxVisitors) return VisitorSpawnFailure.MaxVisitors;
            if (!_destinations.TryGetEntrance(out var entrance)) return VisitorSpawnFailure.NoEntrance;

            var instance = VisitorFactory.CreateNew(_config, _rng, entrance.SourcePosition);
            if (!_registry.Register(instance)) return VisitorSpawnFailure.Rejected;

            controller = Rent();
            controller.name = "Visitor " + instance.VisitorId.Substring(instance.VisitorId.Length - 4);
            controller.Bind(instance, _grid, _config.WalkSpeedCellsPerSecond);
            controller.Arrived += _onArrived;
            controller.gameObject.SetActive(true);
            _byId.Add(instance.VisitorId, controller);
            _active.Add(controller);
            _decisions.Attach(controller);

            if (!_decisions.BeginEntering(instance, entrance))
            {
                Release(controller);
                controller = null;
                return VisitorSpawnFailure.NoEntrance;
            }
            Spawned?.Invoke(controller);
            return VisitorSpawnFailure.None;
        }

        /// <summary>Removes every visitor immediately (development "clear").</summary>
        public void Clear()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (i >= _active.Count) continue;
                Release(_active[i]);
            }
        }

        public bool TryGetController(string visitorId, out VisitorController controller)
        {
            if (visitorId == null) { controller = null; return false; }
            return _byId.TryGetValue(visitorId, out controller);
        }

        // ---- Per frame (called by VisitorSystemDriver) ----------------------------------------------------

        /// <summary>Advances the spawn schedule by simulation seconds and spawns when due.</summary>
        public void Tick(float dt)
        {
            if (!IsBound || !autoSpawn || !(dt > 0f)) return;
            _spawnTimer += dt;
            if (_spawnTimer < _config.SpawnIntervalSeconds) return;
            _spawnTimer = 0f;
            TrySpawn(out _);
        }

        /// <summary>Walks every active visitor by simulation seconds. Backwards, because arriving at the exit removes the visitor.</summary>
        public void StepAgents(float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (i >= _active.Count) continue;
                _active[i].Step(dt);
            }
        }

        // ---- Leaving and pooling ---------------------------------------------------------------------------

        void OnLeft(VisitorInstance v)
        {
            if (_byId.TryGetValue(v.VisitorId, out var controller)) Release(controller);
            else _registry.Unregister(v.VisitorId);
        }

        void Release(VisitorController controller)
        {
            var instance = controller.Instance;
            Despawning?.Invoke(controller);
            if (instance != null)
            {
                _byId.Remove(instance.VisitorId);
                _decisions.Detach(instance.VisitorId);
                _registry.Unregister(instance.VisitorId);
            }
            _active.Remove(controller);
            controller.Arrived -= _onArrived;
            controller.Unbind();
            controller.gameObject.SetActive(false);
            _pool.Push(controller);
        }

        VisitorController Rent()
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                if (pooled != null) return pooled;
            }
            return Create();
        }

        VisitorController Create()
        {
            GameObject go;
            if (prefab != null) go = Instantiate(prefab.gameObject, transform);
            else go = BuildFallbackBody();
            var controller = go.GetComponent<VisitorController>();
            if (controller == null) controller = go.AddComponent<VisitorController>();
            return controller;
        }

        GameObject BuildFallbackBody()
        {
            var root = new GameObject("Visitor");
            root.transform.SetParent(transform, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            var collider = body.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider); // edit-mode tests
            }
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.35f, 0.45f, 0.35f);
            body.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            return root;
        }
    }
}
