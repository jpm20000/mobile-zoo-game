using NUnit.Framework;
using ZooGame.Data;
using ZooGame.Placement;
using ZooGame.Staff;

namespace ZooGame.Tests.EditMode.Staff
{
    /// <summary>Keeper, Maintenance Worker and Veterinarian: shared task pools, one task per member, never a facility.</summary>
    public class StaffTaskTests
    {
        StaffFixture _f;
        PlacedObject _toilet;
        string _toiletId;
        int _pen;

        [SetUp]
        public void SetUp()
        {
            _f = new StaffFixture();
            _f.V.StandardPath();
            _pen = _f.V.Pen(); // 4x3 at (4,7): viewable from the path at z = 6
            _toilet = _f.Stall(6, 5, VisitorFacilityKind.Toilet);
            _toiletId = StaffFixture.IdOf(_toilet);
        }

        [TearDown] public void TearDown() => _f.Dispose();

        // ---- Lifecycle ----

        [Test]
        public void KeeperTask_IsClaimed_Walked_Worked_AndCompleted()
        {
            var keeper = _f.Add(StaffRole.Keeper, 4, 6);
            var task = _f.Tasks.PostForEnclosure(StaffRole.Keeper, _pen, 5f, "tend");
            Assert.AreEqual(StaffTaskState.Pending, task.State);

            _f.Work.Process();

            Assert.AreEqual(StaffTaskState.Active, task.State);
            Assert.AreEqual(keeper.StaffId, task.AssignedStaffId);
            Assert.AreEqual(task.TaskId, keeper.ActiveTaskId);

            // Member is already on a cell beside the pen, so work starts immediately.
            Assert.AreEqual(StaffState.Working, keeper.State);
            _f.Work.Advance(4f);
            Assert.AreEqual(StaffTaskState.Active, task.State);
            Assert.AreEqual(StaffState.Working, keeper.State);

            _f.Work.Advance(1.5f);

            Assert.AreEqual(StaffTaskState.Completed, task.State);
            Assert.IsNull(task.AssignedStaffId, "completion clears the assignment");
            Assert.IsNull(keeper.ActiveTaskId);
            Assert.AreEqual(StaffState.Idle, keeper.State);
            Assert.AreEqual(0, _f.Tasks.Open.Count);
        }

        [Test]
        public void WorkerAwayFromTheTarget_TravelsFirst_AndWorkDoesNotCountDownWhileWalking()
        {
            // the toilet is at (6,5); a maintenance worker at the far end of the path has to walk to (6,6)
            var worker = _f.Add(StaffRole.MaintenanceWorker, 11, 6);
            var task = _f.Tasks.PostForFacility(StaffRole.MaintenanceWorker, _toiletId, 3f);

            _f.Work.Process();

            Assert.AreEqual(StaffState.Travelling, worker.State);
            Assert.AreEqual(StaffFixture.C(6, 6), _f.AgentOf(worker).Destination);
            _f.Work.Advance(10f);
            Assert.AreEqual(StaffTaskState.Active, task.State, "no progress while walking");

            _f.AgentOf(worker).Arrive();
            Assert.AreEqual(StaffState.Working, worker.State);
            _f.Work.Advance(3f);

            Assert.AreEqual(StaffTaskState.Completed, task.State);
            Assert.AreEqual(StaffState.Idle, worker.State);
        }

        [Test]
        public void VeterinarianTask_TargetsAnAnimalsEnclosure()
        {
            var vet = _f.Add(StaffRole.Veterinarian, 5, 6);
            string animalId = AddAnimal();
            var task = _f.Tasks.PostForAnimal(StaffRole.Veterinarian, animalId, 2f);

            _f.Work.Process();
            Assert.AreEqual(StaffTaskState.Active, task.State);
            _f.Work.Advance(2f);

            Assert.AreEqual(StaffTaskState.Completed, task.State);
            Assert.AreEqual(StaffState.Idle, vet.State);
        }

