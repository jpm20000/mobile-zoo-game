using System;
using System.Collections.Generic;

namespace ZooGame.Staff
{
    /// <summary>The authoritative store of employed <see cref="StaffInstance"/> records. Knows nothing about scene objects, movement or UI.</summary>
    public interface IStaffRegistry
    {
        int Count { get; }

        /// <summary>Live list for index-based iteration. Copy it before registering or unregistering while iterating.</summary>
        IReadOnlyList<StaffInstance> All { get; }

        /// <summary>Raised after a member was added / after one was removed (already out of the registry).</summary>
        event Action<StaffInstance> Registered;
        event Action<StaffInstance> Unregistered;

        /// <summary>False for null, an empty id, or an id already registered.</summary>
        bool Register(StaffInstance staff);

        bool Unregister(string staffId);

        bool TryGet(string staffId, out StaffInstance staff);

        bool Contains(string staffId);

        /// <summary>Appends every member to <paramref name="results"/> (a safe snapshot to iterate while changing the registry).</summary>
        void CopyTo(List<StaffInstance> results);

        /// <summary>How many employed members have <paramref name="role"/>.</summary>
        int CountOf(StaffRole role);
    }
}
