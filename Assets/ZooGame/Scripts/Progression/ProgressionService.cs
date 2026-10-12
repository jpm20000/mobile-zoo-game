using System;
using UnityEngine;

namespace ZooGame.Progression
{
    /// <summary>
    /// The persistent XP total. Plain serializable data (JsonUtility-ready). Readable by anyone; written only by
    /// <see cref="ProgressionService"/> (the mutator is internal).
    /// </summary>
    [Serializable]
    public sealed class ProgressionState
    {
        [SerializeField] long totalXp;

        public long TotalXp => totalXp;

        public ProgressionState() { }
        public ProgressionState(long totalXp) { this.totalXp = totalXp; }

        public ProgressionState Clone() => (ProgressionState)MemberwiseClone();
        public bool IsValid => totalXp >= 0;

        internal void Add(long amount) => totalXp += amount;
    }

    /// <summary>
    /// Zoo level thresholds: <c>XpForLevel(n)</c> is the total XP needed to be level n. Level 1 needs 0 XP. Values must
    /// be non-decreasing; the level cap is the number of entries.
    /// </summary>
    public sealed class ProgressionSettings
    {
        static readonly long[] DefaultThresholds = { 0, 100, 250, 500, 900, 1500, 2500, 4000, 6000, 9000 };

        readonly long[] _thresholds;

        public ProgressionSettings(long[] xpForLevel = null)
        {
            var src = xpForLevel != null && xpForLevel.Length > 0 ? xpForLevel : DefaultThresholds;
            _thresholds = new long[src.Length];
            long previous = 0;
            for (int i = 0; i < src.Length; i++)
            {
                long v = i == 0 ? 0 : Math.Max(previous, src[i]);
                _thresholds[i] = v;
                previous = v;
            }
        }

        public int MaxLevel => _thresholds.Length;

        /// <summary>Total XP needed to reach <paramref name="level"/> (1-based, clamped to the valid range).</summary>
        public long XpForLevel(int level) => _thresholds[Math.Max(1, Math.Min(level, _thresholds.Length)) - 1];

        public int LevelFor(long xp)
        {
            int level = 1;
            for (int i = 1; i < _thresholds.Length && xp >= _thresholds[i]; i++) level = i + 1;
            return level;
        }
    }

    /// <summary>
    /// The only writer of XP. XP is granted with a source id (for example a quest id), accumulates, and maps to a zoo
    /// level through <see cref="ProgressionSettings"/>. Event driven; nothing runs per frame. Star rating remains
    /// <see cref="ZooRatingService"/>'s concern; level and stars are independent.
    /// </summary>
    public sealed class ProgressionService
    {
        readonly ProgressionSettings _settings;
        ProgressionState _state = new ProgressionState();
        int _level = 1;

        public ProgressionService(ProgressionSettings settings = null)
        {
            _settings = settings ?? new ProgressionSettings();
        }

        public ProgressionSettings Settings => _settings;
        public long TotalXp => _state.TotalXp;
        public int Level => _level;

        /// <summary>XP still needed for the next level, or 0 at the cap.</summary>
        public long XpToNextLevel => _level >= _settings.MaxLevel ? 0 : _settings.XpForLevel(_level + 1) - _state.TotalXp;

        /// <summary>Raised after XP was granted (amount, source id).</summary>
        public event Action<long, string> XpGranted;

        /// <summary>Raised with (oldLevel, newLevel) after the level changed.</summary>
        public event Action<int, int> LevelChanged;

        /// <summary>Raised after <see cref="Restore"/> replaced the state.</summary>
        public event Action StateRestored;

        /// <summary>False for a non-positive amount or numeric overflow; nothing changes then.</summary>
        public bool TryAddXp(long amount, string sourceId)
        {
            if (amount <= 0 || _state.TotalXp > long.MaxValue - amount) return false;
            _state.Add(amount);
            int before = _level;
            _level = _settings.LevelFor(_state.TotalXp);
            XpGranted?.Invoke(amount, sourceId ?? string.Empty);
            if (_level != before) LevelChanged?.Invoke(before, _level);
            return true;
        }

        /// <summary>Whether <paramref name="amount"/> could be added without overflow.</summary>
        public bool CanAddXp(long amount) => amount > 0 && _state.TotalXp <= long.MaxValue - amount;

        public ProgressionState Snapshot() => _state.Clone();

        /// <summary>Replaces the XP with loaded data. Rejects null or negative data; grants nothing and raises no level event.</summary>
        public bool Restore(ProgressionState state)
        {
            if (state == null || !state.IsValid) return false;
            _state = state.Clone();
            _level = _settings.LevelFor(_state.TotalXp);
            StateRestored?.Invoke();
            return true;
        }
    }
}
