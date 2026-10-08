using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZooGame.World;

namespace ZooGame.Tests.EditMode
{
    public class EnclosureTests
    {
        ConstructionFixture _f;

        [SetUp] public void SetUp() => _f = new ConstructionFixture();

        static GridCoord C(int x, int z) => new GridCoord(x, z);
        static EdgeCoord XEdge(int x, int z) => new EdgeCoord(EdgeAxis.X, x, z);
        static EdgeCoord ZEdge(int x, int z) => new EdgeCoord(EdgeAxis.Z, x, z);

        EnclosureMap Enclosures => _f.Model.Enclosures;

        // ---- Closed vs open ----

        [Test]
        public void ClosedRectangle_CreatesOneEnclosure()
        {
            _f.BuildRect(5, 5, 4, 3);
            Assert.AreEqual(1, Enclosures.Count);
        }

        [Test]
        public void ClosedRectangle_IdentifiesExactlyTheInteriorCells()
        {
            _f.BuildRect(5, 5, 4, 3);
            var e = Enclosures.GetAt(C(5, 5));
            Assert.IsNotNull(e);
            var expected = new HashSet<GridCoord>();
            for (int x = 5; x < 9; x++)
            for (int z = 5; z < 8; z++) expected.Add(C(x, z));
            CollectionAssert.AreEquivalent(expected, e.Cells);
            Assert.AreEqual(12, e.Area);
        }

        [Test]
        public void InteriorCells_CarryTheEnclosureId_AndOutsideCellsDoNot()
        {
            _f.BuildRect(5, 5, 4, 3);
            int id = Enclosures.GetAt(C(6, 6)).Id;
            Assert.AreNotEqual(GridCell.NoEnclosure, id);
            for (int x = 5; x < 9; x++)
            for (int z = 5; z < 8; z++) Assert.AreEqual(id, _f.EnclosureIdAt(x, z));
            Assert.AreEqual(0, _f.EnclosureIdAt(4, 5));
            Assert.AreEqual(0, _f.EnclosureIdAt(9, 5));
            Assert.AreEqual(0, _f.EnclosureIdAt(5, 8));
            Assert.AreEqual(0, _f.EnclosureIdAt(5, 4));
        }

        [Test]
        public void OneCellEnclosure_Works()
        {
            _f.BuildRect(6, 6, 1, 1);
            Assert.AreEqual(1, Enclosures.Count);
            Assert.AreEqual(1, Enclosures.GetAt(C(6, 6)).Area);
        }

        [Test]
        public void OpenBoundary_DoesNotCreateAnEnclosure()
        {
            var edges = ConstructionFixture.RectEdges(5, 5, 4, 3);
            edges.RemoveAll(e => e.Edge == XEdge(6, 8)); // gap in the top side
            _f.Model.BuildFences(edges);
            Assert.AreEqual(0, Enclosures.Count);
            for (int x = 5; x < 9; x++)
            for (int z = 5; z < 8; z++) Assert.AreEqual(0, _f.EnclosureIdAt(x, z));
        }

        [Test]
        public void ShortFenceRun_IsNotAnEnclosure()
        {
            _f.Model.BuildFences(new List<FenceEdge> { new FenceEdge(XEdge(6, 6), EdgeKind.Fence), new FenceEdge(XEdge(7, 6), EdgeKind.Fence) });
            Assert.AreEqual(0, Enclosures.Count);
        }

        [Test]
        public void OpenAndClosedLayouts_AreDistinguishable_PerFence()
        {
            _f.BuildRect(5, 5, 3, 3);
            var stray = new List<FenceEdge> { new FenceEdge(XEdge(10, 10), EdgeKind.Fence) };
            _f.Model.BuildFences(stray);

            Assert.IsTrue(Enclosures.IsClosedBoundary(XEdge(5, 5), out int id));
            Assert.AreEqual(Enclosures.GetAt(C(5, 5)).Id, id);
            Assert.IsFalse(Enclosures.IsClosedBoundary(XEdge(10, 10), out _), "a stray fence belongs to no enclosure");
        }

