using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Data;
using ZooGame.Placement;
using ZooGame.Visitors;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Visitors
{
    /// <summary>
    /// The decision layer with real destinations, navigation and enclosures: state transitions, urgent-need priority,
    /// attraction weighting, facility use, exits and the fallbacks for destinations that stop being reachable.
    /// </summary>
    public class VisitorDecisionTests
    {
        VisitorFixture _f;

        // Standard layout: path z = 6 (x 4..11), pen at (4..7, 7..9), facilities under the path, entrance at the east end.
        int _pen;
        PlacedObject _entrance, _food, _drink, _toilet, _bench;

        [SetUp] public void SetUp() => _f = new VisitorFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        void FullZoo()
        {
            _f.StandardPath();
            _pen = _f.Pen();
            _entrance = _f.Place(VisitorFacilityKind.Entrance, 11, 5);
            _food = _f.Place(VisitorFacilityKind.FoodStall, 9, 5);
            _drink = _f.Place(VisitorFacilityKind.DrinkStall, 8, 5);
            _toilet = _f.Place(VisitorFacilityKind.Toilet, 7, 5);
            _bench = _f.Place(VisitorFacilityKind.Bench, 6, 5);
        }

        VisitorDestination Dest(string id)
        {
            Assert.IsTrue(_f.Destinations.TryGet(id, out var d), "no destination " + id);
            return d;
        }

        VisitorDestination TargetOf(VisitorInstance v) => Dest(v.TargetDestinationId);

        // ---- Entering ----

        [Test]
        public void ANewVisitor_EntersAtTheEntrance_ThenChoosesADestination()
        {
            FullZoo();
            Assert.IsTrue(_f.Destinations.TryGetEntrance(out var entrance));
            var v = _f.AddVisitor(11, 5);
            v.Position = entrance.SourcePosition;
            Assert.IsTrue(_f.Decisions.BeginEntering(v, entrance));
            Assert.AreEqual(VisitorState.Entering, v.State);
            Assert.AreEqual(entrance.Id, v.TargetDestinationId);
            CollectionAssert.AreEqual(new[] { C(11, 6) }, _f.AgentOf(v).Route, "steps onto the entrance's path cell");

            _f.AgentOf(v).Arrive();
            Assert.AreEqual(VisitorState.Walking, v.State, "the only thing to do is look at the pen");
            Assert.AreEqual(VisitorDestinationKind.EnclosureViewing, TargetOf(v).Kind);
        }

        [Test]
        public void BeginEntering_RefusesAnEntranceWithNoPath()
        {
            _f.Place(VisitorFacilityKind.Entrance, 4, 4);
            _f.Destinations.TryGet("facility-1", out var gate);
            var v = _f.AddVisitor(4, 4);
            Assert.IsFalse(_f.Decisions.BeginEntering(v, gate));
            Assert.IsFalse(_f.Decisions.BeginEntering(v, null));
            Assert.IsFalse(_f.Decisions.BeginEntering(null, gate));
        }

        // ---- Attractions and viewing ----

        [Test]
        public void WithNoUrgentNeed_AVisitorWalksToAReachableEnclosureViewingPoint()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.Walking, v.State);
            var target = TargetOf(v);
            Assert.AreEqual(VisitorDestinationKind.EnclosureViewing, target.Kind);
            Assert.AreEqual(_pen, target.EnclosureId);
            var agent = _f.AgentOf(v);
            Assert.IsTrue(agent.HasRoute);
            Assert.AreEqual(target.InteractionPoint, agent.Destination);
            Assert.AreEqual(1, agent.FollowCalls, "one route for one destination");
        }

        [Test]
        public void ReachingAViewingPoint_StartsViewing_ForTheConfiguredDuration()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            _f.Decisions.Decide(v);
            _f.AgentOf(v).Arrive();
            Assert.AreEqual(VisitorState.ViewingAnimal, v.State);
            Assert.AreEqual(_pen, v.CurrentEnclosureId);
            Assert.AreEqual(_f.Config.ViewDurationMinutes, v.ActivityTimeLeft);
            Assert.IsFalse(_f.AgentOf(v).HasRoute, "standing still");
        }

        [Test]
        public void Viewing_LastsUntilTheDurationIsUp_ThenAppliesSatisfaction_AndChoosesAgain()
        {
            FullZoo();
            _f.Animals.Lion.ConfigureAppeal(4f);
            _f.AddAnimal(_f.Animals.Lion, _pen);
            _f.Config.WithViewing(8f, 8f, 20f); // appeal 4 of 8 = score 50
            var v = _f.AddVisitor(10, 6);
            v.VisitSatisfaction = 90f;
            _f.Decisions.Decide(v);
            _f.AgentOf(v).Arrive();

            _f.Decisions.OnTick(v, 4f, VisitorNeed.None, false);
            Assert.AreEqual(VisitorState.ViewingAnimal, v.State, "half way");
            Assert.AreEqual(4f, v.ActivityTimeLeft, 1e-4f);
            Assert.AreEqual(90f, v.VisitSatisfaction);

            _f.Decisions.OnTick(v, 4f, VisitorNeed.None, false);
            Assert.AreEqual(70f, v.VisitSatisfaction, 1e-4f, "moved 20 towards the viewing score of 50");
            Assert.IsTrue(v.RecentlyViewed(_pen));
            Assert.That(v.State, Is.EqualTo(VisitorState.Walking).Or.EqualTo(VisitorState.ViewingAnimal), "decides again");
        }

        [Test]
        public void ViewingAnEmptyEnclosure_PullsSatisfactionDown()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.VisitSatisfaction = 60f;
            _f.Decisions.Decide(v);
            _f.AgentOf(v).Arrive();
            _f.Decisions.OnTick(v, 100f, VisitorNeed.None, false);
            Assert.AreEqual(40f, v.VisitSatisfaction, 1e-4f, "score 0, adjustment 20");
        }

        [Test]
        public void ViewingAnAppealingEnclosure_RaisesSatisfaction_AndHappiness()
        {
            FullZoo();
            _f.Animals.Lion.ConfigureAppeal(200f);
            _f.AddAnimal(_f.Animals.Lion, _pen);
            var v = _f.AddVisitor(10, 6);
            v.VisitSatisfaction = 60f;
            VisitorMath.RecalculateHappiness(v);
            float before = v.Happiness;
            _f.Decisions.Decide(v);
            _f.AgentOf(v).Arrive();
            _f.Decisions.OnTick(v, 100f, VisitorNeed.None, false);
            Assert.AreEqual(80f, v.VisitSatisfaction, 1e-4f);
            Assert.Greater(v.Happiness, before);
        }

        [Test]
        public void StateChanges_AreReported()
        {
            FullZoo();
            var states = new List<VisitorState>();
            _f.Decisions.StateChanged += x => states.Add(x.State);
            var v = _f.AddVisitor(10, 6);
            _f.Decisions.Decide(v);
            _f.AgentOf(v).Arrive();
            CollectionAssert.AreEqual(new[] { VisitorState.Walking, VisitorState.ViewingAnimal }, states);
        }

        // ---- Attraction weighting ----

        int[] CountChoices(int trials, int pen2, System.Action<VisitorInstance> beforeEach = null)
        {
            var v = _f.AddVisitor(7, 6);
            var counts = new int[2];
            for (int i = 0; i < trials; i++)
            {
                beforeEach?.Invoke(v);
                _f.Decisions.Decide(v);
                int enclosure = TargetOf(v).EnclosureId;
                counts[enclosure == _pen ? 0 : 1]++;
            }
            return counts;
        }

        [Test]
        public void Attractions_AreChosenByAppealWeight()
        {
            _f.StandardPath();
            _pen = _f.Pen(4, 7, 3, 3);                // viewpoints (4..6, 6)
            int pen2 = _f.Pen(9, 7, 3, 3);            // viewpoints (9..11, 6)
            _f.Animals.Lion.ConfigureAppeal(30f);
            _f.AddAnimal(_f.Animals.Lion, _pen);       // weight 30
            _f.AddAnimal(_f.Animals.Rabbit, pen2);     // appeal 1 -> weight 1
            var counts = CountChoices(300, pen2);
            Assert.Greater(counts[0], 255, "about 97% should pick the appealing pen: " + counts[0] + " vs " + counts[1]);
            Assert.Greater(counts[1], 0, "the other pen is still possible");
        }

        [Test]
        public void EmptyEnclosures_StillHaveWeightOne()
        {
            _f.StandardPath();
            _pen = _f.Pen(4, 7, 3, 3);
            int pen2 = _f.Pen(9, 7, 3, 3);
            var counts = CountChoices(300, pen2);
            Assert.Greater(counts[0], 100);
            Assert.Greater(counts[1], 100, "two empty pens are an even choice");
        }

        [Test]
        public void ARecentlyViewedEnclosure_IsLessLikely()
        {
            _f.StandardPath();
            _pen = _f.Pen(4, 7, 3, 3);
            int pen2 = _f.Pen(9, 7, 3, 3);
            var counts = CountChoices(300, pen2, v => { v.ForgetRecent(); v.RememberViewed(_pen); });
            Assert.Greater(counts[1], counts[0] * 2, "weight 0.25 against 1: " + counts[0] + " vs " + counts[1]);
            Assert.Greater(counts[0], 0);
        }

        // ---- Urgent needs ----

        [Test]
        public void UrgentHunger_SendsTheVisitorToAFoodStall_AndEatingRecoversIt()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 80f;
            float satisfaction = v.VisitSatisfaction;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.SeekingFood, v.State);
            Assert.AreEqual(VisitorFixture.FacilityId(_food), v.TargetDestinationId);

            _f.AgentOf(v).Arrive();
            Assert.AreEqual(10f, v.Hunger, 1e-4f, "80 - 70");
            Assert.AreEqual(satisfaction + 5f, v.VisitSatisfaction, 1e-4f);
            Assert.AreEqual(VisitorState.Walking, v.State, "satisfied: back to the attractions");
        }

        [Test]
        public void UrgentThirst_UsesADrinkStall()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.Thirst = 85f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.SeekingDrink, v.State);
            Assert.AreEqual(VisitorFixture.FacilityId(_drink), v.TargetDestinationId);
            _f.AgentOf(v).Arrive();
            Assert.AreEqual(10f, v.Thirst, 1e-4f, "85 - 75");
        }

        [Test]
        public void UrgentToilet_UsesAToilet_AndResetsTheNeed()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.ToiletNeed = 80f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.SeekingToilet, v.State);
            Assert.AreEqual(VisitorFixture.FacilityId(_toilet), v.TargetDestinationId);
            _f.AgentOf(v).Arrive();
            Assert.AreEqual(0f, v.ToiletNeed);
        }

        [Test]
        public void LowEnergy_SendsTheVisitorToABench_WhereItRestsThenRecoversEnergy()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.Energy = 20f;
            float satisfaction = v.VisitSatisfaction;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.Walking, v.State);
            Assert.AreEqual(VisitorFixture.FacilityId(_bench), v.TargetDestinationId);

            _f.AgentOf(v).Arrive();
            Assert.AreEqual(VisitorState.Resting, v.State);
            Assert.AreEqual(20f, v.Energy, "the energy comes back when the rest is over");

            _f.Decisions.OnTick(v, 2f, VisitorNeed.None, false);
            Assert.AreEqual(VisitorState.Resting, v.State);
            _f.Decisions.OnTick(v, 3f, VisitorNeed.None, false);
            Assert.AreEqual(70f, v.Energy, 1e-4f, "20 + 50");
            Assert.AreEqual(satisfaction + 5f, v.VisitSatisfaction, 1e-4f);
            Assert.AreNotEqual(VisitorState.Resting, v.State);
        }

        [Test]
        public void FacilityRecovery_ClampsToTheValidRange()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 30f;
            v.State = VisitorState.SeekingFood;
            v.TargetDestinationId = VisitorFixture.FacilityId(_food);
            _f.Decisions.OnArrived(v);
            Assert.AreEqual(0f, v.Hunger, "30 - 70 stops at 0");

            v.Energy = 80f;
            v.State = VisitorState.Walking;
            v.TargetDestinationId = VisitorFixture.FacilityId(_bench);
            _f.Decisions.OnArrived(v);
            _f.Decisions.OnTick(v, 10f, VisitorNeed.None, false);
            Assert.AreEqual(100f, v.Energy, "80 + 50 stops at 100");
        }

        [Test]
        public void FacilityEffects_AreConfigurable()
        {
            FullZoo();
            _f.Config.WithFacilityEffects(30f, 40f, 20f, 10f, 2f);
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 80f; v.Thirst = 75f; v.ToiletNeed = 80f;
            float satisfaction = v.VisitSatisfaction;

            _f.Decisions.Decide(v);                         // thirst 0.75... hunger 0.8 is first
            Assert.AreEqual(VisitorState.SeekingFood, v.State);
            _f.AgentOf(v).Arrive();
            Assert.AreEqual(50f, v.Hunger, 1e-4f, "80 - 30");
            Assert.AreEqual(satisfaction + 2f, v.VisitSatisfaction, 1e-4f);

            Assert.AreEqual(VisitorState.SeekingToilet, v.State, "toilet 0.8 now beats thirst 0.75");
            _f.AgentOf(v).Arrive();
            Assert.AreEqual(20f, v.ToiletNeed, 1e-4f, "toilet need after use is configurable");

            Assert.AreEqual(VisitorState.SeekingDrink, v.State);
            _f.AgentOf(v).Arrive();
            Assert.AreEqual(35f, v.Thirst, 1e-4f, "75 - 40");
        }

        // ---- Priority ----

        [Test]
        public void SeveralUrgentNeeds_AreServedInOrderOfUrgency()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 80f; v.Thirst = 95f; v.ToiletNeed = 76f;
            var order = new List<VisitorState>();

            _f.Decisions.Decide(v);
            for (int i = 0; i < 3; i++)
            {
                order.Add(v.State);
                _f.AgentOf(v).Arrive();
            }
            CollectionAssert.AreEqual(new[] { VisitorState.SeekingDrink, VisitorState.SeekingFood, VisitorState.SeekingToilet }, order);
            Assert.That(v.State, Is.EqualTo(VisitorState.Walking).Or.EqualTo(VisitorState.ViewingAnimal));
        }

        [Test]
        public void EnergyUrgency_IsInverted_InThePriorityOrder()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 72f; v.Energy = 10f; // food 0.72, rest 0.90
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorFixture.FacilityId(_bench), v.TargetDestinationId);
        }

        [Test]
        public void TheMostUrgentNeedWithoutAFacility_IsSkipped_ForTheNextWithOne()
        {
            _f.StandardPath();
            _pen = _f.Pen();
            _food = _f.Place(VisitorFacilityKind.FoodStall, 9, 5); // no drink stall anywhere
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 75f; v.Thirst = 99f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.SeekingFood, v.State);
        }

        [Test]
        public void UrgentNeedWithNoFacility_FallsBackToNormalBehaviour()
        {
            _f.StandardPath();
            _pen = _f.Pen();
            _f.Place(VisitorFacilityKind.Entrance, 11, 5);
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 95f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.Walking, v.State, "no food anywhere: carry on looking at animals");
            Assert.AreEqual(VisitorDestinationKind.EnclosureViewing, TargetOf(v).Kind);
        }

        [Test]
        public void AFacilityOnAnotherNetwork_IsNotAChoice()
        {
            FullZoo();
            _f.BuildPath(4, 10, 7);
            _f.Place(VisitorFacilityKind.FoodStall, 5, 11); // reachable only from the separate z = 10 path
            _f.Destinations.SetEnabled(VisitorFixture.FacilityId(_food), false);
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 90f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.Walking, v.State);
        }

        [Test]
        public void ADisabledFacility_IsNotAChoice()
        {
            FullZoo();
            _f.Destinations.SetEnabled(VisitorFixture.FacilityId(_food), false);
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 90f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.Walking, v.State);
        }

        [Test]
        public void TheNearestFacilityOfAKind_IsChosen()
        {
            FullZoo();
            var far = _f.Place(VisitorFacilityKind.FoodStall, 4, 5);
            var v = _f.AddVisitor(5, 6);
            v.Hunger = 90f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorFixture.FacilityId(far), v.TargetDestinationId);
        }

        [Test]
        public void AVisitorAlreadyAtTheFacility_UsesItAtOnce()
        {
            FullZoo();
            var v = _f.AddVisitor(9, 6); // standing on the food stall interaction cell
            v.Hunger = 90f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(20f, v.Hunger, 1e-4f, "90 - 70, no walking needed");
            Assert.AreEqual(VisitorState.Walking, v.State);
        }

        // ---- Interrupting a walk ----

        [Test]
        public void AUrgentNeed_InterruptsAWalkToAnAttraction_WhenAFacilityIsReachable()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.Walking, v.State);
            v.Thirst = 95f;
            _f.Decisions.OnTick(v, 2f, VisitorNeed.Drink, false);
            Assert.AreEqual(VisitorState.SeekingDrink, v.State);
        }

        [Test]
        public void AUrgentNeed_DoesNotKeepReroutingAWalk_WhenNoFacilityCanHelp()
        {
            _f.StandardPath();
            _pen = _f.Pen();
            var v = _f.AddVisitor(10, 6);
            _f.Decisions.Decide(v);
            var agent = _f.AgentOf(v);
            int follows = agent.FollowCalls;
            v.Thirst = 95f;
            for (int i = 0; i < 20; i++) _f.Decisions.OnTick(v, 2f, VisitorNeed.Drink, false);
            Assert.AreEqual(follows, agent.FollowCalls, "no pointless re-planning");
            Assert.AreEqual(VisitorState.Walking, v.State);
        }

        [Test]
        public void ViewingIsNotInterruptedByANeed_ButLeavingOverridesIt()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            _f.Decisions.Decide(v);
            _f.AgentOf(v).Arrive();
            v.Hunger = 99f;
            _f.Decisions.OnTick(v, 1f, VisitorNeed.Food, false);
            Assert.AreEqual(VisitorState.ViewingAnimal, v.State);
            _f.Decisions.OnTick(v, 1f, VisitorNeed.Food, true);
            Assert.AreEqual(VisitorState.Exiting, v.State);
        }

        // ---- Exiting ----

        [Test]
        public void AVisitorWhoseTimeIsUp_WalksToTheExit_AndLeaves()
        {
            FullZoo();
            var v = _f.AddVisitor(5, 6, 10f);
            v.VisitTime = 10f;
            var left = new List<VisitorInstance>();
            _f.Decisions.Left += left.Add;

            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.Exiting, v.State);
            Assert.AreEqual(VisitorFixture.FacilityId(_entrance), v.TargetDestinationId);
            Assert.AreEqual(C(11, 6), _f.AgentOf(v).Destination);
            Assert.AreEqual(0, left.Count, "still walking");

            _f.AgentOf(v).Arrive();
            CollectionAssert.AreEqual(new[] { v }, left);
            Assert.AreEqual(0, _f.Decisions.ForcedLeaveCount);
        }

        [Test]
        public void NoMeaningfulDestination_MeansLeaving()
        {
            _f.StandardPath();
            _f.Place(VisitorFacilityKind.Entrance, 11, 5); // a path and a way out, but nothing to see or use
            var v = _f.AddVisitor(5, 6);
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.Exiting, v.State);
        }

        [Test]
        public void AnUnreachableExit_LetsTheVisitorLeaveWhereTheyStand()
        {
            _f.StandardPath();
            _f.Pen();
            var v = _f.AddVisitor(5, 6, 1f);
            v.VisitTime = 5f;
            var left = new List<VisitorInstance>();
            _f.Decisions.Left += left.Add;
            _f.Decisions.Decide(v); // there is no entrance at all
            CollectionAssert.AreEqual(new[] { v }, left);
            Assert.AreEqual(1, _f.Decisions.ForcedLeaveCount);
        }

        [Test]
        public void AnExitOnAnotherNetwork_IsUnreachableToo()
        {
            FullZoo();
            _f.Model.RemovePath(C(10, 6)); // cuts the entrance end off
            var v = _f.AddVisitor(5, 6);
            v.VisitTime = v.MaximumVisitTime;
            _f.Decisions.Decide(v);
            Assert.AreEqual(1, _f.Decisions.ForcedLeaveCount);
            Assert.AreEqual(1, _f.LeftVisitors.Count);
        }

        [Test]
        public void ALeftVisitor_IsNotReportedTwice()
        {
            FullZoo();
            _f.Decisions.Left += x => _f.Registry.Unregister(x.VisitorId); // as the spawner does
            var v = _f.AddVisitor(5, 6, 10f);
            v.VisitTime = 10f;
            _f.Decisions.Decide(v);
            _f.AgentOf(v).Arrive();
            _f.AgentOf(v).Arrive();
            Assert.AreEqual(1, _f.LeftVisitors.FindAll(x => x == v).Count, "a detached visitor is not reported twice... ");
        }

        // ---- Unreachable destination fallback ----

        void TwoPens(out int left, out int right)
        {
            _f.StandardPath();
            left = _f.Pen(4, 7, 3, 3);    // viewpoints (4..6, 6)
            right = _f.Pen(9, 7, 3, 3);   // viewpoints (9..11, 6)
        }

        [Test]
        public void WhenTheTargetBecomesUnreachable_TheRouteIsClearedAndAnotherDestinationIsChosen()
        {
            TwoPens(out int left, out int right);
            var v = _f.AddVisitor(7, 6);
            _f.Decisions.Decide(v);
            var agent = _f.AgentOf(v);
            string firstTarget = v.TargetDestinationId;
            int firstEnclosure = TargetOf(v).EnclosureId;
            int stopsBefore = agent.StopCalls;

            // Remove the path cells beside the chosen enclosure: its viewing points disappear.
            var cut = new List<GridCoord>();
            for (int x = firstEnclosure == left ? 4 : 9; x <= (firstEnclosure == left ? 6 : 11); x++) cut.Add(C(x, 6));
            _f.Model.RemovePaths(cut);
            _f.Decisions.Process();

            Assert.Greater(agent.StopCalls, stopsBefore, "the old route was cleared");
            Assert.AreEqual(VisitorState.Walking, v.State);
            Assert.AreNotEqual(firstTarget, v.TargetDestinationId);
            Assert.AreNotEqual(firstEnclosure, TargetOf(v).EnclosureId, "the other enclosure");
            Assert.IsTrue(agent.HasRoute);
            Assert.IsTrue(_f.Nav.AreConnected(C(7, 6), agent.Destination));
        }

        [Test]
        public void WhenNothingUsefulRemainsReachable_TheVisitorHeadsForTheExit()
        {
            _f.StandardPath();
            _pen = _f.Pen(4, 7, 3, 3);
            _entrance = _f.Place(VisitorFacilityKind.Entrance, 9, 5);
            var v = _f.AddVisitor(8, 6);
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.Walking, v.State);

            _f.Model.RemovePaths(new[] { C(4, 6), C(5, 6), C(6, 6) });
            _f.Decisions.Process();

            Assert.AreEqual(VisitorState.Exiting, v.State);
            Assert.AreEqual(VisitorFixture.FacilityId(_entrance), v.TargetDestinationId);
        }

        [Test]
        public void ARemovedFacility_SendsTheVisitorElsewhere()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.Hunger = 90f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.SeekingFood, v.State);

            _f.Placement.Remove(_food);
            _f.Decisions.Process();
            Assert.AreEqual(VisitorState.Walking, v.State, "no food any more: back to the attractions");
            Assert.AreEqual(VisitorDestinationKind.EnclosureViewing, TargetOf(v).Kind);
        }

        [Test]
        public void ADisabledTarget_IsAbandoned()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            v.Thirst = 90f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.SeekingDrink, v.State);
            _f.Destinations.SetEnabled(VisitorFixture.FacilityId(_drink), false);
            _f.Decisions.Process();
            Assert.AreEqual(VisitorState.Walking, v.State);
        }

        [Test]
        public void ABlockedExit_ReplansOrLeaves()
        {
            FullZoo();
            var v = _f.AddVisitor(5, 6, 10f);
            v.VisitTime = 10f;
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.Exiting, v.State);
            _f.Model.RemovePath(C(9, 6)); // between the visitor and the entrance
            _f.Decisions.Process();
            Assert.AreEqual(1, _f.Decisions.ForcedLeaveCount, "no way out any more");
            Assert.AreEqual(1, _f.LeftVisitors.Count);
        }

        [Test]
        public void ARouteThatStaysValid_IsReplannedOnce_WhenTheNetworkChanges()
        {
            TwoPens(out _, out _);
            var v = _f.AddVisitor(7, 6);
            _f.Decisions.Decide(v);
            var agent = _f.AgentOf(v);
            string target = v.TargetDestinationId;
            int follows = agent.FollowCalls;

            _f.Model.BuildPaths(new[] { C(7, 5) }); // an unrelated spur changes the network
            _f.Decisions.Process();
            Assert.AreEqual(follows + 1, agent.FollowCalls);
            Assert.AreEqual(target, v.TargetDestinationId, "same destination");
            Assert.AreEqual(VisitorState.Walking, v.State);
        }

        [Test]
        public void NothingIsPlanned_WhenNothingChanged()
        {
            TwoPens(out _, out _);
            var v = _f.AddVisitor(7, 6);
            _f.Decisions.Decide(v);
            _f.Decisions.Process(); // settle any change flags raised while building
            var agent = _f.AgentOf(v);
            int follows = agent.FollowCalls;
            int searches = _f.Nav.RouteRequestCount;
            for (int i = 0; i < 200; i++) _f.Decisions.Process();
            Assert.AreEqual(follows, agent.FollowCalls);
            Assert.AreEqual(searches, _f.Nav.RouteRequestCount, "no pathfinding per frame");
        }

        [Test]
        public void ViewersOnlyCheckTheGroundBeneathThem()
        {
            FullZoo();
            var v = _f.AddVisitor(10, 6);
            _f.Decisions.Decide(v);
            _f.AgentOf(v).Arrive();
            Assert.AreEqual(VisitorState.ViewingAnimal, v.State);
            var cell = _f.Grid.WorldToGrid(v.Position);
            _f.Model.RemovePath(cell);
            _f.Decisions.Process();
            Assert.AreNotEqual(VisitorState.ViewingAnimal, v.State, "the viewing point is gone");
        }

        // ---- Safety ----

        [Test]
        public void ANeedThatNeverSatisfies_DoesNotRecurseForever()
        {
            FullZoo();
            _f.Config.WithFacilityEffects(70f, 75f, 100f, 50f, 5f); // a toilet that leaves the need at 100
            var v = _f.AddVisitor(7, 6);
            v.ToiletNeed = 100f;
            Assert.DoesNotThrow(() => _f.Decisions.Decide(v));
            Assert.DoesNotThrow(() => _f.Decisions.OnTick(v, 2f, VisitorNeed.Toilet, false));
            Assert.AreEqual(100f, v.ToiletNeed);
        }

        [Test]
        public void ArrivalsForUnknownOrRemovedVisitors_AreIgnored()
        {
            FullZoo();
            var stranger = VisitorInstance.CreateNew(Vector3.zero, 10f);
            Assert.DoesNotThrow(() => _f.Decisions.OnArrived(stranger));
            Assert.DoesNotThrow(() => _f.Decisions.OnArrived(null));
            Assert.DoesNotThrow(() => _f.Decisions.OnTick(null, 1f, VisitorNeed.None, false));
            Assert.DoesNotThrow(() => _f.Decisions.Decide(null));
        }

        [Test]
        public void ADecisionNeedsNoAgent()
        {
            FullZoo();
            var v = VisitorInstance.CreateNew(_f.Grid.GridToWorld(C(9, 6)), 1000f);
            v.State = VisitorState.ChoosingDestination;
            _f.Registry.Register(v);
            Assert.DoesNotThrow(() => _f.Decisions.Decide(v));
            Assert.AreEqual(VisitorState.Walking, v.State);
        }
    }
}
