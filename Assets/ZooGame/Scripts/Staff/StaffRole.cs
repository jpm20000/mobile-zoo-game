using ZooGame.Data;

namespace ZooGame.Staff
{
    /// <summary>What a staff member does. Append only.</summary>
    public enum StaffRole
    {
        None = 0,
        /// <summary>Runs one food or drink stall. The only role with a facility assignment.</summary>
        Vendor,
        /// <summary>Works from the shared Keeper task pool.</summary>
        Keeper,
        /// <summary>Works from the shared Maintenance task pool.</summary>
        MaintenanceWorker,
        /// <summary>Task based only in M8.</summary>
        Veterinarian
    }

    /// <summary>What a staff member is doing. Changed only by <see cref="StaffWorkService"/>. Append only.</summary>
    public enum StaffState
    {
        Idle = 0,
        /// <summary>Walking to the assigned facility or to a task target.</summary>
        Travelling,
        /// <summary>Running the assigned facility, or carrying out a task at its target.</summary>
        Working,
        /// <summary>Assigned (vendor) but no path leads to the facility from where the staff member stands.</summary>
        Unreachable
    }

    public static class StaffRules
    {
        /// <summary>Roles that take tasks from a pool and are never assigned to a facility.</summary>
        public static bool IsTaskBased(StaffRole role) =>
            role == StaffRole.Keeper || role == StaffRole.MaintenanceWorker || role == StaffRole.Veterinarian;

        public static bool IsDefined(StaffRole role) => role >= StaffRole.Vendor && role <= StaffRole.Veterinarian;

        /// <summary>The role a facility kind needs, or <see cref="StaffRole.None"/> when it is not staffed (toilets, benches, the entrance).</summary>
        public static StaffRole RoleFor(VisitorFacilityKind kind) =>
            kind == VisitorFacilityKind.FoodStall || kind == VisitorFacilityKind.DrinkStall ? StaffRole.Vendor : StaffRole.None;
    }
}
