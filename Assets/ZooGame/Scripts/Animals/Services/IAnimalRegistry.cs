using System.Collections.Generic;

namespace ZooGame.Animals
{
    /// <summary>The authoritative store of <see cref="AnimalInstance"/> records. It knows nothing about scene objects, movement, UI or geometry.</summary>
    public interface IAnimalRegistry
    {
        int Count { get; }

        /// <summary>False for null, a missing id, or an id already registered.</summary>
        bool Register(AnimalInstance animal);

        /// <summary>Removes the record. This ends the animal's life in data; despawning its GameObject is separate.</summary>
        bool Unregister(string animalId);

        bool TryGet(string animalId, out AnimalInstance animal);

        IReadOnlyCollection<AnimalInstance> GetAll();

        /// <summary>
        /// Residents of an enclosure; pass null for animals that have none. A live view, valid until the registry next
        /// changes, so copy it if you modify the registry while iterating.
        /// </summary>
        IReadOnlyCollection<AnimalInstance> GetByEnclosure(string enclosureId);

        /// <summary>Moves an animal between enclosures (null clears it) without touching its identity.</summary>
        bool SetEnclosure(string animalId, string enclosureId);
    }
}
