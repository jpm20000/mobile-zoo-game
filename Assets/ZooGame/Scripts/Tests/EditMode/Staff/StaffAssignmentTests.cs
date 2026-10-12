using NUnit.Framework;
using ZooGame.Data;
using ZooGame.Placement;
using ZooGame.Staff;

namespace ZooGame.Tests.EditMode.Staff
{
    /// <summary>Validation, the one-to-one rule and reassignment of vendors to stalls, through the assignment service only.</summary>
    public class StaffAssignmentTests
    {
        StaffFixture _f;
        PlacedObject _food, _drink;
        string _foodId, _drinkId;

        [SetUp]
        public void SetUp()
        {
            _f = new StaffFixture();
            _f.V.StandardPath();
            _food = _f.Stall(9, 5, VisitorFacilityKind.FoodStall);
            _drink = _f.Stall(7, 5, VisitorFacilityKind.DrinkStall);
            _foodId = StaffFixture.IdOf(_food);
            _drinkId = StaffFixture.IdOf(_drink);
        }

        [TearDown] public void TearDown() => _f.Dispose();

        void AssertUnassigned(StaffInstance s) => Assert.IsNull(s.AssignedFacilityId, s.DisplayName + " should have no facility");

        void AssertLinked(StaffInstance s, string facilityId)
        {
            Assert.AreEqual(facilityId, s.AssignedFacilityId);
            Assert.IsTrue(_f.Facilities.TryGet(facilityId, out var f));
            Assert.AreEqual(s.StaffId, f.AssignedStaffId);
        }

        // ---- Facilities ----

        [Test]
        public void FoodAndDrinkStalls_AreVendorFacilities_OthersAreNot()
        {
            _f.Stall(5, 5, VisitorFacilityKind.Toilet);
            _f.Stall(6, 5, VisitorFacilityKind.Bench);
            _f.Stall(11, 5, VisitorFacilityKind.Entrance);

            Assert.AreEqual(2, _f.Facilities.Count);
            Assert.AreEqual(StaffRole.Vendor, _f.FacilityOf(_food).AllowedRole);
            Assert.AreEqual(StaffRole.Vendor, _f.FacilityOf(_drink).AllowedRole);
            Assert.IsNull(_f.FacilityOf(_food).AssignedStaffId);
        }

        // ---- Valid assignment ----

        [Test]
        public void ValidVendorAssignment_SetsBothIds()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);

            Assert.AreEqual(StaffAssignmentResult.Success, _f.Assignments.Assign(vendor.StaffId, _foodId));

