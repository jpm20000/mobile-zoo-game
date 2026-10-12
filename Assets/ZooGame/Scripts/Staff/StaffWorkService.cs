using System;
using System.Collections.Generic;
using ZooGame.Animals;
using ZooGame.Visitors;
using ZooGame.World;

namespace ZooGame.Staff
{
    /// <summary>The scene body of a staff member: walks the cell route it is given and reports arrival through the work service.</summary>
    public interface IStaffAgent
    {
        StaffInstance Instance { get; }

        /// <summary>Walk through these cells in order (they are copied). Arrival is reported with <see cref="StaffWorkService.OnArrived"/>.</summary>
        void Follow(IReadOnlyList<GridCoord> route);

        void Stop();
    }

    /// <summary>
    /// The only place a staff member's <see cref="StaffState"/> changes. Plain C#, no per-member MonoBehaviour.
    ///
    /// Vendors: an assigned vendor travels to their facility, enters Working on arrival and keeps the facility
    /// operational (it becomes usable for visitors through <see cref="VisitorDestinationService.Operational"/>). If no path
    /// leads there the vendor is Unreachable and the facility stays closed; nobody is auto-assigned in their place.
    ///
    /// Task-based roles (Keeper, Maintenance Worker, Veterinarian): an Idle member claims the oldest pending task of
    /// their role whose target they can reach, travels there, works for the task's duration and returns to Idle. Tasks
    /// whose target vanishes or becomes unreachable fail. Routes are only searched when something changed (a path,
    /// fence, placement or assignment), never per frame.
    /// </summary>
    public sealed class StaffWorkService : IDisposable
    {
        readonly IStaffRegistry _staff;
        readonly StaffFacilityRegistry _facilities;
        readonly StaffAssignmentService _assignments;
        readonly StaffTaskPool _tasks;
        readonly VisitorNavigationService _navigation;
        readonly VisitorDestinationService _destinations;
        readonly IAnimalRegistry _animals;

        readonly Dictionary<string, IStaffAgent> _agents = new Dictionary<string, IStaffAgent>();
        readonly List<StaffInstance> _snapshot = new List<StaffInstance>(16);
        readonly List<GridCoord> _route = new List<GridCoord>(32);
        readonly List<GridCoord> _cells = new List<GridCoord>(16);
        readonly Func<VisitorDestination, bool> _operationalGate;
        bool _dirty = true;
        bool _refreshing;

        public StaffWorkService(IStaffRegistry staff, StaffFacilityRegistry facilities, StaffAssignmentService assignments,
            StaffTaskPool tasks, VisitorNavigationService navigation, VisitorDestinationService destinations, IAnimalRegistry animals = null)
        {
            _staff = staff ?? throw new ArgumentNullException(nameof(staff));
            _facilities = facilities ?? throw new ArgumentNullException(nameof(facilities));
            _assignments = assignments ?? throw new ArgumentNullException(nameof(assignments));
            _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _destinations = destinations ?? throw new ArgumentNullException(nameof(destinations));
            _animals = animals;

            _assignments.Changed += OnAssignmentChanged;
            _facilities.Added += OnFacilityAdded;
            _facilities.Removed += OnFacilityRemoved;
            _facilities.Moved += OnTopologyChanged;
            _facilities.EnabledChanged += OnFacilityEnabledChanged;
            _staff.Registered += OnStaffChanged;
            _staff.Unregistered += OnStaffRemoved;
            _tasks.Posted += OnTaskPosted;
            _tasks.Ended += OnTaskEnded;
            _navigation.GraphChanged += OnGraphChanged;
            _destinations.Changed += OnDestinationsChanged;

            _operationalGate = IsOperational;
            _destinations.Operational = _operationalGate;
            RefreshOperational();
        }

        /// <summary>Raised after a member's state changed (state, target, task).</summary>
        public event Action<StaffInstance> StateChanged;

        public void Dispose()
        {
            _assignments.Changed -= OnAssignmentChanged;
            _facilities.Added -= OnFacilityAdded;
            _facilities.Removed -= OnFacilityRemoved;
            _facilities.Moved -= OnTopologyChanged;
            _facilities.EnabledChanged -= OnFacilityEnabledChanged;
            _staff.Registered -= OnStaffChanged;
            _staff.Unregistered -= OnStaffRemoved;
            _tasks.Posted -= OnTaskPosted;
            _tasks.Ended -= OnTaskEnded;
            _navigation.GraphChanged -= OnGraphChanged;
            _destinations.Changed -= OnDestinationsChanged;
            if (_destinations.Operational == _operationalGate)
            {
                _destinations.Operational = null;
                _destinations.RefreshOperational();
            }
        }

        // ---- Agents ----------------------------------------------------------------------------------------

        public void Attach(IStaffAgent agent)
        {
            if (agent?.Instance != null) _agents[agent.Instance.StaffId] = agent;
        }

        public void Detach(string staffId)
        {
            if (staffId != null) _agents.Remove(staffId);
        }