        string AddAnimal()
        {
            var before = _f.V.Animals.Registry.Count;
            _f.V.AddAnimal(_f.V.Animals.Rabbit, _pen);
            Assert.AreEqual(before + 1, _f.V.Animals.Registry.Count);
            foreach (var a in _f.V.Animals.Registry.GetAll()) return a.AnimalId;
            return null;
        }

        // ---- One task per member, one member per task ----

        [Test]
        public void OneMemberCarriesOutOneTaskAtATime_ThenTakesTheNext()
        {
            var keeper = _f.Add(StaffRole.Keeper, 4, 6);
            var first = _f.Tasks.PostForEnclosure(StaffRole.Keeper, _pen, 2f);
            var second = _f.Tasks.PostForEnclosure(StaffRole.Keeper, _pen, 2f);

            _f.Work.Process();

            Assert.AreEqual(StaffTaskState.Active, first.State);
            Assert.AreEqual(StaffTaskState.Pending, second.State);
            Assert.IsNull(second.AssignedStaffId);
            Assert.AreEqual(first.TaskId, keeper.ActiveTaskId);

            _f.Work.Advance(2f);

            Assert.AreEqual(StaffTaskState.Completed, first.State);
            Assert.AreEqual(StaffTaskState.Active, second.State, "next task is picked up right after");
            Assert.AreEqual(second.TaskId, keeper.ActiveTaskId);
        }

        [Test]
        public void OneTaskIsHeldByExactlyOneMember()
        {
            var a = _f.Add(StaffRole.Keeper, 4, 6);
            var b = _f.Add(StaffRole.Keeper, 5, 6);
            var task = _f.Tasks.PostForEnclosure(StaffRole.Keeper, _pen, 5f);

            _f.Work.Process();

            Assert.AreEqual(StaffTaskState.Active, task.State);
            Assert.AreEqual(a.StaffId, task.AssignedStaffId, "oldest idle member wins");
            Assert.IsNull(b.ActiveTaskId);
            Assert.AreEqual(StaffState.Idle, b.State);
        }

        [Test]
        public void TasksGoOnlyToTheirOwnRole()
        {
            var keeper = _f.Add(StaffRole.Keeper, 4, 6);
            var vet = _f.Add(StaffRole.Veterinarian, 5, 6);
            var maintenance = _f.Add(StaffRole.MaintenanceWorker, 6, 6);
            var task = _f.Tasks.PostForFacility(StaffRole.MaintenanceWorker, _toiletId, 5f);

            _f.Work.Process();

            Assert.AreEqual(maintenance.StaffId, task.AssignedStaffId);
            Assert.IsNull(keeper.ActiveTaskId);
            Assert.IsNull(vet.ActiveTaskId);
        }

        [Test]
        public void VendorsCannotTakeOrBeGivenTasks()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);

