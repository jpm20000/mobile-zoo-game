using System;

namespace ZooGame.Core
{
    public enum SimulationSpeed
    {
        Paused = 0,
        X1 = 1,
        X2 = 2,
        X3 = 3
    }

    public readonly struct SimulationSpeedChanged : IGameEvent
    {
        public readonly SimulationSpeed Previous;
        public readonly SimulationSpeed Current;

        public SimulationSpeedChanged(SimulationSpeed previous, SimulationSpeed current)
        {
            Previous = previous;
            Current = current;
        }
    }

    /// <summary>Tuning for <see cref="GameClock"/>. Supplied from data (GameConfig); defaults are 1/2/3x.</summary>
    public readonly struct GameClockSettings
    {
        public readonly float Multiplier1x;
        public readonly float Multiplier2x;
        public readonly float Multiplier3x;
        /// <summary>Real-time frame delta is clamped to this before scaling (avoids huge steps after hitches/backgrounding).</summary>
        public readonly float MaxRealDeltaSeconds;

        public GameClockSettings(float x1, float x2, float x3, float maxRealDeltaSeconds)
        {
            Multiplier1x = x1;
            Multiplier2x = x2;
            Multiplier3x = x3;
            MaxRealDeltaSeconds = maxRealDeltaSeconds;
        }

        public static GameClockSettings Default => new GameClockSettings(1f, 2f, 3f, 0.25f);
    }

    /// <summary>
    /// Simulation time, independent of UnityEngine.Time.timeScale. The owner calls <see cref="Tick"/> once per frame
    /// with real (unscaled) delta; simulation systems read <see cref="DeltaTime"/> and never scale time themselves.
    /// </summary>
    public sealed class GameClock
    {
        readonly GameClockSettings _settings;
        readonly EventBus _events;

        public SimulationSpeed Speed { get; private set; } = SimulationSpeed.X1;
        /// <summary>Simulation seconds elapsed this frame (0 while paused).</summary>
        public float DeltaTime { get; private set; }
        /// <summary>Total simulation seconds since the clock was created.</summary>
        public double ElapsedTime { get; private set; }
        public bool IsPaused => Speed == SimulationSpeed.Paused;

        public float Multiplier
        {
            get
            {
                switch (Speed)
                {
                    case SimulationSpeed.X1: return _settings.Multiplier1x;
                    case SimulationSpeed.X2: return _settings.Multiplier2x;
                    case SimulationSpeed.X3: return _settings.Multiplier3x;
                    default: return 0f;
                }
            }
        }

        public GameClock(GameClockSettings settings, EventBus events)
        {
            _settings = settings;
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public bool SetSpeed(SimulationSpeed speed)
        {
            if (speed == Speed) return false;
            var previous = Speed;
            Speed = speed;
            _events.Publish(new SimulationSpeedChanged(previous, speed));
            return true;
        }

        public void Tick(float realDeltaSeconds)
        {
            if (realDeltaSeconds < 0f) realDeltaSeconds = 0f;
            if (realDeltaSeconds > _settings.MaxRealDeltaSeconds) realDeltaSeconds = _settings.MaxRealDeltaSeconds;
            DeltaTime = realDeltaSeconds * Multiplier;
            ElapsedTime += DeltaTime;
        }
    }
}
