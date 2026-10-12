using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Data;
using ZooGame.Economy;
using ZooGame.Progression;
using ZooGame.Quests;

namespace ZooGame.Tests.EditMode.Quests
{
    /// <summary>Settable stand-in for the live systems: tests change a number, then notify the quest service.</summary>
    public sealed class FakeQuestWorld : IQuestWorld
    {
        public readonly Dictionary<string, int> Placed = new Dictionary<string, int>();
        public readonly Dictionary<PlaceableCategory, int> PlacedByCategory = new Dictionary<PlaceableCategory, int>();
        public readonly Dictionary<string, int> Animals = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Staff = new Dictionary<string, int>();

        public int Paths;
        public int Fences;
        public int Enclosures;
        public int Welfare;
        public int Visitors;
        public int Happiness;
        public int StarCount = 1;
        public int LevelCount = 1;

        public int CountPlaced(string placeableId, bool matchCategory, PlaceableCategory category)
        {
            if (matchCategory) return PlacedByCategory.TryGetValue(category, out var c) ? c : 0;
            if (placeableId != null) return Placed.TryGetValue(placeableId, out var n) ? n : 0;
            int total = 0;
            foreach (var v in Placed.Values) total += v;
            return total;
        }

        public int PathCellCount => Paths;
        public int FenceSegmentCount => Fences;
        public int EnclosureCount => Enclosures;

        public int CountAnimals(string speciesId)
        {
            if (speciesId != null) return Animals.TryGetValue(speciesId, out var n) ? n : 0;
            int total = 0;
            foreach (var v in Animals.Values) total += v;
            return total;
        }

        public int CountSpecies()
        {
            int species = 0;
            foreach (var v in Animals.Values) if (v > 0) species++;
            return species;
        }

        public int AverageWelfare => Welfare;
        public int VisitorCount => Visitors;
        public int VisitorHappiness => Happiness;

        public int CountStaff(string roleId)
        {
            if (roleId != null) return Staff.TryGetValue(roleId, out var n) ? n : 0;
            int total = 0;
            foreach (var v in Staff.Values) total += v;
            return total;
        }

        public int Stars => StarCount;
        public int Level => LevelCount;
    }

    /// <summary>Builds definitions and a fully wired quest service over fakes plus the real economy and progression services.</summary>
    public abstract class QuestTestBase
    {
        readonly List<Object> _created = new List<Object>();

        protected FakeQuestWorld World;
        protected EconomyService Economy;
        protected ProgressionService Progression;
        protected QuestService Quests;

        [SetUp]
        public void BaseSetUp()
        {
            World = new FakeQuestWorld();
            Economy = new EconomyService(EconomySettings.Default);
            Progression = new ProgressionService();
        }

        [TearDown]
        public void BaseTearDown()
        {
            for (int i = 0; i < _created.Count; i++) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        protected QuestObjectiveDefinition Obj(string id, QuestObjectiveType type, int target, string targetId = null) =>
            new QuestObjectiveDefinition(id, type, target, targetId);

        protected QuestRewardDefinition Reward(QuestRewardType type, int amount) => new QuestRewardDefinition(type, amount);

        protected QuestDefinition Def(string id, QuestObjectiveDefinition[] objectives, QuestRewardDefinition[] rewards = null,
            string[] prerequisites = null, int level = 0, int stars = 0)
        {
            var def = ScriptableObject.CreateInstance<QuestDefinition>();
            def.name = id;
            def.Configure(id, "Title " + id, objectives, rewards, prerequisites, level, stars, "Description " + id);
            _created.Add(def);
            return def;
        }

        protected QuestDefinition Simple(string id, QuestObjectiveType type = QuestObjectiveType.BuildPath, int target = 1,
            QuestRewardDefinition[] rewards = null, string[] prerequisites = null, int level = 0, int stars = 0) =>
            Def(id, new[] { Obj(id + "-o", type, target) }, rewards, prerequisites, level, stars);

        protected QuestService Make(bool autoStart = true, params QuestDefinition[] definitions)
        {
            Quests = NewService(autoStart, definitions);
            return Quests;
        }

        protected QuestService NewService(bool autoStart, params QuestDefinition[] definitions) =>
            new QuestService(new QuestLibrary(definitions), World, new QuestRewardService(Economy, Progression), autoStart);

        protected void Own(params QuestDefinition[] defs)
        {
            for (int i = 0; i < defs.Length; i++) if (!_created.Contains(defs[i])) _created.Add(defs[i]);
        }

        protected static QuestEvent Ev(QuestEventKind kind, string id = null, long amount = 0) => new QuestEvent(kind, id, amount);

        protected QuestInstance Get(string id)
        {
            Assert.IsTrue(Quests.TryGet(id, out var q), "quest " + id);
            return q;
        }

        protected QuestState StateOf(string id) => Get(id).State;
    }
}