            AssertLinked(vendor, _foodId);
            Assert.IsTrue(_f.Assignments.TryGetFacilityOf(vendor.StaffId, out var f));
            Assert.AreEqual(_foodId, f.FacilityId);
            Assert.IsTrue(_f.Assignments.TryGetStaffOf(_foodId, out var s));
            Assert.AreSame(vendor, s);
        }

        [Test]
        public void Assignment_RaisesChangedOnce()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            int raised = 0;
            StaffAssignmentChange last = default;
            _f.Assignments.Changed += c => { raised++; last = c; };

            _f.Assignments.Assign(vendor.StaffId, _foodId);

            Assert.AreEqual(1, raised);
            Assert.AreEqual(vendor.StaffId, last.StaffId);
            Assert.IsNull(last.PreviousFacilityId);
            Assert.AreEqual(_foodId, last.FacilityId);
        }

        [Test]
        public void Unassign_ClearsBothIds()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);

            Assert.AreEqual(StaffAssignmentResult.Success, _f.Assignments.Unassign(vendor.StaffId));

            AssertUnassigned(vendor);
            Assert.IsNull(_f.FacilityOf(_food).AssignedStaffId);
            Assert.AreEqual(StaffAssignmentResult.NotAssigned, _f.Assignments.Unassign(vendor.StaffId));
        }

        [Test]
        public void UnassignFacility_ReleasesWhoeverRunsIt()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);

            Assert.AreEqual(StaffAssignmentResult.Success, _f.Assignments.UnassignFacility(_foodId));

            AssertUnassigned(vendor);
            Assert.IsNull(_f.FacilityOf(_food).AssignedStaffId);
            Assert.AreEqual(StaffAssignmentResult.NotAssigned, _f.Assignments.UnassignFacility(_foodId));
        }

        // ---- Rejections ----

        [Test]
        public void IncompatibleRoles_AreRejected_AndNothingChanges()
        {
            var keeper = _f.Add(StaffRole.Keeper, 4, 6);
            var maintenance = _f.Add(StaffRole.MaintenanceWorker, 5, 6);
            var vet = _f.Add(StaffRole.Veterinarian, 6, 6);
            int raised = 0;
            _f.Assignments.Changed += _ => raised++;

            foreach (var s in new[] { keeper, maintenance, vet })
            {
                Assert.AreEqual(StaffAssignmentResult.IncompatibleRole, _f.Assignments.Assign(s.StaffId, _foodId), s.Role.ToString());
                AssertUnassigned(s);
            }
            Assert.IsNull(_f.FacilityOf(_food).AssignedStaffId);
            Assert.AreEqual(0, raised);
        }

        [Test]
        public void UnknownStaffOrFacility_IsRejected()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            var toilet = _f.Stall(5, 5, VisitorFacilityKind.Toilet);

            Assert.AreEqual(StaffAssignmentResult.UnknownStaff, _f.Assignments.Assign("staff-missing", _foodId));
            Assert.AreEqual(StaffAssignmentResult.UnknownStaff, _f.Assignments.Assign(null, _foodId));
            Assert.AreEqual(StaffAssignmentResult.UnknownFacility, _f.Assignments.Assign(vendor.StaffId, "facility-999"));
            Assert.AreEqual(StaffAssignmentResult.UnknownFacility, _f.Assignments.Assign(vendor.StaffId, null));
            // a toilet is a visitor facility but has no staff role, so it is not an assignable facility at all
            Assert.AreEqual(StaffAssignmentResult.UnknownFacility, _f.Assignments.Assign(vendor.StaffId, StaffFixture.IdOf(toilet)));
            AssertUnassigned(vendor);
        }

        [Test]
        public void DisabledFacility_IsRejected_AndAcceptsAgainWhenEnabled()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            _f.Facilities.SetEnabled(_foodId, false);

            Assert.AreEqual(StaffAssignmentResult.FacilityDisabled, _f.Assignments.Assign(vendor.StaffId, _foodId));
            AssertUnassigned(vendor);

            _f.Facilities.SetEnabled(_foodId, true);
            Assert.AreEqual(StaffAssignmentResult.Success, _f.Assignments.Assign(vendor.StaffId, _foodId));
        }

        // ---- One-to-one ----

        [Test]
        public void OneVendorPerFacility()
        {
            var first = _f.Add(StaffRole.Vendor, 4, 6);
            var second = _f.Add(StaffRole.Vendor, 5, 6);
            _f.Assignments.Assign(first.StaffId, _foodId);

            Assert.AreEqual(StaffAssignmentResult.FacilityAlreadyStaffed, _f.Assignments.Assign(second.StaffId, _foodId));

            AssertLinked(first, _foodId);
            AssertUnassigned(second);
        }

        [Test]
        public void OneFacilityPerVendor()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);

            Assert.AreEqual(StaffAssignmentResult.StaffAlreadyAssigned, _f.Assignments.Assign(vendor.StaffId, _drinkId));

            AssertLinked(vendor, _foodId);
            Assert.IsNull(_f.FacilityOf(_drink).AssignedStaffId);
        }

        [Test]
        public void AssigningToTheSameFacilityAgain_ChangesNothing()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);
            int raised = 0;
            _f.Assignments.Changed += _ => raised++;

            Assert.AreEqual(StaffAssignmentResult.AlreadyAssignedToFacility, _f.Assignments.Assign(vendor.StaffId, _foodId));
            Assert.AreEqual(StaffAssignmentResult.AlreadyAssignedToFacility, _f.Assignments.Reassign(vendor.StaffId, _foodId));

            AssertLinked(vendor, _foodId);
            Assert.AreEqual(0, raised);
        }

        // ---- Reassignment ----

        [Test]
        public void Reassign_MovesTheVendor_AndFreesTheOldFacility()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            var other = _f.Add(StaffRole.Vendor, 5, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);
            StaffAssignmentChange last = default;
            _f.Assignments.Changed += c => last = c;

            Assert.AreEqual(StaffAssignmentResult.Success, _f.Assignments.Reassign(vendor.StaffId, _drinkId));

            AssertLinked(vendor, _drinkId);
            Assert.IsNull(_f.FacilityOf(_food).AssignedStaffId);
            Assert.AreEqual(_foodId, last.PreviousFacilityId);
            Assert.AreEqual(_drinkId, last.FacilityId);
            // the vacated stall is free for someone else
            Assert.AreEqual(StaffAssignmentResult.Success, _f.Assignments.Assign(other.StaffId, _foodId));
            AssertLinked(other, _foodId);
        }

        [Test]
        public void Reassign_WithoutAnAssignment_IsRejected()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);

            Assert.AreEqual(StaffAssignmentResult.NotAssigned, _f.Assignments.Reassign(vendor.StaffId, _foodId));

            AssertUnassigned(vendor);
            Assert.IsNull(_f.FacilityOf(_food).AssignedStaffId);
        }

        [Test]
        public void FailedReassignment_PreservesTheOriginalAssignment()
        {
            var vendor = _f.Add(StaffRole.Vendor, 4, 6);
            var other = _f.Add(StaffRole.Vendor, 5, 6);
            var keeper = _f.Add(StaffRole.Keeper, 6, 6);
            _f.Assignments.Assign(vendor.StaffId, _foodId);
            _f.Assignments.Assign(other.StaffId, _drinkId);
            int raised = 0;
            _f.Assignments.Changed += _ => raised++;

            // target already staffed
            Assert.AreEqual(StaffAssignmentResult.FacilityAlreadyStaffed, _f.Assignments.Reassign(vendor.StaffId, _drinkId));
            AssertLinked(vendor, _foodId);
            AssertLinked(other, _drinkId);

            // target unknown
            Assert.AreEqual(StaffAssignmentResult.UnknownFacility, _f.Assignments.Reassign(vendor.StaffId, "facility-999"));
            AssertLinked(vendor, _foodId);

            // incompatible role
            Assert.AreEqual(StaffAssignmentResult.IncompatibleRole, _f.Assignments.Reassign(keeper.StaffId, _drinkId));
            AssertUnassigned(keeper);

            // target disabled (free stall)
            var third = _f.Stall(8, 5, VisitorFacilityKind.DrinkStall);
            _f.Facilities.SetEnabled(StaffFixture.IdOf(third), false);
            Assert.AreEqual(StaffAssignmentResult.FacilityDisabled, _f.Assignments.Reassign(vendor.StaffId, StaffFixture.IdOf(third)));
            AssertLinked(vendor, _foodId);
            Assert.IsNull(_f.FacilityOf(third).AssignedStaffId);

            Assert.AreEqual(0, raised);
        }

        [Test]
        public void StaffInstanceAssignedFacilityId_CannotBeWrittenOutsideTheStaffAssembly()
        {
            // Compile-time guarantee, checked by reflection: the setter is not public.
            var prop = typeof(StaffInstance).GetProperty(nameof(StaffInstance.AssignedFacilityId));
            Assert.IsNotNull(prop);
            Assert.IsFalse(prop.SetMethod.IsPublic);
            var facilityProp = typeof(StaffFacility).GetProperty(nameof(StaffFacility.AssignedStaffId));
            Assert.IsFalse(facilityProp.SetMethod.IsPublic);
        }
    }
}
