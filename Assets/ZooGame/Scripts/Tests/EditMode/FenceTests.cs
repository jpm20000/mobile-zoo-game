using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.World;

namespace ZooGame.Tests.EditMode
{
    public class FenceTests
    {
        ConstructionFixture _f;

        [SetUp] public void SetUp() => _f = new ConstructionFixture();

        static GridCoord C(int x, int z) => new GridCoord(x, z);
        static EdgeCoord XEdge(int x, int z) => new EdgeCoord(EdgeAxis.X, x, z);
        static EdgeCoord ZEdge(int x, int z) => new EdgeCoord(EdgeAxis.Z, x, z);
        static FenceEdge Fence(EdgeCoord e) => new FenceEdge(e, EdgeKind.Fence);
        static FenceEdge Gate(EdgeCoord e) => new FenceEdge(e, EdgeKind.Gate);

        // ---- Edge identity ----

        [Test]
        public void SharedEdge_HasOneCoordinateFromEitherSide()
        {
            Assert.AreEqual(EdgeCoord.OfCell(C(6, 6), Direction4.North), EdgeCoord.OfCell(C(6, 7), Direction4.South));
            Assert.AreEqual(EdgeCoord.OfCell(C(6, 6), Direction4.East), EdgeCoord.OfCell(C(7, 6), Direction4.West));
            Assert.AreNotEqual(EdgeCoord.OfCell(C(6, 6), Direction4.North), EdgeCoord.OfCell(C(6, 6), Direction4.South));
        }

        [Test]
        public void EdgeCells_AreTheTwoSidesOfTheEdge()
        {
            var x = XEdge(6, 7); // between (6,6) south and (6,7) north
            Assert.AreEqual(C(6, 6), x.CellA);
            Assert.AreEqual(C(6, 7), x.CellB);
            var z = ZEdge(6, 7); // between (5,7) west and (6,7) east
            Assert.AreEqual(C(5, 7), z.CellA);
            Assert.AreEqual(C(6, 7), z.CellB);
        }

        [Test]
        public void EdgePoints_SpanOneCell()
        {
            Assert.AreEqual(C(6, 7), XEdge(6, 7).PointStart);
            Assert.AreEqual(C(7, 7), XEdge(6, 7).PointEnd);
            Assert.AreEqual(C(6, 8), ZEdge(6, 7).PointEnd);
        }

        // ---- FenceMap ----

        [Test]
        public void SettingAnEdge_FromEitherAdjacentCell_AffectsTheSameSingleSegment()
        {
            var map = _f.Model.Fences;
            map.Set(EdgeCoord.OfCell(C(6, 6), Direction4.North), EdgeKind.Fence);
            map.Set(EdgeCoord.OfCell(C(6, 7), Direction4.South), EdgeKind.Fence); // same edge again
            Assert.AreEqual(1, map.Count, "a shared edge is one logical segment");
            Assert.AreEqual(EdgeKind.Fence, map.GetSide(C(6, 6), Direction4.North));
            Assert.AreEqual(EdgeKind.Fence, map.GetSide(C(6, 7), Direction4.South));
        }

        [Test]
        public void BuildFences_IgnoresDuplicatesInTheBatch()
        {
            var batch = new List<FenceEdge> { Fence(XEdge(6, 6)), Fence(XEdge(6, 6)), Fence(XEdge(7, 6)) };
            Assert.AreEqual(2, _f.Model.BuildFences(batch));
            Assert.AreEqual(2, _f.Model.Fences.Count);
        }

        [Test]
        public void Remove_ClearsTheEdgeAndNothingElse()
        {
            _f.Model.BuildFences(new List<FenceEdge> { Fence(XEdge(6, 6)), Fence(XEdge(7, 6)) });
            Assert.IsTrue(_f.Model.RemoveFence(XEdge(6, 6)));
            Assert.AreEqual(EdgeKind.None, _f.Model.Fences.Get(XEdge(6, 6)));
            Assert.AreEqual(EdgeKind.Fence, _f.Model.Fences.Get(XEdge(7, 6)));
            Assert.AreEqual(1, _f.Model.Fences.Count);
            Assert.IsFalse(_f.Model.RemoveFence(XEdge(6, 6)), "already gone");
        }

        [Test]
        public void FencesDoNotOccupyCells()
        {
            _f.Model.BuildFences(ConstructionFixture.RectEdges(5, 5, 3, 3));
            for (int x = 5; x < 8; x++)
            for (int z = 5; z < 8; z++)
            {
                Assert.IsFalse(_f.Grid.IsOccupied(C(x, z)));
                Assert.IsTrue(_f.Grid.IsAvailable(C(x, z)));
            }
        }

