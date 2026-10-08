using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Animals;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Animals
{
    public class AnimalEnclosureTests
    {
        AnimalFixture _f;

        [SetUp] public void SetUp() => _f = new AnimalFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        Vector3 Centre(int x, int z) => _f.Construction.Grid.GridToWorld(new GridCoord(x, z));

        // ---- Enclosure service ----

        [Test]
        public void ClosedFence_IsAValidEnclosure_WithItsArea()
        {
            string id = _f.BuildEnclosure(4, 4, 4, 4);
            Assert.IsTrue(_f.Enclosures.IsValidEnclosure(id));
            Assert.IsTrue(_f.Enclosures.TryGetArea(id, out int area));
            Assert.AreEqual(16, area);
        }

        [Test]
        public void UnknownMalformedAndNullIds_AreInvalid()
        {
            Assert.IsFalse(_f.Enclosures.IsValidEnclosure("enclosure-99"));
            Assert.IsFalse(_f.Enclosures.IsValidEnclosure("garbage"));
            Assert.IsFalse(_f.Enclosures.IsValidEnclosure(null));
            Assert.IsFalse(_f.Enclosures.IsValidEnclosure(""));
        }

        [Test]
        public void OpenFence_IsNotAnEnclosure()
        {
            // Three sides only: no closed region exists.
            var edges = ConstructionFixture.RectEdges(4, 4, 4, 4);
            edges.RemoveAt(0);
            _f.Construction.Model.BuildFences(edges);
            Assert.AreEqual(0, _f.Construction.Model.Enclosures.Count);
            Assert.IsFalse(_f.Enclosures.IsValidEnclosure(AnimalEnclosureService.ToId(1)));
        }

        [Test]
        public void MeetsMinimumArea_ComparesAgainstTheEnclosureSize()
        {
            string id = _f.BuildEnclosure(4, 4, 4, 4); // 16 cells
            Assert.IsTrue(_f.Enclosures.MeetsMinimumArea(id, 4));
            Assert.IsTrue(_f.Enclosures.MeetsMinimumArea(id, 16));
            Assert.IsFalse(_f.Enclosures.MeetsMinimumArea(id, 17));
            Assert.IsFalse(_f.Enclosures.MeetsMinimumArea("enclosure-99", 1), "an invalid enclosure meets nothing");
        }

        [Test]
        public void ContainsPosition_InsideAndOutside()
        {
            string id = _f.BuildEnclosure(4, 4, 4, 4);
            Assert.IsTrue(_f.Enclosures.ContainsPosition(id, Centre(5, 5)));
            Assert.IsFalse(_f.Enclosures.ContainsPosition(id, Centre(9, 9)));
            Assert.IsFalse(_f.Enclosures.ContainsPosition(id, new Vector3(-50, 0, -50)), "off the map");
            Assert.IsFalse(_f.Enclosures.ContainsPosition("enclosure-99", Centre(5, 5)));
        }

        [Test]
        public void RandomValidPosition_IsAlwaysInsideTheEnclosure_AndClearOfTheCellEdges()
        {
            string id = _f.BuildEnclosure(4, 4, 3, 5);
            for (int i = 0; i < 500; i++)
            {
                Assert.IsTrue(_f.Enclosures.TryGetRandomValidPosition(id, out var p));
                Assert.IsTrue(_f.Enclosures.ContainsPosition(id, p));
                Assert.IsTrue(p.x > 4.2f && p.x < 6.8f && p.z > 4.2f && p.z < 8.8f, "kept off the fences: " + p);
            }
        }

        [Test]
        public void RandomValidPosition_FailsForAnInvalidEnclosure() =>
            Assert.IsFalse(_f.Enclosures.TryGetRandomValidPosition("enclosure-5", out _));

        [Test]
        public void EnclosureIds_ListsEveryEnclosure()
        {
            string a = _f.BuildEnclosure(4, 4, 2, 2);
            string b = _f.BuildEnclosure(8, 8, 3, 3);
            var ids = new List<string>();
            _f.Enclosures.GetEnclosureIds(ids);
            CollectionAssert.AreEquivalent(new[] { a, b }, ids);
        }

        [Test]
        public void EnclosuresChanged_FiresWhenAnEnclosureIsBuiltAndRemoved()
        {
            int fired = 0;
            _f.Enclosures.EnclosuresChanged += () => fired++;
            _f.BuildEnclosure(4, 4, 4, 4);
            Assert.AreEqual(1, fired);
            _f.Construction.Model.RemoveFence(new EdgeCoord(EdgeAxis.X, 5, 4));
            Assert.AreEqual(2, fired);
        }

        [Test]
        public void IdFormat_RoundTrips()
        {
            Assert.AreEqual("enclosure-12", AnimalEnclosureService.ToId(12));
            Assert.IsTrue(AnimalEnclosureService.TryParseId("enclosure-12", out int n));
            Assert.AreEqual(12, n);
            Assert.IsFalse(AnimalEnclosureService.TryParseId("enclosure-x", out _));
        }

        // ---- Placement validation ----

        [Test]
        public void Validator_AcceptsAFittingEnclosureAndPosition()
        {
            string id = _f.BuildEnclosure(4, 4, 4, 4);
            var v = new AnimalPlacementValidator(_f.Enclosures);
            Assert.IsTrue(v.Validate(_f.Zebra, id, Centre(5, 5)).IsValid);
        }

        [Test]
        public void Validator_ReportsStructuredFailures()
        {
            string id = _f.BuildEnclosure(4, 4, 4, 4); // 16 cells
            var v = new AnimalPlacementValidator(_f.Enclosures);
            Assert.AreEqual(AnimalPlacementFailure.MissingDefinition, v.Validate(null, id, Centre(5, 5)).Failure);
            Assert.AreEqual(AnimalPlacementFailure.NoEnclosure, v.Validate(_f.Rabbit, null, Centre(5, 5)).Failure);
            Assert.AreEqual(AnimalPlacementFailure.EnclosureInvalid, v.Validate(_f.Rabbit, "enclosure-99", Centre(5, 5)).Failure);
            Assert.AreEqual(AnimalPlacementFailure.EnclosureTooSmall, v.Validate(_f.Lion, id, Centre(5, 5)).Failure);
            Assert.AreEqual(AnimalPlacementFailure.PositionOutsideEnclosure, v.Validate(_f.Rabbit, id, Centre(10, 10)).Failure);
        }

        [Test]
        public void ValidateAt_FindsTheEnclosureUnderAPosition()
        {
            string id = _f.BuildEnclosure(4, 4, 4, 4);
            var v = new AnimalPlacementValidator(_f.Enclosures);

            var inside = v.ValidateAt(_f.Zebra, Centre(5, 5), out string found);
            Assert.IsTrue(inside.IsValid);
            Assert.AreEqual(id, found);

            var outside = v.ValidateAt(_f.Rabbit, Centre(10, 10), out string none);
            Assert.AreEqual(AnimalPlacementFailure.NoEnclosure, outside.Failure);
            Assert.IsNull(none);

            Assert.AreEqual(AnimalPlacementFailure.EnclosureTooSmall, v.ValidateAt(_f.Lion, Centre(5, 5), out _).Failure);
            Assert.AreEqual(AnimalPlacementFailure.MissingDefinition, v.ValidateAt(null, Centre(5, 5), out _).Failure);
        }

        [Test]
        public void TryGetEnclosureAt_IsFalseOnOpenGroundAndOffMap()
        {
            _f.BuildEnclosure(4, 4, 4, 4);
            Assert.IsFalse(_f.Enclosures.TryGetEnclosureAt(Centre(10, 10), out _));
            Assert.IsFalse(_f.Enclosures.TryGetEnclosureAt(new Vector3(-9, 0, -9), out _));
        }

        [Test]
        public void Validator_MinimumAreaDiffersPerSpecies()
        {
            string small = _f.BuildEnclosure(4, 4, 2, 2); // 4 cells
            var v = new AnimalPlacementValidator(_f.Enclosures);
            Assert.IsTrue(v.ValidateEnclosure(_f.Rabbit, small).IsValid);
            Assert.AreEqual(AnimalPlacementFailure.EnclosureTooSmall, v.ValidateEnclosure(_f.Zebra, small).Failure);
            Assert.AreEqual(AnimalPlacementFailure.EnclosureTooSmall, v.ValidateEnclosure(_f.Lion, small).Failure);
        }

        [Test]
        public void Validator_FailureMessages_AreReadable()
        {
            foreach (AnimalPlacementFailure f in System.Enum.GetValues(typeof(AnimalPlacementFailure)))
                Assert.IsNotEmpty(new AnimalPlacementResult(f).Message);
        }

        // ---- Residency survives enclosure changes (data level) ----

        [Test]
        public void AnimalRecord_SurvivesItsEnclosureBecomingInvalid()
        {
            string id = _f.BuildEnclosure(4, 4, 4, 4);
            var a = AnimalInstance.CreateNew("zebra", "Z", AnimalSex.Female, id, Centre(5, 5));
            _f.Registry.Register(a);

            _f.Construction.Model.RemoveFence(new EdgeCoord(EdgeAxis.X, 5, 4)); // opens the pen

            Assert.IsFalse(_f.Enclosures.IsValidEnclosure(id));
            Assert.IsTrue(_f.Registry.TryGet(a.AnimalId, out var kept));
            Assert.AreEqual(id, kept.EnclosureId, "EnclosureId is preserved so a repaired fence can restore it");
        }
    }
}
