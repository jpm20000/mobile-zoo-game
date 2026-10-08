using NUnit.Framework;
using UnityEngine;
using ZooGame.Animals;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Animals
{
    public class AnimalMovementTests
    {
        AnimalFixture _f;

        [SetUp] public void SetUp() => _f = new AnimalFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        AnimalMovementController Start(string enclosure, int x, int z)
        {
            var m = new AnimalMovementController(_f.Enclosures, new System.Random(3));
            m.Begin(enclosure, _f.Construction.Grid.GridToWorld(new GridCoord(x, z)), 2f, 0.1f, 0.3f);
            return m;
        }

        [Test]
        public void Roaming_AlternatesIdleAndMoving_AndNeverLeavesTheEnclosure()
        {
            string id = _f.BuildEnclosure(4, 4, 5, 3);
            var m = Start(id, 5, 5);
            int moves = 0, modeChanges = 0;
            m.ModeChanged += () => modeChanges++;

            for (int i = 0; i < 6000; i++)
            {
                if (m.Tick(0.05f)) moves++;
                Assert.IsTrue(_f.Enclosures.ContainsPosition(id, m.Position), "left the pen at tick " + i + ": " + m.Position);
            }
            Assert.Greater(moves, 100, "it should actually roam");
            Assert.Greater(modeChanges, 10, "and pause between trips");
        }

        [Test]
        public void Roaming_StaysInsideAnLShapedEnclosure()
        {
            // 6x2 bar with a 2x3 block hanging off it: a concave outline built from one closed fence loop.
            var edges = new System.Collections.Generic.List<FenceEdge>();
            void Z(int x, int z0, int z1) { for (int z = z0; z < z1; z++) edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.Z, x, z), EdgeKind.Fence)); }
            void X(int z, int x0, int x1) { for (int x = x0; x < x1; x++) edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.X, x, z), EdgeKind.Fence)); }
            // outline: (4,4)->(10,4)->(10,6)->(6,6)->(6,9)->(4,9)->(4,4)
            X(4, 4, 10); Z(10, 4, 6); X(6, 6, 10); Z(6, 6, 9); X(9, 4, 6); Z(4, 4, 9);
            _f.Construction.Model.BuildFences(edges);
            string id = AnimalEnclosureService.ToId(_f.Construction.EnclosureIdAt(4, 4));
            Assert.IsTrue(_f.Enclosures.IsValidEnclosure(id));
            Assert.IsFalse(_f.Enclosures.ContainsPosition(id, new Vector3(8.5f, 0, 8.5f)), "the notch is outside");

            var m = Start(id, 5, 5);
            for (int i = 0; i < 8000; i++)
            {
                m.Tick(0.05f);
                Assert.IsTrue(_f.Enclosures.ContainsPosition(id, m.Position), "left the L at tick " + i + ": " + m.Position);
            }
        }

        [Test]
        public void NoEnclosure_MeansTheAnimalStandsStill()
        {
            var m = new AnimalMovementController(_f.Enclosures, new System.Random(1));
            m.Begin(null, new Vector3(5, 0, 5), 2f, 0f, 0f);
            for (int i = 0; i < 200; i++) Assert.IsFalse(m.Tick(0.1f));
            Assert.AreEqual(new Vector3(5, 0, 5), m.Position);
            Assert.IsFalse(m.IsMoving);
        }

        [Test]
        public void Halt_StopsAWalkInProgress()
        {
            string id = _f.BuildEnclosure(4, 4, 6, 6);
            var m = Start(id, 5, 5);
            for (int i = 0; i < 200 && !m.IsMoving; i++) m.Tick(0.1f);
            Assert.IsTrue(m.IsMoving);
            m.Halt();
            Assert.IsFalse(m.IsMoving);
        }

        [Test]
        public void Speed_IsRespected()
        {
            string id = _f.BuildEnclosure(4, 4, 8, 8);
            var m = Start(id, 5, 5);
            var last = m.Position;
            for (int i = 0; i < 2000; i++)
            {
                m.Tick(0.05f);
                Assert.LessOrEqual(Vector3.Distance(last, m.Position), 2f * 0.05f + 1e-4f);
                last = m.Position;
            }
        }
    }
}
