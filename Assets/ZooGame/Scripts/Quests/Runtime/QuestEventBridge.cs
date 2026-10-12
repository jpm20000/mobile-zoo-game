using System;
using ZooGame.Animals;
using ZooGame.Economy;
using ZooGame.Placement;
using ZooGame.Progression;
using ZooGame.Staff;
using ZooGame.Visitors;
using ZooGame.World;

namespace ZooGame.Quests
{
    /// <summary>The existing services a quest system listens to. Every member is optional.</summary>
    public sealed class QuestEventSources
    {
        public PlacementMap Placement;
        public ZooGrid Grid;
        public ConstructionModel Construction;
        public IAnimalRegistry Animals;
        public AnimalPlacementTool AnimalPlacement;
        public IVisitorRegistry Visitors;
        public IStaffRegistry Staff;
        public ZooRatingService Rating;
        public ProgressionService Progression;
        public EconomyService Economy;
    }

    /// <summary>
    /// Translates the existing gameplay events into <see cref="QuestEvent"/>s for <see cref="QuestService.Notify"/>.
    /// Subscribes on construction, unsubscribes on <see cref="Dispose"/>; handlers do no work beyond forwarding, so the
    /// source systems are never slowed down and never know quests exist.
    /// </summary>
    public sealed class QuestEventBridge : IDisposable
    {
        readonly QuestService _quests;
        readonly QuestEventSources _s;
        bool _disposed;

        public QuestEventBridge(QuestService quests, QuestEventSources sources)
        {
            _quests = quests ?? throw new ArgumentNullException(nameof(quests));
            _s = sources ?? throw new ArgumentNullException(nameof(sources));

            if (_s.Placement != null)
            {
                _s.Placement.Placed += OnObject;
                _s.Placement.Removed += OnObject;
            }
            if (_s.Grid != null) _s.Grid.PathsChanged += OnPaths;
            if (_s.Construction != null)
            {
                _s.Construction.FencesChanged += OnFences;
                _s.Construction.Enclosures.Changed += OnEnclosures;
            }
            if (_s.Animals != null)
            {
                _s.Animals.MembershipChanged += OnAnimalMembership;
            }
            if (_s.AnimalPlacement != null) _s.AnimalPlacement.Adopted += OnAdopted;
            if (_s.Visitors != null)
            {
                _s.Visitors.Registered += OnVisitor;
                _s.Visitors.Unregistered += OnVisitor;
            }
            if (_s.Staff != null)
            {
                _s.Staff.Registered += OnStaff;
                _s.Staff.Unregistered += OnStaff;
            }
            if (_s.Rating != null) _s.Rating.Changed += OnRating;
            if (_s.Progression != null) _s.Progression.LevelChanged += OnLevel;
            if (_s.Economy != null) _s.Economy.TransactionRecorded += OnTransaction;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_s.Placement != null)
            {
                _s.Placement.Placed -= OnObject;
                _s.Placement.Removed -= OnObject;
            }
            if (_s.Grid != null) _s.Grid.PathsChanged -= OnPaths;
            if (_s.Construction != null)
            {
                _s.Construction.FencesChanged -= OnFences;
                _s.Construction.Enclosures.Changed -= OnEnclosures;
            }
            if (_s.Animals != null) _s.Animals.MembershipChanged -= OnAnimalMembership;
            if (_s.AnimalPlacement != null) _s.AnimalPlacement.Adopted -= OnAdopted;
            if (_s.Visitors != null)
            {
                _s.Visitors.Registered -= OnVisitor;
                _s.Visitors.Unregistered -= OnVisitor;
            }
            if (_s.Staff != null)
            {
                _s.Staff.Registered -= OnStaff;
                _s.Staff.Unregistered -= OnStaff;
            }
            if (_s.Rating != null) _s.Rating.Changed -= OnRating;
            if (_s.Progression != null) _s.Progression.LevelChanged -= OnLevel;
            if (_s.Economy != null) _s.Economy.TransactionRecorded -= OnTransaction;
        }

        void OnObject(PlacedObject _) => _quests.Notify(new QuestEvent(QuestEventKind.ObjectsChanged));
        void OnPaths() => _quests.Notify(new QuestEvent(QuestEventKind.PathsChanged));
        void OnFences() => _quests.Notify(new QuestEvent(QuestEventKind.FencesChanged));
        void OnEnclosures() => _quests.Notify(new QuestEvent(QuestEventKind.EnclosuresChanged));
        void OnAnimalMembership(string _) => _quests.Notify(new QuestEvent(QuestEventKind.AnimalsChanged));
        void OnAdopted(string speciesId) => _quests.Notify(new QuestEvent(QuestEventKind.AnimalAdopted, speciesId, 1));
        void OnVisitor(VisitorInstance _) => _quests.Notify(new QuestEvent(QuestEventKind.VisitorsChanged));
        void OnStaff(StaffInstance _) => _quests.Notify(new QuestEvent(QuestEventKind.StaffChanged));
        void OnRating(ZooRatingBreakdown _) => _quests.Notify(new QuestEvent(QuestEventKind.RatingChanged));
        void OnLevel(int oldLevel, int newLevel) => _quests.Notify(new QuestEvent(QuestEventKind.LevelChanged, null, newLevel));

        // Money earned = visitor revenue and donations. Refunds are not earnings, and neither are quest rewards or
        // debug grants (the latter would let a reward chain trivially satisfy EarnMoney).
        void OnTransaction(TransactionRecord r)
        {
            if (r.Currency != Currency.Money || r.Type != TransactionType.Income) return;
            if (r.Category == TransactionCategory.DemolitionRefund || r.Category == TransactionCategory.QuestReward
                || r.Category == TransactionCategory.DebugGrant || r.Category == TransactionCategory.ConservationReward) return;
            _quests.Notify(new QuestEvent(QuestEventKind.MoneyEarned, null, r.Amount));
        }
    }
}
