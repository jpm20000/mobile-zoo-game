using System.Collections.Generic;
using NUnit.Framework;
using ZooGame.Data;
using ZooGame.Visitors;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Visitors
{
    /// <summary>Destination discovery: viewing points, facilities, the entrance/exit, enable state and reachability.</summary>
    public class VisitorDestinationTests
    {
        VisitorFixture _f;

        [SetUp] public void SetUp() => _f = new VisitorFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        VisitorDestination Get(string id)
        {
            Assert.IsTrue(_f.Destinations.TryGet(id, out var d), "no destination " + id);
            return d;
        }

        // ---- Facilities ----

        [Test]
        public void Facilities_ExposeIdKindAndInteractionPoint()
        {
            _f.StandardPath();
            var food = _f.Place(VisitorFacilityKind.FoodStall, 9, 5);
            var d = Get(VisitorFixture.FacilityId(food));
            Assert.AreEqual("facility-" + food.Id, d.Id);
            Assert.AreEqual(VisitorDestinationKind.FoodStall, d.Kind);
            Assert.AreEqual(C(9, 6), d.InteractionPoint);
            Assert.IsTrue(d.IsPathAccessible);
            Assert.IsTrue(d.Enabled);
            Assert.AreEqual(food.Id, d.PlacedObjectId);
        }

        [Test]
        public void EveryFacilityKind_MapsToItsDestinationKind()
        {
            _f.StandardPath();
            var cases = new[]
            {
                (VisitorFacilityKind.FoodStall, VisitorDestinationKind.FoodStall, 5),
                (VisitorFacilityKind.DrinkStall, VisitorDestinationKind.DrinkStall, 6),
                (VisitorFacilityKind.Toilet, VisitorDestinationKind.Toilet, 7),
                (VisitorFacilityKind.Bench, VisitorDestinationKind.Bench, 8),
                (VisitorFacilityKind.Entrance, VisitorDestinationKind.Exit, 9),
            };
            foreach (var (facility, expected, x) in cases)
                Assert.AreEqual(expected, Get(VisitorFixture.FacilityId(_f.Place(facility, x, 5))).Kind, facility.ToString());
        }

        [Test]
        public void FacilityIds_AreStable_AcrossRebuilds()
        {
            _f.StandardPath();
            var toilet = _f.Place(VisitorFacilityKind.Toilet, 8, 5);
            string id = VisitorFixture.FacilityId(toilet);
            Get(id);
            _f.Place(VisitorFacilityKind.Bench, 10, 5); // forces a rebuild
            _f.Model.BuildPaths(new[] { C(4, 7) });
            Assert.AreEqual(VisitorDestinationKind.Toilet, Get(id).Kind);
        }

        [Test]
        public void ObjectsThatAreNotFacilities_AreNotDestinations()
        {
            _f.StandardPath();
            var plain = UnityEngine.ScriptableObject.CreateInstance<PlaceableDefinition>();
            plain.Configure("rock", "Rock", 1, 1);
            _f.Placement.Add(plain, C(8, 5), Rotation90.Deg0);
            Assert.AreEqual(0, _f.Destinations.Count);
            UnityEngine.Object.DestroyImmediate(plain);
        }

        [Test]
        public void AFacilityWithNoAdjacentPath_IsNotPathAccessible_AndNotReachable()
        {
            _f.StandardPath();
            var far = _f.Place(VisitorFacilityKind.FoodStall, 9, 9);
            var d = Get(VisitorFixture.FacilityId(far));
            Assert.IsFalse(d.IsPathAccessible);
            Assert.AreEqual(0, d.InteractionCells.Count);
            Assert.IsFalse(_f.Destinations.IsReachable(d, C(4, 6)));
            var results = new List<VisitorDestination>();
            Assert.AreEqual(0, _f.Destinations.GetReachable(VisitorDestinationKind.FoodStall, C(4, 6), results));
        }

        [Test]
        public void AMultiCellFacility_ExposesEveryTouchingPathCell()
        {
            _f.StandardPath();
            var big = _f.Place(VisitorFacilityKind.Bench, 8, 4, 3, 2); // cells x 8..10, z 4..5; touches path above
            var d = Get(VisitorFixture.FacilityId(big));
            CollectionAssert.AreEquivalent(new[] { C(8, 6), C(9, 6), C(10, 6) }, d.InteractionCells);
        }

        [Test]
        public void AFenceBetweenTheFacilityAndThePath_RemovesThatInteractionCell()
        {
            _f.StandardPath();
            var food = _f.Place(VisitorFacilityKind.FoodStall, 9, 5);
            // fence along the north side of the stall, between it and the path
            _f.Model.BuildFences(new[] { new FenceEdge(new EdgeCoord(EdgeAxis.X, 9, 6), EdgeKind.Fence) });
            Assert.IsFalse(Get(VisitorFixture.FacilityId(food)).IsPathAccessible);
        }

        [Test]
        public void MovingOrRemovingAFacility_UpdatesTheDestinations()
        {
            _f.StandardPath();
            var food = _f.Place(VisitorFacilityKind.FoodStall, 9, 5);
            string id = VisitorFixture.FacilityId(food);
            Assert.AreEqual(C(9, 6), Get(id).InteractionPoint);
            Assert.IsTrue(_f.Placement.Move(food, C(6, 5), Rotation90.Deg0));
            Assert.AreEqual(C(6, 6), Get(id).InteractionPoint);
            _f.Placement.Remove(food);
            Assert.IsFalse(_f.Destinations.TryGet(id, out _));
            Assert.IsFalse(_f.Destinations.TryGet(null, out _));
            Assert.IsFalse(_f.Destinations.TryGet("facility-999", out _));
        }

        // ---- Entrance / exit ----

        [Test]
        public void TheEntrance_IsTheZooExit_AndSpawnsFromItsCentre()
        {
            _f.StandardPath();
            Assert.IsFalse(_f.Destinations.TryGetEntrance(out _), "no entrance placed yet");
            var gate = _f.Place(VisitorFacilityKind.Entrance, 4, 4, 2, 2);
            Assert.IsTrue(_f.Destinations.TryGetEntrance(out var e));
            Assert.AreEqual(VisitorDestinationKind.Exit, e.Kind);
            Assert.AreEqual(VisitorFixture.FacilityId(gate), e.Id);
            Assert.AreEqual(new UnityEngine.Vector3(5f, 0f, 5f), e.SourcePosition, "centre of the 2x2 footprint");
            Assert.IsTrue(e.IsPathAccessible);
        }

        [Test]
        public void AnEntranceWithoutAPath_DoesNotCount()
        {
            _f.Place(VisitorFacilityKind.Entrance, 4, 4);
            Assert.IsFalse(_f.Destinations.TryGetEntrance(out _));
        }

        // ---- Enclosure viewing points ----

        [Test]
        public void ViewingPoints_AreThePathCellsBesideTheEnclosureBoundary()
        {
            _f.StandardPath();
            int pen = _f.Pen(); // cells (4..7, 7..9); south fence touches path z = 6
            var results = new List<VisitorDestination>();
            Assert.AreEqual(4, _f.Destinations.GetReachableViewpoints(pen, C(4, 6), results));
            var cells = new List<GridCoord>();
            foreach (var d in results)
            {
                Assert.AreEqual(VisitorDestinationKind.EnclosureViewing, d.Kind);
                Assert.AreEqual(pen, d.EnclosureId);
                cells.Add(d.InteractionPoint);
            }
            CollectionAssert.AreEquivalent(new[] { C(4, 6), C(5, 6), C(6, 6), C(7, 6) }, cells);
        }

        [Test]
        public void ViewingPointIds_AreStableAndDescribeTheEnclosureAndCell()
        {
            _f.StandardPath();
            int pen = _f.Pen();
            Assert.AreEqual(VisitorDestinationKind.EnclosureViewing, Get("enclosure-" + pen + "/view-5-6").Kind);
        }

        [Test]
        public void NoViewingPoints_WithoutAPathBesideTheEnclosure()
        {
            int pen = _f.Pen();
            _f.BuildPath(4, 4, 11); // two rows away from the pen
            Assert.AreEqual(0, _f.Destinations.GetReachableEnclosures(C(4, 4), new List<int>()));
            Assert.AreNotEqual(0, pen);
        }

        [Test]
        public void ViewingPoints_AreNotOfferedOnPathInsideAnotherEnclosure()
        {
            _f.StandardPath();
            _f.Pen(4, 7, 4, 3);
            // An adjoining pen whose cells touch the first: its boundary cells are enclosure cells, never walkable.
            _f.Pen(8, 7, 3, 3);
            var enclosures = new List<int>();
            _f.Destinations.GetReachableEnclosures(C(4, 6), enclosures);
            Assert.AreEqual(2, enclosures.Count);
            foreach (var d in _f.Destinations.All)
                if (d.Kind == VisitorDestinationKind.EnclosureViewing)
                    Assert.IsTrue(_f.Nav.IsWalkable(d.InteractionPoint));
        }

        [Test]
        public void ReachableEnclosures_ListsEachOnce_EvenWithManyViewingPoints()
        {
            _f.StandardPath();
            int pen = _f.Pen();
            var ids = new List<int>();
            Assert.AreEqual(1, _f.Destinations.GetReachableEnclosures(C(4, 6), ids));
            CollectionAssert.AreEqual(new[] { pen }, ids);
        }

        [Test]
        public void ClosingAnEnclosure_CreatesViewingPoints_AndOpeningItRemovesThem()
        {
            _f.StandardPath();
            Assert.AreEqual(0, _f.Destinations.GetReachableEnclosures(C(4, 6), new List<int>()));
            _f.Pen();
            Assert.AreEqual(1, _f.Destinations.GetReachableEnclosures(C(4, 6), new List<int>()));
            _f.Model.RemoveFence(new EdgeCoord(EdgeAxis.Z, 4, 8));
            Assert.AreEqual(0, _f.Destinations.GetReachableEnclosures(C(4, 6), new List<int>()));
        }

        // ---- Appeal ----

        [Test]
        public void TotalAnimalAppeal_SumsTheResidents()
        {
            _f.StandardPath();
            int pen = _f.Pen();
            Assert.AreEqual(0f, _f.Destinations.TotalAnimalAppeal(pen));
            _f.AddAnimal(_f.Animals.Rabbit, pen); // 1
            _f.AddAnimal(_f.Animals.Zebra, pen);  // 2
            _f.AddAnimal(_f.Animals.Zebra, pen);  // 2
            Assert.AreEqual(5f, _f.Destinations.TotalAnimalAppeal(pen), 1e-5f);
            Assert.AreEqual(0f, _f.Destinations.TotalAnimalAppeal(pen + 50));
        }

        // ---- Enabled state ----

        [Test]
        public void DisablingADestination_MakesItUnreachable_AndIsRemembered()
        {
            _f.StandardPath();
            var food = _f.Place(VisitorFacilityKind.FoodStall, 9, 5);
            string id = VisitorFixture.FacilityId(food);
            var results = new List<VisitorDestination>();
            Assert.AreEqual(1, _f.Destinations.GetReachable(VisitorDestinationKind.FoodStall, C(4, 6), results));

            int changed = 0;
            _f.Destinations.Changed += () => changed++;
            Assert.IsTrue(_f.Destinations.SetEnabled(id, false));
            Assert.Greater(changed, 0);
            results.Clear();
            Assert.AreEqual(0, _f.Destinations.GetReachable(VisitorDestinationKind.FoodStall, C(4, 6), results));
            Assert.IsFalse(Get(id).Enabled);

            _f.Place(VisitorFacilityKind.Bench, 5, 5); // rebuild: the disabled flag survives
            Assert.IsFalse(Get(id).Enabled);

            Assert.IsTrue(_f.Destinations.SetEnabled(id, true));
            Assert.IsTrue(Get(id).Enabled);
            Assert.IsFalse(_f.Destinations.SetEnabled("facility-999", false));
        }

        [Test]
        public void ADisabledEntrance_IsNotUsed()
        {
            _f.StandardPath();
            var gate = _f.Place(VisitorFacilityKind.Entrance, 4, 5);
            Assert.IsTrue(_f.Destinations.TryGetEntrance(out _));
            _f.Destinations.SetEnabled(VisitorFixture.FacilityId(gate), false);
            Assert.IsFalse(_f.Destinations.TryGetEntrance(out _));
        }

        // ---- Reachability ----

        [Test]
        public void Reachability_FollowsThePathNetwork()
        {
            _f.BuildPath(4, 6, 7);
            _f.BuildPath(4, 10, 7);
            var near = _f.Place(VisitorFacilityKind.Toilet, 6, 5);
            var far = _f.Place(VisitorFacilityKind.Toilet, 6, 11);
            Assert.IsTrue(_f.Destinations.IsReachable(VisitorFixture.FacilityId(near), C(4, 6)));
            Assert.IsFalse(_f.Destinations.IsReachable(VisitorFixture.FacilityId(far), C(4, 6)), "separate network");
            Assert.IsTrue(_f.Destinations.IsReachable(VisitorFixture.FacilityId(far), C(4, 10)));

            _f.BuildPathColumn(4, 7, 9);
            Assert.IsTrue(_f.Destinations.IsReachable(VisitorFixture.FacilityId(far), C(4, 6)));
            Assert.IsFalse(_f.Destinations.IsReachable(VisitorFixture.FacilityId(far), C(9, 9)), "standing off the network");
            Assert.IsFalse(_f.Destinations.IsReachable("nope", C(4, 6)));
        }

        // ---- Caching ----

        [Test]
        public void Destinations_AreCached_UntilTheZooChanges()
        {
            _f.StandardPath();
            _f.Place(VisitorFacilityKind.FoodStall, 9, 5);
            _f.Pen();
            var results = new List<VisitorDestination>();
            for (int i = 0; i < 20; i++)
            {
                _f.Destinations.GetReachable(VisitorDestinationKind.FoodStall, C(4, 6), results);
                _f.Destinations.GetReachableEnclosures(C(4, 6), new List<int>());
                _f.Destinations.TryGetEntrance(out _);
            }
            Assert.AreEqual(1, _f.Destinations.RebuildCount, "twenty rounds of queries, one build");

            _f.Place(VisitorFacilityKind.Bench, 7, 5);
            _f.Destinations.Count.ToString();
            Assert.AreEqual(2, _f.Destinations.RebuildCount);
        }
    }
}
