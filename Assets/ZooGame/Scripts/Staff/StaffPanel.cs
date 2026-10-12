using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Animals;
using ZooGame.Placement;
using ZooGame.Visitors;

namespace ZooGame.Staff
{
    /// <summary>
    /// Staff controls: hire, pick a member with Next, dismiss, and manage the selected stall's vendor (view, assign,
    /// unassign, reassign). The stall is whatever placed object is selected with the existing placement selection.
    /// The panel only calls <see cref="StaffHiringService"/> and <see cref="StaffAssignmentService"/>; it never
    /// writes an assignment field itself. The three "Post ... task" buttons are development controls: nothing in M8
    /// generates tasks on its own.
    /// </summary>
    public sealed class StaffPanel : MonoBehaviour
    {
        [SerializeField] Text statusLabel;
        [SerializeField] Button hireVendorButton;
        [SerializeField] Button hireKeeperButton;
        [SerializeField] Button hireMaintenanceButton;
        [SerializeField] Button hireVeterinarianButton;
        [Header("Staff member")]
        [SerializeField] Text staffLabel;
        [SerializeField] Button nextStaffButton;
        [SerializeField] Button dismissButton;
        [Header("Selected facility")]
        [SerializeField] GameObject facilitySection;
        [SerializeField] Text facilityLabel;
        [SerializeField] Button assignButton;
        [SerializeField] Text assignButtonLabel;
        [SerializeField] Button unassignButton;
        [Header("Development")]
        [SerializeField] Button postKeeperTaskButton;
        [SerializeField] Button postMaintenanceTaskButton;
        [SerializeField] Button postVeterinarianTaskButton;

        readonly StringBuilder _sb = new StringBuilder(256);
        StaffHiringService _hiring;
        IStaffRegistry _staff;
        StaffFacilityRegistry _facilities;
        StaffAssignmentService _assignments;
        StaffTaskPool _tasks;
        StaffWorkService _work;
        PlacementSession _selection;
        VisitorDestinationService _destinations;
        IAnimalRegistry _animals;
        StaffConfig _config;
        int _cursor;
        string _message;

        public void Bind(StaffHiringService hiring, IStaffRegistry staff, StaffFacilityRegistry facilities,
            StaffAssignmentService assignments, StaffTaskPool tasks, StaffWorkService work, PlacementSession selection,
            VisitorDestinationService destinations, IAnimalRegistry animals, StaffConfig config)
        {
            Unbind();
            _hiring = hiring;
            _staff = staff;
            _facilities = facilities;
            _assignments = assignments;
            _tasks = tasks;
            _work = work;
            _selection = selection;
            _destinations = destinations;
            _animals = animals;
            _config = config;

            Listen(hireVendorButton, OnHireVendor);
            Listen(hireKeeperButton, OnHireKeeper);
            Listen(hireMaintenanceButton, OnHireMaintenance);
            Listen(hireVeterinarianButton, OnHireVeterinarian);
            Listen(nextStaffButton, OnNextStaff);
            Listen(dismissButton, OnDismiss);
            Listen(assignButton, OnAssign);
            Listen(unassignButton, OnUnassign);
            Listen(postKeeperTaskButton, OnPostKeeperTask);
            Listen(postMaintenanceTaskButton, OnPostMaintenanceTask);
            Listen(postVeterinarianTaskButton, OnPostVeterinarianTask);

            _staff.Registered += OnStaffChanged;
            _staff.Unregistered += OnStaffChanged;
            _facilities.Added += OnFacilityChanged;
            _facilities.Removed += OnFacilityChanged;
            _facilities.EnabledChanged += OnFacilityChanged;
            _assignments.Changed += OnAssignmentChanged;
            _work.StateChanged += OnStaffChanged;
            _tasks.Posted += OnTaskChanged;
            _tasks.Ended += OnTaskEnded;
            if (_selection != null) _selection.Changed += Refresh;
            Refresh();
        }

        void Unbind()
        {
            if (_staff == null) return;
            Forget(hireVendorButton, OnHireVendor);
            Forget(hireKeeperButton, OnHireKeeper);
            Forget(hireMaintenanceButton, OnHireMaintenance);
            Forget(hireVeterinarianButton, OnHireVeterinarian);
            Forget(nextStaffButton, OnNextStaff);
            Forget(dismissButton, OnDismiss);
            Forget(assignButton, OnAssign);
            Forget(unassignButton, OnUnassign);
            Forget(postKeeperTaskButton, OnPostKeeperTask);
            Forget(postMaintenanceTaskButton, OnPostMaintenanceTask);
            Forget(postVeterinarianTaskButton, OnPostVeterinarianTask);

            _staff.Registered -= OnStaffChanged;
            _staff.Unregistered -= OnStaffChanged;
            _facilities.Added -= OnFacilityChanged;
            _facilities.Removed -= OnFacilityChanged;
            _facilities.EnabledChanged -= OnFacilityChanged;
            _assignments.Changed -= OnAssignmentChanged;
            _work.StateChanged -= OnStaffChanged;
            _tasks.Posted -= OnTaskChanged;
            _tasks.Ended -= OnTaskEnded;
            if (_selection != null) _selection.Changed -= Refresh;
            _staff = null;
        }

