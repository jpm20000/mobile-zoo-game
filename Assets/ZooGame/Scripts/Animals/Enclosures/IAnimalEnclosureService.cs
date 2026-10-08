using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZooGame.Animals
{
    /// <summary>
    /// The only way animal systems see enclosures. Enclosure ids are strings here (the M3 map uses ints), animals
    /// store the string, and nothing outside the implementation touches fences or the enclosure map directly.
    /// </summary>
    public interface IAnimalEnclosureService
    {
        /// <summary>World size of one grid cell, for sampling movement paths.</summary>
        float CellSize { get; }

        /// <summary>Raised when enclosures were created, removed or rebuilt (after a fence/gate change was committed).</summary>
        event Action EnclosuresChanged;

        /// <summary>True when the enclosure exists and is closed.</summary>
        bool IsValidEnclosure(string enclosureId);

        bool ContainsPosition(string enclosureId, Vector3 worldPosition);

        /// <summary>The closed enclosure covering a world position, if any.</summary>
        bool TryGetEnclosureAt(Vector3 worldPosition, out string enclosureId);

        /// <summary>False for a missing or invalid enclosure.</summary>
        bool MeetsMinimumArea(string enclosureId, int minimumArea);

        bool TryGetArea(string enclosureId, out int area);

        /// <summary>A random point comfortably inside the enclosure (clear of its fences).</summary>
        bool TryGetRandomValidPosition(string enclosureId, out Vector3 position);

        /// <summary>Appends the id of every current enclosure.</summary>
        void GetEnclosureIds(List<string> results);
    }
}
