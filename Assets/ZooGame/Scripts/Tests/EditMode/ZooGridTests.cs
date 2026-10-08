using NUnit.Framework;
using UnityEngine;
using ZooGame.Data;
using ZooGame.World;

namespace ZooGame.Tests.EditMode
{
    public class ZooGridTests
    {
        static ZooGrid Default() =>
            new ZooGrid(GridSettings.Centered(64, 64, 1f, Vector3.zero, 32, 32));

        // ---- Layout ----

        [Test]
        public void Default_Is64x64_With32x32UnlockedCentred()
        {
            var g = Default();
            Assert.AreEqual(64, g.Width);
            Assert.AreEqual(64, g.Depth);
            Assert.AreEqual(64 * 64, g.CellCount);
            Assert.AreEqual(32 * 32, g.UnlockedCount);

            Assert.IsTrue(g.IsUnlocked(new GridCoord(16, 16)));
            Assert.IsTrue(g.IsUnlocked(new GridCoord(47, 47)));
            Assert.IsFalse(g.IsUnlocked(new GridCoord(15, 16)));
            Assert.IsFalse(g.IsUnlocked(new GridCoord(48, 47)));
            Assert.IsFalse(g.IsUnlocked(new GridCoord(0, 0)));
            Assert.IsFalse(g.IsUnlocked(new GridCoord(63, 63)));
        }

        [Test]
        public void WorldConfigDefaults_ProduceTheSpecifiedGrid()
        {
            var config = ScriptableObject.CreateInstance<WorldConfig>();
            try
            {
                var g = new ZooGrid(config.ToGridSettings());
                Assert.AreEqual(64, g.Width);
                Assert.AreEqual(64, g.Depth);
                Assert.AreEqual(1024, g.UnlockedCount);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void InvalidSettings_Throw()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ZooGrid(GridSettings.Centered(0, 8, 1f, Vector3.zero, 0, 0)));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ZooGrid(GridSettings.Centered(8, 8, 0f, Vector3.zero, 4, 4)));
        }

        [Test]
        public void OversizedUnlockedArea_IsClampedToGrid()
        {
            var g = new ZooGrid(GridSettings.Centered(8, 8, 1f, Vector3.zero, 100, 100));
            Assert.AreEqual(64, g.UnlockedCount);
        }

        // ---- Bounds ----

        [Test]
        public void IsInsideGrid_CoversExactlyTheGrid()
        {
            var g = Default();
            Assert.IsTrue(g.IsInsideGrid(new GridCoord(0, 0)));
            Assert.IsTrue(g.IsInsideGrid(new GridCoord(63, 63)));
            Assert.IsFalse(g.IsInsideGrid(new GridCoord(-1, 0)));
            Assert.IsFalse(g.IsInsideGrid(new GridCoord(0, -1)));
            Assert.IsFalse(g.IsInsideGrid(new GridCoord(64, 0)));
            Assert.IsFalse(g.IsInsideGrid(new GridCoord(0, 64)));
            Assert.IsFalse(g.IsInsideGrid(new GridCoord(int.MinValue, int.MaxValue)));
        }

        // ---- WorldToGrid ----

        [Test]
        public void WorldToGrid_MapsCellInteriorsAndFloorsEdges()
        {
            var g = Default();
            Assert.AreEqual(new GridCoord(0, 0), g.WorldToGrid(new Vector3(0.0f, 5f, 0.0f)));
            Assert.AreEqual(new GridCoord(0, 0), g.WorldToGrid(new Vector3(0.999f, 0f, 0.999f)));
            Assert.AreEqual(new GridCoord(1, 1), g.WorldToGrid(new Vector3(1.0f, 0f, 1.0f)));
            Assert.AreEqual(new GridCoord(63, 63), g.WorldToGrid(new Vector3(63.5f, 0f, 63.5f)));
            Assert.AreEqual(new GridCoord(12, 40), g.WorldToGrid(new Vector3(12.3f, 0f, 40.9f)));
        }

