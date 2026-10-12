using System;

namespace ZooGame.Staff
{
    public enum StaffAssignmentResult
    {
        Success = 0,
        UnknownStaff,
        UnknownFacility,
        /// <summary>The member's role is not the one the facility allows (this also covers every task-based role).</summary>
        IncompatibleRole,
        FacilityDisabled,
        /// <summary>Assign was asked for a member who already runs a different facility (use Reassign to move them).</summary>
        StaffAlreadyAssigned,
        /// <summary>Somebody else already runs the facility.</summary>
        FacilityAlreadyStaffed,
        /// <summary>The member already runs exactly this facility: nothing to do.</summary>
        AlreadyAssignedToFacility,
        /// <summary>Unassign or reassign for a member who runs no facility.</summary>
        NotAssigned
    }

    /// <summary>One assignment change. Either id may be null (an assignment made, or one cleared).</summary>
    public readonly struct StaffAssignmentChange
    {
        public readonly string StaffId;
        public readonly string PreviousFacilityId;
        public readonly string FacilityId;

        public StaffAssignmentChange(string staffId, string previousFacilityId, string facilityId)
        {
            StaffId = staffId;
            PreviousFacilityId = previousFacilityId;
            FacilityId = facilityId;
        }
    }

    /// <summary>
    /// The only writer of <see cref="StaffInstance.AssignedFacilityId"/> and <see cref="StaffFacility.AssignedStaffId"/>.
    /// Both ids are updated together, after validation, so a rejected request changes nothing and the two sides can
    /// never disagree. It also owns the cleanup when a member is dismissed or a facility is demolished.
    ///
    /// Validation order (first failure wins): valid staff, valid facility, compatible role, enabled facility, member not
    /// assigned elsewhere, facility not already staffed.
    /// </summary>
    public sealed class StaffAssignmentService : IDisposable
    {
        readonly IStaffRegistry _staff;
        readonly StaffFacilityRegistry _facilities;

        public StaffAssignmentService(IStaffRegistry staff, StaffFacilityRegistry facilities)
        {
            _staff = staff ?? throw new ArgumentNullException(nameof(staff));
            _facilities = facilities ?? throw new ArgumentNullException(nameof(facilities));
            _staff.Unregistered += OnStaffRemoved;
            _facilities.Removed += OnFacilityRemoved;
        }

        /// <summary>Raised after an assignment was made, moved or cleared (both ids already in step).</summary>
        public event Action<StaffAssignmentChange> Changed;

        public void Dispose()
        {
            _staff.Unregistered -= OnStaffRemoved;
            _facilities.Removed -= OnFacilityRemoved;
        }

        // ---- Queries ---------------------------------------------------------------------------------------

        /// <summary>The facility a member runs, or false.</summary>
        public bool TryGetFacilityOf(string staffId, out StaffFacility facility)
        {
            facility = null;
            return _staff.TryGet(staffId, out var s) && s.AssignedFacilityId != null && _facilities.TryGet(s.AssignedFacilityId, out facility);
        }

        /// <summary>The member running a facility, or false.</summary>
        public bool TryGetStaffOf(string facilityId, out StaffInstance staff)
        {
            staff = null;
            return _facilities.TryGet(facilityId, out var f) && f.AssignedStaffId != null && _staff.TryGet(f.AssignedStaffId, out staff);
        }

        /// <summary>What <see cref="Assign"/> would return, without changing anything (for enabling UI buttons).</summary>
        public StaffAssignmentResult CanAssign(string staffId, string facilityId) =>
            ValidateAssign(staffId, facilityId, out _, out _);

        /// <summary>What <see cref="Reassign"/> would return, without changing anything.</summary>
        public StaffAssignmentResult CanReassign(string staffId, string newFacilityId) =>
            ValidateReassign(staffId, newFacilityId, out _, out _, out _);

        // ---- Commands --------------------------------------------------------------------------------------

        /// <summary>Gives an unassigned member a free, compatible, enabled facility.</summary>
        public StaffAssignmentResult Assign(string staffId, string facilityId)
        {
            var result = ValidateAssign(staffId, facilityId, out var staff, out var facility);
            if (result != StaffAssignmentResult.Success) return result;
            Commit(staff, facility, null);
            return StaffAssignmentResult.Success;
        }

        /// <summary>
        /// Moves a member from the facility they run to another. The new facility is validated first; only then is the
        /// old one released and the new one taken, so a failure leaves the original assignment exactly as it was.
        /// </summary>
        public StaffAssignmentResult Reassign(string staffId, string newFacilityId)
        {
            var result = ValidateReassign(staffId, newFacilityId, out var staff, out var facility, out var previous);
            if (result != StaffAssignmentResult.Success) return result;
            Commit(staff, facility, previous);
            return StaffAssignmentResult.Success;
        }