        void OnDestroy() => Unbind();

        static void Listen(Button b, UnityEngine.Events.UnityAction a)
        {
            if (b != null) b.onClick.AddListener(a);
        }

        static void Forget(Button b, UnityEngine.Events.UnityAction a)
        {
            if (b != null) b.onClick.RemoveListener(a);
        }

        // ---- Model events ----------------------------------------------------------------------------------

        void OnStaffChanged(StaffInstance _) => Refresh();
        void OnFacilityChanged(StaffFacility _) => Refresh();
        void OnAssignmentChanged(StaffAssignmentChange _) => Refresh();
        void OnTaskChanged(StaffTask _) => Refresh();
        void OnTaskEnded(StaffTask _, string __) => Refresh();

        // ---- Commands --------------------------------------------------------------------------------------

        void OnHireVendor() => Hire(StaffRole.Vendor);
        void OnHireKeeper() => Hire(StaffRole.Keeper);
        void OnHireMaintenance() => Hire(StaffRole.MaintenanceWorker);
        void OnHireVeterinarian() => Hire(StaffRole.Veterinarian);

        void Hire(StaffRole role)
        {
            var result = _hiring.Hire(role, out var hired);
            if (result == StaffHireResult.Success)
            {
                _cursor = IndexOf(hired.StaffId);
                _message = "Hired " + hired.DisplayName;
            }
            else _message = Describe(result);
            Refresh();
        }

        void OnNextStaff()
        {
            if (_staff.Count > 0) _cursor = (Mathf.Clamp(_cursor, 0, _staff.Count - 1) + 1) % _staff.Count;
            _message = null;
            Refresh();
        }

        void OnDismiss()
        {
            var s = Current();
            if (s == null) return;
            string name = s.DisplayName;
            _hiring.Dismiss(s.StaffId);
            _message = "Dismissed " + name;
            Refresh();
        }

        void OnAssign()
        {
            var s = Current();
            var f = SelectedFacility();
            if (s == null || f == null) return;
            var result = s.AssignedFacilityId == null
                ? _assignments.Assign(s.StaffId, f.FacilityId)
                : _assignments.Reassign(s.StaffId, f.FacilityId);
            _message = result == StaffAssignmentResult.Success ? s.DisplayName + " now runs " + f.DisplayName : Describe(result);
            Refresh();
        }

        void OnUnassign()
        {
            var f = SelectedFacility();
            if (f == null) return;
            var result = _assignments.UnassignFacility(f.FacilityId);
            _message = result == StaffAssignmentResult.Success ? f.DisplayName + " has no vendor" : Describe(result);
            Refresh();
        }

