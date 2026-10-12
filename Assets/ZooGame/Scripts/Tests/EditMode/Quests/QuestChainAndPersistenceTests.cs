using NUnit.Framework;
using UnityEngine;
using ZooGame.Economy;
using ZooGame.Quests;

namespace ZooGame.Tests.EditMode.Quests
{
    /// <summary>Prerequisite chains (including the starter chain) and save/restore reconstruction.</summary>
    public class QuestChainAndPersistenceTests : QuestTestBase
    {
        // ---- Chains ----

        [Test]
        public void Chain_UnlocksOneQuestAtATime()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 1);
            var b = Simple("b", QuestObjectiveType.BuildFence, 1, prerequisites: new[] { "a" });
            var c = Simple("c", QuestObjectiveType.CreateEnclosure, 1, prerequisites: new[] { "b" });
            var service = Make(true, a, b, c);

            Assert.AreEqual(QuestState.Active, StateOf("a"));
            Assert.AreEqual(QuestState.Locked, StateOf("b"));
            Assert.AreEqual(QuestState.Locked, StateOf("c"));

            World.Paths = 1; service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.AreEqual(QuestState.Completed, StateOf("a"));
            Assert.AreEqual(QuestState.Active, StateOf("b"));
            Assert.AreEqual(QuestState.Locked, StateOf("c"));

