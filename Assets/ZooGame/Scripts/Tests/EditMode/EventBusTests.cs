using NUnit.Framework;
using ZooGame.Core;

namespace ZooGame.Tests.EditMode
{
    public class EventBusTests
    {
        readonly struct TestEvent : IGameEvent
        {
            public readonly int Value;
            public TestEvent(int value) { Value = value; }
        }

        [Test]
        public void Publish_DeliversToSubscribers()
        {
            var bus = new EventBus();
            int received = 0;
            bus.Subscribe<TestEvent>(e => received = e.Value);
            bus.Publish(new TestEvent(7));
            Assert.AreEqual(7, received);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            var bus = new EventBus();
            int count = 0;
            System.Action<TestEvent> handler = _ => count++;
            bus.Subscribe(handler);
            bus.Unsubscribe(handler);
            bus.Publish(new TestEvent(1));
            Assert.AreEqual(0, count);
        }

        [Test]
        public void Publish_WithNoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => new EventBus().Publish(new TestEvent(1)));
        }

        [Test]
        public void Unsubscribe_DuringPublish_IsSafe()
        {
            var bus = new EventBus();
            int a = 0, b = 0;
            System.Action<TestEvent> first = null;
            first = _ => { a++; bus.Unsubscribe(first); };
            bus.Subscribe(first);
            bus.Subscribe<TestEvent>(_ => b++);

            bus.Publish(new TestEvent(1));
            bus.Publish(new TestEvent(1));

            Assert.AreEqual(1, a);
            Assert.AreEqual(2, b);
        }
    }
}
