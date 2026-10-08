using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Animals;

namespace ZooGame.Tests.EditMode.Animals
{
    public class AnimalRegistryTests
    {
        AnimalRegistry _r;

        [SetUp] public void SetUp() => _r = new AnimalRegistry();

        static AnimalInstance Animal(string id, string enclosure = null) =>
            new AnimalInstance(id, "rabbit", id, AnimalSex.Female, enclosure, Vector3.zero);

        [Test]
        public void Register_Lookup_Unregister()
        {
            var a = Animal("a");
            Assert.IsTrue(_r.Register(a));
            Assert.AreEqual(1, _r.Count);
            Assert.IsTrue(_r.TryGet("a", out var found));
            Assert.AreSame(a, found);

            Assert.IsTrue(_r.Unregister("a"));
            Assert.AreEqual(0, _r.Count);
            Assert.IsFalse(_r.TryGet("a", out _));
        }

        [Test]
        public void Register_RejectsNullAndDuplicateIds()
        {
            Assert.IsFalse(_r.Register(null));
            Assert.IsTrue(_r.Register(Animal("a")));
            Assert.IsFalse(_r.Register(Animal("a")));
            Assert.AreEqual(1, _r.Count);
        }

        [Test]
        public void Unregister_UnknownOrNull_ReturnsFalse()
        {
            Assert.IsFalse(_r.Unregister("nope"));
            Assert.IsFalse(_r.Unregister(null));
            Assert.IsFalse(_r.TryGet(null, out _));
        }

        [Test]
        public void GetAll_ReturnsEveryRecord()
        {
            _r.Register(Animal("a")); _r.Register(Animal("b", "enclosure-1"));
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, _r.GetAll().Select(x => x.AnimalId));
        }

        [Test]
        public void GetByEnclosure_ReturnsOnlyThatEnclosuresResidents()
        {
            _r.Register(Animal("a", "enclosure-1"));
            _r.Register(Animal("b", "enclosure-1"));
            _r.Register(Animal("c", "enclosure-2"));
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, _r.GetByEnclosure("enclosure-1").Select(x => x.AnimalId));
            CollectionAssert.AreEquivalent(new[] { "c" }, _r.GetByEnclosure("enclosure-2").Select(x => x.AnimalId));
            Assert.IsEmpty(_r.GetByEnclosure("enclosure-9"));
        }

        [Test]
        public void NoEnclosureAnimals_AreQueryableWithNull()
        {
            _r.Register(Animal("a")); _r.Register(Animal("b", "enclosure-1"));
            CollectionAssert.AreEquivalent(new[] { "a" }, _r.GetByEnclosure(null).Select(x => x.AnimalId));
            Assert.IsFalse(_r.TryGet("a", out var a) && a.HasEnclosure);
        }

        [Test]
        public void SetEnclosure_MovesTheAnimal_KeepingItsIdentity()
        {
            var a = Animal("a", "enclosure-1");
            _r.Register(a);
            Assert.IsTrue(_r.SetEnclosure("a", "enclosure-2"));

            Assert.AreEqual("enclosure-2", a.EnclosureId);
            Assert.IsEmpty(_r.GetByEnclosure("enclosure-1"));
            CollectionAssert.AreEqual(new[] { "a" }, _r.GetByEnclosure("enclosure-2").Select(x => x.AnimalId));
            Assert.AreEqual("a", a.AnimalId);
            Assert.AreEqual(1, _r.Count);
        }

        [Test]
        public void SetEnclosure_Null_ClearsTheAssignment()
        {
            var a = Animal("a", "enclosure-1");
            _r.Register(a);
            Assert.IsTrue(_r.SetEnclosure("a", null));
            Assert.IsNull(a.EnclosureId);
            Assert.IsEmpty(_r.GetByEnclosure("enclosure-1"));
            CollectionAssert.AreEqual(new[] { "a" }, _r.GetByEnclosure(null).Select(x => x.AnimalId));
        }

        [Test]
        public void SetEnclosure_UnknownAnimal_ReturnsFalse() => Assert.IsFalse(_r.SetEnclosure("ghost", "enclosure-1"));

        [Test]
        public void Unregister_RemovesTheEnclosureIndexEntry()
        {
            _r.Register(Animal("a", "enclosure-1"));
            _r.Unregister("a");
            Assert.IsEmpty(_r.GetByEnclosure("enclosure-1"));
        }

        [Test]
        public void RegistryHoldsNoEnclosureObjects_EnclosuresDoNotOwnAnimalLifetime()
        {
            // Removing every resident from an enclosure query does not remove the record, and vice versa.
            _r.Register(Animal("a", "enclosure-1"));
            _r.SetEnclosure("a", null);
            Assert.AreEqual(1, _r.Count);
            Assert.IsTrue(_r.TryGet("a", out _));
        }
    }
}