        [Test]
        public void ClosingTheLastGap_CreatesTheEnclosure()
        {
            var edges = ConstructionFixture.RectEdges(5, 5, 3, 3);
            var gap = edges.First(e => e.Edge == ZEdge(8, 6));
            edges.Remove(gap);
            _f.Model.BuildFences(edges);
            Assert.AreEqual(0, Enclosures.Count);

            _f.Model.BuildFences(new List<FenceEdge> { gap });
            Assert.AreEqual(1, Enclosures.Count);
            Assert.AreEqual(9, Enclosures.GetAt(C(6, 6)).Area);
        }

        [Test]
        public void FenceAlongTheMapBorder_CanCloseAnEnclosure()
        {
            _f.Grid.UnlockRect(0, 0, 16, 16);
            _f.BuildRect(0, 0, 3, 3); // two sides lie on the map border
            Assert.AreEqual(1, Enclosures.Count);
            Assert.AreEqual(9, Enclosures.GetAt(C(1, 1)).Area);
        }

        [Test]
        public void EnclosureOnTheBorderOfLockedLand_IsStillEnclosed()
        {
            // Fence the whole unlocked 8x8 block: the outside is locked land, which is simply "outside".
            _f.BuildRect(4, 4, 8, 8);
            Assert.AreEqual(1, Enclosures.Count);
            Assert.AreEqual(64, Enclosures.GetAt(C(5, 5)).Area);
        }

        // ---- Boundary data ----

        [Test]
        public void BoundaryEdges_ArePerimeterSegmentsOnly()
        {
            _f.BuildRect(5, 5, 4, 3);
            var e = Enclosures.GetAt(C(5, 5));
            Assert.AreEqual(2 * (4 + 3), e.BoundaryEdges.Count);
            CollectionAssert.AllItemsAreUnique(e.BoundaryEdges);
            foreach (var edge in e.BoundaryEdges) Assert.IsTrue(_f.Model.Fences.HasBoundary(edge));
        }

        [Test]
        public void InteriorWall_IsNotPartOfTheBoundary_ButEnclosureStaysOne()
        {
            _f.BuildRect(5, 5, 4, 3);
            _f.Model.BuildFences(new List<FenceEdge> { new FenceEdge(ZEdge(7, 5), EdgeKind.Fence), new FenceEdge(ZEdge(7, 6), EdgeKind.Fence) });
            Assert.AreEqual(1, Enclosures.Count, "a dangling wall inside does not split it");
            var e = Enclosures.GetAt(C(5, 5));
            Assert.AreEqual(14, e.BoundaryEdges.Count);
            Assert.AreEqual(12, e.Area);
        }

        // ---- Gates ----

        [Test]
        public void Gate_InTheBoundary_StillCountsAsClosed()
        {
            var edges = ConstructionFixture.RectEdges(5, 5, 3, 3);
            int i = edges.FindIndex(e => e.Edge == XEdge(6, 5));
            edges[i] = new FenceEdge(XEdge(6, 5), EdgeKind.Gate);
            _f.Model.BuildFences(edges);

            Assert.AreEqual(1, Enclosures.Count);
            var e = Enclosures.GetAt(C(6, 6));
            Assert.AreEqual(1, e.Gates.Count);
            Assert.AreEqual(XEdge(6, 5), e.Gates[0].Edge);
            Assert.AreEqual(EdgeKind.Gate, e.Gates[0].Kind);
            Assert.AreEqual(12, e.BoundaryEdges.Count, "the gate is a boundary segment too");
        }

        [Test]
        public void ReplacingAFenceWithAGate_KeepsTheEnclosureAndItsId()
        {
            _f.BuildRect(5, 5, 3, 3);
            int id = Enclosures.GetAt(C(6, 6)).Id;

            _f.Model.BuildFences(new List<FenceEdge> { new FenceEdge(ZEdge(5, 6), EdgeKind.Gate) });

            Assert.AreEqual(1, Enclosures.Count);
            Assert.AreEqual(id, Enclosures.GetAt(C(6, 6)).Id);
            Assert.AreEqual(1, Enclosures.GetAt(C(6, 6)).Gates.Count);
        }

