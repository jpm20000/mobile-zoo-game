using System;
using System.Collections.Generic;

namespace ZooGame.Staff
{
    public enum StaffTaskState
    {
        /// <summary>Waiting in the pool for a member of <see cref="StaffTask.RequiredRole"/>.</summary>
        Pending = 0,
        /// <summary>Claimed by exactly one member.</summary>
        Active,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// A unit of work for the task-based roles. It names where it must be done (a facility, an enclosure or an animal's
    /// enclosure; at most one) and how long it takes once the worker is there. State and
    /// <see cref="AssignedStaffId"/> are written only by <see cref="StaffTaskPool"/>.
    /// </summary>
    public sealed class StaffTask
    {
        public const int NoEnclosure = 0;

        internal StaffTask(string taskId, StaffRole requiredRole, string facilityId, int enclosureId, string animalId,
            float workSeconds, string description)
        {
            TaskId = taskId;
            RequiredRole = requiredRole;
            TargetFacilityId = facilityId;
            TargetEnclosureId = enclosureId;
            TargetAnimalId = animalId;
            WorkSeconds = Math.Max(0f, workSeconds);
            RemainingSeconds = WorkSeconds;
            Description = description ?? string.Empty;
        }

        public string TaskId { get; }
        public StaffRole RequiredRole { get; }
        /// <summary>A <see cref="StaffFacility.FacilityId"/> (a repair, say), or null.</summary>
        public string TargetFacilityId { get; }
        /// <summary>An M3 enclosure id, or <see cref="NoEnclosure"/>.</summary>
        public int TargetEnclosureId { get; }
        /// <summary>An animal id: the worker goes to that animal's enclosure. Null when not an animal task.</summary>
        public string TargetAnimalId { get; }
        public float WorkSeconds { get; }
        public string Description { get; }

        public StaffTaskState State { get; internal set; }
        /// <summary>The one member carrying it out; null unless <see cref="StaffTaskState.Active"/>.</summary>
        public string AssignedStaffId { get; internal set; }
        /// <summary>GameClock seconds of work left; only counts down while the worker is at the target.</summary>
        public float RemainingSeconds { get; internal set; }

        public bool IsOpen => State == StaffTaskState.Pending || State == StaffTaskState.Active;
    }

    /// <summary>
    /// The shared pool of open tasks for the task-based roles (one pool, filtered by <see cref="StaffTask.RequiredRole"/>,
    /// so Keepers, Maintenance Workers and Veterinarians each draw from their own queue). It enforces the task
    /// invariants: a task has at most one assigned member, a member has at most one active task, and finishing a task
    /// (completed, failed or cancelled) clears both sides of the link. It never moves staff; <see cref="StaffWorkService"/> does.
    /// </summary>
    public sealed class StaffTaskPool : IDisposable
    {
        readonly IStaffRegistry _staff;
        readonly Dictionary<string, StaffTask> _byId = new Dictionary<string, StaffTask>();
        readonly List<StaffTask> _open = new List<StaffTask>(16); // post order
        readonly List<StaffTask> _scratch = new List<StaffTask>(8);
        int _nextId = 1;

        public StaffTaskPool(IStaffRegistry staff)
        {
            _staff = staff ?? throw new ArgumentNullException(nameof(staff));
            _staff.Unregistered += OnStaffRemoved;
        }

        /// <summary>Open (pending or active) tasks, oldest first. Copy before changing the pool while iterating.</summary>
        public IReadOnlyList<StaffTask> Open => _open;

        public event Action<StaffTask> Posted;

        /// <summary>A pending task was claimed by a member (<see cref="StaffTask.AssignedStaffId"/> is set).</summary>
        public event Action<StaffTask> Claimed;

        /// <summary>A task finished or was cancelled. The second argument is the member who held it (null if it was pending).</summary>
        public event Action<StaffTask, string> Ended;

        /// <summary>An active task went back to pending because its worker left (the argument is the former worker).</summary>
        public event Action<StaffTask, string> Released;

        public void Dispose() => _staff.Unregistered -= OnStaffRemoved;

        // ---- Posting ---------------------------------------------------------------------------------------

        public StaffTask PostForFacility(StaffRole role, string facilityId, float workSeconds, string description = null) =>
            string.IsNullOrEmpty(facilityId) ? null : Post(role, facilityId, StaffTask.NoEnclosure, null, workSeconds, description);

        public StaffTask PostForEnclosure(StaffRole role, int enclosureId, float workSeconds, string description = null) =>
            enclosureId == StaffTask.NoEnclosure ? null : Post(role, null, enclosureId, null, workSeconds, description);

