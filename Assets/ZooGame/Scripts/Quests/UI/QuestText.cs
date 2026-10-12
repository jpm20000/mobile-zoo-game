using System.Text;

namespace ZooGame.Quests
{
    /// <summary>Plain-text descriptions of objectives and rewards for the quest UI. No Unity types, no state.</summary>
    public static class QuestText
    {
        /// <summary>The objective's label: its display text, or one generated from its type and target.</summary>
        public static string Describe(QuestObjectiveDefinition o)
        {
            if (o == null) return string.Empty;
            if (o.DisplayText != null) return o.DisplayText;

            string target = Humanize(o.TargetId);
            int n = o.TargetValue;
            switch (o.Type)
            {
                case QuestObjectiveType.PlaceObject:
                    if (o.MatchCategory) return "Place " + n + " " + o.Category + " object" + Plural(n);
                    return target != null ? "Place " + Count(n) + target : "Place " + n + " object" + Plural(n);
                case QuestObjectiveType.BuildPath: return "Build " + n + " path tile" + Plural(n);
                case QuestObjectiveType.BuildFence: return "Build " + n + " fence segment" + Plural(n);
                case QuestObjectiveType.CreateEnclosure: return "Create " + n + " enclosure" + Plural(n);
                case QuestObjectiveType.AdoptAnimal:
                    return target != null ? "Adopt " + Count(n) + target : "Adopt " + n + " animal" + Plural(n);
                case QuestObjectiveType.OwnSpeciesCount: return "Own " + n + " species";
                case QuestObjectiveType.OwnAnimalCount:
                    return target != null ? "Own " + n + " " + target : "Own " + n + " animal" + Plural(n);
                case QuestObjectiveType.ReachAnimalWelfare: return "Reach animal welfare " + n;
                case QuestObjectiveType.ReachVisitorCount: return "Reach " + n + " visitor" + Plural(n);
                case QuestObjectiveType.ReachVisitorHappiness: return "Reach visitor happiness " + n;
                case QuestObjectiveType.HireStaff:
                    return target != null ? "Hire " + Count(n) + target : "Hire " + n + " staff";
                case QuestObjectiveType.ReachZooRating: return "Reach a " + n + "-star zoo rating";
                case QuestObjectiveType.ReachZooLevel: return "Reach zoo level " + n;
                case QuestObjectiveType.EarnMoney: return "Earn $" + n + " from visitors";
                default: return o.Type.ToString();
            }
        }

        /// <summary>"$500, 2 tokens, 50 XP" or "none".</summary>
        public static string DescribeRewards(QuestDefinition def)
        {
            if (def == null || def.Rewards.Length == 0) return "none";
            var sb = new StringBuilder(48);
            var rewards = def.Rewards;
            for (int i = 0; i < rewards.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var r = rewards[i];
                switch (r.Type)
                {
                    case QuestRewardType.Money: sb.Append('$').Append(r.Amount); break;
                    case QuestRewardType.ConservationTokens: sb.Append(r.Amount).Append(r.Amount == 1 ? " token" : " tokens"); break;
                    case QuestRewardType.Xp: sb.Append(r.Amount).Append(" XP"); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>"[x] Label (3/5)" or "[ ] Label (3/5)".</summary>
        public static void AppendObjective(StringBuilder sb, QuestObjectiveProgress p)
        {
            sb.Append(p.IsComplete ? "[x] " : "[ ] ").Append(Describe(p.Definition));
            if (p.TargetValue > 1) sb.Append(" (").Append(p.CurrentValue).Append('/').Append(p.TargetValue).Append(')');
        }

        static string Count(int n) => n > 1 ? n + " " : string.Empty;

        static string Plural(int n) => n == 1 ? string.Empty : "s";

        static string Humanize(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return id.Replace('_', ' ').Replace('-', ' ');
        }
    }
}
