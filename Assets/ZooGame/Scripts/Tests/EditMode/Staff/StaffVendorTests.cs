using NUnit.Framework;
using ZooGame.Data;
using ZooGame.Placement;
using ZooGame.Staff;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Staff
{
    /// <summary>Vendor behaviour, and what happens to assignments when staff or facilities are deleted or moved.</summary>
    public class StaffVendorTests
    {
        StaffFixture _f;
        PlacedObject _food;
        string _foodId;

        [SetUp]
        public void SetUp()
        {
            _f = new StaffFixture();
            _f.V.StandardPath();
            _food = _f.Stall(9, 5, VisitorFacilityKind.FoodStall);
            _foodId = StaffFixture.IdOf(_food);
        }

        [TearDown] public void TearDown() => _f.Dispose();

        // ---- Behaviour ----

        [Test]
        public void UnstaffedStall_IsNotUsableByVisitors()
        {
            Assert.IsTrue(_f.Destination(_food).Exists);
            Assert.IsFalse(_f.Destination(_food).Enabled);
        }

        [Test]
        public void NewStall_IsClosed_EvenWhenAListenerRebuildsTheDestinationsMidPlacement()
        {
            // The visitor debug panel does exactly this: it queries destinations from the Changed event, which fires
            // before the staff facility registry has seen the new object.
            _f.V.Destinations.Changed += () => _f.V.Destinations.TryGetEntrance(out _);

            var late = _f.Stall(8, 5, VisitorFacilityKind.DrinkStall);

            Assert.IsTrue(_f.Destination(late).Exists);
            Assert.IsFalse(_f.Destination(late).Enabled);
        }

        [Test]
        public void AssignedVendor_TravelsThenWorks_AndOnlyThenTheStallOpens()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);

            _f.Assignments.Assign(vendor.StaffId, _foodId);

            Assert.AreEqual(StaffState.Travelling, vendor.State);
            Assert.IsTrue(_f.AgentOf(vendor).HasRoute);
            Assert.AreEqual(StaffFixture.C(9, 6), _f.AgentOf(vendor).Destination);
            Assert.IsFalse(_f.Destination(_food).Enabled, "closed until the vendor is there");

            _f.AgentOf(vendor).Arrive();

            Assert.AreEqual(StaffState.Working, vendor.State);
            Assert.IsTrue(_f.Work.IsOperating(_f.FacilityOf(_food)));
            Assert.IsTrue(_f.Destination(_food).Enabled);
        }

        [Test]
        public void VendorAlreadyBesideTheStall_StartsWorkingAtOnce()
        {
            var vendor = _f.Add(StaffRole.Vendor, 9, 6);

            _f.Assignments.Assign(vendor.StaffId, _foodId);

            Assert.AreEqual(StaffState.Working, vendor.State);
            Assert.IsTrue(_f.Destination(_food).Enabled);
        }

        [Test]
        public void Unassigning_ClosesTheStall_AndIdlesTheVendor()
        {
            var vendor = _f.Add(StaffRole.Vendor, 9, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);
            Assert.IsTrue(_f.Destination(_food).Enabled);

            _f.Assignments.Unassign(vendor.StaffId);

            Assert.AreEqual(StaffState.Idle, vendor.State);
            Assert.IsFalse(_f.Destination(_food).Enabled);
        }

        [Test]
        public void DisablingTheFacility_ClosesItButKeepsTheAssignment()
        {
            var vendor = _f.Add(StaffRole.Vendor, 9, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);

            _f.Facilities.SetEnabled(_foodId, false);

            Assert.AreEqual(_foodId, vendor.AssignedFacilityId);
            Assert.IsFalse(_f.Destination(_food).Enabled);
            _f.Facilities.SetEnabled(_foodId, true);
            Assert.IsTrue(_f.Destination(_food).Enabled);
        }

        [Test]
        public void StallBecomesReachableForVisitors_OnlyWhileTheVendorWorks()
        {
            var visitorCell = StaffFixture.C(5, 6);
            Assert.IsFalse(_f.V.Destinations.IsReachable(_foodId, visitorCell));

            var vendor = _f.Add(StaffRole.Vendor, 9, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);
            Assert.IsTrue(_f.V.Destinations.IsReachable(_foodId, visitorCell));

            _f.Assignments.Unassign(vendor.StaffId);
            Assert.IsFalse(_f.V.Destinations.IsReachable(_foodId, visitorCell));
        }

        // ---- Unreachable ----

        [Test]
        public void StallWithNoPathAccess_LeavesTheVendorUnreachable_AndClosed()
        {
            var cut = _f.Stall(5, 8, VisitorFacilityKind.DrinkStall); // not beside the path
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            var spare = _f.Add(StaffRole.Vendor, 5, 6);

            Assert.AreEqual(StaffAssignmentResult.Success, _f.Assignments.Assign(vendor.StaffId, StaffFixture.IdOf(cut)));

            Assert.AreEqual(StaffState.Unreachable, vendor.State);
            Assert.IsFalse(_f.Destination(cut).Enabled);
            Assert.AreEqual(StaffState.Idle, spare.State, "no replacement is auto-assigned");
            Assert.IsNull(spare.AssignedFacilityId);
            Assert.AreEqual(vendor.StaffId, _f.FacilityOf(cut).AssignedStaffId, "the assignment itself is kept");
        }

        [Test]
        public void CuttingThePath_MakesTheVendorUnreachable_AndRebuildingRestoresIt()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);
            _f.Settle(vendor);
            Assert.AreEqual(StaffState.Working, vendor.State);
            Assert.IsTrue(_f.Destination(_food).Enabled);

            // cut the walkway between the vendor (x = 9) and the left part, then send the vendor to the left side
            vendor.Position = _f.Grid.GridToWorld(StaffFixture.C(4, 6));
            _f.V.Model.RemovePath(StaffFixture.C(7, 6));
            _f.Work.Process();

            Assert.AreEqual(StaffState.Unreachable, vendor.State);
            Assert.AreEqual(_foodId, vendor.AssignedFacilityId, "the assignment survives");
            Assert.IsFalse(_f.Destination(_food).Enabled);

            _f.V.BuildPath(7, 6, 7);
            _f.Work.Process();

            Assert.AreEqual(StaffState.Travelling, vendor.State);
            _f.Settle(vendor);
            Assert.AreEqual(StaffState.Working, vendor.State);
            Assert.IsTrue(_f.Destination(_food).Enabled);
        }

        // ---- Deletion ----

        [Test]
        public void DeletingTheVendor_ClearsTheFacilityAssignment_AndClosesTheStall()
        {
            var vendor = _f.Add(StaffRole.Vendor, 9, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);

            Assert.IsTrue(_f.Staff.Unregister(vendor.StaffId));

            Assert.IsNull(_f.FacilityOf(_food).AssignedStaffId);
            Assert.IsNull(vendor.AssignedFacilityId);
            Assert.IsFalse(_f.Destination(_food).Enabled);
            // and the stall can be staffed by someone else
            var next = _f.Add(StaffRole.Vendor, 5, 6);
            Assert.AreEqual(StaffAssignmentResult.Success, _f.Assignments.Assign(next.StaffId, _foodId));
        }

        [Test]
        public void DeletingTheFacility_ClearsTheStaffAssignment_AndIdlesTheVendor()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);
            Assert.AreEqual(StaffState.Travelling, vendor.State);

            Assert.IsTrue(_f.Placement.Remove(_food));

            Assert.IsNull(vendor.AssignedFacilityId);
            Assert.AreEqual(StaffState.Idle, vendor.State);
            Assert.IsFalse(_f.AgentOf(vendor).HasRoute, "the walk to the demolished stall is cancelled");
            Assert.IsFalse(_f.Facilities.Contains(_foodId));
            Assert.IsTrue(_f.Staff.Contains(vendor.StaffId), "the vendor is not fired");
        }

        [Test]
        public void DeletingTheFacility_CancelsTasksAimedAtIt_AndReturnsThoseWorkersToIdle()
        {
            var worker = _f.Add(StaffRole.MaintenanceWorker, 4, 6);
            var task = _f.Tasks.PostForFacility(StaffRole.MaintenanceWorker, _foodId, 5f, "repair");
            _f.Work.Process();
            Assert.AreEqual(StaffTaskState.Active, task.State);
            Assert.AreEqual(worker.StaffId, task.AssignedStaffId);
            Assert.AreEqual(StaffState.Travelling, worker.State);

            _f.Placement.Remove(_food);

            Assert.AreEqual(StaffTaskState.Cancelled, task.State);
            Assert.IsNull(task.AssignedStaffId);
            Assert.IsNull(worker.ActiveTaskId);
            Assert.AreEqual(StaffState.Idle, worker.State);
            Assert.IsFalse(_f.AgentOf(worker).HasRoute);
            Assert.AreEqual(0, _f.Tasks.Open.Count);
        }

        // ---- Movement ----

        [Test]
        public void MovingTheFacility_KeepsTheAssignment_AndTheVendorFollows()
        {
            var vendor = _f.Add(StaffRole.Vendor, 9, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);
            Assert.AreEqual(StaffState.Working, vendor.State);

            Assert.IsTrue(_f.Placement.Move(_food, StaffFixture.C(5, 5), Rotation90.Deg0));

            Assert.AreEqual(_foodId, vendor.AssignedFacilityId, "same facility id after a move");
            Assert.AreEqual(vendor.StaffId, _f.FacilityOf(_food).AssignedStaffId);
            Assert.AreEqual(StaffAssignmentResult.AlreadyAssignedToFacility, _f.Assignments.Assign(vendor.StaffId, _foodId));

            _f.Work.Process();
            Assert.AreEqual(StaffState.Travelling, vendor.State, "now has to walk to the new spot");
            Assert.IsFalse(_f.Destination(_food).Enabled, "closed while the vendor is away");
            Assert.AreEqual(StaffFixture.C(5, 6), _f.AgentOf(vendor).Destination);

            _f.AgentOf(vendor).Arrive();
            Assert.AreEqual(StaffState.Working, vendor.State);
            Assert.IsTrue(_f.Destination(_food).Enabled);
        }

        [Test]
        public void MovingTheFacilityOffThePath_MarksTheVendorUnreachable_ButKeepsTheAssignment()
        {
            var vendor = _f.Add(StaffRole.Vendor, 9, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);

            Assert.IsTrue(_f.Placement.Move(_food, StaffFixture.C(6, 10), Rotation90.Deg0));
            _f.Work.Process();

            Assert.AreEqual(_foodId, vendor.AssignedFacilityId);
            Assert.AreEqual(vendor.StaffId, _f.FacilityOf(_food).AssignedStaffId);
            Assert.AreEqual(StaffState.Unreachable, vendor.State);
            Assert.IsFalse(_f.Destination(_food).Enabled);

            // moving it back beside the path recovers
            Assert.IsTrue(_f.Placement.Move(_food, StaffFixture.C(9, 5), Rotation90.Deg0));
            _f.Work.Process();
            Assert.AreEqual(StaffState.Working, vendor.State);
            Assert.IsTrue(_f.Destination(_food).Enabled);
        }

        [Test]
        public void MovingAnUnstaffedFacility_KeepsItsRecord()
        {
            Assert.IsTrue(_f.Placement.Move(_food, StaffFixture.C(5, 5), Rotation90.Deg0));

            Assert.IsTrue(_f.Facilities.Contains(_foodId));
            Assert.AreEqual(1, _f.Facilities.Count);
        }
    }
}
