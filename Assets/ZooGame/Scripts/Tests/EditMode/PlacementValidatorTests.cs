using NUnit.Framework;
using ZooGame.Placement;
using ZooGame.World;

namespace ZooGame.Tests.EditMode
{
    public class PlacementValidatorTests
    {
        PlacementFixture _f;

        [SetUp] public void SetUp() => _f = new PlacementFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        [Test]
        public void FootprintFullyInsideUnlockedLand_IsValid()
        {
            var def = _f.Define("a", 3, 4);
            Assert.IsTrue(_f.Validator.Validate(def, new GridCoord(5, 5), Rotation90.Deg0).IsValid);
        }

        [Test]
        public void FootprintTouchingUnlockedEdges_IsValid()
        {
            var def = _f.Define("a", 8, 8);
            Assert.IsTrue(_f.Validator.Validate(def, new GridCoord(4, 4), Rotation90.Deg0).IsValid);
        }

        [TestCase(-1, 0)]
        [TestCase(0, -1)]
        [TestCase(15, 15)]
        [TestCase(100, 100)]
        public void CellsOutsideGrid_AreRejectedAsOutOfBounds(int x, int z)
        {
            var def = _f.Define("a", 2, 2);
            var r = _f.Validator.Validate(def, new GridCoord(x, z), Rotation90.Deg0);
            Assert.AreEqual(PlacementFailure.OutOfBounds, r.Failure);
        }

        [Test]
        public void FootprintStraddlingTheGridEdge_IsRejectedEvenIfSomeCellsAreInside()
        {
            var def = _f.Define("a", 2, 2);
            var r = _f.Validator.Validate(def, new GridCoord(15, 8), Rotation90.Deg0); // x 15..16
            Assert.AreEqual(PlacementFailure.OutOfBounds, r.Failure);
            Assert.AreEqual(16, r.BlockingCell.X);
        }

        [Test]
        public void LockedCell_IsRejected()
        {
            var def = _f.Define("a", 1, 1);
            var r = _f.Validator.Validate(def, new GridCoord(2, 2), Rotation90.Deg0);
            Assert.AreEqual(PlacementFailure.LockedLand, r.Failure);
            Assert.AreEqual(new GridCoord(2, 2), r.BlockingCell);
        }

        [Test]
        public void FootprintPartlyOnLockedLand_IsRejected()
        {
            var def = _f.Define("a", 3, 3);
            // unlocked is 4..11; x = 3..5 includes locked column 3.
            Assert.AreEqual(PlacementFailure.LockedLand, _f.Validator.Validate(def, new GridCoord(3, 6), Rotation90.Deg0).Failure);
            // far side: x = 10..12 includes locked column 12.
            Assert.AreEqual(PlacementFailure.LockedLand, _f.Validator.Validate(def, new GridCoord(10, 6), Rotation90.Deg0).Failure);
        }

        [Test]
        public void UnlockingLand_MakesPreviouslyRejectedPlacementValid()
        {
            var def = _f.Define("a", 2, 2);
            Assert.IsFalse(_f.Validator.Validate(def, new GridCoord(12, 6), Rotation90.Deg0).IsValid);
            _f.Grid.UnlockRect(12, 4, 2, 8);
            Assert.IsTrue(_f.Validator.Validate(def, new GridCoord(12, 6), Rotation90.Deg0).IsValid);
        }

        [Test]
        public void OccupiedCell_IsRejected()
        {
            var def = _f.Define("a", 2, 2);
            Assert.IsNotNull(_f.Map.Add(def, new GridCoord(6, 6), Rotation90.Deg0));
            var r = _f.Validator.Validate(def, new GridCoord(7, 7), Rotation90.Deg0); // overlaps only at (7,7)
            Assert.AreEqual(PlacementFailure.Occupied, r.Failure);
            Assert.AreEqual(new GridCoord(7, 7), r.BlockingCell);
        }

        [Test]
        public void CellOccupiedBySomethingOtherThanAPlacedObject_IsAlsoRejected()
        {
            var def = _f.Define("a", 1, 1);
            _f.Grid.SetOccupied(new GridCoord(6, 6), true);
            Assert.AreEqual(PlacementFailure.Occupied, _f.Validator.Validate(def, new GridCoord(6, 6), Rotation90.Deg0).Failure);
        }

