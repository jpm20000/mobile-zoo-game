using UnityEngine;
using ZooGame.Core;

namespace ZooGame.Animals
{
    /// <summary>
    /// The single per-frame hook for the needs simulation: forwards the GameClock's simulation delta (zero while
    /// paused, scaled by 1x/2x/3x) to <see cref="AnimalNeedsSystem.Advance"/>. All scheduling lives in the system.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimalNeedsDriver : MonoBehaviour
    {
        AnimalNeedsSystem _system;
        GameClock _clock;

        public AnimalNeedsSystem Needs => _system;

        public void Bind(AnimalNeedsSystem system, GameClock clock)
        {
            _system = system;
            _clock = clock;
        }

        void Update()
        {
            if (_system != null) _system.Advance(_clock != null ? _clock.DeltaTime : Time.deltaTime);
        }
    }
}
