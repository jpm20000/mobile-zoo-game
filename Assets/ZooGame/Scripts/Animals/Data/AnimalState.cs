namespace ZooGame.Animals
{
    /// <summary>What a spawned animal is doing right now. Runtime-only: it is never saved.</summary>
    public enum AnimalState
    {
        Idle = 0,
        Moving,
        /// <summary>Has an EnclosureId, but that enclosure is open/gone or no longer contains the animal. It stands still.</summary>
        Stranded
    }
}
