using System;
using System.Collections.Generic;

namespace ZooGame.Quests
{
    public enum QuestClaimFailure
    {
        None = 0,
        UnknownQuest,
        /// <summary>The quest has not completed yet (or is not active).</summary>
        NotCompleted,
        /// <summary>The reward was already claimed; nothing was granted again.</summary>
        AlreadyClaimed,
        /// <summary>The rewards could not be applied (see <see cref="QuestClaimResult.RewardFailure"/>). The quest stays claimable.</summary>
        RewardFailed
    }

    public readonly struct QuestClaimResult
    {
        public readonly QuestClaimFailure Failure;
        public readonly QuestRewardFailure RewardFailure;

        public QuestClaimResult(QuestClaimFailure failure, QuestRewardFailure rewardFailure = QuestRewardFailure.None)
        {
            Failure = failure;
            RewardFailure = rewardFailure;
        }

        public bool Success => Failure == QuestClaimFailure.None;
    }

    /// <summary>
    /// The authoritative quest lifecycle. It owns every <see cref="QuestInstance"/>, resolves definitions by id and is
    /// the only place states change:
    /// <c>Locked -> Available -> Active -> Completed -> RewardClaimed</c>.
    ///
    /// Progress is event driven: gameplay facts arrive through <see cref="Notify"/> (the <see cref="QuestEventBridge"/>
    /// translates the existing services' events), and only the active objectives that care about that kind of fact are
    /// re-evaluated, through a per-kind index. Nothing runs per frame and nothing scans the zoo. Events raised while one is
    /// being processed (a reward granting XP that changes the level, say) are queued and handled in order, so processing
    /// is never re-entered.
    ///
    /// A quest unlocks when its prerequisites are completed (Completed or RewardClaimed) and the zoo level and star
    /// rating minimums are met; with <see cref="AutoStart"/> it then becomes Active at once and picks up what already
    /// exists. Plain C#: no scene objects, no MonoBehaviour.
    /// </summary>
    public sealed class QuestService
    {
        readonly QuestLibrary _library;
        readonly IQuestWorld _world;
        readonly QuestObjectiveEvaluator _evaluator;
        readonly QuestRewardService _rewards;

        readonly Dictionary<string, QuestInstance> _byId = new Dictionary<string, QuestInstance>();
        readonly List<QuestInstance> _all = new List<QuestInstance>();
        readonly List<QuestInstance>[] _activeByKind = new List<QuestInstance>[(int)QuestEventKind.Count];
        readonly List<QuestInstance> _scratch = new List<QuestInstance>(8);
        readonly Queue<QuestEvent> _pending = new Queue<QuestEvent>();

        bool _processing;
        bool _refreshing;
        bool _refreshAgain;
        string _pinnedQuestId;

        /// <param name="rewards">Null disables rewards: quests with rewards cannot be claimed.</param>
        /// <param name="autoStart">Unlocked quests become Active immediately (default). When false they wait in Available for <see cref="Start"/>.</param>
        public QuestService(QuestLibrary library, IQuestWorld world, QuestRewardService rewards = null, bool autoStart = true)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _evaluator = new QuestObjectiveEvaluator(world);
            _rewards = rewards;
            AutoStart = autoStart;

            for (int i = 0; i < _activeByKind.Length; i++) _activeByKind[i] = new List<QuestInstance>(4);
            for (int i = 0; i < library.All.Count; i++)
            {
                var instance = new QuestInstance(library.All[i]);
                _byId.Add(instance.QuestId, instance);
                _all.Add(instance);
            }
            RefreshAvailability();
        }

        public QuestLibrary Library => _library;
        public bool AutoStart { get; set; }
        public int Count => _all.Count;

        /// <summary>Every quest in catalogue order. Live list: do not modify.</summary>
        public IReadOnlyList<QuestInstance> All => _all;

        /// <summary>The quest pinned to the HUD, or null.</summary>
        public string PinnedQuestId => _pinnedQuestId;

        /// <summary>Raised after any state or progress change (for UI refresh).</summary>
        public event Action Changed;

