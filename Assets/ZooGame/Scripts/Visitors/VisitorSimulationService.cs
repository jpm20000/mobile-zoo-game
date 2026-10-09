using System;
using System.Collections.Generic;

namespace ZooGame.Visitors
{
    /// <summary>
    /// The centralized visitor tick. Plain C#, no per-visitor MonoBehaviour and no Update: <see cref="VisitorSystemDriver"/>
    /// feeds it GameClock time through <see cref="Advance"/>, which accumulates simulation seconds and runs one scheduled
    /// tick per <see cref="VisitorConfig.TickIntervalSeconds"/>. A paused clock supplies zero time, so nothing advances;
    /// 2x/3x supply proportionally more time, so the same number of ticks happens sooner.
    ///
    /// Each tick, for every visitor:
    ///  1. progress needs (hunger, thirst, toilet up; energy down) by rate x simulated minutes, and visit time;
    ///  2. update happiness (and how long it has been at or below the unhappy threshold);
    ///  3. check urgent needs;
    ///  4. check visit duration and sustained unhappiness;
    ///  5. hand the result to <see cref="VisitorDecisionService"/>, which requests a new decision when required.
    /// </summary>
    public sealed class VisitorSimulationService
    {
        readonly VisitorConfig _config;
        readonly IVisitorRegistry _registry;
        readonly VisitorDecisionService _decisions;
        readonly List<VisitorInstance> _snapshot = new List<VisitorInstance>(32);
        float _accumulated;

        public VisitorSimulationService(VisitorConfig config, IVisitorRegistry registry, VisitorDecisionService decisions)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        }

        /// <summary>Raised after a visitor's tick (needs, happiness and any decision are current).</summary>
        public event Action<VisitorInstance> Evaluated;

        /// <summary>Scheduled ticks run so far (diagnostics and tests).</summary>
        public int TicksRun { get; private set; }

        public VisitorConfig Config => _config;

        /// <summary>Adds simulation seconds (GameClock.DeltaTime) and runs every tick that is now due.</summary>
        public void Advance(float simulatedSeconds)
        {
            if (!(simulatedSeconds > 0f)) return;
            _accumulated += simulatedSeconds;
            float interval = _config.TickIntervalSeconds;
            int ran = 0;
            while (_accumulated >= interval && ran < _config.MaxTicksPerAdvance)
            {
                _accumulated -= interval;
                Step(_config.MinutesPerTick);
                ran++;
            }
            if (_accumulated >= interval) _accumulated = 0f; // hitch: drop what the cap could not process
        }

        /// <summary>Runs one tick covering the given simulated minutes for every visitor. <see cref="Advance"/> calls this on schedule.</summary>
        public void Step(float simulatedMinutes)
        {
            if (simulatedMinutes > 0f) TicksRun++;
            _snapshot.Clear();
            _registry.CopyTo(_snapshot); // decisions may make visitors leave, which changes the registry
            for (int i = 0; i < _snapshot.Count; i++)
            {
                var v = _snapshot[i];
                if (_registry.Contains(v.VisitorId)) Tick(v, simulatedMinutes);
            }
        }

        void Tick(VisitorInstance v, float minutes)
        {
            // 1. needs and visit time
            VisitorMath.ProgressNeeds(v, _config, minutes);
            v.VisitTime += minutes;

            // 2. happiness
            VisitorMath.RecalculateHappiness(v);
            if (v.Happiness <= _config.UnhappyAtOrBelow) v.UnhappyTime += minutes;
            else v.UnhappyTime = 0f;

            // 3. urgent needs, 4. visit duration / sustained unhappiness
            var urgent = VisitorMath.MostUrgent(v, _config);
            bool exit = VisitorMath.ShouldExit(v, _config);

            // 5. new decision when required
            _decisions.OnTick(v, minutes, urgent, exit);
            Evaluated?.Invoke(v);
        }
    }
}