            Assert.IsNull(_f.Tasks.PostForEnclosure(StaffRole.Vendor, _pen, 1f));
            Assert.IsNull(_f.Tasks.PostForFacility(StaffRole.None, _toiletId, 1f));
            Assert.AreEqual(0, _f.Tasks.Open.Count);
            Assert.IsNull(vendor.ActiveTaskId);
        }

        // ---- Completion, failure and cancellation clear the link ----

        [Test]
        public void CancellingAnActiveTask_ClearsBothSides_AndIdlesTheWorker()
        {
            var keeper = _f.Add(StaffRole.Keeper, 4, 6);
            var task = _f.Tasks.PostForEnclosure(StaffRole.Keeper, _pen, 5f);
            _f.Work.Process();

            Assert.IsTrue(_f.Tasks.Cancel(task.TaskId));

            Assert.AreEqual(StaffTaskState.Cancelled, task.State);
            Assert.IsNull(task.AssignedStaffId);
            Assert.IsNull(keeper.ActiveTaskId);
            Assert.AreEqual(StaffState.Idle, keeper.State);
        }

        [Test]
        public void TaskWhoseTargetBecomesUnreachable_Fails_AndWorkerIdles()
        {
            var worker = _f.Add(StaffRole.MaintenanceWorker, 11, 6);
            var task = _f.Tasks.PostForFacility(StaffRole.MaintenanceWorker, _toiletId, 5f);
            _f.Work.Process();
            Assert.AreEqual(StaffState.Travelling, worker.State);

            _f.V.Model.RemovePath(StaffFixture.C(8, 6)); // splits the walkway between worker and toilet
            _f.Work.Process();

            Assert.AreEqual(StaffTaskState.Failed, task.State);
            Assert.IsNull(task.AssignedStaffId);
            Assert.IsNull(worker.ActiveTaskId);
            Assert.AreEqual(StaffState.Idle, worker.State);
            Assert.IsFalse(_f.AgentOf(worker).HasRoute);
        }

        [Test]
        public void TaskWithAnUnreachableTarget_StaysPending_UntilSomethingCanReachIt()
        {
            var cut = _f.Stall(9, 9, VisitorFacilityKind.Toilet); // no path beside it
            var worker = _f.Add(StaffRole.MaintenanceWorker, 4, 6);
            var task = _f.Tasks.PostForFacility(StaffRole.MaintenanceWorker, StaffFixture.IdOf(cut), 1f);

            _f.Work.Process();

            Assert.AreEqual(StaffTaskState.Pending, task.State);
            Assert.IsNull(worker.ActiveTaskId);
            Assert.AreEqual(StaffState.Idle, worker.State);
        }

        [Test]
        public void DismissedWorker_ReturnsTheirTaskToThePool()
        {
            var first = _f.Add(StaffRole.Keeper, 4, 6);
            var task = _f.Tasks.PostForEnclosure(StaffRole.Keeper, _pen, 5f);
            _f.Work.Process();
            Assert.AreEqual(first.StaffId, task.AssignedStaffId);
            var second = _f.Add(StaffRole.Keeper, 5, 6);

            _f.Staff.Unregister(first.StaffId);

            Assert.AreEqual(StaffTaskState.Pending, task.State, "not lost with the worker");
            Assert.IsNull(task.AssignedStaffId);
            _f.Work.Process();
            Assert.AreEqual(second.StaffId, task.AssignedStaffId);
        }

        // ---- Never assigned to a facility ----

        [Test]
        public void TaskBasedRoles_NeverGetAFacilityAssignment()
        {
            var stall = _f.Stall(9, 5, VisitorFacilityKind.FoodStall);
            var keeper = _f.Add(StaffRole.Keeper, 4, 6);
            var maintenance = _f.Add(StaffRole.MaintenanceWorker, 5, 6);
            var vet = _f.Add(StaffRole.Veterinarian, 6, 6);
            _f.Tasks.PostForEnclosure(StaffRole.Keeper, _pen, 1f);
            _f.Tasks.PostForFacility(StaffRole.MaintenanceWorker, StaffFixture.IdOf(stall), 1f);
            _f.Tasks.PostForAnimal(StaffRole.Veterinarian, AddAnimal(), 1f);

            foreach (var s in new[] { keeper, maintenance, vet })
                Assert.AreEqual(StaffAssignmentResult.IncompatibleRole, _f.Assignments.Assign(s.StaffId, StaffFixture.IdOf(stall)));

            _f.Settle(maintenance); // walks to the stall
            for (int i = 0; i < 6; i++) _f.Work.Advance(1f);

            Assert.AreEqual(0, _f.Tasks.Open.Count, "all three tasks were carried out");
            foreach (var s in new[] { keeper, maintenance, vet })
            {
                Assert.IsNull(s.AssignedFacilityId, s.Role + " must stay facility-free");
                Assert.AreEqual(StaffState.Idle, s.State);
            }
            Assert.IsNull(_f.FacilityOf(stall).AssignedStaffId);
        }

        [Test]
        public void PausedClock_DoesNotProgressWork()
        {
            var keeper = _f.Add(StaffRole.Keeper, 4, 6);
            var task = _f.Tasks.PostForEnclosure(StaffRole.Keeper, _pen, 2f);
            _f.Work.Process();

            _f.Work.Advance(0f);
            _f.Work.Advance(-1f);

            Assert.AreEqual(StaffTaskState.Active, task.State);
            Assert.AreEqual(2f, task.RemainingSeconds, 1e-4f);
            Assert.AreEqual(StaffState.Working, keeper.State);
        }
    }
}