        [Test]
        public void EdgesOnTheMapBorder_AreValid_AndBeyondAreNot()
        {
            var map = _f.Model.Fences;
            Assert.IsTrue(map.IsValidEdge(XEdge(0, 0)));
            Assert.IsTrue(map.IsValidEdge(XEdge(15, 16)));
            Assert.IsTrue(map.IsValidEdge(ZEdge(16, 15)));
            Assert.IsFalse(map.IsValidEdge(XEdge(16, 0)));
            Assert.IsFalse(map.IsValidEdge(XEdge(0, 17)));
            Assert.IsFalse(map.IsValidEdge(ZEdge(17, 0)));
            Assert.IsFalse(map.IsValidEdge(ZEdge(0, 16)));
            Assert.IsFalse(map.Set(XEdge(-1, 0), EdgeKind.Fence));
            Assert.AreEqual(EdgeKind.None, map.Get(XEdge(100, 100)));
        }

        [Test]
        public void ForEach_VisitsEveryBuiltEdgeOnce()
        {
            _f.Model.BuildFences(ConstructionFixture.RectEdges(5, 5, 3, 2));
            var seen = new HashSet<EdgeCoord>();
            _f.Model.Fences.ForEach((e, k) => Assert.IsTrue(seen.Add(e)));
            Assert.AreEqual(10, seen.Count);
        }

        [Test]
        public void EdgeChanged_ReportsOldAndNewKind_AndSkipsNoOps()
        {
            var log = new List<string>();
            _f.Model.Fences.EdgeChanged += (e, a, b) => log.Add(a + ">" + b);
            _f.Model.Fences.Set(XEdge(6, 6), EdgeKind.Fence);
            _f.Model.Fences.Set(XEdge(6, 6), EdgeKind.Fence);
            _f.Model.Fences.Set(XEdge(6, 6), EdgeKind.Gate);
            _f.Model.Fences.Set(XEdge(6, 6), EdgeKind.None);
            CollectionAssert.AreEqual(new[] { "None>Fence", "Fence>Gate", "Gate>None" }, log);
        }

        // ---- Gates ----

        [Test]
        public void Gate_OccupiesAnEdge_IsABoundary_AndIsPassable()
        {
            _f.Model.BuildFences(new List<FenceEdge> { Gate(XEdge(6, 6)) });
            var map = _f.Model.Fences;
            Assert.AreEqual(EdgeKind.Gate, map.Get(XEdge(6, 6)));
            Assert.IsTrue(map.IsGate(XEdge(6, 6)));
            Assert.IsTrue(map.HasBoundary(XEdge(6, 6)));
            Assert.IsTrue(EdgeKind.Gate.IsPassable());
            Assert.IsFalse(EdgeKind.Fence.IsPassable());
            Assert.IsTrue(EdgeKind.Gate.IsBoundary());
        }

        [Test]
        public void Gate_ReplacesAFence_AndUsesTheSameEdge()
        {
            _f.Model.BuildFences(new List<FenceEdge> { Fence(XEdge(6, 6)) });
            Assert.AreEqual(1, _f.Model.BuildFences(new List<FenceEdge> { Gate(XEdge(6, 6)) }));
            Assert.AreEqual(1, _f.Model.Fences.Count, "no second segment on the same edge");
            Assert.AreEqual(EdgeKind.Gate, _f.Model.Fences.Get(XEdge(6, 6)));
        }

        [Test]
        public void Gate_RemovalClearsTheEdge()
        {
            _f.Model.BuildFences(new List<FenceEdge> { Gate(ZEdge(6, 6)) });
            Assert.IsTrue(_f.Model.RemoveFence(ZEdge(6, 6)));
            Assert.AreEqual(EdgeKind.None, _f.Model.Fences.Get(ZEdge(6, 6)));
        }

        // ---- Validation ----

        [Test]
        public void EdgeBetweenTwoLockedCells_IsRejected()
        {
            Assert.AreEqual(BuildFailure.LockedLand, _f.Model.CanBuildFence(XEdge(1, 1), EdgeKind.Fence).Failure);
            Assert.AreEqual(0, _f.Model.BuildFences(new List<FenceEdge> { Fence(XEdge(1, 1)), Gate(ZEdge(2, 2)) }));
            Assert.AreEqual(0, _f.Model.Fences.Count);
        }

        [Test]
        public void EdgeOnTheBorderOfTheUnlockedArea_IsAllowed()
        {
            // Unlocked cells are 4..11; the edge x=4 (west side of cell 4) touches unlocked cell (4,6).
            Assert.IsTrue(_f.Model.CanBuildFence(ZEdge(4, 6), EdgeKind.Fence).IsValid);
            Assert.IsTrue(_f.Model.CanBuildFence(ZEdge(12, 6), EdgeKind.Fence).IsValid); // east side of cell 11
            Assert.IsFalse(_f.Model.CanBuildFence(ZEdge(3, 6), EdgeKind.Fence).IsValid);
            Assert.IsFalse(_f.Model.CanBuildFence(ZEdge(13, 6), EdgeKind.Fence).IsValid);
        }

