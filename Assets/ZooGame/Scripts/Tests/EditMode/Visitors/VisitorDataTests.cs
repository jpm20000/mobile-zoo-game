using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Visitors;

namespace ZooGame.Tests.EditMode.Visitors
{
    /// <summary>VisitorInstance, the starting-needs factory, and the registry lifecycle.</summary>
    public class VisitorDataTests
    {
        VisitorConfig _config;

        [SetUp] public void SetUp() => _config = VisitorConfig.CreateDefault();
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(_config);

        // ---- VisitorId ----

        [Test]
        public void VisitorIds_AreUnique_AndPrefixed()
        {
            var ids = new HashSet<string>();
            for (int i = 0; i < 2000; i++)
            {
                string id = VisitorInstance.NewId();
                StringAssert.StartsWith("visitor-", id);
                Assert.IsTrue(ids.Add(id), "duplicate id " + id);
            }
        }

        [Test]
        public void NewVisitors_GetDistinctIds_AndAnIdIsRequired()
        {
            var a = VisitorInstance.CreateNew(Vector3.zero, 100f);
            var b = VisitorInstance.CreateNew(Vector3.zero, 100f);
            Assert.AreNotEqual(a.VisitorId, b.VisitorId);
            Assert.Throws<ArgumentException>(() => new VisitorInstance("", Vector3.zero, 10f));
            Assert.Throws<ArgumentException>(() => new VisitorInstance(null, Vector3.zero, 10f));
            Assert.AreEqual("v1", new VisitorInstance("v1", Vector3.zero, 10f).VisitorId);
        }

        // ---- Values ----

        [Test]
        public void Needs_AreClampedToZeroHundred()
        {
            var v = VisitorInstance.CreateNew(Vector3.zero, 100f);
            v.Hunger = 150f; v.Thirst = -3f; v.ToiletNeed = 100.5f; v.Energy = 400f;
            v.Happiness = -1f; v.NeedsHappiness = 101f; v.VisitSatisfaction = 1e9f;
            Assert.AreEqual(100f, v.Hunger);
            Assert.AreEqual(0f, v.Thirst);
            Assert.AreEqual(100f, v.ToiletNeed);
            Assert.AreEqual(100f, v.Energy);
            Assert.AreEqual(0f, v.Happiness);
            Assert.AreEqual(100f, v.NeedsHappiness);
            Assert.AreEqual(100f, v.VisitSatisfaction);
            v.Hunger = float.NaN;
            Assert.AreEqual(0f, v.Hunger, "NaN never gets stored");
        }

        [Test]
        public void TimesAndTargets_AreSanitised()
        {
            var v = VisitorInstance.CreateNew(Vector3.zero, -5f);
            Assert.AreEqual(0f, v.MaximumVisitTime);
            v.VisitTime = -2f;
            Assert.AreEqual(0f, v.VisitTime);
            v.TargetDestinationId = "";
            Assert.IsNull(v.TargetDestinationId);
            v.TargetDestinationId = "facility-3";
            Assert.AreEqual("facility-3", v.TargetDestinationId);
        }

        [Test]
        public void RecentEnclosures_AreRemembered_UpToTheCapacity()
        {
            var v = VisitorInstance.CreateNew(Vector3.zero, 100f);
            Assert.IsFalse(v.RecentlyViewed(1));
            v.RememberViewed(1); v.RememberViewed(2); v.RememberViewed(3);
            Assert.IsTrue(v.RecentlyViewed(1) && v.RecentlyViewed(2) && v.RecentlyViewed(3));
            v.RememberViewed(4); // forgets the oldest
            Assert.IsFalse(v.RecentlyViewed(1));
            Assert.IsTrue(v.RecentlyViewed(4));
            v.RememberViewed(VisitorInstance.NoEnclosure);
            Assert.IsFalse(v.RecentlyViewed(VisitorInstance.NoEnclosure));
            v.ForgetRecent();
            Assert.IsFalse(v.RecentlyViewed(4));
        }

        // ---- Starting values ----

        [Test]
        public void NewVisitors_StartWithinTheConfiguredRanges()
        {
            var rng = new System.Random(5);
            for (int i = 0; i < 300; i++)
            {
                var v = VisitorFactory.CreateNew(_config, rng, Vector3.one);
                Assert.That(v.Hunger, Is.InRange(5f, 20f));
                Assert.That(v.Thirst, Is.InRange(5f, 20f));
                Assert.That(v.ToiletNeed, Is.InRange(0f, 15f));
                Assert.That(v.Energy, Is.InRange(80f, 100f));
                Assert.AreEqual(60f, v.VisitSatisfaction);
                Assert.That(v.MaximumVisitTime, Is.InRange(_config.MinVisitMinutes, _config.MaxVisitMinutes));
                Assert.AreEqual(0f, v.VisitTime);
                Assert.AreEqual(VisitorState.Entering, v.State);
                Assert.AreEqual(Vector3.one, v.Position);
            }
        }

        [Test]
        public void NewVisitors_HaveHappinessCalculatedNormally()
        {
            var v = VisitorFactory.CreateNew(_config, new System.Random(1), Vector3.zero);
            float needs = VisitorMath.NeedsHappiness(v.Hunger, v.Thirst, v.ToiletNeed, v.Energy);
            Assert.AreEqual(needs, v.NeedsHappiness, 1e-3f);
            Assert.AreEqual(0.65f * needs + 0.35f * 60f, v.Happiness, 1e-3f);
            Assert.Greater(v.Happiness, 60f, "a fresh visitor starts content");
        }

