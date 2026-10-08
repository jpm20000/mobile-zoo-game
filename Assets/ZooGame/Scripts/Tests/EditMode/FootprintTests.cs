using NUnit.Framework;
using UnityEngine;
using ZooGame.World;

namespace ZooGame.Tests.EditMode
{
    public class FootprintTests
    {
        [TestCase(1, 1, 1)]
        [TestCase(2, 2, 4)]
        [TestCase(2, 3, 6)]
        [TestCase(3, 4, 12)]
        public void Fill_ProducesEveryCellOfTheFootprintOnce(int w, int h, int expected)
        {
            var buffer = new GridCoord[w * h];
            int count = Footprint.Fill(new GridCoord(5, 7), w, h, buffer);
            Assert.AreEqual(expected, count);
            CollectionAssert.AllItemsAreUnique(buffer);
            foreach (var c in buffer)
                Assert.IsTrue(c.X >= 5 && c.X < 5 + w && c.Z >= 7 && c.Z < 7 + h, c.ToString());
            Assert.AreEqual(new GridCoord(5, 7), buffer[0]);
            Assert.AreEqual(new GridCoord(5 + w - 1, 7 + h - 1), buffer[count - 1]);
        }

        [Test]
        public void RotatedSize_SwapsWidthAndHeightOnQuarterTurns()
        {
            Footprint.RotatedSize(2, 3, Rotation90.Deg0, out int w0, out int h0);
            Footprint.RotatedSize(2, 3, Rotation90.Deg90, out int w1, out int h1);
            Footprint.RotatedSize(2, 3, Rotation90.Deg180, out int w2, out int h2);
            Footprint.RotatedSize(2, 3, Rotation90.Deg270, out int w3, out int h3);
            Assert.AreEqual((2, 3), (w0, h0));
            Assert.AreEqual((3, 2), (w1, h1));
            Assert.AreEqual((2, 3), (w2, h2));
            Assert.AreEqual((3, 2), (w3, h3));
        }

        [Test]
        public void RotatedSize_SquareFootprintsNeverChange()
        {
            for (var r = Rotation90.Deg0; r <= Rotation90.Deg270; r++)
            {
                Footprint.RotatedSize(2, 2, r, out int w, out int h);
                Assert.AreEqual((2, 2), (w, h));
            }
        }

        [Test]
        public void Next_CyclesThroughFourStepsAndPreviousInverts()
        {
            var r = Rotation90.Deg0;
            for (int i = 0; i < 4; i++) r = Footprint.Next(r);
            Assert.AreEqual(Rotation90.Deg0, r);
            Assert.AreEqual(Rotation90.Deg270, Footprint.Previous(Rotation90.Deg0));
            Assert.AreEqual(Rotation90.Deg90, Footprint.Next(Rotation90.Deg0));
            Assert.AreEqual(180f, Footprint.Degrees(Rotation90.Deg180));
        }

        [Test]
        public void Contains_IsHalfOpenOnMaxEdges()
        {
            var o = new GridCoord(2, 2);
            Assert.IsTrue(Footprint.Contains(o, 3, 2, new GridCoord(2, 2)));
            Assert.IsTrue(Footprint.Contains(o, 3, 2, new GridCoord(4, 3)));
            Assert.IsFalse(Footprint.Contains(o, 3, 2, new GridCoord(5, 2)));
            Assert.IsFalse(Footprint.Contains(o, 3, 2, new GridCoord(2, 4)));
        }

        [Test]
        public void OriginForCentre_SnapsOddAndEvenFootprints()
        {
            var grid = new ZooGrid(GridSettings.Centered(16, 16, 1f, Vector3.zero, 8, 8));
            // 1x1: the cell containing the point.
            Assert.AreEqual(new GridCoord(10, 6), Footprint.OriginForCentre(grid, new Vector3(10.3f, 0, 6.9f), 1, 1));
            // 2x2: centre sits on the nearest cell corner.
            Assert.AreEqual(new GridCoord(10, 6), Footprint.OriginForCentre(grid, new Vector3(10.9f, 0, 6.8f), 2, 2));
            Assert.AreEqual(new GridCoord(9, 5), Footprint.OriginForCentre(grid, new Vector3(10.4f, 0, 6.4f), 2, 2));
            // 3x2: odd along X (cell centre), even along Z (cell corner).
            Assert.AreEqual(new GridCoord(8, 4), Footprint.OriginForCentre(grid, new Vector3(9.5f, 0, 5.0f), 3, 2));
        }

        [Test]
        public void OriginForCentre_RespectsOriginAndCellSize()
        {
            var grid = new ZooGrid(GridSettings.Centered(16, 16, 2f, new Vector3(10, 0, 20), 8, 8));
            // local = (point - origin) / cellSize = (3.2, 1.1) -> 1x1 cell (3, 1)
            Assert.AreEqual(new GridCoord(3, 1), Footprint.OriginForCentre(grid, new Vector3(16.4f, 0, 22.2f), 1, 1));
        }

        [Test]
        public void OriginForCentre_IsDeterministicForNonFiniteInput()
        {
            var grid = new ZooGrid(GridSettings.Centered(16, 16, 1f, Vector3.zero, 8, 8));
            var nan = Footprint.OriginForCentre(grid, new Vector3(float.NaN, 0, float.PositiveInfinity), 2, 2);
            Assert.IsFalse(grid.IsInsideGrid(nan));
        }

        [Test]
        public void CentreWorld_IsMiddleOfTheRotatedRectangle()
        {
            var grid = new ZooGrid(GridSettings.Centered(16, 16, 1f, Vector3.zero, 8, 8));
            Assert.AreEqual(new Vector3(6f, 0f, 6.5f), Footprint.CentreWorld(grid, new GridCoord(5, 5), 2, 3));
            Assert.AreEqual(new Vector3(6.5f, 0f, 6f), Footprint.CentreWorld(grid, new GridCoord(5, 5), 3, 2));
        }
    }
}
