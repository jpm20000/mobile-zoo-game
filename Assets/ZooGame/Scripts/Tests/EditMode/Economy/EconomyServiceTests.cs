using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Economy;

namespace ZooGame.Tests.EditMode.Economy
{
    /// <summary>The dual-currency core: balances, validation, lifetime totals, records, bounded history, persistence data.</summary>
    public class EconomyServiceTests
    {
        static EconomySettings Settings(int history = 100, long money = 25000, long tokens = 0, float refund = 0.5f) =>
            new EconomySettings(money, tokens, 10, 6, 4, refund, history);

        static EconomyService Make(EconomySettings? settings = null, System.Func<double> time = null) =>
            new EconomyService(settings ?? Settings(), time);

        // ---- Starting balances ----

        [Test]
        public void StartingMoney_DefaultsTo25000()
        {
            var e = new EconomyService(EconomySettings.Default);
            Assert.AreEqual(25000, e.GetBalance(Currency.Money));
        }

        [Test]
        public void StartingConservationTokens_DefaultToZero()
        {
            var e = new EconomyService(EconomySettings.Default);
            Assert.AreEqual(0, e.GetBalance(Currency.ConservationTokens));
        }

        [Test]
        public void StartingBalances_ComeFromSettings_NotFromCode()
        {
            var e = Make(Settings(money: 500, tokens: 7));
            Assert.AreEqual(500, e.GetBalance(Currency.Money));
            Assert.AreEqual(7, e.GetBalance(Currency.ConservationTokens));
            Assert.AreEqual(0, e.State.LifetimeMoneyIncome, "starting funds are not income");
        }

        [Test]
        public void EconomyConfig_DefaultsMatchTheSpec()
        {
            var config = EconomyConfig.CreateDefault();
            try
            {
                var s = config.ToSettings();
                Assert.AreEqual(25000, s.StartingMoney);
                Assert.AreEqual(0, s.StartingConservationTokens);
                Assert.AreEqual(10, s.EntranceTicketPrice);
                Assert.AreEqual(6, s.FoodPrice);
                Assert.AreEqual(4, s.DrinkPrice);
                Assert.AreEqual(5000, s.RefundBasisPoints);
                Assert.AreEqual(100, s.HistoryCapacity);
            }
            finally { Object.DestroyImmediate(config); }
        }

        // ---- Money ----

        [Test]
        public void MoneyIncome_RaisesBalance_AndRecordsTheTransaction()
        {
            var e = Make();
            var r = e.AddIncome(Currency.Money, 100, TransactionCategory.EntranceTicket, "entrance-1");
            Assert.IsTrue(r.Success);
            Assert.AreEqual(25100, e.GetBalance(Currency.Money));
            Assert.AreEqual(100, e.State.LifetimeMoneyIncome);
            Assert.AreEqual(1, e.HistoryCount);
        }

        [Test]
        public void MoneyExpense_LowersBalance_AndCountsAsLifetimeExpense()
        {
            var e = Make();
            var r = e.TrySpend(Currency.Money, 300, TransactionCategory.BuildingPurchase, "shop");
            Assert.IsTrue(r.Success);
            Assert.AreEqual(24700, e.GetBalance(Currency.Money));
            Assert.AreEqual(300, e.State.LifetimeMoneyExpenses);
            Assert.AreEqual(0, e.State.LifetimeMoneyIncome);
        }

        [Test]
        public void SpendingExactlyTheBalance_Succeeds()
        {
            var e = Make(Settings(money: 50));
            Assert.IsTrue(e.TrySpend(Currency.Money, 50, TransactionCategory.BuildingPurchase, "x").Success);
            Assert.AreEqual(0, e.GetBalance(Currency.Money));
        }

        // ---- Conservation Tokens through the same service ----

