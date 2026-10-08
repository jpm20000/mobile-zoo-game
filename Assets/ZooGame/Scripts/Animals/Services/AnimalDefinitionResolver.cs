using System.Collections.Generic;
using UnityEngine;

namespace ZooGame.Animals
{
    /// <summary>Dictionary lookup built once from the definitions assigned in the scene; no Resources.Load during play.</summary>
    public sealed class AnimalDefinitionResolver : IAnimalDefinitionResolver
    {
        readonly Dictionary<string, AnimalDefinition> _bySpecies = new Dictionary<string, AnimalDefinition>();

        public AnimalDefinitionResolver(IEnumerable<AnimalDefinition> definitions)
        {
            if (definitions == null) return;
            foreach (var d in definitions)
            {
                if (d == null || string.IsNullOrEmpty(d.SpeciesId)) continue;
                if (_bySpecies.ContainsKey(d.SpeciesId))
                {
                    Debug.LogWarning("Duplicate SpeciesId '" + d.SpeciesId + "'; keeping the first definition.");
                    continue;
                }
                _bySpecies.Add(d.SpeciesId, d);
            }
        }

        public int Count => _bySpecies.Count;

        public bool TryGet(string speciesId, out AnimalDefinition definition)
        {
            if (speciesId == null) { definition = null; return false; }
            return _bySpecies.TryGetValue(speciesId, out definition);
        }
    }
}
