using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using ZooGame.Animals;
using ZooGame.Core;
using ZooGame.Data;
using ZooGame.Placement;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Animals
{
    /// <summary>
    /// The real M3 enclosures, a real placement map, the real habitat service and the real needs system, so needs tests
    /// exercise the actual aggregation. Tick interval 2 s and 1 simulated minute per second: one tick = 2 minutes.
    /// Rates are set to easy numbers: hunger -1 / +10, thirst -2 / +20 per minute.
    /// </summary>
    public sealed class NeedsFixture
    {
        readonly List<Object> _created = new List<Object>();

        public readonly AnimalFixture Animals = new AnimalFixture();
        public readonly PlacementMap Placement;
        public readonly EnclosureHabitatService Habitats;
        public readonly AnimalNeedsConfig Config = AnimalNeedsConfig.CreateDefault();
        public readonly AnimalNeedsSystem Needs;
        public readonly GameClock Clock = new GameClock(GameClockSettings.Default, new EventBus());

        public ZooGrid Grid => Animals.Construction.Grid;
        public AnimalDefinition Rabbit => Animals.Rabbit;
        public AnimalDefinition Zebra => Animals.Zebra;

        public NeedsFixture()
        {
            Placement = new PlacementMap(Grid);
            Habitats = new EnclosureHabitatService(Grid, Animals.Construction.Model.Enclosures, Placement, Animals.Registry);
            Needs = new AnimalNeedsSystem(Animals.Registry, Animals.Resolver, Animals.Enclosures, Habitats, Config);
            foreach (var d in new[] { Animals.Rabbit, Animals.Zebra, Animals.Lion })
                d.ConfigureNeeds(1f, 2f, 10f, 20f).ConfigureHabitat(1, 4);
        }

        public string Pen(int minX = 4, int minZ = 4, int w = 4, int d = 4) => Animals.BuildEnclosure(minX, minZ, w, d);

        public Vector3 Centre(int x, int z) => Grid.GridToWorld(new GridCoord(x, z));

        public AnimalInstance Add(AnimalDefinition def, string enclosureId, int x = 5, int z = 5)
        {
            var a = AnimalInstance.CreateNew(def.SpeciesId, def.DisplayName, AnimalSex.Female, enclosureId, Centre(x, z));
            Animals.Registry.Register(a);
            return a;
        }

        public PlacedObject Place(HabitatResourceKind kind, float amount, int x, int z, int w = 1, int h = 1)
        {
            var def = ScriptableObject.CreateInstance<PlaceableDefinition>();
            def.Configure(kind + "-" + x + "-" + z, kind.ToString(), w, h, false, PlaceableCategory.Habitat);
            def.ConfigureHabitatResource(kind, amount);
            _created.Add(def);
            var obj = Placement.Add(def, new GridCoord(x, z), Rotation90.Deg0);
            Assert.IsNotNull(obj, "placement failed at " + x + "," + z);
            return obj;
        }

        /// <summary>Forces one evaluation with no time passing (so direct edits to an animal are picked up), then returns its report.</summary>
        public AnimalWelfareReport Report(AnimalInstance a)
        {
            Needs.Step(0f);
            Assert.IsTrue(Needs.TryGetReport(a.AnimalId, out var r));
            return r;
        }

        /// <summary>Drives the system the way the scene does: real time through the GameClock, in exact binary steps.</summary>
        public void RunRealSeconds(float seconds, float step = 0.25f)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += step)
            {
                Clock.Tick(step);
                Needs.Advance(Clock.DeltaTime);
            }
        }

        public void Dispose()
        {
            Needs.Dispose();
            Habitats.Dispose();
            for (int i = 0; i < _created.Count; i++) Object.DestroyImmediate(_created[i]);
            Object.DestroyImmediate(Config);
            Animals.Dispose();
        }
    }
}
