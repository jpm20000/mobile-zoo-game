namespace ZooGame.Animals
{
    /// <summary>
    /// The derived, non-persistent result of the last evaluation of one animal: factor scores, suitability, caps and
    /// band. Recomputable from the persistent needs plus the world, so it is never saved. One object per animal is
    /// reused in place by <see cref="AnimalNeedsSystem"/>.
    /// </summary>
    public sealed class AnimalWelfareReport
    {
        public HabitatFactors Factors;
        public float ComfortTarget;
        public float HabitatSuitability;
        public float BasicNeeds;
        /// <summary>Welfare before any critical cap.</summary>
        public float UncappedWelfare;
        /// <summary>The cap that applied (100 = none).</summary>
        public float Cap;
        public float Welfare;
        public WelfareBand Band;
        /// <summary>The animal's enclosure exists, is closed and contains it. Otherwise habitat factors are 0 and the invalid-enclosure cap applies.</summary>
        public bool EnclosureValid;
        public int SameSpeciesCount;
        public int EnclosureArea;
    }
}