        IStaffAgent AgentOf(StaffInstance s) => _agents.TryGetValue(s.StaffId, out var a) ? a : null;

        GridCoord CellOf(StaffInstance s) => _navigation.Grid.WorldToGrid(s.Position);

        // ---- Operational gate ------------------------------------------------------------------------------

        /// <summary>
        /// A staffable facility is usable only while it is enabled and its assigned vendor is Working there. Anything
        /// that is not staffable (toilets, benches, the entrance) is always operational as far as staff are concerned.
        /// </summary>
        public bool IsOperational(VisitorDestination destination)
        {
            if (destination == null || !_facilities.TryGet(destination.Id, out var facility)) return true;
            return IsOperating(facility);
        }

        public bool IsOperating(StaffFacility facility) =>
            facility != null && facility.Enabled && facility.AssignedStaffId != null
            && _staff.TryGet(facility.AssignedStaffId, out var s) && s.State == StaffState.Working;

        void RefreshOperational()
        {
            if (_refreshing) return;
            _refreshing = true;
            try { _destinations.RefreshOperational(); }
            finally { _refreshing = false; }
        }

        // ---- Per frame -------------------------------------------------------------------------------------

        /// <summary>Re-validates after the zoo changed, then hands pending tasks to idle members. Cheap when nothing changed.</summary>
        public void Process()
        {
            if (!_dirty) return;
            _dirty = false;

            _snapshot.Clear();
            _staff.CopyTo(_snapshot);
            for (int i = 0; i < _snapshot.Count; i++)
            {
                var s = _snapshot[i];
                if (!_staff.Contains(s.StaffId)) continue;
                if (s.Role == StaffRole.Vendor) EvaluateVendor(s);
                else if (s.ActiveTaskId != null) EvaluateTask(s);
            }
            Dispatch();
        }

        /// <summary>Counts down the work of members who are at their task's target. GameClock seconds.</summary>
        public void Advance(float dt)
        {
            if (!(dt > 0f)) return;
            _snapshot.Clear();
            _staff.CopyTo(_snapshot);
            for (int i = 0; i < _snapshot.Count; i++)
            {
                var s = _snapshot[i];
                if (s.State != StaffState.Working || s.ActiveTaskId == null || !_tasks.TryGet(s.ActiveTaskId, out var task)) continue;
                task.RemainingSeconds -= dt;
                if (task.RemainingSeconds <= 0f)
                {
                    task.RemainingSeconds = 0f;
                    _tasks.Complete(task); // raises Ended, which returns the member to Idle
                }
            }
            if (_dirty) Process();
        }

        /// <summary>The member reached the end of their route.</summary>
        public void OnArrived(StaffInstance s)
        {
            if (s == null || !_staff.Contains(s.StaffId)) return;
            if (s.Role == StaffRole.Vendor) EvaluateVendor(s);
            else if (s.ActiveTaskId != null) EvaluateTask(s);
        }

        // ---- Vendors ---------------------------------------------------------------------------------------

        void EvaluateVendor(StaffInstance s)
        {
            var agent = AgentOf(s);
            if (s.AssignedFacilityId == null)
            {
                agent?.Stop();
                SetState(s, StaffState.Idle);
                return;
            }

            if (!_facilities.Contains(s.AssignedFacilityId)
                || !_destinations.TryGet(s.AssignedFacilityId, out var dest) || !dest.IsPathAccessible)
            {
                agent?.Stop();
                SetState(s, StaffState.Unreachable);
                return;
            }
            GoTo(s, dest.InteractionCells, StaffState.Working, StaffState.Unreachable);
        }

        // ---- Tasks -----------------------------------------------------------------------------------------

        void EvaluateTask(StaffInstance s)
        {
            if (!_tasks.TryGet(s.ActiveTaskId, out var task)) return;
            if (!ResolveTarget(task, _cells))
            {
                _tasks.Fail(task);
                return;
            }
            var from = CellOf(s);
            if (!CanReach(from, _cells))
            {
                _tasks.Fail(task);
                return;
            }
            GoTo(s, _cells, StaffState.Working, StaffState.Idle);
        }

        void Dispatch()
        {
            _snapshot.Clear();
            _staff.CopyTo(_snapshot);
            for (int i = 0; i < _snapshot.Count; i++)
            {
                var s = _snapshot[i];
                if (!_staff.Contains(s.StaffId) || !StaffRules.IsTaskBased(s.Role) || s.ActiveTaskId != null) continue;
                var open = _tasks.Open;
                for (int t = 0; t < open.Count; t++)
                {
                    var task = open[t];
                    if (task.State != StaffTaskState.Pending || task.RequiredRole != s.Role) continue;
                    if (!ResolveTarget(task, _cells) || !CanReach(CellOf(s), _cells)) continue;
                    if (!_tasks.TryClaim(task, s)) continue;
                    GoTo(s, _cells, StaffState.Working, StaffState.Idle);
                    break;
                }
            }
        }

