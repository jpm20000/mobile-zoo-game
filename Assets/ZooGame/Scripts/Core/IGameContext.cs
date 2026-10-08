namespace ZooGame.Core
{
    /// <summary>
    /// What gameplay/UI code needs from the game host: shared services plus a few high-level commands.
    /// Depend on this interface instead of the concrete GameManager.
    /// </summary>
    public interface IGameContext
    {
        EventBus Events { get; }
        GameStateMachine State { get; }
        GameClock Clock { get; }

        /// <summary>Playing to Paused and back. No-op in other states.</summary>
        void SetPaused(bool paused);
        void SetSimulationSpeed(SimulationSpeed speed);
    }
}
