using System;
using ZooGame.Economy;
using ZooGame.Visitors;

namespace ZooGame.Staff
{
    public enum StaffHireResult
    {
        Success = 0,
        /// <summary>None or an undefined role.</summary>
        InvalidRole,
        StaffCapReached,
        /// <summary>New staff arrive through the zoo entrance, so one with a path to step onto is needed.</summary>
        NoEntrance,
        InsufficientFunds
    }

    /// <summary>
    /// Hires and dismisses staff. Hiring follows the same purchase flow as construction: authorize, act, then charge
    /// only for what was actually committed, so a refused hire costs nothing. Dismissal only removes the registry
    /// record; the assignment service, task pool and work service clean up through the registry's events.
    /// </summary>
    public sealed class StaffHiringService
    {
        readonly StaffConfig _config;
        readonly IStaffRegistry _staff;
        readonly VisitorDestinationService _destinations;
        readonly PurchaseService _purchases;
        int _hired;

        /// <param name="purchases">Null makes hiring free (tests, sandbox).</param>
        public StaffHiringService(StaffConfig config, IStaffRegistry staff, VisitorDestinationService destinations, PurchaseService purchases = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _staff = staff ?? throw new ArgumentNullException(nameof(staff));
            _destinations = destinations ?? throw new ArgumentNullException(nameof(destinations));
            _purchases = purchases;
        }

        public int MaxStaff => _config.MaxStaff;

        public int Cost(StaffRole role) => _config.HireCost(role);

        /// <summary>What <see cref="Hire"/> would do right now, without changing anything.</summary>
        public StaffHireResult CanHire(StaffRole role)
        {
            if (!StaffRules.IsDefined(role)) return StaffHireResult.InvalidRole;
            if (_staff.Count >= _config.MaxStaff) return StaffHireResult.StaffCapReached;
            if (!_destinations.TryGetEntrance(out _)) return StaffHireResult.NoEntrance;
            if (_purchases != null && !_purchases.CanAfford(_config.HireCost(role))) return StaffHireResult.InsufficientFunds;
            return StaffHireResult.Success;
        }

        public StaffHireResult Hire(StaffRole role, out StaffInstance hired)
        {
            hired = null;
            if (!StaffRules.IsDefined(role)) return StaffHireResult.InvalidRole;
            if (_staff.Count >= _config.MaxStaff) return StaffHireResult.StaffCapReached;
            if (!_destinations.TryGetEntrance(out var entrance)) return StaffHireResult.NoEntrance;

            int cost = _config.HireCost(role);
            if (_purchases != null && !_purchases.Authorize(cost, "hire-" + role)) return StaffHireResult.InsufficientFunds;

            var staff = StaffInstance.CreateNew(role, Name(role), entrance.SourcePosition);
            if (!_staff.Register(staff)) return StaffHireResult.InvalidRole; // an id clash cannot really happen
            _purchases?.Charge(TransactionCategory.StaffHiring, staff.StaffId, cost, 1);
            hired = staff;
            return StaffHireResult.Success;
        }

        /// <summary>Ends a member's employment: any facility assignment is cleared and any task goes back to the pool. No refund.</summary>
        public bool Dismiss(string staffId) => _staff.Unregister(staffId);

        string Name(StaffRole role)
        {
            _hired++;
            return role == StaffRole.MaintenanceWorker ? "Maintenance " + _hired : role + " " + _hired;
        }
    }
}
