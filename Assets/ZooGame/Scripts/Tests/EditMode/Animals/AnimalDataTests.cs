using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZooGame.Animals;

namespace ZooGame.Tests.EditMode.Animals
{
    public class AnimalDataTests
    {
        AnimalFixture _f;

        [SetUp] public void SetUp() => _f = new AnimalFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        // ---- SpeciesId lookup ----

        [Test]
        public void Resolver_FindsDefinitionsBySpeciesId()
        {
            Assert.IsTrue(_f.Resolver.TryGet("zebra", out var d));
            Assert.AreSame(_f.Zebra, d);
            Assert.AreEqual(3, _f.Resolver.Count);
        }

        [Test]
        public void Resolver_RejectsUnknownNullAndEmptySpecies()
        {
            Assert.IsFalse(_f.Resolver.TryGet("unicorn", out var d));
            Assert.IsNull(d);
            Assert.IsFalse(_f.Resolver.TryGet(null, out _));
            Assert.IsFalse(_f.Resolver.TryGet("", out _));
        }

        [Test]
        public void Resolver_DuplicateSpeciesId_KeepsTheFirst()
        {
            var other = AnimalDefinition.Create("rabbit", "Other Rabbit", 9);
            LogAssert.Expect(LogType.Warning, "Duplicate SpeciesId 'rabbit'; keeping the first definition.");
            var resolver = new AnimalDefinitionResolver(new[] { _f.Rabbit, other });
            Assert.IsTrue(resolver.TryGet("rabbit", out var d));
            Assert.AreSame(_f.Rabbit, d);
            Object.DestroyImmediate(other);
        }

        [Test]
        public void Resolver_IgnoresNullEntries_AndNullList()
        {
            Assert.AreEqual(1, new AnimalDefinitionResolver(new AnimalDefinition[] { null, _f.Lion }).Count);
            Assert.AreEqual(0, new AnimalDefinitionResolver(null).Count);
        }

        [Test]
        public void SpeciesMinimumAreas_Differ()
        {
            Assert.Less(_f.Rabbit.MinimumEnclosureArea, _f.Zebra.MinimumEnclosureArea);
            Assert.Less(_f.Zebra.MinimumEnclosureArea, _f.Lion.MinimumEnclosureArea);
        }

        // ---- Definition vs instance ----

        [Test]
        public void ChangingAnInstance_NeverChangesItsDefinition()
        {
            var a = AnimalInstance.CreateNew("zebra", "Stripes", AnimalSex.Female, "enclosure-1", Vector3.zero);
            string species = _f.Zebra.SpeciesId, name = _f.Zebra.DisplayName;
            int area = _f.Zebra.MinimumEnclosureArea;
            float speed = _f.Zebra.BaseMoveSpeed;

            a.DisplayName = "Renamed";
            a.Position = new Vector3(3, 0, 4);
            _f.Registry.Register(a);
            _f.Registry.SetEnclosure(a.AnimalId, "enclosure-2");

            Assert.AreEqual(species, _f.Zebra.SpeciesId);
            Assert.AreEqual(name, _f.Zebra.DisplayName);
            Assert.AreEqual(area, _f.Zebra.MinimumEnclosureArea);
            Assert.AreEqual(speed, _f.Zebra.BaseMoveSpeed);
        }

        [Test]
        public void AnimalDefinition_ExposesNoWritableMembers()
        {
            foreach (var p in typeof(AnimalDefinition).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                Assert.IsFalse(p.CanWrite && p.GetSetMethod() != null && p.GetSetMethod().IsPublic, p.Name + " must be read-only");
        }

        // ---- AnimalId ----

        [Test]
        public void GeneratedAnimalIds_AreUnique()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 5000; i++)
                Assert.IsTrue(seen.Add(AnimalInstance.CreateNew("rabbit", "r", AnimalSex.Male, null, Vector3.zero).AnimalId));
        }

        [Test]
        public void SuppliedAnimalId_IsPreserved_WhenReconstructing()
        {
            var a = new AnimalInstance("animal-fixed-1", "lion", "Leo", AnimalSex.Male, "enclosure-3", new Vector3(1, 0, 2));
            Assert.AreEqual("animal-fixed-1", a.AnimalId);
            Assert.IsTrue(_f.Registry.Register(a));
            Assert.IsTrue(_f.Registry.TryGet("animal-fixed-1", out var found));
            Assert.AreSame(a, found);
        }

        [Test]
        public void AnimalId_IsNotDerivedFromName()
        {
            var a = AnimalInstance.CreateNew("rabbit", "Same Name", AnimalSex.Female, null, Vector3.zero);
            var b = AnimalInstance.CreateNew("rabbit", "Same Name", AnimalSex.Female, null, Vector3.zero);
            Assert.AreNotEqual(a.AnimalId, b.AnimalId);
        }

        [Test]
        public void Instance_RequiresAnIdAndASpecies()
        {
            Assert.Throws<System.ArgumentException>(() => new AnimalInstance("", "rabbit", "x", AnimalSex.Male, null, Vector3.zero));
            Assert.Throws<System.ArgumentException>(() => new AnimalInstance("id", null, "x", AnimalSex.Male, null, Vector3.zero));
        }

        // ---- Save preparation ----

        [Test]
        public void Instance_RoundTripsThroughJson_WithIdentityIntact()
        {
            var a = new AnimalInstance("animal-json", "zebra", "Zed", AnimalSex.Male, "enclosure-9", new Vector3(5, 0, 6));
            var copy = JsonUtility.FromJson<AnimalInstance>(JsonUtility.ToJson(a));
            Assert.AreEqual(a.AnimalId, copy.AnimalId);
            Assert.AreEqual(a.SpeciesId, copy.SpeciesId);
            Assert.AreEqual(a.DisplayName, copy.DisplayName);
            Assert.AreEqual(a.Sex, copy.Sex);
            Assert.AreEqual(a.EnclosureId, copy.EnclosureId);
            Assert.AreEqual(a.Position, copy.Position);
        }

        [Test]
        public void Instance_WithoutEnclosure_HasNullEnclosureId()
        {
            var a = AnimalInstance.CreateNew("rabbit", "r", AnimalSex.Female, "", Vector3.zero);
            Assert.IsNull(a.EnclosureId);
            Assert.IsFalse(a.HasEnclosure);
        }
    }
}