        /// <summary>Raised with (quest, previousState) after a state transition.</summary>
        public event Action<QuestInstance, QuestState> StateChanged;

        /// <summary>Raised after a quest's reward was granted.</summary>
        public event Action<QuestInstance> RewardClaimed;

        /// <summary>Raised after <see cref="Restore"/> replaced the runtime state.</summary>
        public event Action Restored;

        // ---- Queries ---------------------------------------------------------------------------------------

        public bool TryGet(string questId, out QuestInstance instance)
        {
            if (questId == null) { instance = null; return false; }
            return _byId.TryGetValue(questId, out instance);
        }

        public QuestState GetState(string questId) => TryGet(questId, out var q) ? q.State : QuestState.Locked;

        /// <summary>Appends the quests in <paramref name="state"/> to <paramref name="into"/> (cleared first), catalogue order.</summary>
        public void CopyByState(QuestState state, List<QuestInstance> into)
        {
            into.Clear();
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].State == state) into.Add(_all[i]);
        }

        /// <summary>
        /// The quest the HUD should track: the pinned one while it is active or claimable, otherwise the first claimable
        /// one, otherwise the first active one.
        /// </summary>
        public bool TryGetTracked(out QuestInstance quest)
        {
            if (_pinnedQuestId != null && _byId.TryGetValue(_pinnedQuestId, out quest)
                && (quest.State == QuestState.Active || quest.State == QuestState.Completed)) return true;
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].State == QuestState.Completed) { quest = _all[i]; return true; }
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].State == QuestState.Active) { quest = _all[i]; return true; }
            quest = null;
            return false;
        }

        // ---- Commands --------------------------------------------------------------------------------------

        /// <summary>Pins a quest to the HUD (null clears). False for an unknown id.</summary>
        public bool SetPinned(string questId)
        {
            if (questId != null && !_byId.ContainsKey(questId)) return false;
            if (_pinnedQuestId == questId) return true;
            _pinnedQuestId = questId;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Starts an Available quest. False if unknown or not Available.</summary>
        public bool Start(string questId)
        {
            if (!TryGet(questId, out var q) || q.State != QuestState.Available) return false;
            Activate(q);
            RefreshAvailability();
            return true;
        }

        /// <summary>
        /// Claims a Completed quest's reward. The state moves to RewardClaimed before anything is granted, so a repeated or
        /// re-entrant claim can never grant twice. Rewards are validated first: if they cannot be applied nothing is
        /// granted and the quest stays claimable.
        /// </summary>
        public QuestClaimResult Claim(string questId)
        {
            if (!TryGet(questId, out var q)) return new QuestClaimResult(QuestClaimFailure.UnknownQuest);
            if (q.IsRewardClaimed) return new QuestClaimResult(QuestClaimFailure.AlreadyClaimed);
            if (q.State != QuestState.Completed) return new QuestClaimResult(QuestClaimFailure.NotCompleted);

            var rewards = q.Definition.Rewards;
            if (rewards.Length > 0)
            {
                var rewardService = _rewards;
                var failure = rewardService != null ? rewardService.Validate(rewards) : QuestRewardFailure.NoEconomy;
                if (failure != QuestRewardFailure.None)
                    return new QuestClaimResult(QuestClaimFailure.RewardFailed, failure);

                SetState(q, QuestState.RewardClaimed);
                rewardService.Grant(q.QuestId, rewards);
            }
            else
            {
                SetState(q, QuestState.RewardClaimed);
            }

            RewardClaimed?.Invoke(q);
            return new QuestClaimResult(QuestClaimFailure.None);
        }

        // ---- Events ----------------------------------------------------------------------------------------

        /// <summary>Reports a gameplay fact. Re-evaluates only the active objectives that depend on that kind of fact.</summary>
        public void Notify(in QuestEvent ev)
        {
            if (ev.Kind < 0 || ev.Kind >= QuestEventKind.Count) return;
            _pending.Enqueue(ev);
            if (_processing) return; // handled by the loop already running

            _processing = true;
            try
            {
                while (_pending.Count > 0)
                {
                    var next = _pending.Dequeue();
                    ProcessEvent(next);
                    RefreshAvailability();
                }
            }
            finally
            {
                _processing = false;
            }
        }

        void ProcessEvent(in QuestEvent ev)
        {
            var list = _activeByKind[(int)ev.Kind];
            if (list.Count == 0) return;

            _scratch.Clear();
            _scratch.AddRange(list); // completing a quest edits the index while we iterate
            for (int i = 0; i < _scratch.Count; i++)
            {
                var q = _scratch[i];
                if (q.State != QuestState.Active) continue;

                bool progressed = false;
                var objectives = q.ObjectiveList;
                for (int o = 0; o < objectives.Count; o++)
                {
                    var p = objectives[o];
                    var def = p.Definition;
                    if (def == null || QuestObjectiveEvaluator.KindOf(def.Type) != ev.Kind) continue;
                    int next = _evaluator.Evaluate(def, p.CurrentValue, ev, initial: false);
                    if (next == p.CurrentValue) continue;
                    p.Set(next);
                    progressed = true;
                }

                if (!progressed) continue;
                if (q.AllObjectivesComplete) SetState(q, QuestState.Completed);
                Changed?.Invoke();
            }
            _scratch.Clear();
        }

        // ---- Lifecycle -------------------------------------------------------------------------------------

        /// <summary>Unlocks every Locked quest whose requirements are met (and starts Available ones when auto-starting), repeating until nothing more changes (chains).</summary>
        void RefreshAvailability()
        {
            if (_refreshing) { _refreshAgain = true; return; }
            _refreshing = true;
            try
            {
                bool changed;
                do
                {
                    changed = false;
                    _refreshAgain = false;
                    for (int i = 0; i < _all.Count; i++)
                    {
                        var q = _all[i];
                        if (q.State == QuestState.Locked && RequirementsMet(q.Definition))
                        {
                            SetState(q, QuestState.Available);
                            changed = true;
                        }
                        if (q.State == QuestState.Available && AutoStart)
                        {
                            Activate(q);
                            changed = true;
                        }
                    }
                } while (changed || _refreshAgain);
            }
            finally
            {
                _refreshing = false;
            }
        }

        bool RequirementsMet(QuestDefinition def)
        {
            var pre = def.PrerequisiteQuestIds;
            for (int i = 0; i < pre.Length; i++)
                if (!_byId.TryGetValue(pre[i], out var p) || !p.IsCompleted) return false;
            if (def.MinimumLevel > 1 && _world.Level < def.MinimumLevel) return false;
            if (def.MinimumStars > 1 && _world.Stars < def.MinimumStars) return false;
            return true;
        }

        void Activate(QuestInstance q)
        {
            SetState(q, QuestState.Active);
            ReadBaseline(q);
        }

        // Count-style objectives pick up what already exists the moment a quest becomes active.
        void ReadBaseline(QuestInstance q)
        {
            var objectives = q.ObjectiveList;
            var none = default(QuestEvent);
            for (int i = 0; i < objectives.Count; i++)
            {
                var p = objectives[i];
                p.Set(_evaluator.Evaluate(p.Definition, p.CurrentValue, none, initial: true));
            }
            if (q.State == QuestState.Active && q.AllObjectivesComplete) SetState(q, QuestState.Completed);
        }

        static bool IsAllowed(QuestState from, QuestState to) =>
            (from == QuestState.Locked && to == QuestState.Available)
            || (from == QuestState.Available && to == QuestState.Active)
            || (from == QuestState.Active && to == QuestState.Completed)
            || (from == QuestState.Completed && to == QuestState.RewardClaimed);

        /// <summary>The one place a state changes.</summary>
        void SetState(QuestInstance q, QuestState next)
        {
            var previous = q.State;
            if (!IsAllowed(previous, next)) return;

            if (previous == QuestState.Active) RemoveFromIndex(q);
            q.State = next;
            if (next == QuestState.Active) AddToIndex(q);
            if (next == QuestState.RewardClaimed) q.RewardClaimedFlag = true;

            StateChanged?.Invoke(q, previous);
            Changed?.Invoke();
        }

        void AddToIndex(QuestInstance q)
        {
            var objectives = q.ObjectiveList;
            for (int i = 0; i < objectives.Count; i++)
            {
                var def = objectives[i].Definition;
                if (def == null) continue;
                var kind = QuestObjectiveEvaluator.KindOf(def.Type);
                if (kind == QuestEventKind.Count) continue;
                var list = _activeByKind[(int)kind];
                if (!list.Contains(q)) list.Add(q);
            }
        }

        void RemoveFromIndex(QuestInstance q)
        {
            for (int i = 0; i < _activeByKind.Length; i++) _activeByKind[i].Remove(q);
        }

        // ---- Persistence -----------------------------------------------------------------------------------

        /// <summary>A copy of the runtime state: ids, states, objective progress and claimed flags. No definition data.</summary>
        public QuestSaveData Capture()
        {
            var data = new QuestSaveData { PinnedQuestId = _pinnedQuestId };
            for (int i = 0; i < _all.Count; i++) data.Quests.Add(_all[i].CloneData());
            return data;
        }

        /// <summary>
        /// Rebuilds the runtime state from saved data, resolving definitions by id. Unknown quest ids are dropped, quests
        /// the save does not know start Locked, objective progress is matched by objective id and clamped, and the state is
        /// made consistent with the objectives. Grants nothing: a claimed reward stays claimed. Returns false for null.
        /// </summary>
        public bool Restore(QuestSaveData data)
        {
            if (data == null || data.Quests == null) return false;

            for (int i = 0; i < _activeByKind.Length; i++) _activeByKind[i].Clear();
            _pending.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                var fresh = new QuestInstance(_all[i].Definition);
                _all[i] = fresh;
                _byId[fresh.QuestId] = fresh;
            }

            for (int i = 0; i < data.Quests.Count; i++)
            {
                var saved = data.Quests[i];
                if (saved == null || !_byId.TryGetValue(saved.QuestId ?? string.Empty, out var q)) continue;
                ApplySaved(q, saved);
            }

            _pinnedQuestId = data.PinnedQuestId != null && _byId.ContainsKey(data.PinnedQuestId) ? data.PinnedQuestId : null;

            for (int i = 0; i < _all.Count; i++)
                if (_all[i].State == QuestState.Active) AddToIndex(_all[i]);

            // Active quests pick up what exists now; newly unlockable quests unlock.
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].State == QuestState.Active) ReadBaseline(_all[i]);
            RefreshAvailability();

            Restored?.Invoke();
            Changed?.Invoke();
            return true;
        }

        static void ApplySaved(QuestInstance q, QuestInstance saved)
        {
            var objectives = q.ObjectiveList;
            var savedObjectives = saved.Objectives;
            for (int i = 0; i < objectives.Count; i++)
            {
                for (int s = 0; s < savedObjectives.Count; s++)
                {
                    if (savedObjectives[s] == null || savedObjectives[s].ObjectiveId != objectives[i].ObjectiveId) continue;
                    int value = savedObjectives[s].CurrentValue;
                    objectives[i].Set(Math.Max(0, Math.Min(value, objectives[i].TargetValue)));
                    break;
                }
            }

            var state = saved.State;
            if (state < QuestState.Locked || state > QuestState.RewardClaimed) state = QuestState.Locked;
            if (saved.RewardClaimedFlag) state = QuestState.RewardClaimed; // a claimed reward is never claimable again

            if (state == QuestState.Completed || state == QuestState.RewardClaimed)
            {
                for (int i = 0; i < objectives.Count; i++) objectives[i].Set(objectives[i].TargetValue);
            }
            else if (state == QuestState.Active && q.AllObjectivesComplete)
            {
                state = QuestState.Completed;
            }

            q.State = state;
            q.RewardClaimedFlag = state == QuestState.RewardClaimed;
        }
    }
}