        [Test]
        public void TokenIncome_GoesThroughTheService_AndUpdatesTokenTotalsOnly()
        {
            var e = Make();
            var r = e.AddIncome(Currency.ConservationTokens, 3, TransactionCategory.ConservationReward, "release-1");
            Assert.IsTrue(r.Success);
            Assert.AreEqual(3, e.GetBalance(Currency.ConservationTokens));
            Assert.AreEqual(3, e.State.LifetimeConservationTokensEarned);
            Assert.AreEqual(0, e.State.LifetimeConservationTokensSpent);
            Assert.AreEqual(25000, e.GetBalance(Currency.Money));
            Assert.AreEqual(0, e.State.LifetimeMoneyIncome);
        }

        [Test]
        public void TokenExpense_GoesThroughTheService_AndUpdatesTokenTotalsOnly()
        {
            var e = Make(Settings(tokens: 10));
            var r = e.TrySpend(Currency.ConservationTokens, 4, TransactionCategory.ConservationUnlock, "unlock-1");
            Assert.IsTrue(r.Success);
            Assert.AreEqual(6, e.GetBalance(Currency.ConservationTokens));
            Assert.AreEqual(4, e.State.LifetimeConservationTokensSpent);
            Assert.AreEqual(25000, e.GetBalance(Currency.Money));
            Assert.AreEqual(0, e.State.LifetimeMoneyExpenses);
        }

        [Test]
        public void TokenSpend_WithoutTokens_IsRejected()
        {
            var e = Make();
            var r = e.TrySpend(Currency.ConservationTokens, 1, TransactionCategory.ConservationUnlock, "x");
            Assert.IsFalse(r.Success);
            Assert.AreEqual(TransactionFailure.InsufficientFunds, r.Failure);
            Assert.AreEqual(0, e.GetBalance(Currency.ConservationTokens));
        }

        [Test]
        public void Currencies_AreSeparate_PlentyOfMoneyDoesNotPayInTokens()
        {
            var e = Make(Settings(money: 1000000, tokens: 2));
            Assert.IsFalse(e.CanAfford(Currency.ConservationTokens, 3));
            Assert.IsFalse(e.TrySpend(Currency.ConservationTokens, 3, TransactionCategory.ConservationUnlock, "x").Success);
            Assert.AreEqual(1000000, e.GetBalance(Currency.Money));
            Assert.IsTrue(e.CanAfford(Currency.Money, 3));
        }

        [Test]
        public void DebugGrant_CanGiveTokens_ForTesting()
        {
            var e = Make();
            Assert.IsTrue(e.DebugGrant(Currency.ConservationTokens, 5).Success);
            Assert.AreEqual(5, e.GetBalance(Currency.ConservationTokens));
            e.TryGetLastTransaction(out var last);
            Assert.AreEqual(TransactionCategory.DebugGrant, last.Category);
        }

        // ---- Rejections ----

        [Test]
        public void InsufficientFunds_IsRejected_ChangesNothing_AndIsReported()
        {
            var e = Make(Settings(money: 100));
            InsufficientFundsInfo info = default;
            int reports = 0;
            e.InsufficientFunds += i => { info = i; reports++; };
            int recorded = 0;
            e.TransactionRecorded += _ => recorded++;

            var r = e.TrySpend(Currency.Money, 101, TransactionCategory.BuildingPurchase, "big");

            Assert.IsFalse(r.Success);
            Assert.AreEqual(TransactionFailure.InsufficientFunds, r.Failure);
            Assert.AreEqual(100, e.GetBalance(Currency.Money));
            Assert.AreEqual(0, e.State.LifetimeMoneyExpenses);
            Assert.AreEqual(0, e.HistoryCount);
            Assert.AreEqual(0, recorded);
            Assert.AreEqual(1, reports);
            Assert.AreEqual(101, info.Needed);
            Assert.AreEqual(100, info.Available);
            Assert.AreEqual(1, info.Shortfall);
            Assert.AreEqual("big", info.SourceId);
        }