        [Test]
        public void TheSameSeed_ProducesTheSameCrowd()
        {
            var a = VisitorFactory.CreateNew(_config, new System.Random(99), Vector3.zero);
            var b = VisitorFactory.CreateNew(_config, new System.Random(99), Vector3.zero);
            Assert.AreEqual(a.Hunger, b.Hunger);
            Assert.AreEqual(a.Thirst, b.Thirst);
            Assert.AreEqual(a.ToiletNeed, b.ToiletNeed);
            Assert.AreEqual(a.Energy, b.Energy);
            Assert.AreEqual(a.MaximumVisitTime, b.MaximumVisitTime);
            var c = VisitorFactory.CreateNew(_config, new System.Random(100), Vector3.zero);
            Assert.AreNotEqual(a.Hunger, c.Hunger);
        }

        // ---- Registry ----

        [Test]
        public void Registry_RegistersAndLooksUpById()
        {
            var r = new VisitorRegistry();
            var v = VisitorInstance.CreateNew(Vector3.zero, 100f);
            Assert.IsTrue(r.Register(v));
            Assert.AreEqual(1, r.Count);
            Assert.IsTrue(r.Contains(v.VisitorId));
            Assert.IsTrue(r.TryGet(v.VisitorId, out var found));
            Assert.AreSame(v, found);
            Assert.IsFalse(r.TryGet("visitor-nope", out _));
            Assert.IsFalse(r.TryGet(null, out _));
            Assert.IsFalse(r.Contains(null));
        }

        [Test]
        public void Registry_RejectsNullAndDuplicates()
        {
            var r = new VisitorRegistry();
            var v = VisitorInstance.CreateNew(Vector3.zero, 100f);
            Assert.IsFalse(r.Register(null));
            Assert.IsTrue(r.Register(v));
            Assert.IsFalse(r.Register(v));
            Assert.IsFalse(r.Register(new VisitorInstance(v.VisitorId, Vector3.zero, 5f)), "same id, different object");
            Assert.AreEqual(1, r.Count);
        }

        [Test]
        public void Registry_Unregister_RemovesAndCountsDown()
        {
            var r = new VisitorRegistry();
            var a = VisitorInstance.CreateNew(Vector3.zero, 100f);
            var b = VisitorInstance.CreateNew(Vector3.zero, 100f);
            var c = VisitorInstance.CreateNew(Vector3.zero, 100f);
            r.Register(a); r.Register(b); r.Register(c);
            Assert.AreEqual(3, r.Count);

            Assert.IsTrue(r.Unregister(a.VisitorId));
            Assert.AreEqual(2, r.Count);
            Assert.IsFalse(r.Contains(a.VisitorId));
            Assert.IsFalse(r.Unregister(a.VisitorId), "already gone");
            Assert.IsFalse(r.Unregister(null));
            Assert.IsTrue(r.Contains(b.VisitorId) && r.Contains(c.VisitorId));
            CollectionAssert.AreEquivalent(new[] { b, c }, r.All);

            r.Unregister(b.VisitorId);
            r.Unregister(c.VisitorId);
            Assert.AreEqual(0, r.Count);
            Assert.IsTrue(r.Register(a), "an id can be registered again once removed");
        }

        [Test]
        public void Registry_RaisesEvents()
        {
            var r = new VisitorRegistry();
            var registered = new List<VisitorInstance>();
            var unregistered = new List<VisitorInstance>();
            r.Registered += registered.Add;
            r.Unregistered += unregistered.Add;
            var v = VisitorInstance.CreateNew(Vector3.zero, 100f);
            r.Register(v);
            r.Register(v);
            r.Unregister(v.VisitorId);
            r.Unregister(v.VisitorId);
            CollectionAssert.AreEqual(new[] { v }, registered);
            CollectionAssert.AreEqual(new[] { v }, unregistered);
        }

        [Test]
        public void Registry_Snapshot_AllowsChangesWhileIterating()
        {
            var r = new VisitorRegistry();
            for (int i = 0; i < 10; i++) r.Register(VisitorInstance.CreateNew(Vector3.zero, 100f));
            var snapshot = new List<VisitorInstance>();
            r.CopyTo(snapshot);
            Assert.AreEqual(10, snapshot.Count);
            for (int i = 0; i < snapshot.Count; i++) r.Unregister(snapshot[i].VisitorId);
            Assert.AreEqual(0, r.Count);
            Assert.AreEqual(10, snapshot.Count);
        }

        [Test]
        public void Registry_ActiveCount_TracksTheLifecycle()
        {
            var r = new VisitorRegistry();
            var ids = new List<string>();
            for (int i = 0; i < 25; i++)
            {
                var v = VisitorInstance.CreateNew(Vector3.zero, 100f);
                r.Register(v);
                ids.Add(v.VisitorId);
                Assert.AreEqual(i + 1, r.Count);
            }
            for (int i = 0; i < 25; i += 2) r.Unregister(ids[i]);
            Assert.AreEqual(12, r.Count);
            for (int i = 0; i < r.All.Count; i++) Assert.IsTrue(r.Contains(r.All[i].VisitorId));
        }
    }
}