        [Test]
        public void ReplacingAGateWithAFence_RemovesItFromTheGateList()
        {
            var edges = ConstructionFixture.RectEdges(5, 5, 3, 3);
            edges[0] = new FenceEdge(edges[0].Edge, EdgeKind.Gate);
            _f.Model.BuildFences(edges);
            Assert.AreEqual(1, Enclosures.GetAt(C(6, 6)).Gates.Count);

            _f.Model.BuildFences(new List<FenceEdge> { new FenceEdge(edges[0].Edge, EdgeKind.Fence) });
            Assert.AreEqual(0, Enclosures.GetAt(C(6, 6)).Gates.Count);
        }

        // ---- Recalculation ----

        [Test]
        public void RemovingABoundaryFence_InvalidatesTheEnclosure()
        {
            _f.BuildRect(5, 5, 3, 3);
            Assert.AreEqual(1, Enclosures.Count);

            _f.Model.RemoveFence(XEdge(6, 8));

            Assert.AreEqual(0, Enclosures.Count);
            for (int x = 5; x < 8; x++)
            for (int z = 5; z < 8; z++) Assert.AreEqual(0, _f.EnclosureIdAt(x, z), "cell " + x + "," + z);
            Assert.IsFalse(Enclosures.IsClosedBoundary(XEdge(5, 5), out _), "remaining fences are now an open layout");
        }

        [Test]
        public void RebuildingARemovedFence_RestoresAnEnclosureWithANewConsistentId()
        {
            _f.BuildRect(5, 5, 3, 3);
            int first = Enclosures.GetAt(C(6, 6)).Id;
            _f.Model.RemoveFence(XEdge(6, 8));
            _f.Model.BuildFences(new List<FenceEdge> { new FenceEdge(XEdge(6, 8), EdgeKind.Fence) });

            Assert.AreEqual(1, Enclosures.Count);
            int second = Enclosures.GetAt(C(6, 6)).Id;
            Assert.AreNotEqual(0, second);
            Assert.AreNotEqual(first, second, "ids are never reused after an enclosure disappears");
            for (int x = 5; x < 8; x++)
            for (int z = 5; z < 8; z++) Assert.AreEqual(second, _f.EnclosureIdAt(x, z));
        }

        [Test]
        public void AddingAnUnrelatedFence_DoesNotChangeExistingEnclosureIds()
        {
            _f.BuildRect(5, 5, 3, 3);
            int id = Enclosures.GetAt(C(6, 6)).Id;
            _f.Model.BuildFences(new List<FenceEdge> { new FenceEdge(XEdge(10, 10), EdgeKind.Fence) });
            Assert.AreEqual(id, Enclosures.GetAt(C(6, 6)).Id);
            Assert.AreEqual(1, Enclosures.Count);
        }

        [Test]
        public void TwoSeparateEnclosures_GetDistinctIds()
        {
            _f.BuildRect(4, 4, 3, 3);
            _f.BuildRect(8, 8, 3, 3);
            Assert.AreEqual(2, Enclosures.Count);
            Assert.AreNotEqual(Enclosures.GetAt(C(5, 5)).Id, Enclosures.GetAt(C(9, 9)).Id);
        }

        [Test]
        public void BuildingAnotherEnclosure_LeavesTheFirstUntouched()
        {
            _f.BuildRect(4, 4, 3, 3);
            var before = Enclosures.GetAt(C(5, 5));
            _f.BuildRect(8, 8, 3, 3);
            Assert.AreSame(before, Enclosures.GetAt(C(5, 5)), "not even rebuilt: recalculation is local");
        }

        [Test]
        public void SplittingAnEnclosure_KeepsTheLargerHalfsIdAndCreatesANewOne()
        {
            _f.BuildRect(4, 4, 6, 3);                 // 18 cells
            int original = Enclosures.GetAt(C(4, 4)).Id;

            // A full wall at x = 8 splits it into 4x3 (12 cells, west) and 2x3 (6 cells, east).
            _f.Model.BuildFences(new List<FenceEdge>
            {
                new FenceEdge(ZEdge(8, 4), EdgeKind.Fence), new FenceEdge(ZEdge(8, 5), EdgeKind.Fence), new FenceEdge(ZEdge(8, 6), EdgeKind.Fence)
            });

            Assert.AreEqual(2, Enclosures.Count);
            Assert.AreEqual(original, Enclosures.GetAt(C(4, 4)).Id, "the larger part keeps the original id");
            Assert.AreEqual(12, Enclosures.GetAt(C(4, 4)).Area);
            Assert.AreNotEqual(original, Enclosures.GetAt(C(8, 4)).Id);
            Assert.AreEqual(6, Enclosures.GetAt(C(8, 4)).Area);
        }