        [Test]
        public void AdjacentFootprints_DoNotConflict()
        {
            var def = _f.Define("a", 2, 2);
            _f.Map.Add(def, new GridCoord(6, 6), Rotation90.Deg0);
            Assert.IsTrue(_f.Validator.Validate(def, new GridCoord(8, 6), Rotation90.Deg0).IsValid);
            Assert.IsTrue(_f.Validator.Validate(def, new GridCoord(6, 8), Rotation90.Deg0).IsValid);
            Assert.IsTrue(_f.Validator.Validate(def, new GridCoord(4, 6), Rotation90.Deg0).IsValid);
        }

        [Test]
        public void RotationChangesWhichCellsAreChecked()
        {
            var def = _f.Define("a", 2, 3);
            // Origin (10, 4): unrotated 2x3 spans x 10..11, z 4..6 -> valid. Rotated 3x2 spans x 10..12 -> locked column 12.
            Assert.IsTrue(_f.Validator.Validate(def, new GridCoord(10, 4), Rotation90.Deg0).IsValid);
            Assert.AreEqual(PlacementFailure.LockedLand, _f.Validator.Validate(def, new GridCoord(10, 4), Rotation90.Deg90).Failure);
            Assert.IsTrue(_f.Validator.Validate(def, new GridCoord(10, 4), Rotation90.Deg180).IsValid);
        }

        [Test]
        public void RotationAlsoChangesOccupancyConflicts()
        {
            var def = _f.Define("a", 2, 3);
            var blocker = _f.Define("b", 1, 1);
            _f.Map.Add(blocker, new GridCoord(8, 5), Rotation90.Deg0);
            // Unrotated 2x3 at (6,4) covers x 6..7: free. Rotated 3x2 covers x 6..8, z 4..5: hits (8,5).
            Assert.IsTrue(_f.Validator.Validate(def, new GridCoord(6, 4), Rotation90.Deg0).IsValid);
            Assert.AreEqual(PlacementFailure.Occupied, _f.Validator.Validate(def, new GridCoord(6, 4), Rotation90.Deg90).Failure);
        }

        [Test]
        public void IgnoredObject_DoesNotBlockItself()
        {
            var def = _f.Define("a", 2, 2);
            var obj = _f.Map.Add(def, new GridCoord(6, 6), Rotation90.Deg0);
            Assert.AreEqual(PlacementFailure.Occupied, _f.Validator.Validate(def, new GridCoord(7, 6), Rotation90.Deg0).Failure);
            Assert.IsTrue(_f.Validator.Validate(def, new GridCoord(7, 6), Rotation90.Deg0, obj).IsValid);
        }

        [Test]
        public void IgnoredObject_StillBlocksOnOtherObjects()
        {
            var def = _f.Define("a", 2, 2);
            var obj = _f.Map.Add(def, new GridCoord(4, 4), Rotation90.Deg0);
            _f.Map.Add(def, new GridCoord(8, 4), Rotation90.Deg0);
            Assert.AreEqual(PlacementFailure.Occupied, _f.Validator.Validate(def, new GridCoord(7, 4), Rotation90.Deg0, obj).Failure);
        }

        [Test]
        public void NullDefinition_IsInvalid()
        {
            Assert.AreEqual(PlacementFailure.InvalidDefinition, _f.Validator.Validate(null, new GridCoord(6, 6), Rotation90.Deg0).Failure);
        }

        sealed class RejectColumnRule : IPlacementRule
        {
            public int Column;
            public PlacementResult Evaluate(in PlacementQuery q)
            {
                if (Footprint.Contains(q.Origin, q.Width, q.Height, new GridCoord(Column, q.Origin.Z)))
                    return PlacementResult.Fail(PlacementFailure.RuleFailed, new GridCoord(Column, q.Origin.Z), "no building here");
                return PlacementResult.Valid;
            }
        }

        [Test]
        public void CustomRules_PlugInWithoutChangingTheValidator()
        {
            var def = _f.Define("a", 2, 2);
            Assert.IsTrue(_f.Validator.Validate(def, new GridCoord(6, 6), Rotation90.Deg0).IsValid);

            var rule = new RejectColumnRule { Column = 7 };
            _f.Validator.AddRule(rule);
            var r = _f.Validator.Validate(def, new GridCoord(6, 6), Rotation90.Deg0);
            Assert.AreEqual(PlacementFailure.RuleFailed, r.Failure);
            Assert.AreEqual("no building here", r.Detail);

            _f.Validator.RemoveRule(rule);
            Assert.IsTrue(_f.Validator.Validate(def, new GridCoord(6, 6), Rotation90.Deg0).IsValid);
        }
    }
}
