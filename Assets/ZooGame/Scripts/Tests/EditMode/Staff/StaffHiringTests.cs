using NUnit.Framework;
using ZooGame.Data;
using ZooGame.Economy;
using ZooGame.Staff;

namespace ZooGame.Tests.EditMode.Staff
{
    /// <summary>Hiring goes through the purchase flow: refused hires cost nothing, dismissal cleans up through events.</summary>
    public class StaffHiringTests
    {
        StaffFixture _f;
        EconomyService _economy;
        PurchaseService _purchases;
        StaffHiringService _hiring;

        [SetUp]
        public void SetUp()
        {
            _f = new StaffFixture();
            _economy = new EconomyService(EconomySettings.Default); // 25000 Money
            _purchases = new PurchaseService(_economy);
            _hiring = new StaffHiringService(_f.Config, _f.Staff, _f.V.Destinations, _purchases);
        }

        [TearDown] public void TearDown() => _f.Dispose();

        long Money => _economy.GetBalance(Currency.Money);

        void Entrance()
        {
            _f.V.StandardPath();
            _f.Stall(11, 5, VisitorFacilityKind.Entrance);
        }

        [Test]
        public void HireWithoutAnEntrance_IsRefused_AndCostsNothing()
        {
            Assert.AreEqual(StaffHireResult.NoEntrance, _hiring.Hire(StaffRole.Vendor, out var hired));

            Assert.IsNull(hired);
            Assert.AreEqual(0, _f.Staff.Count);
            Assert.AreEqual(25000, Money);
        }

        [Test]
        public void Hire_RegistersTheMember_AtTheEntrance_AndChargesTheHireCost()
        {
            Entrance();
            int cost = _f.Config.HireCost(StaffRole.Keeper);
            Assert.Greater(cost, 0);

            Assert.AreEqual(StaffHireResult.Success, _hiring.Hire(StaffRole.Keeper, out var hired));

            Assert.AreSame(hired, _f.Staff.All[0]);
            Assert.AreEqual(StaffRole.Keeper, hired.Role);
            Assert.IsNull(hired.AssignedFacilityId);
            Assert.AreEqual(StaffState.Idle, hired.State);
            Assert.AreEqual(25000 - cost, Money);
            Assert.IsTrue(_economy.TryGetLastTransaction(out var t));
            Assert.AreEqual(TransactionCategory.StaffHiring, t.Category);
            Assert.AreEqual(TransactionType.Expense, t.Type);
            Assert.AreEqual(cost, t.Amount);
            Assert.AreEqual(hired.StaffId, t.SourceId);
        }

        [Test]
        public void HireWithInsufficientFunds_IsRefused_AndChangesNothing()
        {
            Entrance();
            _economy.DebugCharge(Currency.Money, 24950);
            var before = _economy.HistoryCount;

            Assert.AreEqual(StaffHireResult.InsufficientFunds, _hiring.Hire(StaffRole.Veterinarian, out var hired));

            Assert.IsNull(hired);
            Assert.AreEqual(0, _f.Staff.Count);
            Assert.AreEqual(50, Money);
            Assert.AreEqual(before, _economy.HistoryCount);
        }

        [Test]
        public void InvalidRole_IsRefused()
        {
            Entrance();

            Assert.AreEqual(StaffHireResult.InvalidRole, _hiring.Hire(StaffRole.None, out _));
            Assert.AreEqual(StaffHireResult.InvalidRole, _hiring.Hire((StaffRole)99, out _));
            Assert.AreEqual(25000, Money);
        }

        [Test]
        public void StaffCap_IsEnforced()
        {
            Entrance();
            for (int i = 0; i < _hiring.MaxStaff; i++)
                Assert.AreEqual(StaffHireResult.Success, _hiring.Hire(StaffRole.Keeper, out _), "hire " + i);

            Assert.AreEqual(StaffHireResult.StaffCapReached, _hiring.Hire(StaffRole.Keeper, out var extra));
            Assert.IsNull(extra);
            Assert.AreEqual(_hiring.MaxStaff, _f.Staff.Count);
        }

        [Test]
        public void DismissingAVendor_FreesTheStall()
        {
            Entrance();
            var stall = _f.Stall(9, 5, VisitorFacilityKind.FoodStall);
            _hiring.Hire(StaffRole.Vendor, out var vendor);
            Assert.AreEqual(StaffAssignmentResult.Success, _f.Assignments.Assign(vendor.StaffId, StaffFixture.IdOf(stall)));

            Assert.IsTrue(_hiring.Dismiss(vendor.StaffId));

            Assert.IsFalse(_f.Staff.Contains(vendor.StaffId));
            Assert.IsNull(_f.FacilityOf(stall).AssignedStaffId);
            Assert.IsFalse(_hiring.Dismiss(vendor.StaffId), "already gone");
        }
    }
}