        [Test]
        public void WorldToGrid_NegativeAndFarPointsAreOutside()
        {
            var g = Default();
            Assert.AreEqual(new GridCoord(-1, -1), g.WorldToGrid(new Vector3(-0.001f, 0f, -0.001f)));
            Assert.IsFalse(g.TryWorldToGrid(new Vector3(-0.5f, 0f, 3f), out _));
            Assert.IsFalse(g.TryWorldToGrid(new Vector3(64f, 0f, 3f), out _)); // far edge belongs to the next (absent) cell
            Assert.IsTrue(g.TryWorldToGrid(new Vector3(63.999f, 0f, 63.999f), out var c));
            Assert.AreEqual(new GridCoord(63, 63), c);
        }

        [Test]
        public void WorldToGrid_NaNInfinityAndHugeValuesAreSafelyOutside()
        {
            var g = Default();
            Assert.IsFalse(g.TryWorldToGrid(new Vector3(float.NaN, 0f, 0f), out _));
            Assert.IsFalse(g.TryWorldToGrid(new Vector3(0f, 0f, float.PositiveInfinity), out _));
            Assert.IsFalse(g.TryWorldToGrid(new Vector3(float.NegativeInfinity, 0f, 0f), out _));
            Assert.IsFalse(g.TryWorldToGrid(new Vector3(1e30f, 0f, -1e30f), out _));
        }

        [Test]
        public void WorldToGrid_RespectsOriginAndCellSize()
        {
            var g = new ZooGrid(GridSettings.Centered(10, 10, 2f, new Vector3(-10f, 3f, 4f), 4, 4));
            Assert.AreEqual(new GridCoord(0, 0), g.WorldToGrid(new Vector3(-10f, 0f, 4f)));
            Assert.AreEqual(new GridCoord(0, 0), g.WorldToGrid(new Vector3(-8.01f, 0f, 5.99f)));
            Assert.AreEqual(new GridCoord(1, 1), g.WorldToGrid(new Vector3(-8f, 0f, 6f)));
            Assert.AreEqual(new GridCoord(9, 9), g.WorldToGrid(new Vector3(9.9f, 0f, 23.9f)));
            Assert.IsFalse(g.TryWorldToGrid(new Vector3(10f, 0f, 5f), out _));
        }

        // ---- GridToWorld ----

        [Test]
        public void GridToWorld_ReturnsCellCentreAtGroundHeight()
        {
            var g = Default();
            Assert.AreEqual(new Vector3(0.5f, 0f, 0.5f), g.GridToWorld(new GridCoord(0, 0)));
            Assert.AreEqual(new Vector3(63.5f, 0f, 63.5f), g.GridToWorld(new GridCoord(63, 63)));
            Assert.AreEqual(new Vector3(10.5f, 0f, 20.5f), g.GridToWorld(new GridCoord(10, 20)));
        }

        [Test]
        public void GridToWorldCorner_ReturnsMinimumCorner()
        {
            var g = new ZooGrid(GridSettings.Centered(10, 10, 2f, new Vector3(-10f, 3f, 4f), 4, 4));
            Assert.AreEqual(new Vector3(-10f, 3f, 4f), g.GridToWorldCorner(new GridCoord(0, 0)));
            Assert.AreEqual(new Vector3(-6f, 3f, 10f), g.GridToWorldCorner(new GridCoord(2, 3)));
            Assert.AreEqual(new Vector3(-9f, 3f, 5f), g.GridToWorld(new GridCoord(0, 0)));
        }

        [Test]
        public void RoundTrip_EveryCellCentreMapsBackToItself()
        {
            var g = Default();
            for (int z = 0; z < g.Depth; z++)
                for (int x = 0; x < g.Width; x++)
                {
                    var c = new GridCoord(x, z);
                    Assert.AreEqual(c, g.WorldToGrid(g.GridToWorld(c)));
                    Assert.AreEqual(c, g.WorldToGrid(g.GridToWorldCorner(c)));
                }
        }

