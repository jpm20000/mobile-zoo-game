using System.Collections.Generic;
using NUnit.Framework;
using ZooGame.Placement;
using ZooGame.World;

namespace ZooGame.Tests.EditMode
{
    public class PlacementMapTests
    {
        PlacementFixture _f;

        [SetUp] public void SetUp() => _f = new PlacementFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(2, 3)]
        [TestCase(3, 4)]
        public void Add_OccupiesEveryFootprintCellAndAssociatesThemWithTheObject(int w, int h)
        {
            var def = _f.Define("a", w, h);
            var obj = _f.Map.Add(def, new GridCoord(5, 5), Rotation90.Deg0);

            Assert.IsNotNull(obj);
            Assert.AreEqual(w * h, obj.Cells.Count);
            Assert.AreEqual(w * h, _f.OccupiedCount());
            foreach (var c in obj.Cells)
            {
                Assert.IsTrue(_f.Grid.IsOccupied(c), c.ToString());
                Assert.AreEqual(obj.Id, _f.Map.OwnerIdAt(c));
                Assert.AreSame(obj, _f.Map.GetAt(c));
            }
            Assert.IsNull(_f.Map.GetAt(new GridCoord(5 + w, 5)));
        }

        [Test]
        public void Add_RotatedFootprintOccupiesTheSwappedRectangle()
        {
            var def = _f.Define("a", 2, 3);
            var obj = _f.Map.Add(def, new GridCoord(5, 5), Rotation90.Deg90);
            Assert.AreEqual((3, 2), (obj.Width, obj.Height));
            Assert.IsTrue(_f.Grid.IsOccupied(new GridCoord(7, 6)));   // x extends to 7
            Assert.IsFalse(_f.Grid.IsOccupied(new GridCoord(5, 7)));  // z no longer reaches 7
            Assert.AreEqual(6, _f.OccupiedCount());
        }

        [Test]
        public void Add_OverlappingObjectIsRefusedAndChangesNothing()
        {
            var def = _f.Define("a", 2, 2);
            var first = _f.Map.Add(def, new GridCoord(5, 5), Rotation90.Deg0);
            var second = _f.Map.Add(def, new GridCoord(6, 6), Rotation90.Deg0);

            Assert.IsNull(second);
            Assert.AreEqual(1, _f.Map.Count);
            Assert.AreEqual(4, _f.OccupiedCount());
            Assert.AreSame(first, _f.Map.GetAt(new GridCoord(6, 6)));
            Assert.IsNull(_f.Map.GetAt(new GridCoord(7, 7)));
        }

        [Test]
        public void Add_OutsideGridIsRefused()
        {
            var def = _f.Define("a", 2, 2);
            Assert.IsNull(_f.Map.Add(def, new GridCoord(15, 15), Rotation90.Deg0));
            Assert.AreEqual(0, _f.Map.Count);
            Assert.AreEqual(0, _f.OccupiedCount());
        }

        [Test]
        public void Remove_ReleasesAllCells()
        {
            var def = _f.Define("a", 3, 4);
            var obj = _f.Map.Add(def, new GridCoord(5, 5), Rotation90.Deg0);
            Assert.AreEqual(12, _f.OccupiedCount());

            Assert.IsTrue(_f.Map.Remove(obj));
            Assert.AreEqual(0, _f.OccupiedCount());
            Assert.AreEqual(0, _f.Map.Count);
            foreach (var c in obj.Cells)
            {
                Assert.AreEqual(PlacementMap.NoOwner, _f.Map.OwnerIdAt(c));
                Assert.IsTrue(_f.Grid.IsAvailable(c));
            }
            Assert.IsFalse(_f.Map.Remove(obj), "removing twice is a no-op");
        }

        [Test]
        public void Remove_LeavesOtherObjectsUntouched()
        {
            var def = _f.Define("a", 2, 2);
            var a = _f.Map.Add(def, new GridCoord(4, 4), Rotation90.Deg0);
            var b = _f.Map.Add(def, new GridCoord(6, 4), Rotation90.Deg0);
            _f.Map.Remove(a);
            Assert.AreEqual(4, _f.OccupiedCount());
            Assert.AreSame(b, _f.Map.GetAt(new GridCoord(6, 4)));
        }

        [Test]
        public void RemovedSpace_CanBeReusedImmediately()
        {
            var def = _f.Define("a", 2, 2);
            var a = _f.Map.Add(def, new GridCoord(5, 5), Rotation90.Deg0);
            _f.Map.Remove(a);
            Assert.IsNotNull(_f.Map.Add(def, new GridCoord(5, 5), Rotation90.Deg0));
        }

