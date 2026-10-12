using NUnit.Framework;
using ZooGame.Economy;
using ZooGame.Quests;

namespace ZooGame.Tests.EditMode.Quests
{
    /// <summary>Claiming: states, once-only grants, and the three reward types through the authoritative services.</summary>
    public class QuestRewardTests : QuestTestBase
    {
        QuestService Completed(params QuestRewardDefinition[] rewards)
        {
            var service = Make(true, Simple("q", QuestObjectiveType.BuildPath, 1, rewards));
            World.Paths = 1;
            service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.AreEqual(QuestState.Completed, StateOf("q"));
            return service;
        }

        // ---- Claiming ----

        [Test]
        public void CompletingAQuest_GrantsNothing_UntilClaimed()
        {
            long before = Economy.GetBalance(Currency.Money);
            Completed(Reward(QuestRewardType.Money, 500));
            Assert.AreEqual(before, Economy.GetBalance(Currency.Money));
            Assert.IsTrue(Get("q").CanClaim);
        }

        [Test]
        public void Claim_MovesToRewardClaimed_AndRaisesEventOnce()
        {
            var service = Completed(Reward(QuestRewardType.Money, 500));
            int raised = 0;
            service.RewardClaimed += _ => raised++;

            var result = service.Claim("q");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(QuestState.RewardClaimed, StateOf("q"));
            Assert.IsTrue(Get("q").IsRewardClaimed);
            Assert.IsFalse(Get("q").CanClaim);
            Assert.AreEqual(1, raised);
        }

        [Test]
        public void Claim_Twice_GrantsOnlyOnce()
        {
            var service = Completed(Reward(QuestRewardType.Money, 500), Reward(QuestRewardType.Xp, 40));
            long money = Economy.GetBalance(Currency.Money);

            Assert.IsTrue(service.Claim("q").Success);
            var second = service.Claim("q");

            Assert.IsFalse(second.Success);
            Assert.AreEqual(QuestClaimFailure.AlreadyClaimed, second.Failure);
            Assert.AreEqual(money + 500, Economy.GetBalance(Currency.Money));
            Assert.AreEqual(40, Progression.TotalXp);
        }

        [Test]
        public void Claim_Active_UnknownOrLocked_IsRefused()
        {
            var service = Make(true, Simple("a", QuestObjectiveType.BuildPath, 5, new[] { Reward(QuestRewardType.Money, 100) }),
                Simple("b", QuestObjectiveType.BuildPath, 1, null, new[] { "a" }));
            long money = Economy.GetBalance(Currency.Money);

            Assert.AreEqual(QuestClaimFailure.NotCompleted, service.Claim("a").Failure);
            Assert.AreEqual(QuestClaimFailure.NotCompleted, service.Claim("b").Failure);
            Assert.AreEqual(QuestClaimFailure.UnknownQuest, service.Claim("zzz").Failure);
            Assert.AreEqual(QuestClaimFailure.UnknownQuest, service.Claim(null).Failure);
            Assert.AreEqual(money, Economy.GetBalance(Currency.Money));
        }

        [Test]
        public void Claim_QuestWithoutRewards_Succeeds()
        {
            var service = Completed();
            Assert.IsTrue(service.Claim("q").Success);
            Assert.AreEqual(QuestState.RewardClaimed, StateOf("q"));
        }

        [Test]
        public void ReentrantClaim_DuringGrant_CannotDoubleGrant()
        {
            var service = Completed(Reward(QuestRewardType.Money, 500));
            long money = Economy.GetBalance(Currency.Money);
            // A listener that tries to claim again from inside the grant (the transaction event).
            Economy.TransactionRecorded += _ => service.Claim("q");

            service.Claim("q");

            Assert.AreEqual(money + 500, Economy.GetBalance(Currency.Money));
        }

        // ---- Reward types ----

        [Test]
        public void MoneyReward_IsAnIncomeTransaction_ThroughEconomyService()
        {
            var service = Completed(Reward(QuestRewardType.Money, 750));
            long before = Economy.GetBalance(Currency.Money);

            service.Claim("q");

            Assert.AreEqual(before + 750, Economy.GetBalance(Currency.Money));
            Assert.IsTrue(Economy.TryGetLastTransaction(out var record));
            Assert.AreEqual(Currency.Money, record.Currency);
            Assert.AreEqual(TransactionType.Income, record.Type);
            Assert.AreEqual(TransactionCategory.QuestReward, record.Category);
            Assert.AreEqual("q", record.SourceId);
            Assert.AreEqual(750, Economy.GetLifetimeIncome(Currency.Money));
        }