        public StaffTask PostForAnimal(StaffRole role, string animalId, float workSeconds, string description = null) =>
            string.IsNullOrEmpty(animalId) ? null : Post(role, null, StaffTask.NoEnclosure, animalId, workSeconds, description);

        StaffTask Post(StaffRole role, string facilityId, int enclosureId, string animalId, float workSeconds, string description)
        {
            if (!StaffRules.IsTaskBased(role)) return null; // vendors run a facility, they take no tasks
            var task = new StaffTask("task-" + _nextId++, role, facilityId, enclosureId, animalId, workSeconds, description);
            _byId.Add(task.TaskId, task);
            _open.Add(task);
            Posted?.Invoke(task);
            return task;
        }

        // ---- Queries ---------------------------------------------------------------------------------------

        public bool TryGet(string taskId, out StaffTask task)
        {
            if (taskId == null) { task = null; return false; }
            return _byId.TryGetValue(taskId, out task);
        }

        public int CountOpen(StaffRole role)
        {
            int n = 0;
            for (int i = 0; i < _open.Count; i++)
                if (_open[i].RequiredRole == role) n++;
            return n;
        }

        // ---- Claiming and finishing (the work service drives these) ----------------------------------------

        /// <summary>
        /// Gives a pending task to a member. Refused unless the task is pending and unassigned, the member's role is the
        /// one required, and the member has no active task: one task per member, one member per task.
        /// </summary>
        internal bool TryClaim(StaffTask task, StaffInstance staff)
        {
            if (task == null || staff == null || !_byId.ContainsKey(task.TaskId)) return false;
            if (task.State != StaffTaskState.Pending || task.AssignedStaffId != null) return false;
            if (staff.Role != task.RequiredRole || staff.ActiveTaskId != null) return false;
            task.State = StaffTaskState.Active;
            task.AssignedStaffId = staff.StaffId;
            task.RemainingSeconds = task.WorkSeconds;
            staff.ActiveTaskId = task.TaskId;
            Claimed?.Invoke(task);
            return true;
        }

        internal void Complete(StaffTask task) => End(task, StaffTaskState.Completed);

        internal void Fail(StaffTask task) => End(task, StaffTaskState.Failed);

        /// <summary>Cancels an open task. If a member held it they are freed (the work service returns them to Idle).</summary>
        public bool Cancel(string taskId) => TryGet(taskId, out var t) && End(t, StaffTaskState.Cancelled);

        /// <summary>Cancels every open task that targets the facility. Returns how many.</summary>
        public int CancelForFacility(string facilityId)
        {
            if (facilityId == null) return 0;
            return CancelWhere(t => t.TargetFacilityId == facilityId);
        }

        /// <summary>Cancels every open task that targets the enclosure. Returns how many.</summary>
        public int CancelForEnclosure(int enclosureId)
        {
            if (enclosureId == StaffTask.NoEnclosure) return 0;
            return CancelWhere(t => t.TargetEnclosureId == enclosureId);
        }

        /// <summary>Cancels every open task that targets the animal. Returns how many.</summary>
        public int CancelForAnimal(string animalId)
        {
            if (animalId == null) return 0;
            return CancelWhere(t => t.TargetAnimalId == animalId);
        }

        int CancelWhere(Func<StaffTask, bool> match)
        {
            _scratch.Clear();
            for (int i = 0; i < _open.Count; i++)
                if (match(_open[i])) _scratch.Add(_open[i]);
            int n = 0;
            for (int i = 0; i < _scratch.Count; i++)
                if (End(_scratch[i], StaffTaskState.Cancelled)) n++;
            _scratch.Clear();
            return n;
        }

        bool End(StaffTask task, StaffTaskState finalState)
        {
            if (task == null || !task.IsOpen || !_byId.ContainsKey(task.TaskId)) return false;
            string staffId = task.AssignedStaffId;
            task.AssignedStaffId = null;
            task.State = finalState;
            if (staffId != null && _staff.TryGet(staffId, out var staff) && staff.ActiveTaskId == task.TaskId)
                staff.ActiveTaskId = null;
            _open.Remove(task);
            _byId.Remove(task.TaskId);
            Ended?.Invoke(task, staffId);
            return true;
        }

        /// <summary>A dismissed worker's task is not lost: it goes back to pending for someone else.</summary>
        void OnStaffRemoved(StaffInstance staff)
        {
            string taskId = staff.ActiveTaskId;
            if (taskId == null || !_byId.TryGetValue(taskId, out var task)) return;
            staff.ActiveTaskId = null;
            if (task.AssignedStaffId != staff.StaffId) return;
            task.AssignedStaffId = null;
            task.State = StaffTaskState.Pending;
            task.RemainingSeconds = task.WorkSeconds;
            Released?.Invoke(task, staff.StaffId);
        }
    }
}
