using UnityEngine;

namespace ZooGame.Quests
{
    /// <summary>The set of quests in the game, in display order. Create via Assets > Create > ZooGame > Quest Catalog.</summary>
    [CreateAssetMenu(menuName = "ZooGame/Quest Catalog", fileName = "QuestCatalog")]
    public sealed class QuestCatalog : ScriptableObject
    {
        [SerializeField] QuestDefinition[] quests = new QuestDefinition[0];

        public QuestDefinition[] Quests => quests ?? new QuestDefinition[0];

        public QuestLibrary BuildLibrary() => new QuestLibrary(Quests);

        /// <summary>Replaces the list from code (editor setup, tests).</summary>
        public void Set(QuestDefinition[] definitions) => quests = definitions;
    }
}
