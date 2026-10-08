using System;

namespace ZooGame.Core
{
    public enum GameState
    {
        Boot,
        Loading,
        Playing,
        Paused
    }

    public readonly struct GameStateChanged : IGameEvent
    {
        public readonly GameState Previous;
        public readonly GameState Current;

        public GameStateChanged(GameState previous, GameState current)
        {
            Previous = previous;
            Current = current;
        }
    }

    /// <summary>
    /// Owns the current <see cref="GameState"/> and validates transitions. Publishes <see cref="GameStateChanged"/>.
    /// Valid: Boot to Loading, Loading to Playing, Playing and Paused to each other, Playing or Paused to Loading.
    /// </summary>
    public sealed class GameStateMachine
    {
        readonly EventBus _events;

        public GameState Current { get; private set; } = GameState.Boot;

        public GameStateMachine(EventBus events)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public static bool IsValidTransition(GameState from, GameState to)
        {
            switch (from)
            {
                case GameState.Boot: return to == GameState.Loading;
                case GameState.Loading: return to == GameState.Playing;
                case GameState.Playing: return to == GameState.Paused || to == GameState.Loading;
                case GameState.Paused: return to == GameState.Playing || to == GameState.Loading;
                default: return false;
            }
        }

        /// <summary>Returns false (and changes nothing) if the transition is not valid.</summary>
        public bool TryTransition(GameState to)
        {
            if (!IsValidTransition(Current, to)) return false;
            var previous = Current;
            Current = to;
            _events.Publish(new GameStateChanged(previous, to));
            return true;
        }
    }
}
