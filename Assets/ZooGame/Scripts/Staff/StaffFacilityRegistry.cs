using System;
using System.Collections.Generic;
using ZooGame.Placement;
using ZooGame.Visitors;

namespace ZooGame.Staff
{
    /// <summary>
    /// A placed object that can be staffed. Created and removed only by <see cref="StaffFacilityRegistry"/> as objects are
    /// placed and demolished; <see cref="AssignedStaffId"/> is written only by <see cref="StaffAssignmentService"/>.
    /// The id is the same "facility-N" the visitor destinations use, and it survives a move.
    /// </summary>
    public sealed class StaffFacility
    {
        internal StaffFacility(string facilityId, int placedObjectId, StaffRole allowedRole, string displayName)
        {
            FacilityId = facilityId;
            PlacedObjectId = placedObjectId;
            AllowedRole = allowedRole;
            DisplayName = displayName;
        }

        public string FacilityId { get; }
        public int PlacedObjectId { get; }
        /// <summary>The only role that may be assigned here.</summary>
        public StaffRole AllowedRole { get; }
        public string DisplayName { get; }

        /// <summary>A disabled facility cannot be assigned to and is not operational.</summary>
        public bool Enabled { get; internal set; } = true;

        /// <summary>Id of the staff member running it; null when nobody does.</summary>
        public string AssignedStaffId { get; internal set; }

        public bool IsStaffed => AssignedStaffId != null;
    }

    /// <summary>
    /// Tracks every staffable placed object (food and drink stalls) by following the placement map. It owns the
    /// <see cref="StaffFacility"/> records, so a facility keeps its record, and therefore its assignment, while the
    /// player moves or rotates it; only removing the object removes the record.
    /// </summary>
    public sealed class StaffFacilityRegistry : IDisposable
    {
        readonly PlacementMap _placement;
        readonly Dictionary<string, StaffFacility> _byId = new Dictionary<string, StaffFacility>();
        readonly List<StaffFacility> _all = new List<StaffFacility>(8);

        public StaffFacilityRegistry(PlacementMap placement)
        {
            _placement = placement ?? throw new ArgumentNullException(nameof(placement));
            foreach (var obj in _placement.Objects) Track(obj);
            _placement.Placed += OnPlaced;
            _placement.Moved += OnMoved;
            _placement.Removed += OnRemoved;
        }

        public int Count => _all.Count;
        public IReadOnlyList<StaffFacility> All => _all;

        /// <summary>Raised after a staffable object was placed.</summary>
        public event Action<StaffFacility> Added;

        /// <summary>Raised after a staffable object was moved or rotated (the record, and its assignment, are unchanged).</summary>
        public event Action<StaffFacility> Moved;

        /// <summary>Raised after a staffable object was removed; the record is already out of the registry.</summary>
        public event Action<StaffFacility> Removed;

        public event Action<StaffFacility> EnabledChanged;

        public void Dispose()
        {
            _placement.Placed -= OnPlaced;
            _placement.Moved -= OnMoved;
            _placement.Removed -= OnRemoved;
        }

        public bool TryGet(string facilityId, out StaffFacility facility)
        {
            if (facilityId == null) { facility = null; return false; }
            return _byId.TryGetValue(facilityId, out facility);
        }

        public bool TryGetForPlacedObject(int placedObjectId, out StaffFacility facility) =>
            TryGet(VisitorDestinationService.FacilityIdFor(placedObjectId), out facility);

        public bool Contains(string facilityId) => facilityId != null && _byId.ContainsKey(facilityId);

        public bool SetEnabled(string facilityId, bool enabled)
        {
            if (!TryGet(facilityId, out var f)) return false;
            if (f.Enabled == enabled) return true;
            f.Enabled = enabled;
            EnabledChanged?.Invoke(f);
            return true;
        }

        public void CopyTo(List<StaffFacility> results)
        {
            for (int i = 0; i < _all.Count; i++) results.Add(_all[i]);
        }

        StaffFacility Track(PlacedObject obj)
        {
            var role = StaffRules.RoleFor(obj.Definition.VisitorFacility.Kind);
            if (role == StaffRole.None) return null;
            string id = VisitorDestinationService.FacilityIdFor(obj.Id);
            if (_byId.ContainsKey(id)) return null;
            var facility = new StaffFacility(id, obj.Id, role, obj.Definition.DisplayName);
            _byId.Add(id, facility);
            _all.Add(facility);
            return facility;
        }

        void OnPlaced(PlacedObject obj)
        {
            var f = Track(obj);
            if (f != null) Added?.Invoke(f);
        }

        void OnMoved(PlacedObject obj)
        {
            if (TryGetForPlacedObject(obj.Id, out var f)) Moved?.Invoke(f);
        }

        void OnRemoved(PlacedObject obj)
        {
            string id = VisitorDestinationService.FacilityIdFor(obj.Id);
            if (!_byId.TryGetValue(id, out var f)) return;
            _byId.Remove(id);
            _all.Remove(f);
            Removed?.Invoke(f);
        }
    }
}