        void OnPostKeeperTask()
        {
            var all = _destinations.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Kind != VisitorDestinationKind.EnclosureViewing) continue;
                _tasks.PostForEnclosure(StaffRole.Keeper, all[i].EnclosureId, _config.DefaultTaskSeconds, "Tend enclosure");
                _message = "Keeper task posted";
                Refresh();
                return;
            }
            _message = "No enclosure with a path beside it";
            Refresh();
        }

        void OnPostMaintenanceTask()
        {
            string id = null;
            var selected = _selection?.Selected;
            if (selected != null && _destinations.TryGet(VisitorDestinationService.FacilityIdFor(selected.Id), out _))
                id = VisitorDestinationService.FacilityIdFor(selected.Id);
            if (id == null)
            {
                var all = _destinations.All;
                for (int i = 0; i < all.Count && id == null; i++)
                    if (all[i].PlacedObjectId != 0 && all[i].IsPathAccessible) id = all[i].Id;
            }
            if (id == null) _message = "No facility to repair";
            else
            {
                _tasks.PostForFacility(StaffRole.MaintenanceWorker, id, _config.DefaultTaskSeconds, "Repair facility");
                _message = "Maintenance task posted";
            }
            Refresh();
        }

        void OnPostVeterinarianTask()
        {
            string animalId = null;
            if (_animals != null)
                foreach (var a in _animals.GetAll()) { animalId = a.AnimalId; break; }
            if (animalId == null) _message = "No animal to treat";
            else
            {
                _tasks.PostForAnimal(StaffRole.Veterinarian, animalId, _config.DefaultTaskSeconds, "Check animal");
                _message = "Veterinarian task posted";
            }
            Refresh();
        }

        // ---- Display ---------------------------------------------------------------------------------------

        StaffInstance Current()
        {
            if (_staff.Count == 0) return null;
            _cursor = Mathf.Clamp(_cursor, 0, _staff.Count - 1);
            return _staff.All[_cursor];
        }

        int IndexOf(string staffId)
        {
            for (int i = 0; i < _staff.All.Count; i++)
                if (_staff.All[i].StaffId == staffId) return i;
            return 0;
        }

        StaffFacility SelectedFacility()
        {
            var selected = _selection?.Selected;
            return selected != null && _facilities.TryGetForPlacedObject(selected.Id, out var f) ? f : null;
        }

        void Refresh()
        {
            if (_staff == null) return;
            var member = Current();
            var facility = SelectedFacility();

            if (statusLabel != null)
            {
                _sb.Clear();
                _sb.Append("Staff ").Append(_staff.Count).Append('/').Append(_hiring.MaxStaff)
                   .Append("  (V ").Append(_staff.CountOf(StaffRole.Vendor))
                   .Append(" K ").Append(_staff.CountOf(StaffRole.Keeper))
                   .Append(" M ").Append(_staff.CountOf(StaffRole.MaintenanceWorker))
                   .Append(" Vet ").Append(_staff.CountOf(StaffRole.Veterinarian)).Append(')');
                if (_message != null) _sb.Append('\n').Append(_message);
                statusLabel.text = _sb.ToString();
            }

            if (staffLabel != null)
            {
                _sb.Clear();
                if (member == null) _sb.Append("No staff hired");
                else
                {
                    _sb.Append(member.DisplayName).Append(" - ").Append(member.State);
                    if (member.AssignedFacilityId != null) _sb.Append(" @ ").Append(member.AssignedFacilityId);
                    if (member.ActiveTaskId != null) _sb.Append(" [").Append(member.ActiveTaskId).Append(']');
                }
                staffLabel.text = _sb.ToString();
            }
            if (dismissButton != null) dismissButton.interactable = member != null;
            if (nextStaffButton != null) nextStaffButton.interactable = _staff.Count > 1;

            if (facilitySection != null) facilitySection.SetActive(facility != null);
            if (facility != null)
            {
                if (facilityLabel != null)
                {
                    _sb.Clear();
                    _sb.Append(facility.DisplayName).Append(" (").Append(facility.FacilityId).Append(")\n");
                    if (_assignments.TryGetStaffOf(facility.FacilityId, out var vendor))
                        _sb.Append("Vendor: ").Append(vendor.DisplayName).Append(" - ").Append(vendor.State)
                           .Append(_work.IsOperating(facility) ? "  (open)" : "  (closed)");
                    else _sb.Append("Vendor: none  (closed)");
                    facilityLabel.text = _sb.ToString();
                }

                bool memberIsAssigned = member != null && member.AssignedFacilityId != null;
                if (assignButtonLabel != null)
                    assignButtonLabel.text = member == null ? "Assign" : (memberIsAssigned ? "Reassign " : "Assign ") + member.DisplayName;
                if (assignButton != null)
                    assignButton.interactable = member != null && (memberIsAssigned
                        ? _assignments.CanReassign(member.StaffId, facility.FacilityId)
                        : _assignments.CanAssign(member.StaffId, facility.FacilityId)) == StaffAssignmentResult.Success;
                if (unassignButton != null) unassignButton.interactable = facility.IsStaffed;
            }
        }

        // ---- Text ------------------------------------------------------------------------------------------

        public static string Describe(StaffAssignmentResult result)
        {
            switch (result)
            {
                case StaffAssignmentResult.Success: return "Done";
                case StaffAssignmentResult.UnknownStaff: return "No such staff member";
                case StaffAssignmentResult.UnknownFacility: return "No such facility";
                case StaffAssignmentResult.IncompatibleRole: return "Only a Vendor can run a stall";
                case StaffAssignmentResult.FacilityDisabled: return "That facility is disabled";
                case StaffAssignmentResult.StaffAlreadyAssigned: return "Already runs another facility";
                case StaffAssignmentResult.FacilityAlreadyStaffed: return "That stall already has a vendor";
                case StaffAssignmentResult.AlreadyAssignedToFacility: return "Already runs this facility";
                case StaffAssignmentResult.NotAssigned: return "Not assigned to a facility";
                default: return result.ToString();
            }
        }

        public static string Describe(StaffHireResult result)
        {
            switch (result)
            {
                case StaffHireResult.Success: return "Hired";
                case StaffHireResult.InvalidRole: return "Cannot hire that role";
                case StaffHireResult.StaffCapReached: return "Staff limit reached";
                case StaffHireResult.NoEntrance: return "Place an Entrance next to a path first";
                case StaffHireResult.InsufficientFunds: return "Not enough money";
                default: return result.ToString();
            }
        }
    }
}
