using System;
using UnityEngine;

namespace ZooGame.Staff
{
    /// <summary>
    /// One staff member's runtime data: plain serializable state with no scene references. The
    /// <see cref="StaffRegistry"/> owns it while the member is employed; the id never changes.
    /// <see cref="AssignedFacilityId"/> and <see cref="ActiveTaskId"/> have internal setters: the facility assignment is
    /// written only by <see cref="StaffAssignmentService"/> (together with <see cref="StaffFacility.AssignedStaffId"/>)
    /// and the task link only by <see cref="StaffTaskPool"/>, so UI and tests cannot put the two sides out of step.
    /// </summary>
    [Serializable]
    public sealed class StaffInstance
    {
        [SerializeField] string staffId;
        [SerializeField] StaffRole role;
        [SerializeField] string displayName;
        [SerializeField] StaffState state;
        [SerializeField] Vector3 position;
        [SerializeField] string assignedFacilityId;
        [SerializeField] string activeTaskId;

        public StaffInstance(string staffId, StaffRole role, string displayName, Vector3 position)
        {
            if (string.IsNullOrEmpty(staffId)) throw new ArgumentException("A staff member needs an id.", nameof(staffId));
            this.staffId = staffId;
            this.role = role;
            this.displayName = string.IsNullOrEmpty(displayName) ? role.ToString() : displayName;
            this.position = position;
        }

        public static StaffInstance CreateNew(StaffRole role, string displayName, Vector3 position) =>
            new StaffInstance(NewId(), role, displayName, position);

        public static string NewId() => "staff-" + Guid.NewGuid().ToString("N");

        public string StaffId => staffId;
        public StaffRole Role => role;
        public string DisplayName => displayName;
        public StaffState State { get => state; internal set => state = value; }
        public Vector3 Position { get => position; set => position = value; }

        /// <summary>Id of the facility this member runs (<see cref="StaffFacility.FacilityId"/>); null when unassigned.</summary>
        public string AssignedFacilityId
        {
            get => string.IsNullOrEmpty(assignedFacilityId) ? null : assignedFacilityId;
            internal set => assignedFacilityId = string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>Id of the task being carried out (at most one); null when none.</summary>
        public string ActiveTaskId
        {
            get => string.IsNullOrEmpty(activeTaskId) ? null : activeTaskId;
            internal set => activeTaskId = string.IsNullOrEmpty(value) ? null : value;
        }

        public bool IsAssignedToFacility => AssignedFacilityId != null;
        public bool HasActiveTask => ActiveTaskId != null;
    }
}
