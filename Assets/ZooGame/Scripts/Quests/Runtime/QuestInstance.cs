using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZooGame.Quests
{
    /// <summary>
    /// Progress of one objective: its stable id and the clamped current value. The target and type come from the
    /// definition (bound at runtime, never saved). Written only by <see cref="QuestService"/>.
    /// </summary>
    [Serializable]
    public sealed class QuestObjectiveProgress
    {
        [SerializeField] string objectiveId;
        [SerializeField] int currentValue;

        [NonSerialized] QuestObjectiveDefinition _definition;

        public QuestObjectiveProgress() { }

        internal QuestObjectiveProgress(QuestObjectiveDefinition definition)
        {
            objectiveId = definition.ObjectiveId;
            _definition = definition;
        }

        public string ObjectiveId => objectiveId;
        public int CurrentValue => currentValue;
        public QuestObjectiveDefinition Definition => _definition;
        public int TargetValue => _definition != null ? _definition.TargetValue : 0;
        public bool IsComplete => _definition != null && currentValue >= _definition.TargetValue;

        /// <summary>0-1 progress for bars.</summary>
        public float Fraction => TargetValue > 0 ? Mathf.Clamp01((float)currentValue / TargetValue) : 0f;

        internal QuestObjectiveProgress Clone() => (QuestObjectiveProgress)MemberwiseClone();
        internal void Set(int value) => currentValue = value;
        internal void Bind(QuestObjectiveDefinition definition) => _definition = definition;
    }

    /// <summary>
    /// The persistent runtime state of one quest: id, lifecycle state, per-objective progress and whether the reward was
    /// claimed. Plain serializable data (JsonUtility-ready) with no definition contents; <see cref="QuestService"/>
    /// resolves the definition by <see cref="QuestId"/> and is the only writer.
    /// </summary>
    [Serializable]
    public sealed class QuestInstance
    {
        [SerializeField] string questId;
        [SerializeField] QuestState state;
        [SerializeField] List<QuestObjectiveProgress> objectives = new List<QuestObjectiveProgress>();
        [SerializeField] bool rewardClaimed;

        [NonSerialized] QuestDefinition _definition;

        public QuestInstance() { }

        internal QuestInstance(QuestDefinition definition)
        {
            questId = definition.QuestId;
            _definition = definition;
            var defs = definition.Objectives;
            for (int i = 0; i < defs.Length; i++) objectives.Add(new QuestObjectiveProgress(defs[i]));
        }

        public string QuestId => questId;
        public QuestState State { get => state; internal set => state = value; }
        public IReadOnlyList<QuestObjectiveProgress> Objectives => objectives;
        public QuestDefinition Definition => _definition;

        public bool IsActive => state == QuestState.Active;
        public bool IsCompleted => state == QuestState.Completed || state == QuestState.RewardClaimed;
        public bool IsRewardClaimed => rewardClaimed || state == QuestState.RewardClaimed;
        public bool CanClaim => state == QuestState.Completed && !rewardClaimed;

        public bool AllObjectivesComplete
        {
            get
            {
                if (objectives.Count == 0) return false;
                for (int i = 0; i < objectives.Count; i++)
                    if (!objectives[i].IsComplete) return false;
                return true;
            }
        }

        internal List<QuestObjectiveProgress> ObjectiveList => objectives;
        internal bool RewardClaimedFlag { get => rewardClaimed; set => rewardClaimed = value; }
        internal void BindDefinition(QuestDefinition definition) => _definition = definition;

        internal QuestInstance CloneData()
        {
            var copy = new QuestInstance { questId = questId, state = state, rewardClaimed = rewardClaimed, _definition = _definition };
            for (int i = 0; i < objectives.Count; i++) copy.objectives.Add(objectives[i].Clone());
            return copy;
        }
    }

    /// <summary>The whole quest save payload: versioned, ids and progress only. Definitions are never stored.</summary>
    [Serializable]
    public sealed class QuestSaveData
    {
        public const int CurrentVersion = 1;

        [SerializeField] int version = CurrentVersion;
        [SerializeField] string pinnedQuestId;
        [SerializeField] List<QuestInstance> quests = new List<QuestInstance>();

        public int Version => version;
        public string PinnedQuestId { get => pinnedQuestId; set => pinnedQuestId = value; }
        public List<QuestInstance> Quests => quests;
    }
}
