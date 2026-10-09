using UnityEngine;
using ZooGame.Core;

namespace ZooGame.Visitors
{
    /// <summary>
    /// The single per-frame hook for the visitor systems. It forwards GameClock simulation time (zero while paused,
    /// scaled by 1x/2x/3x) to: route re-validation, the scheduled need simulation, the spawn timer and the walk step.
    /// All scheduling and logic live in the services; this only pumps them in a fixed order.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VisitorSystemDriver : MonoBehaviour
    {
        GameClock _clock;
        VisitorDecisionService _decisions;
        VisitorSimulationService _simulation;
        VisitorSpawner _spawner;

        public void Bind(GameClock clock, VisitorDecisionService decisions, VisitorSimulationService simulation, VisitorSpawner spawner)
        {
            _clock = clock;
            _decisions = decisions;
            _simulation = simulation;
            _spawner = spawner;
        }

        void Update()
        {
            if (_simulation == null) return;
            float dt = _clock != null ? _clock.DeltaTime : Time.deltaTime;
            _decisions.Process();
            _simulation.Advance(dt);
            _spawner.Tick(dt);
            _spawner.StepAgents(dt);
        }
    }
}
