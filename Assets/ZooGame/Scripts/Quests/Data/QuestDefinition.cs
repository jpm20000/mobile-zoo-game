using System;
using UnityEngine;
using ZooGame.Data;

namespace ZooGame.Quests
{
    /// <summary>One thing a quest asks for. Plain serializable data; the id is stable within the quest and is what saves refer to.</summary>
    [Serializable]
    public sealed class QuestObjectiveDefinition
    {
        [Tooltip("Stable id, unique within the quest (saved with progress). Do not rename once released.")]
        [SerializeField] string objectiveId = "objective";
        [SerializeField] QuestObjectiveType type;
        [Tooltip("Placeable id, species id or staff role name, depending on the type. Empty = any.")]
        [SerializeField] string targetId;
        [Tooltip("PlaceObject only: match every placeable of this category instead of a single id.")]
        [SerializeField] bool matchCategory;
        [SerializeField] PlaceableCategory category;
        [Tooltip("Value to reach (count, 0-100 score, star or level).")]
        [SerializeField, Min(1)] int targetValue = 1;
        [Tooltip("Optional text shown instead of the generated one.")]
        [SerializeField] string displayText;

        public QuestObjectiveDefinition() { }

        public QuestObjectiveDefinition(string objectiveId, QuestObjectiveType type, int targetValue, string targetId = null,
            string displayText = null)
        {
            this.objectiveId = objectiveId;
            this.type = type;
            this.targetValue = Math.Max(1, targetValue);
            this.targetId = targetId;
            this.displayText = displayText;
        }

        public string ObjectiveId => objectiveId;
        public QuestObjectiveType Type => type;
        /// <summary>Null when unset.</summary>
        public string TargetId => string.IsNullOrEmpty(targetId) ? null : targetId;
        public bool MatchCategory => matchCategory;
        public PlaceableCategory Category => category;
        public int TargetValue => Math.Max(1, targetValue);
        public string DisplayText => string.IsNullOrEmpty(displayText) ? null : displayText;

        /// <summary>Makes a PlaceObject objective match a whole category.</summary>
        public QuestObjectiveDefinition WithCategory(PlaceableCategory value)
        {
            category = value;
            matchCategory = true;
            return this;
        }
    }

    /// <summary>One reward of a quest.</summary>
    [Serializable]
    public sealed class QuestRewardDefinition
    {
        [SerializeField] QuestRewardType type;
        [SerializeField, Min(1)] int amount = 1;

        public QuestRewardDefinition() { }

        public QuestRewardDefinition(QuestRewardType type, int amount)
        {
            this.type = type;
            this.amount = amount;
        }

        public QuestRewardType Type => type;
        public int Amount => amount;
    }

    /// <summary>
    /// Static data for one quest. Create via Assets > Create > ZooGame > Quest Definition. Holds no progress: that lives in
    /// <see cref="QuestInstance"/>, which refers back here by <see cref="QuestId"/>. Never serialized into a save.
    /// </summary>
    [CreateAssetMenu(menuName = "ZooGame/Quest Definition", fileName = "Quest")]
    public sealed class QuestDefinition : ScriptableObject
    {
        [Tooltip("Stable id used by saves and prerequisites. Do not rename once released.")]
        [SerializeField] string questId = "quest";
        [SerializeField] string title = "Quest";
        [TextArea(1, 4)]
        [SerializeField] string description;
        [SerializeField] QuestObjectiveDefinition[] objectives = new QuestObjectiveDefinition[0];
        [SerializeField] QuestRewardDefinition[] rewards = new QuestRewardDefinition[0];
        [Tooltip("Quest ids that must be completed first.")]
        [SerializeField] string[] prerequisiteQuestIds = new string[0];
        [Tooltip("Zoo level needed to unlock (0 or 1 = none).")]
        [SerializeField, Min(0)] int minimumLevel;
        [Tooltip("Star rating needed to unlock (0 or 1 = none).")]
        [SerializeField, Range(0, 5)] int minimumStars;

        public string QuestId => questId;
        public string Title => title;
        public string Description => description ?? string.Empty;
        public QuestObjectiveDefinition[] Objectives => objectives ?? Array.Empty<QuestObjectiveDefinition>();
        public QuestRewardDefinition[] Rewards => rewards ?? Array.Empty<QuestRewardDefinition>();
        public string[] PrerequisiteQuestIds => prerequisiteQuestIds ?? Array.Empty<string>();
        public int MinimumLevel => minimumLevel;
        public int MinimumStars => minimumStars;

        /// <summary>Fills a definition from code (tests, procedural content).</summary>
        public QuestDefinition Configure(string id, string newTitle, QuestObjectiveDefinition[] newObjectives,
            QuestRewardDefinition[] newRewards = null, string[] prerequisites = null, int level = 0, int stars = 0,
            string newDescription = null)
        {
            questId = id;
            title = newTitle;
            description = newDescription;
            objectives = newObjectives ?? new QuestObjectiveDefinition[0];
            rewards = newRewards ?? new QuestRewardDefinition[0];
            prerequisiteQuestIds = prerequisites ?? new string[0];
            minimumLevel = Mathf.Max(0, level);
            minimumStars = Mathf.Clamp(stars, 0, 5);
            return this;
        }
    }
}
