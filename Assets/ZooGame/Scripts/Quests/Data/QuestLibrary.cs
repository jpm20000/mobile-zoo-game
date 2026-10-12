using System.Collections.Generic;

namespace ZooGame.Quests
{
    /// <summary>
    /// Cached lookup of quest definitions by id, in catalogue order. Built once from the catalogue; invalid definitions
    /// (null, empty or duplicate id, no objectives, missing or duplicate objective ids) are skipped and described in
    /// <see cref="Problems"/> so a bad asset cannot break the rest. Unknown prerequisites are kept (and reported): such a
    /// quest simply never unlocks.
    /// </summary>
    public sealed class QuestLibrary
    {
        readonly Dictionary<string, QuestDefinition> _byId = new Dictionary<string, QuestDefinition>();
        readonly List<QuestDefinition> _ordered = new List<QuestDefinition>();
        readonly List<string> _problems = new List<string>();

        public QuestLibrary(IEnumerable<QuestDefinition> definitions)
        {
            if (definitions == null) return;
            foreach (var def in definitions)
            {
                if (def == null) { _problems.Add("Null quest definition skipped."); continue; }
                if (string.IsNullOrEmpty(def.QuestId)) { _problems.Add("Quest '" + def.name + "' has no id."); continue; }
                if (_byId.ContainsKey(def.QuestId)) { _problems.Add("Duplicate quest id '" + def.QuestId + "'."); continue; }
                if (!ObjectivesValid(def)) continue;
                _byId.Add(def.QuestId, def);
                _ordered.Add(def);
            }
            foreach (var def in _ordered)
                foreach (var pre in def.PrerequisiteQuestIds)
                    if (string.IsNullOrEmpty(pre) || !_byId.ContainsKey(pre))
                        _problems.Add("Quest '" + def.QuestId + "' has unknown prerequisite '" + pre + "'.");
        }

        public int Count => _ordered.Count;
        public IReadOnlyList<QuestDefinition> All => _ordered;
        public IReadOnlyList<string> Problems => _problems;

        public bool TryGet(string questId, out QuestDefinition definition)
        {
            if (questId == null) { definition = null; return false; }
            return _byId.TryGetValue(questId, out definition);
        }

        public bool Contains(string questId) => questId != null && _byId.ContainsKey(questId);

        bool ObjectivesValid(QuestDefinition def)
        {
            var objectives = def.Objectives;
            if (objectives.Length == 0) { _problems.Add("Quest '" + def.QuestId + "' has no objectives."); return false; }
            var seen = new HashSet<string>();
            for (int i = 0; i < objectives.Length; i++)
            {
                var o = objectives[i];
                if (o == null || string.IsNullOrEmpty(o.ObjectiveId) || !seen.Add(o.ObjectiveId))
                {
                    _problems.Add("Quest '" + def.QuestId + "' has a missing or duplicate objective id at index " + i + ".");
                    return false;
                }
            }
            return true;
        }
    }
}
