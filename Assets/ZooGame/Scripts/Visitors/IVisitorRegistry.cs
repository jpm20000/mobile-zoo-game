using System;
using System.Collections.Generic;

namespace ZooGame.Visitors
{
    /// <summary>The authoritative store of active <see cref="VisitorInstance"/> records. Knows nothing about scene objects, movement or UI.</summary>
    public interface IVisitorRegistry
    {
        /// <summary>Number of visitors currently in the zoo.</summary>
        int Count { get; }

        /// <summary>Live list for index-based iteration (no enumerator allocation). Copy it before registering or unregistering while iterating.</summary>
        IReadOnlyList<VisitorInstance> All { get; }

        event Action<VisitorInstance> Registered;
        event Action<VisitorInstance> Unregistered;

        /// <summary>False for null, an empty id, or an id already registered.</summary>
        bool Register(VisitorInstance visitor);

        bool Unregister(string visitorId);

        bool TryGet(string visitorId, out VisitorInstance visitor);

        bool Contains(string visitorId);

        /// <summary>Appends every visitor to <paramref name="results"/> (a safe snapshot to iterate while changing the registry).</summary>
        void CopyTo(List<VisitorInstance> results);
    }
}