        /// <summary>Releases the facility a member runs.</summary>
        public StaffAssignmentResult Unassign(string staffId)
        {
            if (!_staff.TryGet(staffId, out var staff)) return StaffAssignmentResult.UnknownStaff;
            if (staff.AssignedFacilityId == null) return StaffAssignmentResult.NotAssigned;
            Release(staff);
            return StaffAssignmentResult.Success;
        }

        /// <summary>Releases whoever runs a facility.</summary>
        public StaffAssignmentResult UnassignFacility(string facilityId)
        {
            if (!_facilities.TryGet(facilityId, out var facility)) return StaffAssignmentResult.UnknownFacility;
            if (facility.AssignedStaffId == null) return StaffAssignmentResult.NotAssigned;
            if (_staff.TryGet(facility.AssignedStaffId, out var staff))
            {
                Release(staff);
                return StaffAssignmentResult.Success;
            }
            facility.AssignedStaffId = null; // stale id: nobody to notify
            return StaffAssignmentResult.Success;
        }

        // ---- Validation ------------------------------------------------------------------------------------

        StaffAssignmentResult ValidateTarget(string staffId, string facilityId, out StaffInstance staff, out StaffFacility facility)
        {
            facility = null;
            if (!_staff.TryGet(staffId, out staff)) return StaffAssignmentResult.UnknownStaff;
            if (!_facilities.TryGet(facilityId, out facility)) return StaffAssignmentResult.UnknownFacility;
            if (staff.Role != facility.AllowedRole) return StaffAssignmentResult.IncompatibleRole;
            if (!facility.Enabled) return StaffAssignmentResult.FacilityDisabled;
            return StaffAssignmentResult.Success;
        }

        StaffAssignmentResult ValidateAssign(string staffId, string facilityId, out StaffInstance staff, out StaffFacility facility)
        {
            var result = ValidateTarget(staffId, facilityId, out staff, out facility);
            if (result != StaffAssignmentResult.Success) return result;
            if (staff.AssignedFacilityId != null)
                return staff.AssignedFacilityId == facilityId
                    ? StaffAssignmentResult.AlreadyAssignedToFacility
                    : StaffAssignmentResult.StaffAlreadyAssigned;
            if (facility.AssignedStaffId != null) return StaffAssignmentResult.FacilityAlreadyStaffed;
            return StaffAssignmentResult.Success;
        }

        StaffAssignmentResult ValidateReassign(string staffId, string newFacilityId, out StaffInstance staff,
            out StaffFacility facility, out StaffFacility previous)
        {
            previous = null;
            var result = ValidateTarget(staffId, newFacilityId, out staff, out facility);
            if (result != StaffAssignmentResult.Success) return result;
            if (staff.AssignedFacilityId == null) return StaffAssignmentResult.NotAssigned;
            if (staff.AssignedFacilityId == newFacilityId) return StaffAssignmentResult.AlreadyAssignedToFacility;
            if (facility.AssignedStaffId != null) return StaffAssignmentResult.FacilityAlreadyStaffed;
            _facilities.TryGet(staff.AssignedFacilityId, out previous);
            return StaffAssignmentResult.Success;
        }

        // ---- Mutation (the only place the two ids are written) ---------------------------------------------

        void Commit(StaffInstance staff, StaffFacility facility, StaffFacility previous)
        {
            string previousId = staff.AssignedFacilityId;
            if (previous != null && previous.AssignedStaffId == staff.StaffId) previous.AssignedStaffId = null;
            staff.AssignedFacilityId = facility.FacilityId;
            facility.AssignedStaffId = staff.StaffId;
            Changed?.Invoke(new StaffAssignmentChange(staff.StaffId, previousId, facility.FacilityId));
        }

        void Release(StaffInstance staff)
        {
            string previousId = staff.AssignedFacilityId;
            if (previousId != null && _facilities.TryGet(previousId, out var facility) && facility.AssignedStaffId == staff.StaffId)
                facility.AssignedStaffId = null;
            staff.AssignedFacilityId = null;
            Changed?.Invoke(new StaffAssignmentChange(staff.StaffId, previousId, null));
        }

        // ---- Cleanup ---------------------------------------------------------------------------------------

        void OnStaffRemoved(StaffInstance staff)
        {
            if (staff.AssignedFacilityId != null) Release(staff);
        }

        void OnFacilityRemoved(StaffFacility facility)
        {
            string staffId = facility.AssignedStaffId;
            if (staffId == null) return;
            facility.AssignedStaffId = null;
            if (!_staff.TryGet(staffId, out var staff)) return;
            string previous = staff.AssignedFacilityId;
            staff.AssignedFacilityId = null;
            Changed?.Invoke(new StaffAssignmentChange(staffId, previous, null));
        }
    }
}
