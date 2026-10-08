using System.Collections.Generic;
using NUnit.Framework;
using ZooGame.World;

namespace ZooGame.Tests.EditMode
{
    public class PathTests
    {
        ConstructionFixture _f;

        [SetUp] public void SetUp() => _f = new ConstructionFixture();

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        // ---- Placing / removing ----

        [Test]
        public void BuildPaths_SetsGridPathFlagsOnly()
        {
            Assert.AreEqual(3, _f.BuildPaths((5, 5), (6, 5), (7, 5)));
            Assert.IsTrue(_f.Grid.HasPath(C(5, 5)));
            Assert.IsTrue(_f.Grid.HasPath(C(7, 5)));
            Assert.IsFalse(_f.Grid.HasPath(C(8, 5)));
            Assert.IsFalse(_f.Grid.IsOccupied(C(5, 5)), "a path is not an object");
            Assert.AreEqual(3, _f.PathCount());
        }

        [Test]
        public void BuildPaths_AlreadyBuiltCellsAreNotCountedTwice()
        {
            _f.BuildPaths((5, 5));
            Assert.AreEqual(1, _f.BuildPaths((5, 5), (6, 5)));
        }

        [Test]
        public void RemovePath_ClearsOnlyThatCell()
        {
            _f.BuildPaths((5, 5), (6, 5), (7, 5));
            Assert.IsTrue(_f.Model.RemovePath(C(6, 5)));
            Assert.IsFalse(_f.Grid.HasPath(C(6, 5)));
            Assert.IsTrue(_f.Grid.HasPath(C(5, 5)));
            Assert.IsTrue(_f.Grid.HasPath(C(7, 5)));
            Assert.IsFalse(_f.Model.RemovePath(C(6, 5)), "nothing left to remove");
        }

        [Test]
        public void RemovePath_LeavesUnrelatedOccupancyAndEnclosuresUntouched()
        {
            _f.BuildPaths((5, 5), (6, 5));
            _f.Grid.SetOccupied(C(9, 9), true);
            _f.BuildRect(8, 8, 3, 3);
            int enclosureId = _f.EnclosureIdAt(9, 9);
            Assert.AreNotEqual(0, enclosureId);

            _f.Model.RemovePath(C(5, 5));

            Assert.IsTrue(_f.Grid.IsOccupied(C(9, 9)));
            Assert.AreEqual(enclosureId, _f.EnclosureIdAt(9, 9));
            Assert.IsTrue(_f.Grid.HasPath(C(6, 5)));
        }

        [Test]
        public void PathsChanged_FiresOnRealChangesOnly()
        {
            int count = 0;
            _f.Grid.PathsChanged += () => count++;
            _f.BuildPaths((5, 5));
            Assert.AreEqual(1, count);
            _f.BuildPaths((5, 5)); // already there
            Assert.AreEqual(1, count);
            _f.Model.RemovePath(C(5, 5));
            Assert.AreEqual(2, count);
            _f.Grid.SetOccupied(C(5, 5), true); // not a path change
            Assert.AreEqual(2, count);
        }

        // ---- Validation ----

        [Test]
        public void LockedCell_IsRejected()
        {
            Assert.AreEqual(BuildFailure.LockedLand, _f.Model.CanBuildPath(C(2, 2)).Failure);
            Assert.AreEqual(0, _f.BuildPaths((2, 2), (3, 3)));
            Assert.AreEqual(0, _f.PathCount());
        }

        [Test]
        public void OutsideGrid_IsRejected()
        {
            Assert.AreEqual(BuildFailure.OutOfBounds, _f.Model.CanBuildPath(C(-1, 5)).Failure);
            Assert.AreEqual(BuildFailure.OutOfBounds, _f.Model.CanBuildPath(C(16, 5)).Failure);
            Assert.AreEqual(0, _f.BuildPaths((-1, 5), (99, 99)));
        }

        [Test]
        public void OccupiedCell_IsRejected_ButNeighbourIsAccepted()
        {
            _f.Grid.SetOccupied(C(6, 6), true);
            Assert.AreEqual(BuildFailure.Occupied, _f.Model.CanBuildPath(C(6, 6)).Failure);
            Assert.AreEqual(1, _f.BuildPaths((6, 6), (7, 6)));
            Assert.IsFalse(_f.Grid.HasPath(C(6, 6)));
            Assert.IsTrue(_f.Grid.HasPath(C(7, 6)));
        }

