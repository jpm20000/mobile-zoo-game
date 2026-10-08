using System;
using UnityEngine;

namespace ZooGame.Data
{
    /// <summary>What a placed object contributes to the enclosure it stands in. Append only: serialised in assets.</summary>
    public enum HabitatResourceKind
    {
        None = 0,
        /// <summary>Usable food: lets animals in the enclosure recover hunger.</summary>
        Food,
        /// <summary>Usable drinking water (a trough, not a pond): lets animals recover thirst.</summary>
        DrinkingWater,
        /// <summary>Adds shelter capacity (how many animals it covers).</summary>
        Shelter,
        /// <summary>Adds enrichment value towards the species' requirement.</summary>
        Enrichment
    }

    /// <summary>
    /// Habitat role of a <see cref="PlaceableDefinition"/>. Food and drinking water are unlimited in M5 (their
    /// <see cref="Amount"/> is ignored); a later staff/restocking milestone adds runtime stock keyed by the placed
    /// object's id without changing this data. Shelter's amount is capacity (animals), enrichment's is value.
    /// </summary>
    [Serializable]
    public struct HabitatResourceInfo
    {
        [SerializeField] HabitatResourceKind kind;
        [SerializeField, Min(0f)] float amount;

        public HabitatResourceInfo(HabitatResourceKind kind, float amount)
        {
            this.kind = kind;
            this.amount = Mathf.Max(0f, amount);
        }

        public HabitatResourceKind Kind => kind;
        public float Amount => amount;
        public bool IsResource => kind != HabitatResourceKind.None;
    }
}