            World.Fences = 1; service.Notify(Ev(QuestEventKind.FencesChanged));
            Assert.AreEqual(QuestState.Active, StateOf("c"));
        }

        [Test]
        public void Chain_WithExistingProgress_CascadesThroughSeveralQuestsAtOnce()
        {
            World.Paths = 1; World.Fences = 1; World.Enclosures = 1;
            var a = Simple("a", QuestObjectiveType.BuildPath, 1);
            var b = Simple("b", QuestObjectiveType.BuildFence, 1, prerequisites: new[] { "a" });
            var c = Simple("c", QuestObjectiveType.CreateEnclosure, 1, prerequisites: new[] { "b" });
            var d = Simple("d", QuestObjectiveType.ReachVisitorCount, 3, prerequisites: new[] { "c" });

            Make(true, a, b, c, d);

            Assert.AreEqual(QuestState.Completed, StateOf("a"));
            Assert.AreEqual(QuestState.Completed, StateOf("b"));
            Assert.AreEqual(QuestState.Completed, StateOf("c"));
            Assert.AreEqual(QuestState.Active, StateOf("d"));
        }

        [Test]
        public void ClaimedXp_RaisesLevel_AndUnlocksALevelGatedQuest()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 1, new[] { Reward(QuestRewardType.Xp, 100) });
            var b = Simple("b", QuestObjectiveType.BuildFence, 1, level: 2);
            // The level is read from the real progression service; the bridge forwards its LevelChanged event.
            var world = new LevelBackedWorld(World, Progression);
            var service = new QuestService(new QuestLibrary(new[] { a, b }), world, new QuestRewardService(Economy, Progression));
            Quests = service;

            using (new QuestEventBridge(service, new QuestEventSources { Progression = Progression }))
            {
                World.Paths = 1;
                service.Notify(Ev(QuestEventKind.PathsChanged));
                Assert.AreEqual(QuestState.Locked, StateOf("b"));

                service.Claim("a"); // +100 XP -> level 2 -> LevelChanged -> unlock
                Assert.AreEqual(2, Progression.Level);
                Assert.AreEqual(QuestState.Active, StateOf("b"));
            }
        }

        // The fake world with the level read from the real progression service.
        sealed class LevelBackedWorld : ZooGame.Quests.IQuestWorld
        {
            readonly FakeQuestWorld _inner;
            readonly ZooGame.Progression.ProgressionService _progression;

            public LevelBackedWorld(FakeQuestWorld inner, ZooGame.Progression.ProgressionService progression)
            {
                _inner = inner;
                _progression = progression;
            }

            public int CountPlaced(string id, bool byCategory, ZooGame.Data.PlaceableCategory c) => _inner.CountPlaced(id, byCategory, c);
            public int PathCellCount => _inner.PathCellCount;
            public int FenceSegmentCount => _inner.FenceSegmentCount;
            public int EnclosureCount => _inner.EnclosureCount;
            public int CountAnimals(string s) => _inner.CountAnimals(s);
            public int CountSpecies() => _inner.CountSpecies();
            public int AverageWelfare => _inner.AverageWelfare;
            public int VisitorCount => _inner.VisitorCount;
            public int VisitorHappiness => _inner.VisitorHappiness;
            public int CountStaff(string r) => _inner.CountStaff(r);
            public int Stars => _inner.Stars;
            public int Level => _progression.Level;
        }

        // ---- Starter chain ----

        [Test]
        public void StarterChain_RunsFromFoodStallToTwoStars()
        {
            var defs = StarterQuests.Create();
            Own(defs);
            var service = Make(true, defs);
            Assert.AreEqual(7, service.Count);
            Assert.AreEqual(0, service.Library.Problems.Count);

            // Only the first quest is available at the start.
            Assert.AreEqual(QuestState.Active, StateOf(StarterQuests.PlaceFoodStall));
            Assert.AreEqual(QuestState.Locked, StateOf(StarterQuests.BuildPath));

            World.Placed[StarterQuests.FoodStallId] = 1;
            service.Notify(Ev(QuestEventKind.ObjectsChanged));
            Assert.AreEqual(QuestState.Completed, StateOf(StarterQuests.PlaceFoodStall));
            Assert.AreEqual(QuestState.Active, StateOf(StarterQuests.BuildPath));

            World.Paths = 5;
            service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.AreEqual(QuestState.Active, StateOf(StarterQuests.CreateEnclosure));

            World.Enclosures = 1;
            service.Notify(Ev(QuestEventKind.EnclosuresChanged));
            Assert.AreEqual(QuestState.Active, StateOf(StarterQuests.AdoptAnimal));

            service.Notify(Ev(QuestEventKind.AnimalAdopted, "rabbit", 1));
            Assert.AreEqual(QuestState.Active, StateOf(StarterQuests.ReachVisitors));

            World.Visitors = 10;
            service.Notify(Ev(QuestEventKind.VisitorsChanged));
            Assert.AreEqual(QuestState.Active, StateOf(StarterQuests.HireKeeper));

            World.Staff["Keeper"] = 1;
            service.Notify(Ev(QuestEventKind.StaffChanged));
            Assert.AreEqual(QuestState.Active, StateOf(StarterQuests.ReachTwoStars));

            World.StarCount = 2;
            service.Notify(Ev(QuestEventKind.RatingChanged));
            Assert.AreEqual(QuestState.Completed, StateOf(StarterQuests.ReachTwoStars));

            // Claim everything: all seven rewards arrive exactly once.
            long money = Economy.GetBalance(Currency.Money);
            for (int i = 0; i < defs.Length; i++) Assert.IsTrue(service.Claim(defs[i].QuestId).Success, defs[i].QuestId);
            for (int i = 0; i < defs.Length; i++) Assert.IsFalse(service.Claim(defs[i].QuestId).Success);

            Assert.AreEqual(money + 500 + 500 + 750 + 1000 + 1000 + 1000 + 2500, Economy.GetBalance(Currency.Money));
            Assert.AreEqual(5, Economy.GetBalance(Currency.ConservationTokens));
            Assert.AreEqual(25 + 25 + 50 + 75 + 100 + 100 + 200, Progression.TotalXp);
        }

        // ---- Persistence ----

        [Test]
        public void SaveData_HoldsIdsStatesAndProgress_NotDefinitionContent()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 4, new[] { Reward(QuestRewardType.Money, 777) });
            var service = Make(true, a);
            World.Paths = 2;
            service.Notify(Ev(QuestEventKind.PathsChanged));

            string json = JsonUtility.ToJson(service.Capture());

            StringAssert.Contains("\"a\"", json);
            StringAssert.Contains("a-o", json, "objective id is saved");
            StringAssert.DoesNotContain("Title a", json);
            StringAssert.DoesNotContain("Description a", json);
            StringAssert.DoesNotContain("777", json);
        }

        [Test]
        public void Restore_RebuildsStates_Progress_AndClaimedFlags_FromJson()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 1, new[] { Reward(QuestRewardType.Money, 100) });
            var b = Def("b", new[] { Obj("x", QuestObjectiveType.BuildFence, 10), Obj("y", QuestObjectiveType.CreateEnclosure, 2) },
                null, new[] { "a" });
            var c = Simple("c", QuestObjectiveType.ReachVisitorCount, 3, prerequisites: new[] { "b" });
            var original = Make(true, a, b, c);

            World.Paths = 1; original.Notify(Ev(QuestEventKind.PathsChanged));
            original.Claim("a");
            World.Fences = 6; original.Notify(Ev(QuestEventKind.FencesChanged));
            original.SetPinned("b");
            string json = JsonUtility.ToJson(original.Capture());

            // A fresh process: new service over the same definitions, then restore.
            World = new FakeQuestWorld();
            var restored = NewService(true, a, b, c);
            Quests = restored;
            Assert.IsTrue(restored.Restore(JsonUtility.FromJson<QuestSaveData>(json)));

            Assert.AreEqual(QuestState.RewardClaimed, StateOf("a"));
            Assert.IsTrue(Get("a").IsRewardClaimed);
            Assert.AreEqual(QuestState.Active, StateOf("b"));
            Assert.AreEqual(6, Get("b").Objectives[0].CurrentValue);
            Assert.AreEqual(0, Get("b").Objectives[1].CurrentValue);
            Assert.AreEqual(10, Get("b").Objectives[0].TargetValue, "target comes from the definition");
            Assert.AreEqual(QuestState.Locked, StateOf("c"));
            Assert.AreEqual("b", restored.PinnedQuestId);
        }

        [Test]
        public void Restore_ContinuesTracking_AfterwardsUnlocksAndCompletes()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 5);
            var b = Simple("b", QuestObjectiveType.BuildFence, 1, prerequisites: new[] { "a" });
            var first = Make(true, a, b);
            World.Paths = 3; first.Notify(Ev(QuestEventKind.PathsChanged));
            var data = JsonUtility.FromJson<QuestSaveData>(JsonUtility.ToJson(first.Capture()));

            var second = NewService(true, a, b);
            Quests = second;
            second.Restore(data);
            Assert.AreEqual(3, Get("a").Objectives[0].CurrentValue);

            World.Paths = 5; second.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.AreEqual(QuestState.Completed, StateOf("a"));
            Assert.AreEqual(QuestState.Active, StateOf("b"));
        }

        [Test]
        public void Restore_NeverRegrantsAClaimedReward()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 1, new[] { Reward(QuestRewardType.Money, 500) });
            var first = Make(true, a);
            World.Paths = 1; first.Notify(Ev(QuestEventKind.PathsChanged));
            first.Claim("a");
            long money = Economy.GetBalance(Currency.Money);
            var data = JsonUtility.FromJson<QuestSaveData>(JsonUtility.ToJson(first.Capture()));

            var second = NewService(true, a);
            Quests = second;
            second.Restore(data);

            Assert.AreEqual(money, Economy.GetBalance(Currency.Money), "restoring grants nothing");
            Assert.AreEqual(QuestClaimFailure.AlreadyClaimed, second.Claim("a").Failure);
            Assert.AreEqual(money, Economy.GetBalance(Currency.Money));
        }

        [Test]
        public void Restore_CompletedButUnclaimed_StaysClaimable_Once()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 1, new[] { Reward(QuestRewardType.Money, 500) });
            var first = Make(true, a);
            World.Paths = 1; first.Notify(Ev(QuestEventKind.PathsChanged));
            var data = JsonUtility.FromJson<QuestSaveData>(JsonUtility.ToJson(first.Capture()));

            World = new FakeQuestWorld(); // the restored world has nothing built; progress is still kept
            var second = NewService(true, a);
            Quests = second;
            second.Restore(data);

            Assert.AreEqual(QuestState.Completed, StateOf("a"));
            Assert.IsTrue(second.Claim("a").Success);
            Assert.IsFalse(second.Claim("a").Success);
        }

        [Test]
        public void Restore_DropsUnknownQuests_ClampsProgress_AndUnlocksQuestsTheSaveDidNotKnow()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 5);
            var added = Simple("added-later", QuestObjectiveType.BuildFence, 1, prerequisites: new[] { "a" });

            // A save written by an older version: one quest that no longer exists, and progress beyond the target.
            const string json = "{\"version\":1,\"pinnedQuestId\":\"\",\"quests\":["
                + "{\"questId\":\"stale\",\"state\":2,\"objectives\":[],\"rewardClaimed\":false},"
                + "{\"questId\":\"a\",\"state\":2,\"objectives\":[{\"objectiveId\":\"a-o\",\"currentValue\":999}],\"rewardClaimed\":false}]}";

            var service = Make(true, a, added);
            Assert.IsTrue(service.Restore(JsonUtility.FromJson<QuestSaveData>(json)));

            Assert.AreEqual(2, service.Count);
            Assert.IsFalse(service.TryGet("stale", out _));
            Assert.AreEqual(5, Get("a").Objectives[0].CurrentValue, "clamped to the target");
            Assert.AreEqual(QuestState.Completed, StateOf("a"));
            Assert.AreEqual(QuestState.Active, StateOf("added-later"));
            Assert.IsNull(service.PinnedQuestId);
        }

        [Test]
        public void Restore_ClaimedFlag_WinsOverAnEarlierState()
        {
            var a = Simple("a", QuestObjectiveType.BuildPath, 1, new[] { Reward(QuestRewardType.Money, 500) });
            const string json = "{\"version\":1,\"pinnedQuestId\":\"\",\"quests\":["
                + "{\"questId\":\"a\",\"state\":2,\"objectives\":[],\"rewardClaimed\":true}]}";
            var service = Make(true, a);
            long money = Economy.GetBalance(Currency.Money);

            service.Restore(JsonUtility.FromJson<QuestSaveData>(json));

            Assert.AreEqual(QuestState.RewardClaimed, StateOf("a"));
            Assert.IsFalse(service.Claim("a").Success);
            Assert.AreEqual(money, Economy.GetBalance(Currency.Money));
        }

        [Test]
        public void Restore_RejectsNull()
        {
            var service = Make(true, Simple("a"));
            Assert.IsFalse(service.Restore(null));
            Assert.AreEqual(QuestState.Active, StateOf("a"));
        }

        [Test]
        public void Capture_IsACopy_NotALiveView()
        {
            var service = Make(true, Simple("a", QuestObjectiveType.BuildPath, 5));
            var snapshot = service.Capture();
            World.Paths = 4; service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.AreEqual(0, snapshot.Quests[0].Objectives[0].CurrentValue);
            Assert.AreEqual(4, Get("a").Objectives[0].CurrentValue);
        }
    }
}
