using System.Collections.Generic;
using NUnit.Framework;
using ZooGame.Quests;

namespace ZooGame.Tests.EditMode.Quests
{
    /// <summary>Id lookup, prerequisite locking and unlocking, level/star gates, state transitions.</summary>
    public class QuestLifecycleTests : QuestTestBase
    {
        // ---- Lookup ----

        [Test]
        public void QuestId_Lookup_FindsDefinitionsAndInstances()
        {
            var a = Simple("a");
            var b = Simple("b");
            var service = Make(true, a, b);

            Assert.IsTrue(service.Library.TryGet("a", out var found));
            Assert.AreSame(a, found);
            Assert.IsTrue(service.TryGet("b", out var instance));
            Assert.AreEqual("b", instance.QuestId);
            Assert.AreSame(b, instance.Definition);
            Assert.IsFalse(service.TryGet("missing", out _));
            Assert.IsFalse(service.TryGet(null, out _));
            Assert.AreEqual(2, service.Count);
        }

        [Test]
        public void Library_SkipsInvalidDefinitions_AndReportsProblems()
        {
            var good = Simple("good");
            var dup = Simple("good");
            var noObjectives = Def("empty", new QuestObjectiveDefinition[0]);
            var noId = Simple("");
            var badPrereq = Simple("orphan", prerequisites: new[] { "nope" });

            var lib = new QuestLibrary(new[] { good, dup, noObjectives, noId, badPrereq, null });

            Assert.AreEqual(2, lib.Count); // good + orphan
            Assert.IsTrue(lib.Contains("good"));
            Assert.IsTrue(lib.Contains("orphan"));
            Assert.IsFalse(lib.Contains("empty"));
            Assert.GreaterOrEqual(lib.Problems.Count, 4);
        }

        [Test]
        public void QuestWithUnknownPrerequisite_NeverUnlocks()
        {
            var service = Make(true, Simple("orphan", prerequisites: new[] { "nope" }));
            Assert.AreEqual(QuestState.Locked, StateOf("orphan"));
            service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.AreEqual(QuestState.Locked, StateOf("orphan"));
        }

        // ---- Prerequisites ----

        [Test]
        public void NoPrerequisites_QuestIsImmediatelyActive()
        {
            Make(true, Simple("a"));
            Assert.AreEqual(QuestState.Active, StateOf("a"));
        }

        [Test]
        public void Prerequisite_KeepsQuestLockedUntilItIsCompleted()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 3);
            var b = Simple("b", QuestObjectiveType.BuildFence, 1, prerequisites: new[] { "a" });
            var service = Make(true, a, b);

            Assert.AreEqual(QuestState.Active, StateOf("a"));
            Assert.AreEqual(QuestState.Locked, StateOf("b"));

            World.Paths = 3;
            service.Notify(Ev(QuestEventKind.PathsChanged));