        [Test]
        public void RemovingTheSharedWall_MergesTwoEnclosuresIntoOne()
        {
            _f.BuildRect(4, 4, 3, 3);
            _f.BuildRect(7, 4, 3, 3);
            Assert.AreEqual(2, Enclosures.Count);
            int west = Enclosures.GetAt(C(4, 4)).Id, east = Enclosures.GetAt(C(8, 4)).Id;

            _f.Model.RemoveFences(new List<EdgeCoord> { ZEdge(7, 4), ZEdge(7, 5), ZEdge(7, 6) });

            Assert.AreEqual(1, Enclosures.Count);
            var merged = Enclosures.GetAt(C(4, 4));
            Assert.AreEqual(18, merged.Area);
            Assert.AreSame(merged, Enclosures.GetAt(C(8, 6)));
            Assert.IsTrue(merged.Id == west || merged.Id == east, "the merged enclosure continues one of the old ids");
        }

        [Test]
        public void NestedEnclosure_IsSeparateAndExcludedFromTheOuterOne()
        {
            _f.BuildRect(4, 4, 7, 7);                 // 49 cells
            _f.BuildRect(6, 6, 2, 2);                 // 4 cells inside
            Assert.AreEqual(2, Enclosures.Count);
            Assert.AreEqual(4, Enclosures.GetAt(C(6, 6)).Area);
            Assert.AreEqual(45, Enclosures.GetAt(C(4, 4)).Area);
            Assert.AreNotEqual(Enclosures.GetAt(C(4, 4)).Id, Enclosures.GetAt(C(6, 6)).Id);
        }

        [Test]
        public void RemovingOneFenceOfTwoEnclosures_OnlyAffectsThatOne()
        {
            _f.BuildRect(4, 4, 3, 3);
            _f.BuildRect(8, 8, 3, 3);
            var other = Enclosures.GetAt(C(9, 9));
            _f.Model.RemoveFence(XEdge(5, 7));
            Assert.AreEqual(1, Enclosures.Count);
            Assert.AreSame(other, Enclosures.GetAt(C(9, 9)));
            Assert.IsNull(Enclosures.GetAt(C(5, 5)));
        }

        [Test]
        public void Changed_FiresOncePerCommittedBatch()
        {
            int changes = 0, fenceEvents = 0;
            Enclosures.Changed += () => changes++;
            _f.Model.FencesChanged += () => fenceEvents++;
            _f.BuildRect(5, 5, 3, 3);   // 12 edges, one commit
            Assert.AreEqual(1, changes);
            Assert.AreEqual(1, fenceEvents);
            _f.Model.RemoveFence(XEdge(5, 5));
            Assert.AreEqual(2, changes);
        }

        [Test]
        public void ObjectsAndPaths_DoNotInfluenceEnclosureDetection()
        {
            _f.Grid.SetOccupied(C(6, 6), true);
            _f.BuildPaths((5, 5), (6, 5));
            _f.BuildRect(5, 5, 3, 3);
            Assert.AreEqual(1, Enclosures.Count);
            Assert.AreEqual(9, Enclosures.GetAt(C(5, 5)).Area);
            Assert.IsTrue(_f.Grid.IsOccupied(C(6, 6)));
            Assert.IsTrue(_f.Grid.HasPath(C(5, 5)));
        }

        [Test]
        public void LargeRectangle_ScansOnlyOnceAndIsCorrect()
        {
            _f.Grid.UnlockRect(0, 0, 16, 16);
            _f.BuildRect(1, 1, 14, 14);
            Assert.AreEqual(1, Enclosures.Count);
            Assert.AreEqual(196, Enclosures.GetAt(C(8, 8)).Area);
        }
    }
}