        [Test]
        public void RoundTrip_HoldsForNonUnitCellSizeAndOffsetOrigin()
        {
            var g = new ZooGrid(GridSettings.Centered(64, 64, 0.75f, new Vector3(-17.3f, 0f, 9.1f), 32, 32));
            for (int z = 0; z < g.Depth; z++)
                for (int x = 0; x < g.Width; x++)
                {
                    var c = new GridCoord(x, z);
                    Assert.AreEqual(c, g.WorldToGrid(g.GridToWorld(c)));
                }
        }

        [Test]
        public void WorldBounds_MatchGridExtent()
        {
            var g = Default();
            Assert.AreEqual(new Vector3(0f, 0f, 0f), g.WorldMin);
            Assert.AreEqual(new Vector3(64f, 0f, 64f), g.WorldMax);
            Assert.AreEqual(new Vector3(32f, 0f, 32f), g.WorldCenter);
        }

        // ---- GetCell / TryGetCell ----

        [Test]
        public void GetCell_InsideGrid_IsValidWithDefaults()
        {
            var g = Default();
            var cell = g.GetCell(new GridCoord(20, 20));
            Assert.IsTrue(cell.IsValid);
            Assert.IsTrue(cell.IsUnlocked);
            Assert.IsFalse(cell.IsOccupied);
            Assert.IsFalse(cell.HasPath);
            Assert.IsFalse(cell.HasEnclosure);
            Assert.AreEqual(TerrainType.Grass, cell.Terrain);
            Assert.AreEqual(new GridCoord(20, 20), cell.Coord);
        }

        [Test]
        public void GetCell_OutsideGrid_IsInvalidAndNeverThrows()
        {
            var g = Default();
            var cell = g.GetCell(new GridCoord(-5, 200));
            Assert.IsFalse(cell.IsValid);
            Assert.IsFalse(cell.IsUnlocked);
            Assert.IsFalse(cell.IsOccupied);
            Assert.IsFalse(cell.IsAvailable);
            Assert.IsFalse(g.TryGetCell(new GridCoord(64, 64), out var c));
            Assert.IsFalse(c.IsValid);
            Assert.IsTrue(g.TryGetCell(new GridCoord(63, 63), out c));
            Assert.IsTrue(c.IsValid);
        }

        // ---- Locked / unlocked ----

        [Test]
        public void LockedCell_IsValidButNotUnlockedOrAvailable()
        {
            var g = Default();
            var c = new GridCoord(2, 2);
            Assert.IsTrue(g.TryGetCell(c, out var cell));
            Assert.IsFalse(cell.IsUnlocked);
            Assert.IsFalse(g.IsUnlocked(c));
            Assert.IsFalse(g.IsAvailable(c));
        }

        [Test]
        public void SetUnlocked_TogglesAndKeepsCountInSync()
        {
            var g = Default();
            var c = new GridCoord(2, 2);
            Assert.IsTrue(g.SetUnlocked(c, true));
            Assert.IsTrue(g.IsUnlocked(c));
            Assert.AreEqual(1025, g.UnlockedCount);
            Assert.IsTrue(g.SetUnlocked(c, true)); // no-op
            Assert.AreEqual(1025, g.UnlockedCount);
            Assert.IsTrue(g.SetUnlocked(c, false));
            Assert.AreEqual(1024, g.UnlockedCount);
        }

        [Test]
        public void UnlockRect_CountsChangedCellsAndClipsToGrid()
        {
            var g = Default();
            Assert.AreEqual(0, g.UnlockRect(16, 16, 32, 32)); // already unlocked
            Assert.AreEqual(4 * 4, g.UnlockRect(-2, -2, 6, 6)); // clipped to 4x4 at the corner
            Assert.AreEqual(1024 + 16, g.UnlockedCount);
            Assert.AreEqual(0, g.UnlockRect(10, 10, 0, 5));
            Assert.AreEqual(0, g.UnlockRect(100, 100, 5, 5));
        }