        sealed class NoColumn7 : IPathRule
        {
            public BuildResult Evaluate(ZooGrid grid, GridCoord cell) =>
                cell.X == 7 ? BuildResult.Fail(BuildFailure.RuleFailed, "reserved") : BuildResult.Valid;
        }

        [Test]
        public void CustomRules_PlugIn()
        {
            var rule = new NoColumn7();
            _f.Model.PathRules.AddRule(rule);
            var r = _f.Model.CanBuildPath(C(7, 5));
            Assert.AreEqual(BuildFailure.RuleFailed, r.Failure);
            Assert.AreEqual("reserved", r.Detail);
            Assert.AreEqual(1, _f.BuildPaths((6, 5), (7, 5)));
            _f.Model.PathRules.RemoveRule(rule);
            Assert.IsTrue(_f.Model.CanBuildPath(C(7, 5)).IsValid);
        }

        // ---- Connectivity ----

        [Test]
        public void Mask_ReportsNorthEastSouthWest()
        {
            _f.BuildPaths((6, 6), (6, 7), (7, 6));            // centre (6,6) with north and east neighbours
            var n = PathConnectivity.Describe(_f.Grid, C(6, 6));
            Assert.IsTrue(n.Connects(Direction4.North));
            Assert.IsTrue(n.Connects(Direction4.East));
            Assert.IsFalse(n.Connects(Direction4.South));
            Assert.IsFalse(n.Connects(Direction4.West));
            Assert.AreEqual(1 | 2, n.Mask);
        }

        [Test]
        public void DiagonalNeighbours_DoNotConnect()
        {
            _f.BuildPaths((6, 6), (7, 7), (5, 5));
            Assert.AreEqual(PathShape.Isolated, PathConnectivity.Describe(_f.Grid, C(6, 6)).Shape);
        }

        [Test]
        public void Shapes_AreClassifiedFromNeighbours()
        {
            // Isolated
            _f.BuildPaths((5, 5));
            Assert.AreEqual(PathShape.Isolated, PathConnectivity.Describe(_f.Grid, C(5, 5)).Shape);

            // Dead end + straight: a line of three.
            _f.BuildPaths((8, 5), (9, 5), (10, 5));
            Assert.AreEqual(PathShape.DeadEnd, PathConnectivity.Describe(_f.Grid, C(8, 5)).Shape);
            Assert.AreEqual(PathShape.Straight, PathConnectivity.Describe(_f.Grid, C(9, 5)).Shape);
            Assert.AreEqual(PathShape.DeadEnd, PathConnectivity.Describe(_f.Grid, C(10, 5)).Shape);

            // Corner: an L.
            _f.BuildPaths((5, 8), (6, 8), (6, 9));
            Assert.AreEqual(PathShape.Corner, PathConnectivity.Describe(_f.Grid, C(6, 8)).Shape);

            // T junction.
            _f.BuildPaths((9, 8), (9, 9), (9, 10), (10, 9));
            Assert.AreEqual(PathShape.TJunction, PathConnectivity.Describe(_f.Grid, C(9, 9)).Shape);
        }

        [Test]
        public void FourWayJunction_IsACross()
        {
            _f.BuildPaths((7, 7), (6, 7), (8, 7), (7, 6), (7, 8));
            var n = PathConnectivity.Describe(_f.Grid, C(7, 7));
            Assert.AreEqual(PathShape.Cross, n.Shape);
            Assert.AreEqual(15, n.Mask);
        }

        [Test]
        public void Straight_RotationDistinguishesHorizontalFromVertical()
        {
            _f.BuildPaths((5, 5), (6, 5), (7, 5), (9, 5), (9, 6), (9, 7));
            Assert.AreEqual(PathShape.Straight, PathConnectivity.Describe(_f.Grid, C(6, 5)).Shape);
            Assert.AreEqual(1, PathConnectivity.Describe(_f.Grid, C(6, 5)).Rotation % 2, "east-west is a quarter turn from north-south");
            Assert.AreEqual(0, PathConnectivity.Describe(_f.Grid, C(9, 6)).Rotation, "north-south is canonical");
        }