        [Test]
        public void NegativeAmounts_AreRejected_ForIncomeAndExpense_InBothCurrencies()
        {
            var e = Make(Settings(tokens: 10));
            foreach (var currency in new[] { Currency.Money, Currency.ConservationTokens })
            {
                var income = e.AddIncome(currency, -5, TransactionCategory.Donation, "x");
                var expense = e.TrySpend(currency, -5, TransactionCategory.BuildingPurchase, "x");
                Assert.AreEqual(TransactionFailure.InvalidAmount, income.Failure);
                Assert.AreEqual(TransactionFailure.InvalidAmount, expense.Failure);
            }
            Assert.AreEqual(25000, e.GetBalance(Currency.Money));
            Assert.AreEqual(10, e.GetBalance(Currency.ConservationTokens));
            Assert.AreEqual(0, e.HistoryCount);
        }

        [Test]
        public void ZeroAmount_IsMalformed_AndRecordsNothing()
        {
            var e = Make();
            Assert.AreEqual(TransactionFailure.InvalidAmount, e.AddIncome(Currency.Money, 0, TransactionCategory.Donation, "x").Failure);
            Assert.AreEqual(TransactionFailure.InvalidAmount, e.TrySpend(Currency.Money, 0, TransactionCategory.BuildingPurchase, "x").Failure);
            Assert.AreEqual(0, e.HistoryCount);
        }

        [Test]
        public void InvalidCurrency_IsRejected()
        {
            var e = Make();
            Assert.AreEqual(TransactionFailure.InvalidCurrency, e.AddIncome(Currency.None, 5, TransactionCategory.Donation, "x").Failure);
            Assert.AreEqual(TransactionFailure.InvalidCurrency, e.TrySpend((Currency)99, 5, TransactionCategory.BuildingPurchase, "x").Failure);
            Assert.IsFalse(e.CanAfford(Currency.None, 1));
            Assert.AreEqual(0, e.GetBalance(Currency.None));
            Assert.AreEqual(0, e.HistoryCount);
        }

        [Test]
        public void MalformedCategories_AreRejected()
        {
            var e = Make();
            Assert.AreEqual(TransactionFailure.InvalidCategory, e.AddIncome(Currency.Money, 5, TransactionCategory.None, "x").Failure);
            Assert.AreEqual(TransactionFailure.InvalidCategory, e.TrySpend(Currency.Money, 5, TransactionCategory.None, "x").Failure);
            Assert.AreEqual(TransactionFailure.InvalidCategory, e.AddIncome(Currency.Money, 5, TransactionCategory.BuildingPurchase, "x").Failure,
                "an expense category cannot be income");
            Assert.AreEqual(TransactionFailure.InvalidCategory, e.TrySpend(Currency.Money, 5, TransactionCategory.EntranceTicket, "x").Failure,
                "an income category cannot be an expense");
            Assert.AreEqual(TransactionFailure.InvalidCategory, e.AddIncome(Currency.Money, 5, (TransactionCategory)999, "x").Failure);
            Assert.AreEqual(25000, e.GetBalance(Currency.Money));
        }

        [Test]
        public void Overflow_IsRejected_AndLeavesTheBalanceAlone()
        {
            var e = Make(Settings(money: long.MaxValue - 1));
            var r = e.AddIncome(Currency.Money, 5, TransactionCategory.Donation, "x");
            Assert.AreEqual(TransactionFailure.Overflow, r.Failure);
            Assert.AreEqual(long.MaxValue - 1, e.GetBalance(Currency.Money));
        }

        // ---- Lifetime totals ----