        [Test]
        public void OutOfGridEdge_IsRejected()
        {
            Assert.AreEqual(BuildFailure.OutOfBounds, _f.Model.CanBuildFence(XEdge(20, 5), EdgeKind.Fence).Failure);
        }

        sealed class NoGatesOnZ : IFenceRule
        {
            public BuildResult Evaluate(ZooGrid grid, FenceMap fences, EdgeCoord edge, EdgeKind kind) =>
                kind == EdgeKind.Gate && edge.Axis == EdgeAxis.Z ? BuildResult.Fail(BuildFailure.RuleFailed, "no") : BuildResult.Valid;
        }

        [Test]
        public void CustomFenceRules_PlugIn()
        {
            _f.Model.FenceRules.AddRule(new NoGatesOnZ());
            Assert.AreEqual(BuildFailure.RuleFailed, _f.Model.CanBuildFence(ZEdge(6, 6), EdgeKind.Gate).Failure);
            Assert.IsTrue(_f.Model.CanBuildFence(ZEdge(6, 6), EdgeKind.Fence).IsValid);
        }

        // ---- Geometry helpers ----

        [Test]
        public void StraightRun_FollowsTheLongerAxisAndNeverBendsOrGoesDiagonal()
        {
            var run = new List<EdgeCoord>();
            EdgeMath.StraightRun(C(5, 5), C(9, 6), run);
            Assert.AreEqual(4, run.Count);
            foreach (var e in run) Assert.AreEqual(EdgeAxis.X, e.Axis);
            Assert.AreEqual(XEdge(5, 5), run[0]);
            Assert.AreEqual(XEdge(8, 5), run[3]);

            run.Clear();
            EdgeMath.StraightRun(C(5, 9), C(6, 5), run); // dragging downwards
            Assert.AreEqual(4, run.Count);
            foreach (var e in run) Assert.AreEqual(EdgeAxis.Z, e.Axis);
            Assert.AreEqual(ZEdge(5, 5), run[0]);
        }

        [Test]
        public void SquareOutline_IsTheClosedPerimeterOfAnNxNSquare()
        {
            var edges = new List<EdgeCoord>();
            EdgeMath.SquareOutline(C(5, 5), 8, edges);
            Assert.AreEqual(32, edges.Count);
            CollectionAssert.AllItemsAreUnique(edges);

            var batch = new List<FenceEdge>();
            foreach (var e in edges) batch.Add(new FenceEdge(e, EdgeKind.Fence));
            _f.Grid.UnlockRect(0, 0, 16, 16);
            _f.Model.BuildFences(batch);
            Assert.AreEqual(1, _f.Model.Enclosures.Count);
            Assert.AreEqual(64, _f.Model.Enclosures.GetAt(C(8, 8)).Area);
        }

        [Test]
        public void StraightRun_SamePointIsEmpty()
        {
            var run = new List<EdgeCoord>();
            EdgeMath.StraightRun(C(5, 5), C(5, 5), run);
            Assert.IsEmpty(run);
        }

        [Test]
        public void NearestPoint_RoundsToTheClosestCorner()
        {
            Assert.AreEqual(C(6, 7), EdgeMath.NearestPoint(_f.Grid, new Vector3(5.7f, 0, 7.2f)));
            Assert.AreEqual(C(5, 7), EdgeMath.NearestPoint(_f.Grid, new Vector3(5.4f, 0, 6.6f)));
        }

        [Test]
        public void NearestEdge_PicksTheClosestEdgeLine()
        {
            // Near the south edge of cell (6,6): z close to 6, x inside the cell.
            Assert.AreEqual(XEdge(6, 6), EdgeMath.NearestEdge(_f.Grid, new Vector3(6.5f, 0, 6.1f)));
            // Near the west edge of cell (6,6).
            Assert.AreEqual(ZEdge(6, 6), EdgeMath.NearestEdge(_f.Grid, new Vector3(6.1f, 0, 6.5f)));
            // Near the north edge.
            Assert.AreEqual(XEdge(6, 7), EdgeMath.NearestEdge(_f.Grid, new Vector3(6.5f, 0, 6.9f)));
        }

        [Test]
        public void NonFiniteInput_DoesNotThrow()
        {
            EdgeMath.NearestEdge(_f.Grid, new Vector3(float.NaN, 0, float.PositiveInfinity));
            EdgeMath.NearestPoint(_f.Grid, new Vector3(float.NegativeInfinity, 0, float.NaN));
        }

        [Test]
        public void PreviewIsNotAModelChange_BuildingNothingChangesNothing()
        {
            int fenceEvents = 0;
            _f.Model.FencesChanged += () => fenceEvents++;
            _f.Model.BuildFences(new List<FenceEdge>());
            _f.Model.RemoveFences(new List<EdgeCoord> { XEdge(6, 6) });
            Assert.AreEqual(0, fenceEvents);
            Assert.AreEqual(0, _f.Model.Fences.Count);
        }
    }
}
