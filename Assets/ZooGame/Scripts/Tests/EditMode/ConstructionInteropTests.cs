using NUnit.Framework;
using ZooGame.Placement;
using ZooGame.World;

namespace ZooGame.Tests.EditMode
{
    /// <summary>M3 construction and M2 object placement share one grid; they must not corrupt each other.</summary>
    public class ConstructionInteropTests
    {
        PlacementFixture _p;
        ConstructionModel _model;

        [SetUp]
        public void SetUp()
        {
            _p = new PlacementFixture();
            _model = new ConstructionModel(_p.Grid);
        }

        [TearDown] public void TearDown() => _p.Dispose();

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        [Test]
        public void ObjectCannotBePlacedOnAPath()
        {
            var def = _p.Define("stall", 2, 2);
            _model.BuildPaths(new[] { C(6, 6) });
            var r = _p.Validator.Validate(def, C(5, 5), Rotation90.Deg0); // covers (6,6)
            Assert.AreEqual(PlacementFailure.RuleFailed, r.Failure);
            Assert.AreEqual(C(6, 6), r.BlockingCell);
            Assert.IsTrue(_p.Validator.Validate(def, C(8, 8), Rotation90.Deg0).IsValid);
        }

        [Test]
        public void PathCannotBeBuiltUnderAnObject()
        {
            var def = _p.Define("stall", 2, 2);
            _p.Map.Add(def, C(6, 6), Rotation90.Deg0);
            Assert.AreEqual(BuildFailure.Occupied, _model.CanBuildPath(C(7, 7)).Failure);
            Assert.AreEqual(1, _model.BuildPaths(new[] { C(7, 7), C(8, 7) }));
            Assert.IsFalse(_p.Grid.HasPath(C(7, 7)));
            Assert.IsTrue(_p.Grid.HasPath(C(8, 7)));
        }

        [Test]
        public void RemovingAPath_NextToAnObject_LeavesTheObjectsOccupancyIntact()
        {
            var def = _p.Define("stall", 2, 2);
            var obj = _p.Map.Add(def, C(6, 6), Rotation90.Deg0);
            _model.BuildPaths(new[] { C(8, 6), C(8, 7) });
            _model.RemovePaths(new[] { C(8, 6), C(8, 7) });
            Assert.AreEqual(4, _p.OccupiedCount());
            Assert.AreSame(obj, _p.Map.GetAt(C(7, 7)));
        }

        [Test]
        public void ObjectsMovingOrBeingDeleted_DoNotAffectFencesOrEnclosures()
        {
            var def = _p.Define("stall", 2, 2);
            var obj = _p.Map.Add(def, C(5, 5), Rotation90.Deg0);
            for (int x = 4; x < 9; x++) // fence a 5x5 pen around it
            {
                _model.BuildFences(new[] { new FenceEdge(new EdgeCoord(EdgeAxis.X, x, 4), EdgeKind.Fence), new FenceEdge(new EdgeCoord(EdgeAxis.X, x, 9), EdgeKind.Fence) });
                _model.BuildFences(new[] { new FenceEdge(new EdgeCoord(EdgeAxis.Z, 4, x), EdgeKind.Fence), new FenceEdge(new EdgeCoord(EdgeAxis.Z, 9, x), EdgeKind.Fence) });
            }
            Assert.AreEqual(1, _model.Enclosures.Count);
            int id = _model.Enclosures.GetAt(C(6, 6)).Id;

            Assert.IsTrue(_p.Map.Move(obj, C(7, 7), Rotation90.Deg0));
            _p.Map.Remove(obj);

            Assert.AreEqual(id, _model.Enclosures.GetAt(C(6, 6)).Id);
            Assert.AreEqual(25, _model.Enclosures.GetAt(C(6, 6)).Area);
        }

        [Test]
        public void ObjectsCanStandInsideAndNextToFencesWithoutBlockingThem()
        {
            var def = _p.Define("stall", 2, 2);
            _model.BuildFences(ConstructionFixture.RectEdges(5, 5, 4, 4));
            // Footprint cells may touch fences freely: fences live on edges.
            Assert.IsTrue(_p.Validator.Validate(def, C(5, 5), Rotation90.Deg0).IsValid);
            Assert.IsTrue(_p.Validator.Validate(def, C(9, 5), Rotation90.Deg0).IsValid);
        }
    }
}