        [TestCase(1, 0)]   // north
        [TestCase(2, 1)]   // east
        [TestCase(4, 2)]   // south
        [TestCase(8, 3)]   // west
        public void DeadEnd_RotationFollowsTheOpenSide(int mask, int rotation)
        {
            var n = PathConnectivity.Classify(mask);
            Assert.AreEqual(PathShape.DeadEnd, n.Shape);
            Assert.AreEqual(rotation, n.Rotation);
            Assert.AreEqual(mask, PathConnectivity.RotateMask(1, n.Rotation));
        }

        [Test]
        public void Corner_AllFourOrientationsRoundTrip()
        {
            foreach (int mask in new[] { 1 | 2, 2 | 4, 4 | 8, 8 | 1 })
            {
                var n = PathConnectivity.Classify(mask);
                Assert.AreEqual(PathShape.Corner, n.Shape, "mask " + mask);
                Assert.AreEqual(mask, PathConnectivity.RotateMask(1 | 2, n.Rotation));
            }
        }

        [Test]
        public void TJunction_AllFourOrientationsRoundTrip()
        {
            foreach (int mask in new[] { 1 | 2 | 4, 2 | 4 | 8, 4 | 8 | 1, 8 | 1 | 2 })
            {
                var n = PathConnectivity.Classify(mask);
                Assert.AreEqual(PathShape.TJunction, n.Shape, "mask " + mask);
                Assert.AreEqual(mask, PathConnectivity.RotateMask(1 | 2 | 4, n.Rotation));
            }
        }

        [Test]
        public void RemovingAPath_UpdatesNeighbourConnections()
        {
            _f.BuildPaths((5, 5), (6, 5), (7, 5));
            Assert.AreEqual(PathShape.Straight, PathConnectivity.Describe(_f.Grid, C(6, 5)).Shape);

            _f.Model.RemovePath(C(7, 5));
            Assert.AreEqual(PathShape.DeadEnd, PathConnectivity.Describe(_f.Grid, C(6, 5)).Shape);

            _f.Model.RemovePath(C(5, 5));
            Assert.AreEqual(PathShape.Isolated, PathConnectivity.Describe(_f.Grid, C(6, 5)).Shape);
        }

        [Test]
        public void AddingAPath_UpgradesNeighbourShapes()
        {
            _f.BuildPaths((6, 6), (7, 6));
            Assert.AreEqual(PathShape.DeadEnd, PathConnectivity.Describe(_f.Grid, C(7, 6)).Shape);
            _f.BuildPaths((7, 7));
            Assert.AreEqual(PathShape.Corner, PathConnectivity.Describe(_f.Grid, C(7, 6)).Shape);
            _f.BuildPaths((8, 6));
            Assert.AreEqual(PathShape.TJunction, PathConnectivity.Describe(_f.Grid, C(7, 6)).Shape);
        }

        [Test]
        public void Connectivity_AtTheGridBorder_DoesNotThrow()
        {
            _f.Grid.UnlockRect(0, 0, 16, 16);
            _f.BuildPaths((0, 0), (1, 0));
            Assert.AreEqual(PathShape.DeadEnd, PathConnectivity.Describe(_f.Grid, C(0, 0)).Shape);
            Assert.AreEqual(PathShape.Isolated, PathConnectivity.Describe(_f.Grid, C(-5, -5)).Shape);
        }

        // ---- Routing ----

        [Test]
        public void OrthogonalRoute_ConnectsCellsWithoutDiagonalsOrGaps()
        {
            var route = new List<GridCoord>();
            EdgeMath.OrthogonalRoute(C(2, 2), C(5, 4), route);
            Assert.AreEqual(5, route.Count);
            Assert.AreEqual(C(5, 4), route[route.Count - 1]);
            var prev = C(2, 2);
            foreach (var c in route)
            {
                Assert.AreEqual(1, System.Math.Abs(c.X - prev.X) + System.Math.Abs(c.Z - prev.Z), "each step moves one cell orthogonally");
                prev = c;
            }
        }

        [Test]
        public void OrthogonalRoute_SameCellIsEmpty()
        {
            var route = new List<GridCoord>();
            EdgeMath.OrthogonalRoute(C(3, 3), C(3, 3), route);
            Assert.IsEmpty(route);
        }
    }
}