        [Test]
        public void ConservationTokenReward_IsGrantedThroughEconomyService()
        {
            var service = Completed(Reward(QuestRewardType.ConservationTokens, 5));
            Assert.AreEqual(0, Economy.GetBalance(Currency.ConservationTokens));

            service.Claim("q");

            Assert.AreEqual(5, Economy.GetBalance(Currency.ConservationTokens));
            Assert.AreEqual(5, Economy.State.LifetimeConservationTokensEarned);
            Assert.IsTrue(Economy.TryGetLastTransaction(out var record));
            Assert.AreEqual(Currency.ConservationTokens, record.Currency);
            Assert.AreEqual(TransactionCategory.QuestReward, record.Category);
        }

        [Test]
        public void XpReward_IsGrantedThroughProgressionService_AndRaisesLevel()
        {
            var service = Completed(Reward(QuestRewardType.Xp, 120));
            int levelEvents = 0;
            Progression.LevelChanged += (_, __) => levelEvents++;

            service.Claim("q");

            Assert.AreEqual(120, Progression.TotalXp);
            Assert.AreEqual(2, Progression.Level);
            Assert.AreEqual(1, levelEvents);
        }

        [Test]
        public void MixedRewards_AreAllApplied()
        {
            var service = Completed(Reward(QuestRewardType.Money, 100), Reward(QuestRewardType.ConservationTokens, 2),
                Reward(QuestRewardType.Xp, 30));
            long money = Economy.GetBalance(Currency.Money);

            service.Claim("q");

            Assert.AreEqual(money + 100, Economy.GetBalance(Currency.Money));
            Assert.AreEqual(2, Economy.GetBalance(Currency.ConservationTokens));
            Assert.AreEqual(30, Progression.TotalXp);
        }

        [Test]
        public void QuestRewardMoney_IsNotCountedAsEarnedMoney()
        {
            // The bridge forwards income as MoneyEarned except refunds, debug grants and quest rewards.
            var service = Make(true, Def("earn", new[] { Obj("o", QuestObjectiveType.EarnMoney, 10) }));
            var sources = new QuestEventSources { Economy = Economy };
            using (new QuestEventBridge(service, sources))
            {
                Economy.AddIncome(Currency.Money, 5, TransactionCategory.QuestReward, "x");
                Economy.AddIncome(Currency.Money, 5, TransactionCategory.DemolitionRefund, "x");
                Economy.AddIncome(Currency.Money, 5, TransactionCategory.DebugGrant, "x");
                Assert.AreEqual(0, Get("earn").Objectives[0].CurrentValue);

                Economy.AddIncome(Currency.Money, 6, TransactionCategory.EntranceTicket, "gate");
                Economy.AddIncome(Currency.Money, 4, TransactionCategory.FoodSale, "stall");
            }
            Assert.AreEqual(QuestState.Completed, StateOf("earn"));
        }

        // ---- Failure leaves the reward claimable ----

        [Test]
        public void RewardsThatCannotBeApplied_AreNotGranted_AndQuestStaysClaimable()
        {
            // No progression service: the XP reward cannot be applied, so nothing at all is granted.
            var service = new QuestService(
                new QuestLibrary(new[] { Simple("q", QuestObjectiveType.BuildPath, 1,
                    new[] { Reward(QuestRewardType.Money, 100), Reward(QuestRewardType.Xp, 10) }) }),
                World, new QuestRewardService(Economy, null));
            Quests = service;
            World.Paths = 1;
            service.Notify(Ev(QuestEventKind.PathsChanged));
            long money = Economy.GetBalance(Currency.Money);

            var result = service.Claim("q");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(QuestClaimFailure.RewardFailed, result.Failure);
            Assert.AreEqual(QuestRewardFailure.NoProgression, result.RewardFailure);
            Assert.AreEqual(money, Economy.GetBalance(Currency.Money));
            Assert.AreEqual(QuestState.Completed, StateOf("q"));
        }

        [Test]
        public void ClaimWithoutRewardService_RefusesQuestsThatHaveRewards()
        {
            var service = new QuestService(
                new QuestLibrary(new[] { Simple("q", QuestObjectiveType.BuildPath, 1, new[] { Reward(QuestRewardType.Money, 1) }) }),
                World, null);
            Quests = service;
            World.Paths = 1;
            service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.AreEqual(QuestClaimFailure.RewardFailed, service.Claim("q").Failure);
            Assert.AreEqual(QuestState.Completed, StateOf("q"));
        }
    }
}
