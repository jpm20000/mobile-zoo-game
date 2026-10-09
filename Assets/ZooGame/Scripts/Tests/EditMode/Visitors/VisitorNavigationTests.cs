using System.Collections.Generic;
using NUnit.Framework;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Visitors
{
    /// <summary>The path graph: walkability, cached connectivity and route finding on the M3 network.</summary>
    public class VisitorNavigationTests
    {
        VisitorFixture _f;

        [SetUp] public void SetUp() => _f = new VisitorFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        [Test]
        public void OnlyPathCells_AreWalkable()
        {
            _f.BuildPath(4, 6, 6);
            Assert.IsTrue(_f.Nav.IsWalkable(C(4, 6)));
            Assert.IsTrue(_f.Nav.IsWalkable(C(6, 6)));
            Assert.IsFalse(_f.Nav.IsWalkable(C(7, 6)), "grass");
            Assert.IsFalse(_f.Nav.IsWalkable(C(4, 5)));
            Assert.IsFalse(_f.Nav.IsWalkable(C(-1, 6)), "outside the grid");
        }

        [Test]
        public void PathInsideAnEnclosure_IsNotWalkable()
        {
            _f.Pen(); // cells (4..7, 7..9)
            Assert.IsTrue(_f.Model.BuildPaths(new[] { C(5, 8) }) == 1, "M3 allows a path in a pen");
            Assert.IsFalse(_f.Nav.IsWalkable(C(5, 8)), "visitors never enter animal enclosures");
        }

        [Test]
        public void ConnectedPaths_ShareAComponent_AndDisconnectedOnesDoNot()
        {
            _f.BuildPath(4, 6, 7);
            _f.BuildPath(4, 10, 7);
            Assert.IsTrue(_f.Nav.AreConnected(C(4, 6), C(7, 6)));
            Assert.IsFalse(_f.Nav.AreConnected(C(4, 6), C(4, 10)));
            Assert.AreNotEqual(0, _f.Nav.ComponentOf(C(4, 6)));
            Assert.AreEqual(0, _f.Nav.ComponentOf(C(9, 9)), "not walkable");
            Assert.IsFalse(_f.Nav.AreConnected(C(9, 9), C(9, 9)));

            _f.BuildPathColumn(4, 7, 9); // joins them
            Assert.IsTrue(_f.Nav.AreConnected(C(7, 6), C(7, 10)));
        }

        [Test]
        public void Diagonals_DoNotConnect()
        {
            _f.Model.BuildPaths(new[] { C(5, 5), C(6, 6) });
            Assert.IsFalse(_f.Nav.AreConnected(C(5, 5), C(6, 6)));
        }

        [Test]
        public void AFence_BlocksWalkingBetweenTwoPathCells()
        {
            _f.BuildPath(4, 6, 7);
            Assert.IsTrue(_f.Nav.AreConnected(C(4, 6), C(7, 6)));
            // vertical fence between (5,6) and (6,6)
            _f.Model.BuildFences(new[] { new FenceEdge(new EdgeCoord(EdgeAxis.Z, 6, 6), EdgeKind.Fence) });
            Assert.IsFalse(_f.Nav.AreConnected(C(4, 6), C(7, 6)));
            Assert.IsFalse(_f.Nav.TryFindRoute(C(4, 6), C(7, 6), new List<GridCoord>()));
            Assert.IsTrue(_f.Nav.AreConnected(C(4, 6), C(5, 6)));
        }

        [Test]
        public void AGate_AlsoBlocksVisitors()
        {
            _f.BuildPath(4, 6, 7);
            _f.Model.BuildFences(new[] { new FenceEdge(new EdgeCoord(EdgeAxis.Z, 6, 6), EdgeKind.Gate) });
            Assert.IsFalse(_f.Nav.AreConnected(C(4, 6), C(7, 6)), "gates are for keepers later, not visitors");
        }

        // ---- Caching ----

        [Test]
        public void ComponentLabels_AreCached_UntilThePathNetworkChanges()
        {
            _f.BuildPath(4, 6, 9);
            _f.Nav.AreConnected(C(4, 6), C(9, 6));
            _f.Nav.ComponentOf(C(5, 6));
            _f.Nav.IsConnectedToAny(C(4, 6), new[] { C(9, 6) });
            Assert.AreEqual(1, _f.Nav.GraphRebuildCount, "many queries, one build");

            _f.Model.BuildPaths(new[] { C(10, 6) });
            _f.Nav.AreConnected(C(4, 6), C(10, 6));
            Assert.AreEqual(2, _f.Nav.GraphRebuildCount);
        }

        [Test]
        public void UnrelatedChanges_DoNotInvalidateTheGraph()
        {
            _f.BuildPath(4, 6, 9);
            _f.Nav.ComponentOf(C(4, 6));
            int before = _f.Nav.GraphRebuildCount;
            _f.Place(ZooGame.Data.VisitorFacilityKind.Bench, 8, 3);
            _f.Grid.SetTerrain(C(5, 5), TerrainType.Sand);
            _f.Nav.ComponentOf(C(4, 6));
            Assert.AreEqual(before, _f.Nav.GraphRebuildCount);
        }

        [Test]
        public void GraphChanged_FiresForPathsAndFences()
        {
            int fired = 0;
            _f.Nav.GraphChanged += () => fired++;
            _f.BuildPath(4, 6, 6);
            int afterBuild = fired;
            Assert.Greater(afterBuild, 0);
            _f.Model.RemovePath(C(5, 6));
            Assert.Greater(fired, afterBuild);
            int afterRemove = fired;
            _f.Model.BuildFences(new[] { new FenceEdge(new EdgeCoord(EdgeAxis.Z, 5, 6), EdgeKind.Fence) });
            Assert.Greater(fired, afterRemove);
        }

        // ---- Routes ----

        [Test]
        public void Route_ExcludesTheStart_AndEndsAtTheTarget()
        {
            _f.BuildPath(4, 6, 9);
            var route = new List<GridCoord>();
            Assert.IsTrue(_f.Nav.TryFindRoute(C(4, 6), C(9, 6), route));
            Assert.AreEqual(5, route.Count);
            Assert.AreEqual(C(5, 6), route[0]);
            Assert.AreEqual(C(9, 6), route[route.Count - 1]);
        }

        [Test]
        public void Route_StepsAreAlwaysOneCell_AlongPathCells()
        {
            _f.BuildPath(4, 6, 11);
            _f.BuildPathColumn(11, 7, 11);
            _f.BuildPath(5, 11, 10);
            var route = new List<GridCoord>();
            Assert.IsTrue(_f.Nav.TryFindRoute(C(4, 6), C(5, 11), route));
            var prev = C(4, 6);
            foreach (var c in route)
            {
                Assert.AreEqual(1, System.Math.Abs(c.X - prev.X) + System.Math.Abs(c.Z - prev.Z), "adjacent steps only");
                Assert.IsTrue(_f.Grid.HasPath(c), "every cell is a path");
                prev = c;
            }
        }

        [Test]
        public void Route_PrefersTheShorterWay()
        {
            // A 5x3 loop: the short side is 2 steps, the long way round is 6.
            _f.BuildPath(4, 6, 6);
            _f.BuildPath(4, 8, 6);
            _f.BuildPathColumn(4, 7, 7);
            _f.BuildPathColumn(6, 7, 7);
            var route = new List<GridCoord>();
            Assert.IsTrue(_f.Nav.TryFindRoute(C(4, 6), C(4, 8), route));
            Assert.AreEqual(2, route.Count);
        }

        [Test]
        public void Route_FromTheTargetItself_IsEmptyButSuccessful()
        {
            _f.BuildPath(4, 6, 6);
            var route = new List<GridCoord> { C(1, 1) };
            Assert.IsTrue(_f.Nav.TryFindRoute(C(5, 6), C(5, 6), route));
            Assert.AreEqual(0, route.Count);
        }

        [Test]
        public void Route_FailsCleanly_WhenDisconnected_OrOffTheNetwork()
        {
            _f.BuildPath(4, 6, 6);
            _f.BuildPath(4, 10, 6);
            var route = new List<GridCoord> { C(1, 1) };
            Assert.IsFalse(_f.Nav.TryFindRoute(C(4, 6), C(4, 10), route));
            Assert.AreEqual(0, route.Count, "a failed search leaves an empty route");
            Assert.IsFalse(_f.Nav.TryFindRoute(C(9, 9), C(4, 6), route), "start is not on a path");
            Assert.IsFalse(_f.Nav.TryFindRoute(C(4, 6), C(9, 9), route), "target is not on a path");
            Assert.IsFalse(_f.Nav.TryFindRoute(C(4, 6), new List<GridCoord>(), out _, route), "no targets");
            Assert.AreEqual(4, _f.Nav.RouteFailureCount);
        }

        [Test]
        public void Route_ToSeveralTargets_FindsTheNearestReachableOne()
        {
            _f.BuildPath(4, 6, 11);
            _f.BuildPath(4, 10, 6);
            var targets = new List<GridCoord> { C(11, 6), C(7, 6), C(5, 10) };
            var route = new List<GridCoord>();
            Assert.IsTrue(_f.Nav.TryFindRoute(C(4, 6), targets, out var reached, route));
            Assert.AreEqual(C(7, 6), reached, "(5,10) is unreachable and (7,6) is nearer than (11,6)");
            Assert.AreEqual(3, route.Count);
        }

        [Test]
        public void Routes_AreOnlySearchedWhenAsked_NotWhenQueryingConnectivity()
        {
            _f.BuildPath(4, 6, 11);
            for (int i = 0; i < 50; i++)
            {
                _f.Nav.AreConnected(C(4, 6), C(11, 6));
                _f.Nav.IsConnectedToAny(C(4, 6), new[] { C(11, 6) });
            }
            Assert.AreEqual(0, _f.Nav.RouteRequestCount);
            _f.Nav.TryFindRoute(C(4, 6), C(11, 6), new List<GridCoord>());
            Assert.AreEqual(1, _f.Nav.RouteRequestCount);
        }

        [Test]
        public void RemovingAPathCell_SplitsTheNetwork()
        {
            _f.BuildPath(4, 6, 9);
            Assert.IsTrue(_f.Nav.AreConnected(C(4, 6), C(9, 6)));
            _f.Model.RemovePath(C(6, 6));
            Assert.IsFalse(_f.Nav.AreConnected(C(4, 6), C(9, 6)));
            Assert.IsTrue(_f.Nav.AreConnected(C(7, 6), C(9, 6)));
        }
    }
}
