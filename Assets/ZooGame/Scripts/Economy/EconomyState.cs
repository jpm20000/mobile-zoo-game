using System;
using UnityEngine;

namespace ZooGame.Economy
{
    /// <summary>
    /// The persistent economy numbers: current balances and lifetime totals for both currencies. Plain serializable
    /// data (JsonUtility-ready) so a save system can store it as is. Readable by anyone; mutated only by
    /// <see cref="EconomyService"/> (the mutators are internal). Transaction history is deliberately not part of it.
    /// </summary>
    [Serializable]
    public sealed class EconomyState
    {
        [SerializeField] long moneyBalance;
        [SerializeField] long conservationTokenBalance;
        [SerializeField] long lifetimeMoneyIncome;
        [SerializeField] long lifetimeMoneyExpenses;
        [SerializeField] long lifetimeConservationTokensEarned;
        [SerializeField] long lifetimeConservationTokensSpent;

        public long MoneyBalance => moneyBalance;
        public long ConservationTokenBalance => conservationTokenBalance;
        public long LifetimeMoneyIncome => lifetimeMoneyIncome;
        public long LifetimeMoneyExpenses => lifetimeMoneyExpenses;
        public long LifetimeConservationTokensEarned => lifetimeConservationTokensEarned;
        public long LifetimeConservationTokensSpent => lifetimeConservationTokensSpent;

        public EconomyState() { }

        public EconomyState(long money, long tokens)
        {
            moneyBalance = money;
            conservationTokenBalance = tokens;
        }

        public EconomyState Clone() => (EconomyState)MemberwiseClone();

        /// <summary>True when every value is non-negative (what a restored save must satisfy).</summary>
        public bool IsValid =>
            moneyBalance >= 0 && conservationTokenBalance >= 0 && lifetimeMoneyIncome >= 0 && lifetimeMoneyExpenses >= 0
            && lifetimeConservationTokensEarned >= 0 && lifetimeConservationTokensSpent >= 0;

        public long BalanceOf(Currency currency)
        {
            switch (currency)
            {
                case Currency.Money: return moneyBalance;
                case Currency.ConservationTokens: return conservationTokenBalance;
                default: return 0;
            }
        }

        public long LifetimeIncomeOf(Currency currency)
        {
            switch (currency)
            {
                case Currency.Money: return lifetimeMoneyIncome;
                case Currency.ConservationTokens: return lifetimeConservationTokensEarned;
                default: return 0;
            }
        }

        public long LifetimeExpensesOf(Currency currency)
        {
            switch (currency)
            {
                case Currency.Money: return lifetimeMoneyExpenses;
                case Currency.ConservationTokens: return lifetimeConservationTokensSpent;
                default: return 0;
            }
        }

        // Callers (EconomyService) have already validated the amount and the numeric range.
        internal void Credit(Currency currency, long amount)
        {
            if (currency == Currency.Money) { moneyBalance += amount; lifetimeMoneyIncome += amount; }
            else { conservationTokenBalance += amount; lifetimeConservationTokensEarned += amount; }
        }

        internal void Debit(Currency currency, long amount)
        {
            if (currency == Currency.Money) { moneyBalance -= amount; lifetimeMoneyExpenses += amount; }
            else { conservationTokenBalance -= amount; lifetimeConservationTokensSpent += amount; }
        }
    }
}
