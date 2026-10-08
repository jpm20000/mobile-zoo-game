using NUnit.Framework;
using ZooGame.Core;

namespace ZooGame.Tests.EditMode
{
    public class GameClockTests
    {
        EventBus _bus;
        GameClock _clock;

        [SetUp]
        public void SetUp()
        {
            _bus = new EventBus();
            _clock = new GameClock(GameClockSettings.Default, _bus);
        }

        [Test]
        public void DefaultsToOneTimes()
        {
            Assert.AreEqual(SimulationSpeed.X1, _clock.Speed);
            Assert.AreEqual(1f, _clock.Multiplier);
        }

        [Test]
        public void Paused_ProducesNoSimulationTime()
        {
            _clock.SetSpeed(SimulationSpeed.Paused);
            _clock.Tick(0.1f);
            Assert.IsTrue(_clock.IsPaused);
            Assert.AreEqual(0f, _clock.DeltaTime);
            Assert.AreEqual(0d, _clock.ElapsedTime);
        }

        [TestCase(SimulationSpeed.X1, 0.1f)]
        [TestCase(SimulationSpeed.X2, 0.2f)]
        [TestCase(SimulationSpeed.X3, 0.3f)]
        public void Speed_ScalesDeltaTime(SimulationSpeed speed, float expected)
        {
            _clock.SetSpeed(speed);
            _clock.Tick(0.1f);
            Assert.AreEqual(expected, _clock.DeltaTime, 1e-5f);
        }

        [Test]
        public void ElapsedTime_AccumulatesScaledTime()
        {
            _clock.SetSpeed(SimulationSpeed.X2);
            _clock.Tick(0.1f);
            _clock.Tick(0.1f);
            Assert.AreEqual(0.4d, _clock.ElapsedTime, 1e-5d);
        }

        [Test]
        public void Tick_ClampsLargeRealDeltas()
        {
            _clock.Tick(10f);
            Assert.AreEqual(GameClockSettings.Default.MaxRealDeltaSeconds, _clock.DeltaTime, 1e-5f);
        }

        [Test]
        public void Tick_IgnoresNegativeDelta()
        {
            _clock.Tick(-1f);
            Assert.AreEqual(0f, _clock.DeltaTime);
        }

        [Test]
        public void SetSpeed_PublishesEventOnlyOnChange()
        {
            int count = 0;
            SimulationSpeedChanged last = default;
            _bus.Subscribe<SimulationSpeedChanged>(e => { count++; last = e; });

            Assert.IsFalse(_clock.SetSpeed(SimulationSpeed.X1));
            Assert.IsTrue(_clock.SetSpeed(SimulationSpeed.X3));

            Assert.AreEqual(1, count);
            Assert.AreEqual(SimulationSpeed.X1, last.Previous);
            Assert.AreEqual(SimulationSpeed.X3, last.Current);
        }

        [Test]
        public void CustomMultipliers_AreHonoured()
        {
            var clock = new GameClock(new GameClockSettings(1f, 4f, 8f, 1f), _bus);
            clock.SetSpeed(SimulationSpeed.X3);
            clock.Tick(0.5f);
            Assert.AreEqual(4f, clock.DeltaTime, 1e-5f);
        }
    }
}
