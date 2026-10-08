using NUnit.Framework;
using ZooGame.Core;

namespace ZooGame.Tests.EditMode
{
    public class GameStateTests
    {
        GameStateMachine _machine;
        EventBus _bus;

        [SetUp]
        public void SetUp()
        {
            _bus = new EventBus();
            _machine = new GameStateMachine(_bus);
        }

        [Test]
        public void InitialState_IsBoot() => Assert.AreEqual(GameState.Boot, _machine.Current);

        [Test]
        public void BootFlow_Boot_Loading_Playing_Paused_Playing()
        {
            Assert.IsTrue(_machine.TryTransition(GameState.Loading));
            Assert.IsTrue(_machine.TryTransition(GameState.Playing));
            Assert.IsTrue(_machine.TryTransition(GameState.Paused));
            Assert.IsTrue(_machine.TryTransition(GameState.Playing));
            Assert.AreEqual(GameState.Playing, _machine.Current);
        }

        [TestCase(GameState.Boot, GameState.Playing)]
        [TestCase(GameState.Boot, GameState.Paused)]
        [TestCase(GameState.Loading, GameState.Paused)]
        [TestCase(GameState.Playing, GameState.Boot)]
        [TestCase(GameState.Playing, GameState.Playing)]
        [TestCase(GameState.Paused, GameState.Boot)]
        public void InvalidTransitions_AreRejected(GameState from, GameState to)
        {
            Assert.IsFalse(GameStateMachine.IsValidTransition(from, to));
        }

        [Test]
        public void InvalidTransition_DoesNotChangeStateOrPublish()
        {
            int published = 0;
            _bus.Subscribe<GameStateChanged>(_ => published++);

            Assert.IsFalse(_machine.TryTransition(GameState.Playing));

            Assert.AreEqual(GameState.Boot, _machine.Current);
            Assert.AreEqual(0, published);
        }

        [Test]
        public void ValidTransition_PublishesPreviousAndCurrent()
        {
            GameStateChanged received = default;
            _bus.Subscribe<GameStateChanged>(e => received = e);

            _machine.TryTransition(GameState.Loading);

            Assert.AreEqual(GameState.Boot, received.Previous);
            Assert.AreEqual(GameState.Loading, received.Current);
        }

        [Test]
        public void ReloadFromPlayingOrPaused_IsAllowed()
        {
            Assert.IsTrue(GameStateMachine.IsValidTransition(GameState.Playing, GameState.Loading));
            Assert.IsTrue(GameStateMachine.IsValidTransition(GameState.Paused, GameState.Loading));
        }
    }
}
