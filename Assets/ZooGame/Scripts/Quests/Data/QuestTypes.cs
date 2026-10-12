namespace ZooGame.Quests
{
    /// <summary>Where a quest is in its lifecycle. Changed only by <see cref="QuestService"/>. Append only: values are saved.</summary>
    public enum QuestState
    {
        /// <summary>Prerequisites, zoo level or star rating not met yet.</summary>
        Locked = 0,
        /// <summary>Unlocked but not started (only seen when the service does not auto-start).</summary>
        Available = 1,
        /// <summary>In progress: objectives are tracked.</summary>
        Active = 2,
        /// <summary>All objectives done; the reward is waiting to be claimed.</summary>
        Completed = 3,
        /// <summary>Reward granted. Terminal.</summary>
        RewardClaimed = 4
    }

    /// <summary>
    /// What an objective measures. Append only: values are stored in assets.
    /// Count-style objectives read the owned amount from the live systems and never go backwards; the two cumulative ones
    /// (<see cref="AdoptAnimal"/>, <see cref="EarnMoney"/>) add up what happens after the quest started.
    /// </summary>
    public enum QuestObjectiveType
    {
        /// <summary>Placed objects: of one definition (TargetId), of one category, or any.</summary>
        PlaceObject = 0,
        /// <summary>Path cells built.</summary>
        BuildPath,
        /// <summary>Fence and gate segments built.</summary>
        BuildFence,
        /// <summary>Closed enclosures.</summary>
        CreateEnclosure,
        /// <summary>Adoptions performed after the quest started (TargetId = species id, empty = any).</summary>
        AdoptAnimal,
        /// <summary>Distinct species owned.</summary>
        OwnSpeciesCount,
        /// <summary>Animals owned (TargetId = species id, empty = all).</summary>
        OwnAnimalCount,
        /// <summary>Average animal welfare, 0-100.</summary>
        ReachAnimalWelfare,
        /// <summary>Visitors in the zoo at once.</summary>
        ReachVisitorCount,
        /// <summary>Visitor happiness (rolling average), 0-100.</summary>
        ReachVisitorHappiness,
        /// <summary>Staff employed (TargetId = role name, e.g. "Keeper"; empty = any).</summary>
        HireStaff,
        /// <summary>Zoo star rating (1-5).</summary>
        ReachZooRating,
        /// <summary>Zoo level (from XP).</summary>
        ReachZooLevel,
        /// <summary>Money earned from visitors after the quest started (refunds, debug grants and quest rewards excluded).</summary>
        EarnMoney
    }

    /// <summary>What a reward grants. Append only: values are stored in assets.</summary>
    public enum QuestRewardType
    {
        Money = 0,
        ConservationTokens,
        Xp
    }

    /// <summary>The gameplay facts a quest can wait for. Raised through <see cref="QuestService.Notify"/>.</summary>
    public enum QuestEventKind
    {
        ObjectsChanged = 0,
        PathsChanged,
        FencesChanged,
        EnclosuresChanged,
        /// <summary>Id = species id.</summary>
        AnimalAdopted,
        /// <summary>An animal was added, removed or moved.</summary>
        AnimalsChanged,
        VisitorsChanged,
        StaffChanged,
        /// <summary>The zoo rating recalculated and something changed (welfare, happiness, stars).</summary>
        RatingChanged,
        LevelChanged,
        /// <summary>Amount = Money earned.</summary>
        MoneyEarned,

        Count
    }

    /// <summary>One gameplay fact for the quest system. Small and allocation free.</summary>
    public readonly struct QuestEvent
    {
        public readonly QuestEventKind Kind;
        public readonly string Id;
        public readonly long Amount;

        public QuestEvent(QuestEventKind kind, string id = null, long amount = 0)
        {
            Kind = kind;
            Id = id;
            Amount = amount;
        }
    }
}
