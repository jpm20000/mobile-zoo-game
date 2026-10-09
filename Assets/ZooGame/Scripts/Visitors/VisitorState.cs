namespace ZooGame.Visitors
{
    /// <summary>What a visitor is doing. Changed only by <see cref="VisitorDecisionService"/>. Append only.</summary>
    public enum VisitorState
    {
        /// <summary>Just spawned at the entrance and stepping onto the path network.</summary>
        Entering = 0,
        /// <summary>Between activities; the next decision is due.</summary>
        ChoosingDestination,
        /// <summary>Walking to an attraction or a bench.</summary>
        Walking,
        ViewingAnimal,
        SeekingFood,
        SeekingDrink,
        SeekingToilet,
        Resting,
        /// <summary>Heading for the zoo exit.</summary>
        Exiting
    }

    /// <summary>The four needs that can send a visitor to a facility.</summary>
    public enum VisitorNeed
    {
        None = 0,
        Food,
        Drink,
        Toilet,
        Rest
    }
}
