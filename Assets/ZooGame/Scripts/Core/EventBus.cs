using System;
using System.Collections.Generic;

namespace ZooGame.Core
{
    /// <summary>Marker for strongly typed game events. Events should be small readonly structs.</summary>
    public interface IGameEvent { }

    /// <summary>
    /// Lightweight typed publish/subscribe bus. Instance-based (owned by the game context), not a static singleton.
    /// Publishing allocates nothing; subscribing/unsubscribing allocates and should happen on enable/disable, not per frame.
    /// Handlers must not throw; an exception in one handler aborts delivery to later handlers for that publish.
    /// </summary>
    public sealed class EventBus
    {
        readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>();

        public void Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _handlers.TryGetValue(typeof(T), out var existing);
            _handlers[typeof(T)] = Delegate.Combine(existing, handler);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            if (handler == null || !_handlers.TryGetValue(typeof(T), out var existing)) return;
            var remaining = Delegate.Remove(existing, handler);
            if (remaining == null) _handlers.Remove(typeof(T));
            else _handlers[typeof(T)] = remaining;
        }

        public void Publish<T>(in T gameEvent) where T : struct, IGameEvent
        {
            if (_handlers.TryGetValue(typeof(T), out var d)) ((Action<T>)d)(gameEvent);
        }
    }
}