        [Test]
        public void Move_ReleasesOldCellsAndClaimsNewOnes()
        {
            var def = _f.Define("a", 2, 3);
            var obj = _f.Map.Add(def, new GridCoord(4, 4), Rotation90.Deg0);
            var oldCells = new List<GridCoord>(obj.Cells);

            Assert.IsTrue(_f.Map.Move(obj, new GridCoord(8, 5), Rotation90.Deg0));

            foreach (var c in oldCells) { Assert.IsFalse(_f.Grid.IsOccupied(c)); Assert.AreEqual(0, _f.Map.OwnerIdAt(c)); }
            foreach (var c in obj.Cells) { Assert.IsTrue(_f.Grid.IsOccupied(c)); Assert.AreEqual(obj.Id, _f.Map.OwnerIdAt(c)); }
            Assert.AreEqual(6, _f.OccupiedCount());
            Assert.AreEqual(new GridCoord(8, 5), obj.Origin);
        }

        [Test]
        public void Move_ToOverlappingItsOwnOldCellsIsAllowed()
        {
            var def = _f.Define("a", 2, 2);
            var obj = _f.Map.Add(def, new GridCoord(5, 5), Rotation90.Deg0);
            Assert.IsTrue(_f.Map.Move(obj, new GridCoord(6, 5), Rotation90.Deg0));
            Assert.AreEqual(4, _f.OccupiedCount());
            Assert.IsFalse(_f.Grid.IsOccupied(new GridCoord(5, 5)));
            Assert.IsTrue(_f.Grid.IsOccupied(new GridCoord(7, 6)));
        }

        [Test]
        public void Move_OntoAnotherObjectFailsAndRestoresNothingBecauseNothingChanged()
        {
            var def = _f.Define("a", 2, 2);
            var a = _f.Map.Add(def, new GridCoord(4, 4), Rotation90.Deg0);
            var b = _f.Map.Add(def, new GridCoord(8, 4), Rotation90.Deg0);

            Assert.IsFalse(_f.Map.Move(a, new GridCoord(7, 4), Rotation90.Deg0));

            Assert.AreEqual(new GridCoord(4, 4), a.Origin);
            Assert.AreEqual(8, _f.OccupiedCount());
            Assert.AreSame(a, _f.Map.GetAt(new GridCoord(5, 5)));
            Assert.AreSame(b, _f.Map.GetAt(new GridCoord(8, 4)));
        }

        [Test]
        public void Move_OutsideGridFailsAndKeepsOriginalCells()
        {
            var def = _f.Define("a", 2, 2);
            var a = _f.Map.Add(def, new GridCoord(4, 4), Rotation90.Deg0);
            Assert.IsFalse(_f.Map.Move(a, new GridCoord(15, 4), Rotation90.Deg0));
            Assert.AreSame(a, _f.Map.GetAt(new GridCoord(4, 4)));
            Assert.AreEqual(4, _f.OccupiedCount());
        }

        [Test]
        public void Move_WithRotationKeepsCellCountAndSwapsExtents()
        {
            var def = _f.Define("a", 2, 3);
            var obj = _f.Map.Add(def, new GridCoord(5, 5), Rotation90.Deg0);
            Assert.IsTrue(_f.Map.Move(obj, new GridCoord(5, 5), Rotation90.Deg90));
            Assert.AreEqual((3, 2), (obj.Width, obj.Height));
            Assert.AreEqual(6, _f.OccupiedCount());
            Assert.IsFalse(_f.Grid.IsOccupied(new GridCoord(5, 7)));
            Assert.IsTrue(_f.Grid.IsOccupied(new GridCoord(7, 6)));
        }

        [Test]
        public void ObjectNotInTheMap_CannotBeMovedOrRemoved()
        {
            var def = _f.Define("a", 1, 1);
            var obj = _f.Map.Add(def, new GridCoord(5, 5), Rotation90.Deg0);
            _f.Map.Remove(obj);
            Assert.IsFalse(_f.Map.Move(obj, new GridCoord(6, 6), Rotation90.Deg0));
            Assert.AreEqual(0, _f.OccupiedCount());
        }

        [Test]
        public void Events_FireOncePerChange()
        {
            var def = _f.Define("a", 1, 1);
            var log = new List<string>();
            _f.Map.Placed += o => log.Add("placed");
            _f.Map.Moved += o => log.Add("moved");
            _f.Map.Removed += o => log.Add("removed");

            var obj = _f.Map.Add(def, new GridCoord(5, 5), Rotation90.Deg0);
            _f.Map.Move(obj, new GridCoord(6, 6), Rotation90.Deg0);
            _f.Map.Move(obj, new GridCoord(99, 99), Rotation90.Deg0); // refused: no event
            _f.Map.Remove(obj);

            CollectionAssert.AreEqual(new[] { "placed", "moved", "removed" }, log);
        }
    }
}
