using System;

namespace ZooGame.Animals
{
    /// <summary>Read access to cached per-enclosure habitat summaries.</summary>
    public interface IEnclosureHabitatService
    {
        /// <summary>Raised when something that can change a summary changed (enclosure shape, terrain, placed objects, residents).</summary>
        event Action Changed;

        /// <summary>
        /// The summary for a closed enclosure, rebuilt first if its contents changed since the last call. False for a
        /// missing, malformed or open enclosure. The returned object is owned by the service; do not keep it across frames.
        /// </summary>
        bool TryGet(string enclosureId, out EnclosureHabitat habitat);
    }
}
