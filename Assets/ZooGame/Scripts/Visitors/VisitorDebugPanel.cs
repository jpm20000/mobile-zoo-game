using UnityEngine;
using UnityEngine.UI;

namespace ZooGame.Visitors
{
    /// <summary>
    /// TEMPORARY development controls for visitors: toggle automatic spawning, spawn one or five now, and remove them
    /// all. Shows the visitor count and whether an entrance exists. No spawn logic lives here.
    /// </summary>
    public sealed class VisitorDebugPanel : MonoBehaviour
    {
        [SerializeField] Text statusLabel;
        [SerializeField] Button autoButton;
        [SerializeField] Text autoButtonLabel;
        [SerializeField] Button spawnOneButton;
        [SerializeField] Button spawnFiveButton;
        [SerializeField] Button clearButton;

        VisitorSpawner _spawner;
        VisitorDestinationService _destinations;
        VisitorSpawnFailure _lastFailure;

        public void Bind(VisitorSpawner spawner, VisitorDestinationService destinations)
        {
            Unbind();
            _spawner = spawner;
            _destinations = destinations;
            autoButton.onClick.AddListener(OnAuto);
            spawnOneButton.onClick.AddListener(OnSpawnOne);
            spawnFiveButton.onClick.AddListener(OnSpawnFive);
            clearButton.onClick.AddListener(OnClear);
            _spawner.Spawned += OnVisitorChanged;
            _spawner.Despawning += OnVisitorChanged;
            _destinations.Changed += Refresh;
            Refresh();
        }

        void Unbind()
        {
            if (_spawner == null) return;
            autoButton.onClick.RemoveListener(OnAuto);
            spawnOneButton.onClick.RemoveListener(OnSpawnOne);
            spawnFiveButton.onClick.RemoveListener(OnSpawnFive);
            clearButton.onClick.RemoveListener(OnClear);
            _spawner.Spawned -= OnVisitorChanged;
            _spawner.Despawning -= OnVisitorChanged;
            _destinations.Changed -= Refresh;
            _spawner = null;
            _destinations = null;
        }

        void OnDestroy() => Unbind();

        void OnAuto()
        {
            _spawner.AutoSpawn = !_spawner.AutoSpawn;
            Refresh();
        }

        void OnSpawnOne()
        {
            _lastFailure = _spawner.TrySpawn(out _);
            Refresh();
        }

        void OnSpawnFive()
        {
            for (int i = 0; i < 5; i++)
            {
                _lastFailure = _spawner.TrySpawn(out _);
                if (_lastFailure != VisitorSpawnFailure.None) break;
            }
            Refresh();
        }

        void OnClear()
        {
            _spawner.Clear();
            _lastFailure = VisitorSpawnFailure.None;
            Refresh();
        }

        void OnVisitorChanged(VisitorController _) => Refresh();

        void Refresh()
        {
            if (_spawner == null) return;
            if (autoButtonLabel != null) autoButtonLabel.text = "Auto spawn: " + (_spawner.AutoSpawn ? "ON" : "OFF");
            if (statusLabel == null) return;
            string note = _spawner.HasEntrance ? "" : "  (place an Entrance next to a path)";
            if (_lastFailure == VisitorSpawnFailure.MaxVisitors) note = "  (visitor cap reached)";
            statusLabel.text = "Visitors " + _spawner.Registry.Count + "/" + _spawner.MaxVisitors + note;
        }
    }
}