        [Test]
        public void LifetimeTotals_AccumulateAcrossTransactions_PerCurrency()
        {
            var e = Make(Settings(tokens: 20));
            e.AddIncome(Currency.Money, 10, TransactionCategory.EntranceTicket, "a");
            e.AddIncome(Currency.Money, 6, TransactionCategory.FoodSale, "b");
            e.TrySpend(Currency.Money, 100, TransactionCategory.BuildingPurchase, "c");
            e.TrySpend(Currency.Money, 25, TransactionCategory.FenceConstruction, "d");
            e.AddIncome(Currency.ConservationTokens, 2, TransactionCategory.ConservationReward, "e");
            e.TrySpend(Currency.ConservationTokens, 5, TransactionCategory.ConservationUnlock, "f");

            Assert.AreEqual(16, e.State.LifetimeMoneyIncome);
            Assert.AreEqual(125, e.State.LifetimeMoneyExpenses);
            Assert.AreEqual(2, e.State.LifetimeConservationTokensEarned);
            Assert.AreEqual(5, e.State.LifetimeConservationTokensSpent);
            Assert.AreEqual(25000 + 16 - 125, e.GetBalance(Currency.Money));
            Assert.AreEqual(17, e.GetBalance(Currency.ConservationTokens));
            Assert.AreEqual(16, e.GetLifetimeIncome(Currency.Money));
            Assert.AreEqual(5, e.GetLifetimeExpenses(Currency.ConservationTokens));
        }

        // ---- Transaction records ----

        [Test]
        public void Transactions_RecordCurrencyTypeCategoryAmountSourceAndTime()
        {
            double now = 12.5;
            var e = Make(time: () => now);
            e.AddIncome(Currency.Money, 10, TransactionCategory.EntranceTicket, "entrance-9");
            now = 20.0;
            e.TrySpend(Currency.ConservationTokens, 0 + 1, TransactionCategory.ConservationUnlock, "nope"); // rejected: no record
            e.AddIncome(Currency.ConservationTokens, 4, TransactionCategory.ConservationReward, "release-2");

            var history = new List<TransactionRecord>();
            e.CopyHistoryTo(history);
            Assert.AreEqual(2, history.Count);

            var a = history[0];
            Assert.AreEqual(1, a.TransactionId);
            Assert.AreEqual(Currency.Money, a.Currency);
            Assert.AreEqual(TransactionType.Income, a.Type);
            Assert.AreEqual(TransactionCategory.EntranceTicket, a.Category);
            Assert.AreEqual(10, a.Amount);
            Assert.AreEqual("entrance-9", a.SourceId);
            Assert.AreEqual(12.5, a.SimulationTime, 1e-9);

            var b = history[1];
            Assert.AreEqual(2, b.TransactionId, "ids are sequential and rejected transactions do not use one");
            Assert.AreEqual(Currency.ConservationTokens, b.Currency);
            Assert.AreEqual(TransactionCategory.ConservationReward, b.Category);
            Assert.AreEqual(20.0, b.SimulationTime, 1e-9);
        }

        [Test]
        public void ExpenseRecords_ArePositiveAmounts_WithAnExpenseType()
        {
            var e = Make();
            var r = e.TrySpend(Currency.Money, 40, TransactionCategory.AnimalAdoption, "rabbit");
            Assert.AreEqual(TransactionType.Expense, r.Record.Type);
            Assert.AreEqual(40, r.Record.Amount);
            Assert.AreEqual(-40, r.Record.SignedAmount);
        }

        [Test]
        public void TransactionRecorded_IsRaisedForBothCurrencies()
        {
            var e = Make();
            var seen = new List<Currency>();
            e.TransactionRecorded += r => seen.Add(r.Currency);
            e.AddIncome(Currency.Money, 1, TransactionCategory.Donation, "x");
            e.AddIncome(Currency.ConservationTokens, 1, TransactionCategory.ConservationReward, "x");
            CollectionAssert.AreEqual(new[] { Currency.Money, Currency.ConservationTokens }, seen);
        }

        // ---- Bounded history ----

