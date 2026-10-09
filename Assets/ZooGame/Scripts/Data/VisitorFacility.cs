using System;
using UnityEngine;

namespace ZooGame.Data
{
    /// <summary>What a placed object is to visitors. Append only: serialised in assets.</summary>
    public enum VisitorFacilityKind
    {
        None = 0,
        /// <summary>Where visitors spawn (they walk in from it) and where they leave the zoo.</summary>
        Entrance,
        FoodStall,
        DrinkStall,
        Toilet,
        Bench
    }

    /// <summary>
    /// Visitor role of a <see cref="PlaceableDefinition"/>. Only the kind lives here; what using the facility does
    /// (recovery amounts, durations) is visitor tuning and lives in the visitor config.
    /// </summary>
    [Serializable]
    public struct VisitorFacilityInfo
    {
        [SerializeField] VisitorFacilityKind kind;

        public VisitorFacilityInfo(VisitorFacilityKind kind) => this.kind = kind;

        public VisitorFacilityKind Kind => kind;
        public bool IsFacility => kind != VisitorFacilityKind.None;
    }
}
