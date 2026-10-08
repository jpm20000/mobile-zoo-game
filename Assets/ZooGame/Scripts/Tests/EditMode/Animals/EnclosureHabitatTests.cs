using NUnit.Framework;
using ZooGame.Animals;
using ZooGame.Data;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Animals
{
    /// <summary>Cached enclosure aggregation: area, terrain, resources, residents and invalidation.</summary>
    public class EnclosureHabitatTests
    {
        NeedsFixture _f;

        [SetUp] public void SetUp() => _f = new NeedsFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        EnclosureHabitat Get(string id)
        {
            Assert.IsTrue(_f.Habitats.TryGet(id, out var h), "no habitat for " + id);
            return h;
        }

        [Test]
        public void Area_AndTerrainComposition_AreAggregated()
        {
            string pen = _f.Pen();
            var h = Get(pen);
            Assert.AreEqual(16, h.Area);
            Assert.AreEqual(16, h.TerrainCells[(int)TerrainType.Grass]);
            Assert.AreEqual(pen, h.EnclosureId);

            _f.Grid.SetTerrain(new GridCoord(4, 4), TerrainType.Sand);
            _f.Grid.SetTerrain(new GridCoord(5, 4), TerrainType.Sand);
            _f.Grid.SetTerrain(new GridCoord(6, 4), TerrainType.Water);
            h = Get(pen);
            Assert.AreEqual(13, h.TerrainCells[(int)TerrainType.Grass]);
            Assert.AreEqual(2, h.TerrainCells[(int)TerrainType.Sand]);
            Assert.AreEqual(1, h.HabitatWaterCells);
            Assert.IsTrue(h.HasHabitatWater);
        }

        [Test]
        public void TerrainOutsideTheEnclosure_IsNotCounted()
        {
            string pen = _f.Pen();
            _f.Grid.SetTerrain(new GridCoord(10, 10), TerrainType.Water);
            Assert.AreEqual(0, Get(pen).HabitatWaterCells);
        }

        [Test]
        public void SameSpeciesCounts_AreAggregatedPerSpecies()
        {
            string pen = _f.Pen();
            _f.Add(_f.Animals.Rabbit, pen);
            _f.Add(_f.Animals.Rabbit, pen);
            _f.Add(_f.Animals.Zebra, pen);
            var h = Get(pen);
            Assert.AreEqual(2, h.CountOf("rabbit"));
            Assert.AreEqual(1, h.CountOf("zebra"));
            Assert.AreEqual(0, h.CountOf("lion"));
            Assert.AreEqual(3, h.AnimalCount);
            Assert.AreEqual(0, h.CountOf(null));
        }

        [Test]
        public void Residents_FollowTheRegistry_OnMoveAndRemoval()
        {
            string a = _f.Pen(4, 4, 3, 3);
            string b = _f.Pen(8, 8, 3, 3);
            var rabbit = _f.Add(_f.Animals.Rabbit, a);
            Assert.AreEqual(1, Get(a).CountOf("rabbit"));
            Assert.AreEqual(0, Get(b).CountOf("rabbit"));

            _f.Animals.Registry.SetEnclosure(rabbit.AnimalId, b);
            Assert.AreEqual(0, Get(a).CountOf("rabbit"));
            Assert.AreEqual(1, Get(b).CountOf("rabbit"));

            _f.Animals.Registry.Unregister(rabbit.AnimalId);
            Assert.AreEqual(0, Get(b).CountOf("rabbit"));
        }

        [Test]
        public void Resources_AreAggregatedByKind()
        {
            string pen = _f.Pen();
            _f.Place(HabitatResourceKind.Food, 1f, 4, 4);
            _f.Place(HabitatResourceKind.Food, 1f, 5, 4);
            _f.Place(HabitatResourceKind.DrinkingWater, 1f, 6, 4);
            _f.Place(HabitatResourceKind.Shelter, 3f, 4, 6);
            _f.Place(HabitatResourceKind.Enrichment, 2f, 7, 4);
            _f.Place(HabitatResourceKind.Enrichment, 3f, 7, 5);
            var h = Get(pen);
            Assert.AreEqual(2, h.FoodSources);
            Assert.AreEqual(1, h.DrinkingWaterSources);
            Assert.AreEqual(3f, h.ShelterCapacity);
            Assert.AreEqual(5f, h.EnrichmentValue);
            Assert.IsTrue(h.HasFood);
            Assert.IsTrue(h.HasDrinkingWater);
        }

        [Test]
        public void AMultiCellObject_CountsOnce()
        {
            string pen = _f.Pen();
            _f.Place(HabitatResourceKind.Shelter, 4f, 6, 6, 2, 2);
            Assert.AreEqual(4f, Get(pen).ShelterCapacity);
        }

        [Test]
        public void ObjectsOutsideTheEnclosure_AreNotCounted()
        {
            string pen = _f.Pen();
            _f.Place(HabitatResourceKind.Food, 1f, 10, 10);
            _f.Place(HabitatResourceKind.Shelter, 4f, 9, 4);
            var h = Get(pen);
            Assert.AreEqual(0, h.FoodSources);
            Assert.AreEqual(0f, h.ShelterCapacity);
        }

        [Test]
        public void PlainPlaceables_AreNotResources()
        {
            string pen = _f.Pen();
            var def = UnityEngine.ScriptableObject.CreateInstance<PlaceableDefinition>();
            def.Configure("bench", "Bench", 1, 1);
            _f.Placement.Add(def, new GridCoord(5, 5), Rotation90.Deg0);
            var h = Get(pen);
            Assert.AreEqual(0, h.FoodSources + h.DrinkingWaterSources);
            Assert.AreEqual(0f, h.ShelterCapacity + h.EnrichmentValue);
            UnityEngine.Object.DestroyImmediate(def);
        }

        [Test]
        public void MovingOrRemovingAnObject_UpdatesTheSummary()
        {
            string pen = _f.Pen();
            var food = _f.Place(HabitatResourceKind.Food, 1f, 5, 5);
            Assert.AreEqual(1, Get(pen).FoodSources);

            Assert.IsTrue(_f.Placement.Move(food, new GridCoord(10, 10), Rotation90.Deg0));
            Assert.AreEqual(0, Get(pen).FoodSources, "moved out of the pen");
            Assert.IsTrue(_f.Placement.Move(food, new GridCoord(6, 6), Rotation90.Deg0));
            Assert.AreEqual(1, Get(pen).FoodSources);
            _f.Placement.Remove(food);
            Assert.AreEqual(0, Get(pen).FoodSources);
        }

        [Test]
        public void OpenOrUnknownEnclosures_HaveNoHabitat()
        {
            string pen = _f.Pen();
            _f.Animals.Construction.Model.RemoveFence(new EdgeCoord(EdgeAxis.X, 5, 4));
            Assert.IsFalse(_f.Habitats.TryGet(pen, out _));
            Assert.IsFalse(_f.Habitats.TryGet("enclosure-99", out _));
            Assert.IsFalse(_f.Habitats.TryGet("garbage", out _));
            Assert.IsFalse(_f.Habitats.TryGet(null, out _));
        }

        // ---- Caching ----

        [Test]
        public void Summaries_AreCached_UntilSomethingChanges()
        {
            string pen = _f.Pen();
            Get(pen); Get(pen); Get(pen);
            Assert.AreEqual(1, _f.Habitats.RebuildCount, "three reads, one build");
        }

        [Test]
        public void ResidentChange_RebuildsOnlyThatEnclosure()
        {
            string a = _f.Pen(4, 4, 3, 3);
            string b = _f.Pen(8, 8, 3, 3);
            Get(a); Get(b);
            Assert.AreEqual(2, _f.Habitats.RebuildCount);

            _f.Add(_f.Animals.Rabbit, a);
            Get(a); Get(b);
            Assert.AreEqual(3, _f.Habitats.RebuildCount, "only the enclosure that gained a resident");
        }

        [Test]
        public void StructuralChange_RebuildsLazily_OnNextRead()
        {
            string a = _f.Pen(4, 4, 3, 3);
            string b = _f.Pen(8, 8, 3, 3);
            Get(a); Get(b);
            int before = _f.Habitats.RebuildCount;

            _f.Place(HabitatResourceKind.Food, 1f, 5, 5);
            Assert.AreEqual(before, _f.Habitats.RebuildCount, "nothing is rebuilt until it is asked for");
            Assert.AreEqual(1, Get(a).FoodSources);
            Assert.AreEqual(before + 1, _f.Habitats.RebuildCount);
            Get(b);
            Assert.AreEqual(before + 2, _f.Habitats.RebuildCount);
        }

        [Test]
        public void Changed_IsRaised_ForEveryInvalidatingChange()
        {
            string pen = _f.Pen();
            int fired = 0;
            _f.Habitats.Changed += () => fired++;

            _f.Add(_f.Animals.Rabbit, pen);
            int afterResident = fired;
            Assert.Greater(afterResident, 0);

            _f.Place(HabitatResourceKind.Food, 1f, 5, 5);
            Assert.Greater(fired, afterResident);
            int afterObject = fired;

            _f.Grid.SetTerrain(new GridCoord(4, 4), TerrainType.Dirt);
            Assert.Greater(fired, afterObject);
        }

        [Test]
        public void NoAnimalPerFrameWork_TheNeedsSystemReadsTheCache()
        {
            string pen = _f.Pen();
            for (int i = 0; i < 5; i++) _f.Add(_f.Animals.Rabbit, pen);
            _f.Needs.Advance(0f);
            int built = _f.Habitats.RebuildCount;
            _f.Needs.Advance(16f); // 8 ticks over 5 animals
            Assert.AreEqual(built, _f.Habitats.RebuildCount, "ticking never rebuilds an unchanged summary");
        }
    }
}