        /// <summary>Cells a worker can stand on to do the task. False when the target no longer exists or has no path access.</summary>
        bool ResolveTarget(StaffTask task, List<GridCoord> cells)
        {
            cells.Clear();
            int enclosureId = task.TargetEnclosureId;
            if (task.TargetFacilityId != null)
            {
                if (!_destinations.TryGet(task.TargetFacilityId, out var f) || !f.IsPathAccessible) return false;
                for (int i = 0; i < f.InteractionCells.Count; i++) cells.Add(f.InteractionCells[i]);
                return cells.Count > 0;
            }
            if (task.TargetAnimalId != null)
            {
                if (_animals == null || !_animals.TryGet(task.TargetAnimalId, out var animal)
                    || !AnimalEnclosureService.TryParseId(animal.EnclosureId, out enclosureId)) return false;
            }
            if (enclosureId == StaffTask.NoEnclosure) return false;

            var all = _destinations.All;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (d.Kind != VisitorDestinationKind.EnclosureViewing || d.EnclosureId != enclosureId) continue;
                for (int c = 0; c < d.InteractionCells.Count; c++) cells.Add(d.InteractionCells[c]);
            }
            return cells.Count > 0;
        }

        // ---- Movement --------------------------------------------------------------------------------------

        /// <summary>
        /// Walks the member to the nearest of <paramref name="cells"/> and puts them in <paramref name="arrivedState"/>
        /// once there. When nothing is reachable the member is put in <paramref name="blockedState"/>.
        /// </summary>
        void GoTo(StaffInstance s, IReadOnlyList<GridCoord> cells, StaffState arrivedState, StaffState blockedState)
        {
            var agent = AgentOf(s);
            var from = CellOf(s);
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].X != from.X || cells[i].Z != from.Z) continue;
                agent?.Stop();
                SetState(s, arrivedState);
                return;
            }

            if (_navigation.TryFindRoute(from, cells, out _, _route) && _route.Count > 0)
            {
                agent?.Follow(_route);
                SetState(s, StaffState.Travelling);
                return;
            }

            // Off the path network (just hired at the entrance, or the ground went from under them): step onto it at
            // the entrance's path cell, and plan the real route on arrival.
            if (!_navigation.IsWalkable(from) && _destinations.TryGetEntrance(out var entrance)
                && _navigation.IsConnectedToAny(entrance.InteractionPoint, cells))
            {
                _route.Clear();
                _route.Add(entrance.InteractionPoint);
                agent?.Follow(_route);
                SetState(s, StaffState.Travelling);
                return;
            }

            agent?.Stop();
            SetState(s, blockedState);
        }

        bool CanReach(GridCoord from, IReadOnlyList<GridCoord> cells)
        {
            if (cells.Count == 0) return false;
            if (_navigation.IsWalkable(from)) return _navigation.IsConnectedToAny(from, cells);
            return _destinations.TryGetEntrance(out var entrance) && _navigation.IsConnectedToAny(entrance.InteractionPoint, cells);
        }

        void SetState(StaffInstance s, StaffState state)
        {
            if (s.State == state) return;
            s.State = state;
            if (s.Role == StaffRole.Vendor) RefreshOperational();
            StateChanged?.Invoke(s);
        }

        // ---- Events ----------------------------------------------------------------------------------------

        void OnAssignmentChanged(StaffAssignmentChange change)
        {
            if (_staff.TryGet(change.StaffId, out var s) && s.Role == StaffRole.Vendor) EvaluateVendor(s);
            RefreshOperational(); // the facility lost or gained its vendor
        }

        void OnFacilityAdded(StaffFacility facility)
        {
            // The destination for the new object may already have been built (a Changed listener asked for it) before
            // this registry knew the facility, so it was judged "not staffable". Judge it again.
            RefreshOperational();
        }

        void OnFacilityRemoved(StaffFacility facility)
        {
            // The assignment service has already cleared the assignment and (through Changed) idled the vendor.
            _tasks.CancelForFacility(facility.FacilityId);
            _dirty = true;
        }

        void OnFacilityEnabledChanged(StaffFacility facility) => RefreshOperational();

        void OnTopologyChanged(StaffFacility _) => _dirty = true;

        void OnGraphChanged() => _dirty = true;

        void OnDestinationsChanged()
        {
            if (!_refreshing) _dirty = true;
        }

        void OnStaffChanged(StaffInstance _) => _dirty = true;

        void OnStaffRemoved(StaffInstance s)
        {
            AgentOf(s)?.Stop();
            Detach(s.StaffId);
            RefreshOperational();
            _dirty = true; // a released task may suit someone else
        }

        void OnTaskPosted(StaffTask _) => _dirty = true;

        void OnTaskEnded(StaffTask task, string staffId)
        {
            if (staffId != null && _staff.TryGet(staffId, out var s))
            {
                AgentOf(s)?.Stop();
                SetState(s, StaffState.Idle);
            }
            _dirty = true;
        }
    }
}