        [Test]
        public void IsAvailable_RequiresUnlockedAndUnoccupied_PathDoesNotMatter()
        {
            var g = Default();
            var c = new GridCoord(20, 20);
            Assert.IsTrue(g.IsAvailable(c));
            g.SetPath(c, true);
            Assert.IsTrue(g.IsAvailable(c));
            g.SetOccupied(c, true);
            Assert.IsFalse(g.IsAvailable(c));
            g.SetOccupied(c, false);
            Assert.IsTrue(g.IsAvailable(c));
        }

        // ---- Per-cell data ----

        [Test]
        public void CellData_TerrainPathEnclosureAreStoredIndependently()
        {
            var g = Default();
            var a = new GridCoord(20, 20);
            var b = new GridCoord(21, 20);
            g.SetTerrain(a, TerrainType.Water);
            g.SetPath(a, true);
            g.SetEnclosure(a, 7);

            var cellA = g.GetCell(a);
            Assert.AreEqual(TerrainType.Water, cellA.Terrain);
            Assert.IsTrue(cellA.HasPath);
            Assert.AreEqual(7, cellA.EnclosureId);
            Assert.IsTrue(cellA.HasEnclosure);

            var cellB = g.GetCell(b);
            Assert.AreEqual(TerrainType.Grass, cellB.Terrain);
            Assert.IsFalse(cellB.HasPath);
            Assert.AreEqual(GridCell.NoEnclosure, cellB.EnclosureId);
        }

        [Test]
        public void Mutators_RejectOutOfRangeCellsWithoutThrowing()
        {
            var g = Default();
            var bad = new GridCoord(-1, 64);
            Assert.IsFalse(g.SetUnlocked(bad, true));
            Assert.IsFalse(g.SetOccupied(bad, true));
            Assert.IsFalse(g.SetPath(bad, true));
            Assert.IsFalse(g.SetTerrain(bad, TerrainType.Sand));
            Assert.IsFalse(g.SetEnclosure(bad, 1));
            Assert.IsFalse(g.SetEnclosure(new GridCoord(1, 1), -1));
            Assert.IsFalse(g.SetEnclosure(new GridCoord(1, 1), 70000));
            Assert.AreEqual(1024, g.UnlockedCount);
        }

        [Test]
        public void Changed_FiresOnRealChangesOnly()
        {
            var g = Default();
            int fired = 0;
            g.Changed += () => fired++;
            var c = new GridCoord(20, 20);

            g.SetOccupied(c, true);
            Assert.AreEqual(1, fired);
            g.SetOccupied(c, true); // unchanged
            Assert.AreEqual(1, fired);
            g.SetTerrain(c, TerrainType.Rock);
            Assert.AreEqual(2, fired);
            g.UnlockRect(0, 0, 4, 4); // one event for the whole batch
            Assert.AreEqual(3, fired);
            g.SetOccupied(new GridCoord(-1, -1), true); // invalid
            Assert.AreEqual(3, fired);
        }

        [Test]
        public void GridCoord_EqualityAndHashing()
        {
            Assert.AreEqual(new GridCoord(3, 4), new GridCoord(3, 4));
            Assert.AreNotEqual(new GridCoord(3, 4), new GridCoord(4, 3));
            Assert.IsTrue(new GridCoord(3, 4) == new GridCoord(3, 4));
            Assert.IsTrue(new GridCoord(3, 4) != new GridCoord(3, 5));
            Assert.AreEqual(new GridCoord(3, 4).GetHashCode(), new GridCoord(3, 4).GetHashCode());
        }

        [Test]
        public void Grid_IsDeterministic_SameInputsSameOutputs()
        {
            var a = Default();
            var b = Default();
            var p = new Vector3(21.37f, 0f, 44.02f);
            Assert.AreEqual(a.WorldToGrid(p), b.WorldToGrid(p));
            Assert.AreEqual(a.UnlockedCount, b.UnlockedCount);
        }
    }
}
