using System;
using ZooGame.Data;

namespace ZooGame.Quests
{
    /// <summary>
    /// The facts objectives are measured against. Implemented over the live systems by <see cref="QuestWorldQuery"/>
    /// and by fakes in tests. Every member is a cheap read: they are called only when a relevant event arrived.
    /// </summary>
    public interface IQuestWorld
    {
        /// <summary>Placed objects matching an id (null = any) or, when <paramref name="matchCategory"/>, a category.</summary>
        int CountPlaced(string placeableId, bool matchCategory, PlaceableCategory category);
        int PathCellCount { get; }
        int FenceSegmentCount { get; }
        int EnclosureCount { get; }
        /// <summary>Animals owned, optionally of one species (null = all).</summary>
        int CountAnimals(string speciesId);
        int CountSpecies();
        /// <summary>Average animal welfare, whole points 0-100.</summary>
        int AverageWelfare { get; }
        int VisitorCount { get; }
        /// <summary>Visitor happiness (rolling average), whole points 0-100.</summary>
        int VisitorHappiness { get; }
        /// <summary>Employed staff, optionally of one role name (null = all).</summary>
        int CountStaff(string roleId);
        int Stars { get; }
        int Level { get; }
    }

    /// <summary>
    /// Turns "something happened" into new objective progress. Stateless apart from the world it reads.
    /// <list type="bullet">
    /// <item>Count-style objectives re-read the owned amount from the world when their event arrives.</item>
    /// <item>Cumulative objectives (adopt, earn Money) add the event's amount.</item>
    /// <item>Progress is clamped to the target and never decreases, so a finished objective stays finished.</item>
    /// </list>
    /// </summary>
    public sealed class QuestObjectiveEvaluator
    {
        readonly IQuestWorld _world;

        public QuestObjectiveEvaluator(IQuestWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>The single event kind that changes this objective type.</summary>
        public static QuestEventKind KindOf(QuestObjectiveType type)
        {
            switch (type)
            {
                case QuestObjectiveType.PlaceObject: return QuestEventKind.ObjectsChanged;
                case QuestObjectiveType.BuildPath: return QuestEventKind.PathsChanged;
                case QuestObjectiveType.BuildFence: return QuestEventKind.FencesChanged;
                case QuestObjectiveType.CreateEnclosure: return QuestEventKind.EnclosuresChanged;
                case QuestObjectiveType.AdoptAnimal: return QuestEventKind.AnimalAdopted;
                case QuestObjectiveType.OwnSpeciesCount:
                case QuestObjectiveType.OwnAnimalCount: return QuestEventKind.AnimalsChanged;
                case QuestObjectiveType.ReachAnimalWelfare:
                case QuestObjectiveType.ReachVisitorHappiness:
                case QuestObjectiveType.ReachZooRating: return QuestEventKind.RatingChanged;
                case QuestObjectiveType.ReachVisitorCount: return QuestEventKind.VisitorsChanged;
                case QuestObjectiveType.HireStaff: return QuestEventKind.StaffChanged;
                case QuestObjectiveType.ReachZooLevel: return QuestEventKind.LevelChanged;
                case QuestObjectiveType.EarnMoney: return QuestEventKind.MoneyEarned;
                default: return QuestEventKind.Count;
            }
        }

        public static bool IsCumulative(QuestObjectiveType type) =>
            type == QuestObjectiveType.AdoptAnimal || type == QuestObjectiveType.EarnMoney;

        /// <summary>
        /// The new progress for an objective. <paramref name="initial"/> is the baseline read when a quest becomes active
        /// (count-style objectives pick up what already exists; cumulative ones start from the current value).
        /// </summary>
        public int Evaluate(QuestObjectiveDefinition objective, int current, in QuestEvent ev, bool initial)
        {
            if (objective == null) return current;
            int target = objective.TargetValue;
            long value;

            switch (objective.Type)
            {
                case QuestObjectiveType.AdoptAnimal:
                    if (initial || ev.Kind != QuestEventKind.AnimalAdopted) return current;
                    if (objective.TargetId != null && objective.TargetId != ev.Id) return current;
                    value = (long)current + Math.Max(1L, ev.Amount);
                    break;

                case QuestObjectiveType.EarnMoney:
                    if (initial || ev.Kind != QuestEventKind.MoneyEarned || ev.Amount <= 0) return current;
                    value = (long)current + ev.Amount;
                    break;

                default:
                    if (!initial && ev.Kind != KindOf(objective.Type)) return current;
                    value = Measure(objective);
                    break;
            }

            if (value > target) value = target;
            if (value < current) value = current; // progress never goes backwards
            return (int)value;
        }

        long Measure(QuestObjectiveDefinition o)
        {
            switch (o.Type)
            {
                case QuestObjectiveType.PlaceObject: return _world.CountPlaced(o.TargetId, o.MatchCategory, o.Category);
                case QuestObjectiveType.BuildPath: return _world.PathCellCount;
                case QuestObjectiveType.BuildFence: return _world.FenceSegmentCount;
                case QuestObjectiveType.CreateEnclosure: return _world.EnclosureCount;
                case QuestObjectiveType.OwnSpeciesCount: return _world.CountSpecies();
                case QuestObjectiveType.OwnAnimalCount: return _world.CountAnimals(o.TargetId);
                case QuestObjectiveType.ReachAnimalWelfare: return _world.AverageWelfare;
                case QuestObjectiveType.ReachVisitorCount: return _world.VisitorCount;
                case QuestObjectiveType.ReachVisitorHappiness: return _world.VisitorHappiness;
                case QuestObjectiveType.HireStaff: return _world.CountStaff(o.TargetId);
                case QuestObjectiveType.ReachZooRating: return _world.Stars;
                case QuestObjectiveType.ReachZooLevel: return _world.Level;
                default: return 0;
            }
        }
    }
}