            Assert.AreEqual(QuestState.Completed, StateOf("a"));
            Assert.AreEqual(QuestState.Active, StateOf("b"));
        }

        [Test]
        public void LockedQuest_IsNotCompletedByEvents()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 100);
            var b = Simple("b", QuestObjectiveType.BuildFence, 1, prerequisites: new[] { "a" });
            var service = Make(true, a, b);

            World.Fences = 5; // would satisfy b if it were tracked
            service.Notify(Ev(QuestEventKind.FencesChanged));

            Assert.AreEqual(QuestState.Locked, StateOf("b"));
            Assert.AreEqual(0, Get("b").Objectives[0].CurrentValue);
        }

        [Test]
        public void ClaimedPrerequisite_AlsoSatisfiesDependents()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 1);
            var b = Simple("b", QuestObjectiveType.BuildFence, 1, prerequisites: new[] { "a" });
            var service = Make(true, a, b);
            World.Paths = 1;
            service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.IsTrue(service.Claim("a").Success);
            Assert.AreEqual(QuestState.RewardClaimed, StateOf("a"));
            Assert.AreEqual(QuestState.Active, StateOf("b"));
        }

        [Test]
        public void MultiplePrerequisites_AllRequired()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 1);
            var b = Simple("b", QuestObjectiveType.BuildFence, 1);
            var c = Simple("c", QuestObjectiveType.CreateEnclosure, 1, prerequisites: new[] { "a", "b" });
            var service = Make(true, a, b, c);

            World.Paths = 1;
            service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.AreEqual(QuestState.Locked, StateOf("c"));

            World.Fences = 1;
            service.Notify(Ev(QuestEventKind.FencesChanged));
            Assert.AreEqual(QuestState.Active, StateOf("c"));
        }

        // ---- Level and star gates ----

        [Test]
        public void MinimumLevel_GatesUnlock_UntilLevelReached()
        {
            var service = Make(true, Simple("gated", level: 3));
            Assert.AreEqual(QuestState.Locked, StateOf("gated"));

            World.LevelCount = 2;
            service.Notify(Ev(QuestEventKind.LevelChanged));
            Assert.AreEqual(QuestState.Locked, StateOf("gated"));

            World.LevelCount = 3;
            service.Notify(Ev(QuestEventKind.LevelChanged));
            Assert.AreEqual(QuestState.Active, StateOf("gated"));
        }

        [Test]
        public void MinimumStars_GatesUnlock_UntilRatingReached()
        {
            var service = Make(true, Simple("gated", stars: 2));
            Assert.AreEqual(QuestState.Locked, StateOf("gated"));

            World.StarCount = 2;
            service.Notify(Ev(QuestEventKind.RatingChanged));
            Assert.AreEqual(QuestState.Active, StateOf("gated"));
        }

        [Test]
        public void LevelAndStars_BothRequired()
        {
            var service = Make(true, Simple("gated", level: 2, stars: 2));
            World.LevelCount = 2;
            service.Notify(Ev(QuestEventKind.LevelChanged));
            Assert.AreEqual(QuestState.Locked, StateOf("gated"));
            World.StarCount = 2;
            service.Notify(Ev(QuestEventKind.RatingChanged));
            Assert.AreEqual(QuestState.Active, StateOf("gated"));
        }

        // ---- States ----

        [Test]
        public void WithoutAutoStart_UnlockedQuestWaitsInAvailable_UntilStarted()
        {
            var service = Make(false, Simple("a", QuestObjectiveType.BuildPath, 1));
            Assert.AreEqual(QuestState.Available, StateOf("a"));

            World.Paths = 1;
            service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.AreEqual(QuestState.Available, StateOf("a"), "not tracked before it is started");

            Assert.IsTrue(service.Start("a"));
            Assert.AreEqual(QuestState.Completed, StateOf("a"), "picks up what already exists");
            Assert.IsFalse(service.Start("a"));
        }

        [Test]
        public void StateTransitions_AreRaisedInOrder()
        {
            var service = Make(true, Simple("a", QuestObjectiveType.BuildPath, 1));
            var seen = new List<QuestState>();
            service.StateChanged += (q, previous) => seen.Add(q.State);

            World.Paths = 1;
            service.Notify(Ev(QuestEventKind.PathsChanged));
            service.Claim("a");

            Assert.AreEqual(new[] { QuestState.Completed, QuestState.RewardClaimed }, seen.ToArray());
        }

        [Test]
        public void CopyByState_ListsMatchingQuestsInCatalogueOrder()
        {
            var service = Make(true, Simple("a"), Simple("b"), Simple("c", prerequisites: new[] { "a" }));
            var list = new List<QuestInstance>();
            service.CopyByState(QuestState.Active, list);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("a", list[0].QuestId);
            Assert.AreEqual("b", list[1].QuestId);
            service.CopyByState(QuestState.Locked, list);
            Assert.AreEqual(1, list.Count);
        }

        [Test]
        public void Tracked_PrefersPinned_ThenClaimable_ThenActive()
        {
            var service = Make(true, Simple("a", QuestObjectiveType.BuildPath, 1), Simple("b", QuestObjectiveType.BuildFence, 5));
            Assert.IsTrue(service.TryGetTracked(out var q));
            Assert.AreEqual("a", q.QuestId);

            Assert.IsTrue(service.SetPinned("b"));
            Assert.IsTrue(service.TryGetTracked(out q));
            Assert.AreEqual("b", q.QuestId);

            Assert.IsFalse(service.SetPinned("missing"));
            service.SetPinned(null);
            World.Paths = 1;
            service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.IsTrue(service.TryGetTracked(out q));
            Assert.AreEqual("a", q.QuestId, "claimable first");
        }
    }
}
