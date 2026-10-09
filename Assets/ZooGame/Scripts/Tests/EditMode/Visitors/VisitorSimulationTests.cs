using System.Collections.Generic;
using NUnit.Framework;
using ZooGame.Core;
using ZooGame.Data;
using ZooGame.Visitors;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Visitors
{
    /// <summary>The scheduled need simulation: formulas per tick, GameClock pause/speed scaling, happiness and exit rules.</summary>
    public class VisitorSimulationTests
    {
        VisitorFixture _f;

        [SetUp]
        public void SetUp()
        {
            _f = new VisitorFixture();
            _f.Config.WithRates(1f, 1.25f, 0.75f, 0.5f);
        }

        [TearDown] public void TearDown() => _f.Dispose();

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        /// <summary>Path, a pen to look at and an entrance, so a visitor always has somewhere to go and a way out.</summary>
        void Zoo()
        {
            _f.StandardPath();
            _f.Pen();
            _f.Place(VisitorFacilityKind.Entrance, 11, 5);
        }

        // ---- Per tick ----

        [Test]
        public void Step_ProgressesNeedsByRateTimesSimulatedMinutes()
        {
            var v = _f.AddVisitor(8, 6);
            v.Hunger = 10f; v.Thirst = 10f; v.ToiletNeed = 10f; v.Energy = 80f;
            _f.Simulation.Step(4f);
            Assert.AreEqual(14f, v.Hunger, 1e-4f);
            Assert.AreEqual(15f, v.Thirst, 1e-4f);
            Assert.AreEqual(13f, v.ToiletNeed, 1e-4f);
            Assert.AreEqual(78f, v.Energy, 1e-4f);
            Assert.AreEqual(4f, v.VisitTime, 1e-4f);
        }

        [Test]
        public void Step_UpdatesHappinessFromTheNewNeeds()
        {
            var v = _f.AddVisitor(8, 6);
            v.Hunger = 0f; v.Thirst = 0f; v.ToiletNeed = 0f; v.Energy = 100f; v.VisitSatisfaction = 60f;
            _f.Simulation.Step(10f); // hunger 10, thirst 12.5, toilet 7.5, energy 95
            float needs = VisitorMath.NeedsHappiness(10f, 12.5f, 7.5f, 95f);
            Assert.AreEqual(needs, v.NeedsHappiness, 1e-3f);
            Assert.AreEqual(0.65f * needs + 0.35f * 60f, v.Happiness, 1e-3f);
        }

        [Test]
        public void Step_CriticalNeedsCapHappiness()
        {
            var v = _f.AddVisitor(8, 6);
            v.Thirst = 89f; v.VisitSatisfaction = 100f;
            _f.Simulation.Step(1f); // thirst 90.25
            Assert.AreEqual(35f, v.Happiness, 1e-3f);
        }

        [Test]
        public void Step_ZeroMinutes_AdvancesNothing()
        {
            var v = _f.AddVisitor(8, 6);
            v.Hunger = 33f;
            _f.Simulation.Step(0f);
            Assert.AreEqual(33f, v.Hunger);
            Assert.AreEqual(0f, v.VisitTime);
            Assert.AreEqual(0, _f.Simulation.TicksRun);
        }

        [Test]
        public void EveryVisitorIsTicked_AndEvaluatedIsRaised()
        {
            var a = _f.AddVisitor(5, 6);
            var b = _f.AddVisitor(6, 6);
            var c = _f.AddVisitor(7, 6);
            var seen = new List<VisitorInstance>();
            _f.Simulation.Evaluated += seen.Add;
            _f.Simulation.Step(2f);
            CollectionAssert.AreEquivalent(new[] { a, b, c }, seen);
            Assert.AreEqual(2f, a.VisitTime);
            Assert.AreEqual(2f, b.VisitTime);
            Assert.AreEqual(2f, c.VisitTime);
        }

        // ---- Scheduling ----

        [Test]
        public void Advance_RunsOneTickPerInterval()
        {
            var v = _f.AddVisitor(8, 6);
            _f.Simulation.Advance(1f);
            Assert.AreEqual(0, _f.Simulation.TicksRun, "half an interval");
            Assert.AreEqual(0f, v.Hunger);
            _f.Simulation.Advance(1f);
            Assert.AreEqual(1, _f.Simulation.TicksRun);
            Assert.AreEqual(2f, v.Hunger, 1e-4f, "default: 2 s per tick x 1 simulated minute per second = 2 minutes");
        }

        [Test]
        public void Advance_KeepsTheRemainder()
        {
            _f.AddVisitor(8, 6);
            _f.Simulation.Advance(3f);
            Assert.AreEqual(1, _f.Simulation.TicksRun);
            _f.Simulation.Advance(1f);
            Assert.AreEqual(2, _f.Simulation.TicksRun);
        }

        [Test]
        public void SimulatedMinutesPerSecond_IsConfigurable()
        {
            _f.Config.WithScheduling(1f, 5f);
            var v = _f.AddVisitor(8, 6);
            _f.Simulation.Advance(1f);
            Assert.AreEqual(5f, v.VisitTime, 1e-4f);
            Assert.AreEqual(5f, v.Hunger, 1e-4f);
        }

        [Test]
        public void AHitch_ProcessesAtMostTheTickCap_AndDropsTheRest()
        {
            _f.AddVisitor(8, 6);
            _f.Simulation.Advance(1000f);
            Assert.AreEqual(_f.Config.MaxTicksPerAdvance, _f.Simulation.TicksRun);
            _f.Simulation.Advance(1f);
            Assert.AreEqual(_f.Config.MaxTicksPerAdvance, _f.Simulation.TicksRun, "the backlog is gone, not queued");
        }

        // ---- Pause and speed (through the real GameClock) ----

        float RunRealSeconds(GameClock clock, VisitorInstance v, float seconds)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += 0.25f)
            {
                clock.Tick(0.25f);
                _f.Simulation.Advance(clock.DeltaTime);
            }
            return v.VisitTime;
        }

        [Test]
        public void Paused_NothingAdvances()
        {
            var clock = new GameClock(GameClockSettings.Default, new EventBus());
            var v = _f.AddVisitor(8, 6);
            clock.SetSpeed(SimulationSpeed.Paused);
            RunRealSeconds(clock, v, 10f);
            Assert.AreEqual(0, _f.Simulation.TicksRun);
            Assert.AreEqual(0f, v.VisitTime);
            Assert.AreEqual(0f, v.Hunger);
        }

        [Test]
        public void Speeds_ScaleSimulationTimeConsistently()
        {
            // 8 real seconds: 1x = 8 simulation seconds = 4 ticks = 8 minutes; 2x = 16 minutes; 3x = 24 minutes.
            float[] expected = { 8f, 16f, 24f };
            var speeds = new[] { SimulationSpeed.X1, SimulationSpeed.X2, SimulationSpeed.X3 };
            for (int i = 0; i < speeds.Length; i++)
            {
                using (var f = new FixtureScope())
                {
                    f.Fixture.Config.WithRates(1f, 1.25f, 0.75f, 0.5f);
                    var clock = new GameClock(GameClockSettings.Default, new EventBus());
                    clock.SetSpeed(speeds[i]);
                    var v = f.Fixture.AddVisitor(8, 6);
                    for (float t = 0f; t < 8f - 1e-4f; t += 0.25f)
                    {
                        clock.Tick(0.25f);
                        f.Fixture.Simulation.Advance(clock.DeltaTime);
                    }
                    Assert.AreEqual(expected[i], v.VisitTime, 1e-3f, speeds[i].ToString());
                    Assert.AreEqual(expected[i], v.Hunger, 1e-3f, speeds[i].ToString());
                }
            }
        }

        [Test]
        public void ResumingAfterPause_ContinuesFromWhereItStopped()
        {
            var clock = new GameClock(GameClockSettings.Default, new EventBus());
            var v = _f.AddVisitor(8, 6);
            RunRealSeconds(clock, v, 4f);
            float before = v.VisitTime;
            clock.SetSpeed(SimulationSpeed.Paused);
            RunRealSeconds(clock, v, 4f);
            Assert.AreEqual(before, v.VisitTime);
            clock.SetSpeed(SimulationSpeed.X1);
            RunRealSeconds(clock, v, 4f);
            Assert.Greater(v.VisitTime, before);
        }

        sealed class FixtureScope : System.IDisposable
        {
            public readonly VisitorFixture Fixture = new VisitorFixture();
            public void Dispose() => Fixture.Dispose();
        }

        // ---- Unhappiness ----

        [Test]
        public void UnhappyTime_AccumulatesWhileUnhappy_AndResetsWhenHappinessRecovers()
        {
            _f.Config.WithUnhappyExit(20f, 1000f); // never exits in this test
            var v = _f.AddVisitor(8, 6);
            v.Hunger = 100f; v.Thirst = 100f; v.ToiletNeed = 100f; v.Energy = 0f; v.VisitSatisfaction = 0f;
            _f.Simulation.Step(2f);
            _f.Simulation.Step(2f);
            Assert.AreEqual(4f, v.UnhappyTime, 1e-4f);

            v.Hunger = 0f; v.Thirst = 0f; v.ToiletNeed = 0f; v.Energy = 100f; v.VisitSatisfaction = 100f;
            _f.Config.WithRates(0f, 0f, 0f, 0f);
            _f.Simulation.Step(2f);
            Assert.AreEqual(0f, v.UnhappyTime);
        }

        [Test]
        public void SustainedUnhappiness_MakesAVisitorLeave_ButOneLowTickDoesNot()
        {
            Zoo();
            _f.Config.WithUnhappyExit(20f, 4f).WithRates(0f, 0f, 0f, 0f);
            var v = _f.AddVisitor(8, 6);
            v.Hunger = 100f; v.Thirst = 100f; v.ToiletNeed = 100f; v.Energy = 0f; v.VisitSatisfaction = 0f;
            _f.Decisions.Decide(v); // nothing urgent is reachable, so it goes to look at the pen
            Assert.AreEqual(VisitorState.Walking, v.State);

            _f.Simulation.Step(2f);
            Assert.AreEqual(2f, v.UnhappyTime, 1e-4f);
            Assert.AreEqual(VisitorState.Walking, v.State, "two minutes unhappy is not yet enough");

            _f.Simulation.Step(2f);
            Assert.AreEqual(VisitorState.Exiting, v.State);
            Assert.IsNotNull(v.TargetDestinationId);
        }

        [Test]
        public void ATemporaryDip_ThatRecoversBeforeTheDuration_DoesNotExit()
        {
            Zoo();
            _f.Config.WithUnhappyExit(20f, 4f).WithRates(0f, 0f, 0f, 0f);
            var v = _f.AddVisitor(8, 6);
            v.Hunger = 100f; v.Thirst = 100f; v.ToiletNeed = 100f; v.Energy = 0f; v.VisitSatisfaction = 0f;
            _f.Decisions.Decide(v);
            _f.Simulation.Step(2f);
            Assert.AreEqual(2f, v.UnhappyTime, 1e-4f);

            v.Hunger = 0f; v.Thirst = 0f; v.ToiletNeed = 0f; v.Energy = 100f; v.VisitSatisfaction = 80f; // a snack and a bench
            _f.Simulation.Step(2f);
            _f.Simulation.Step(2f);
            Assert.AreEqual(0f, v.UnhappyTime);
            Assert.AreNotEqual(VisitorState.Exiting, v.State);
        }

        // ---- Visit duration ----

        [Test]
        public void VisitTimeUp_MakesTheVisitorHeadForTheExit()
        {
            Zoo();
            var v = _f.AddVisitor(8, 6, 5f);
            _f.Decisions.Decide(v);
            Assert.AreEqual(VisitorState.Walking, v.State);
            _f.Simulation.Step(2f);
            _f.Simulation.Step(2f);
            Assert.AreNotEqual(VisitorState.Exiting, v.State, "4 of 5 minutes");
            _f.Simulation.Step(2f);
            Assert.AreEqual(VisitorState.Exiting, v.State);
            var entrance = default(VisitorDestination);
            Assert.IsTrue(_f.Destinations.TryGetEntrance(out entrance));
            Assert.AreEqual(entrance.Id, v.TargetDestinationId);
        }

        [Test]
        public void Visitors_EventuallyLeaveTheZoo()
        {
            Zoo();
            _f.Decisions.Left += v => _f.Registry.Unregister(v.VisitorId);
            var v = _f.AddVisitor(8, 6, 30f);
            _f.Decisions.Decide(v);

            for (int i = 0; i < 60 && _f.Registry.Count > 0; i++)
            {
                _f.Simulation.Step(2f);
                if (_f.AgentOf(v).HasRoute) _f.AgentOf(v).Arrive(); // the walk finishes instantly
            }
            Assert.AreEqual(0, _f.Registry.Count, "the visitor walked out of the zoo");
            Assert.AreEqual(1, _f.LeftVisitors.Count);
            Assert.AreEqual(0, _f.Decisions.ForcedLeaveCount, "it used the exit");
        }

        [Test]
        public void SeveralVisitorsLeavingInTheSameTick_DoNotDisturbTheIteration()
        {
            _f.Decisions.Left += v => _f.Registry.Unregister(v.VisitorId); // no exit placed: all leave on the spot
            for (int i = 0; i < 6; i++) _f.AddVisitor(4 + i, 6, 1f);
            _f.StandardPath();
            Assert.DoesNotThrow(() => _f.Simulation.Step(2f));
            Assert.AreEqual(0, _f.Registry.Count);
            Assert.AreEqual(6, _f.LeftVisitors.Count);
            Assert.AreEqual(6, _f.Decisions.ForcedLeaveCount);
        }
    }
}