        [Test]
        public void History_IsBounded_AndKeepsTheMostRecentInOrder()
        {
            var e = Make(Settings(history: 5));
            for (int i = 1; i <= 12; i++) e.AddIncome(Currency.Money, i, TransactionCategory.Donation, "n" + i);

            Assert.AreEqual(5, e.HistoryCapacity);
            Assert.AreEqual(5, e.HistoryCount);
            var history = new List<TransactionRecord>();
            e.CopyHistoryTo(history);
            Assert.AreEqual(5, history.Count);
            for (int i = 0; i < 5; i++) Assert.AreEqual(8 + i, history[i].TransactionId, "oldest first, ids 8..12");
            e.TryGetLastTransaction(out var last);
            Assert.AreEqual(12, last.TransactionId);
            Assert.AreEqual(78, e.State.LifetimeMoneyIncome, "totals are unaffected by dropped history");
        }

        [Test]
        public void HistoryCapacity_IsClampedToTheSupportedRange()
        {
            Assert.AreEqual(EconomySettings.MinHistory, Settings(history: 0).HistoryCapacity);
            Assert.AreEqual(EconomySettings.MaxHistory, Settings(history: 99999).HistoryCapacity);
        }

        // ---- Persistence data ----

        [Test]
        public void State_RoundTripsThroughJson_AndRestores()
        {
            var e = Make(Settings(tokens: 9));
            e.AddIncome(Currency.Money, 70, TransactionCategory.FoodSale, "a");
            e.TrySpend(Currency.Money, 20, TransactionCategory.BuildingPurchase, "b");
            e.AddIncome(Currency.ConservationTokens, 3, TransactionCategory.ConservationReward, "c");
            e.TrySpend(Currency.ConservationTokens, 2, TransactionCategory.ConservationUnlock, "d");

            string json = JsonUtility.ToJson(e.Snapshot());
            var loaded = JsonUtility.FromJson<EconomyState>(json);

            var fresh = Make();
            Assert.IsTrue(fresh.Restore(loaded));
            Assert.AreEqual(e.GetBalance(Currency.Money), fresh.GetBalance(Currency.Money));
            Assert.AreEqual(e.GetBalance(Currency.ConservationTokens), fresh.GetBalance(Currency.ConservationTokens));
            Assert.AreEqual(70, fresh.State.LifetimeMoneyIncome);
            Assert.AreEqual(20, fresh.State.LifetimeMoneyExpenses);
            Assert.AreEqual(3, fresh.State.LifetimeConservationTokensEarned);
            Assert.AreEqual(2, fresh.State.LifetimeConservationTokensSpent);
            Assert.AreEqual(0, fresh.HistoryCount, "restoring creates no transactions");
        }

        [Test]
        public void Restore_RejectsNullAndNegativeData()
        {
            var e = Make();
            Assert.IsFalse(e.Restore(null));
            Assert.IsFalse(e.Restore(new EconomyState(-1, 0)));
            Assert.AreEqual(25000, e.GetBalance(Currency.Money));
        }

        [Test]
        public void Snapshot_IsACopy_NotALiveView()
        {
            var e = Make();
            var snap = e.Snapshot();
            e.AddIncome(Currency.Money, 100, TransactionCategory.Donation, "x");
            Assert.AreEqual(25000, snap.MoneyBalance);
        }

        // ---- Refund maths ----

        [Test]
        public void RefundFor_FloorsAndNeverExceedsTheOriginalCost()
        {
            var half = Settings(refund: 0.5f);
            Assert.AreEqual(12, half.RefundFor(25));
            Assert.AreEqual(2, half.RefundFor(5));
            Assert.AreEqual(0, half.RefundFor(1));
            Assert.AreEqual(0, half.RefundFor(0));
            Assert.AreEqual(0, half.RefundFor(-10));
            Assert.AreEqual(7, Settings(refund: 0.7f).RefundFor(10), "0.7 x 10 must not floor to 6 through float error");
            Assert.AreEqual(40, Settings(refund: 1f).RefundFor(40));
            Assert.AreEqual(40, Settings(refund: 5f).RefundFor(40), "rate is clamped to 1");
        }
    }
}
