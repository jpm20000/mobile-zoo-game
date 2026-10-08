using System.Collections.Generic;
using UnityEngine;

namespace ZooGame.Data
{
    /// <summary>The set of placeables the player can currently choose from. Ordered; the order is the palette order.</summary>
    [CreateAssetMenu(menuName = "ZooGame/Placeable Catalog", fileName = "PlaceableCatalog")]
    public sealed class PlaceableCatalog : ScriptableObject
    {
        [SerializeField] List<PlaceableDefinition> definitions = new List<PlaceableDefinition>();

        public IReadOnlyList<PlaceableDefinition> Definitions => definitions;

        public bool TryGet(string id, out PlaceableDefinition definition)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                var d = definitions[i];
                if (d != null && d.Id == id)
                {
                    definition = d;
                    return true;
                }
            }
            definition = null;
            return false;
        }
    }
}
