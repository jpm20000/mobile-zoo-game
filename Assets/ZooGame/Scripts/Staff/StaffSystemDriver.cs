using UnityEngine;
using ZooGame.Core;

namespace ZooGame.Staff
{
    /// <summary>
    /// The single per-frame hook for the staff systems. It forwards GameClock simulation time (zero while paused,
    /// scaled by 1x/2x/3x) to the work service and the bodies. All logic lives in the services.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StaffSystemDriver : MonoBehaviour
    {
        GameClock _clock;
        StaffWorkService _work;
        StaffSpawner _spawner;

        public void Bind(GameClock clock, StaffWorkService work, StaffSpawner spawner)
        {
            _clock = clock;
            _work = work;
            _spawner = spawner;
        }

        void Update()
        {
            if (_work == null) return;
            float dt = _clock != null ? _clock.DeltaTime : Time.deltaTime;
            _work.Process();
            _work.Advance(dt);
            _spawner.StepAgents(dt);
        }
    }
}
