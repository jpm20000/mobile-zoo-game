using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ZooGame.Animals;
using ZooGame.Core;
using ZooGame.Data;
using ZooGame.Gameplay;
using ZooGame.Placement;
using ZooGame.World;

namespace ZooGame.Tests.PlayMode.Animals
{
    /// <summary>The needs simulation in the real Zoo scene: GameClock pause and speed, resources, enclosure changes and the info panel.</summary>
    public class AnimalNeedsPlayModeTests
    {
        ZooSceneBinder _binder;
        ConstructionModel _model;
        AnimalSpawner _spawner;
        AnimalNeedsSystem _needs;
        GameClock _clock;
        readonly List<Object> _created = new List<Object>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Bootstrap");
            float timeout = Time.realtimeSinceStartup + 10f;
            while ((GameManager.Instance == null || GameManager.Instance.State.Current != GameState.Playing)
                   && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State.Current);

            _binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            _spawner = _binder.AnimalSpawner;
            Assert.IsNotNull(_spawner, "run ZooGame > M4 > Create Or Update Project Assets");
            _needs = _binder.AnimalNeeds;
            Assert.IsNotNull(_needs, "needs system not bound");
            _model = _binder.Construction;
            _clock = GameManager.Instance.Clock;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameManager.Instance != null) Object.Destroy(GameManager.Instance.gameObject);
            foreach (var o in _created) if (o != null) Object.Destroy(o);
            _created.Clear();
            yield return null;
        }

        // ---- Helpers ----

        string BuildPen(int minX, int minZ, int w, int d)
        {
            var edges = new List<FenceEdge>();
            for (int x = minX; x < minX + w; x++)
            {
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.X, x, minZ), EdgeKind.Fence));
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.X, x, minZ + d), EdgeKind.Fence));
            }
            for (int z = minZ; z < minZ + d; z++)
            {
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.Z, minX, z), EdgeKind.Fence));
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.Z, minX + w, z), EdgeKind.Fence));
            }
            _model.BuildFences(edges);
            var e = _model.Enclosures.GetAt(new GridCoord(minX, minZ));
            Assert.IsNotNull(e, "pen should be closed");
            return AnimalEnclosureService.ToId(e.Id);
        }

        AnimalController SpawnRabbit(string pen)
        {
            var r = _spawner.SpawnNew("rabbit", pen);
            Assert.IsTrue(r.Success, r.Message);
            return r.Controller;
        }

        PlacedObject Place(HabitatResourceKind kind, float amount, int x, int z, int w = 1, int h = 1)
        {
            var def = ScriptableObject.CreateInstance<PlaceableDefinition>();
            def.Configure(kind + "-" + x + "-" + z, kind.ToString(), w, h, false, PlaceableCategory.Habitat);
            def.ConfigureHabitatResource(kind, amount);
            _created.Add(def);
            var obj = _binder.Placement.Add(def, new GridCoord(x, z), Rotation90.Deg0);
            Assert.IsNotNull(obj);
            return obj;
        }

        // ---- Tests ----

        [UnityTest]
        public IEnumerator Needs_DecayWithClockTime_PauseStopsThem_AndSpeedScalesThem()
        {
            string pen = BuildPen(30, 30, 6, 6);
            var rabbit = SpawnRabbit(pen).Instance;
            Assert.AreEqual(100f, rabbit.Hunger);

            _clock.SetSpeed(SimulationSpeed.X3);
            int startTicks = _needs.TicksRun;
            float timeout = Time.realtimeSinceStartup + 10f;
            while (_needs.TicksRun < startTicks + 2 && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.GreaterOrEqual(_needs.TicksRun, startTicks + 2, "ticks run from the GameClock");
            Assert.Less(rabbit.Hunger, 100f);
            Assert.Less(rabbit.Thirst, rabbit.Hunger, "rabbit thirst decays faster than hunger");

            _clock.SetSpeed(SimulationSpeed.Paused);
            yield return null;
            int ticks = _needs.TicksRun;
            float hunger = rabbit.Hunger, thirst = rabbit.Thirst;
            float until = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual(ticks, _needs.TicksRun, "paused: no ticks");
            Assert.AreEqual(hunger, rabbit.Hunger);
            Assert.AreEqual(thirst, rabbit.Thirst);

            _clock.SetSpeed(SimulationSpeed.X1);
            timeout = Time.realtimeSinceStartup + 6f;
            while (_needs.TicksRun == ticks && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.Greater(_needs.TicksRun, ticks, "resumes after pause");
        }

        [UnityTest]
        public IEnumerator FoodAndDrinkingWater_InTheEnclosure_RecoverNeeds()
        {
            string pen = BuildPen(30, 30, 6, 6);
            var rabbit = SpawnRabbit(pen).Instance;
            yield return null;

            rabbit.Hunger = 40f; rabbit.Thirst = 40f;
            _needs.Step(2f);
            Assert.Less(rabbit.Hunger, 40f, "no food: it keeps falling");
            Assert.Less(rabbit.Thirst, 40f);

            rabbit.Hunger = 40f; rabbit.Thirst = 40f;
            Place(HabitatResourceKind.Food, 1f, 34, 34);
            Place(HabitatResourceKind.DrinkingWater, 1f, 33, 34);
            yield return null;
            _needs.Step(2f);
            Assert.Greater(rabbit.Hunger, 40f, "food recovers hunger");
            Assert.Greater(rabbit.Thirst, 40f, "drinking water recovers thirst");
        }

        [UnityTest]
        public IEnumerator HabitatSummary_IsCached_AndSeesPlacedObjects()
        {
            string pen = BuildPen(30, 30, 6, 6);
            SpawnRabbit(pen);
            SpawnRabbit(pen);
            yield return null;

            Assert.IsTrue(_binder.AnimalHabitats.TryGet(pen, out var h));
            Assert.AreEqual(36, h.Area);
            Assert.AreEqual(2, h.CountOf("rabbit"));
            Assert.AreEqual(0f, h.ShelterCapacity);

            Place(HabitatResourceKind.Shelter, 4f, 31, 31, 2, 2);
            Assert.IsTrue(_binder.AnimalHabitats.TryGet(pen, out h));
            Assert.AreEqual(4f, h.ShelterCapacity);
        }

        [UnityTest]
        public IEnumerator SpeciesTuning_ComesFromTheDefinitions()
        {
            Assert.IsTrue(_spawner.Definitions.TryGet("rabbit", out var rabbit), "run ZooGame > M5 > Create Or Update Project Assets");
            Assert.IsTrue(rabbit.RequiresShelter);
            Assert.Greater(rabbit.RequiredEnrichmentValue, 0f);
            Assert.AreEqual(1, rabbit.TerrainPreferences.Count);
            Assert.Greater(rabbit.HungerRecoveryPerMinute, rabbit.HungerDecayPerMinute);
            Assert.IsTrue(_spawner.Definitions.TryGet("lion", out var lion));
            Assert.AreNotEqual(rabbit.HungerDecayPerMinute, lion.HungerDecayPerMinute);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpeningTheEnclosure_CapsWelfare_WithoutDeletingTheAnimal()
        {
            string pen = BuildPen(30, 30, 6, 6);
            var rabbit = SpawnRabbit(pen);
            yield return null;
            yield return null;
            Assert.IsTrue(_needs.TryGetReport(rabbit.Instance.AnimalId, out var r));
            Assert.IsTrue(r.EnclosureValid);
            Assert.Greater(r.Welfare, 50f - 0.01f, "a fresh rabbit in a pen is at least fair");

            _model.RemoveFence(new EdgeCoord(EdgeAxis.X, 32, 30));
            yield return null;
            yield return null;
            Assert.IsFalse(r.EnclosureValid);
            Assert.LessOrEqual(rabbit.Instance.OverallWelfare, 50f);
            Assert.AreEqual(1, _binder.Animals.Count, "the animal is never deleted");
        }

        [UnityTest]
        public IEnumerator InfoPanel_ShowsNeedsHabitatSuitabilityWelfareAndFactors()
        {
            string pen = BuildPen(30, 30, 6, 6);
            var rabbit = SpawnRabbit(pen);
            yield return null;
            yield return null;

            _binder.AnimalSelection.Select(rabbit);
            yield return null;

            var panel = Object.FindAnyObjectByType<AnimalInfoPanel>(FindObjectsInactive.Include);
            var text = new System.Text.StringBuilder();
            foreach (var t in panel.GetComponentsInChildren<Text>(true)) text.AppendLine(t.text);
            string all = text.ToString();
            foreach (string needle in new[] { "Hunger", "Thirst", "Comfort", "Social", "Enrichment", "Habitat Suitability", "Overall Welfare", "Space", "Terrain", "Shelter" })
                StringAssert.Contains(needle, all);
            Assert.IsTrue(_needs.TryGetReport(rabbit.Instance.AnimalId, out var report));
            StringAssert.Contains(report.Band.ToString(), all, "the welfare band is shown");
        }
    }
}
