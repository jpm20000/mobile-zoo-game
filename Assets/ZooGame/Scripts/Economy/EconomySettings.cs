using System;
using UnityEngine;

namespace ZooGame.Economy
{
    /// <summary>
    /// Immutable economy tuning, built from <see cref="EconomyConfig"/> (or directly in tests). Nothing in UI or purchase
    /// code hard-codes a price, a starting balance or the refund rate: it all comes from here.
    /// </summary>
    public readonly struct EconomySettings
    {
        public const int MinHistory = 1;
        public const int MaxHistory = 1000;
        const int BasisPointsScale = 10000;

        public readonly long StartingMoney;
        public readonly long StartingConservationTokens;
        public readonly int EntranceTicketPrice;
        public readonly int FoodPrice;
        public readonly int DrinkPrice;
        /// <summary>Refund share in 1/10000ths (5000 = 50%). Integer so refunds are exactly reproducible.</summary>
        public readonly int RefundBasisPoints;
        public readonly int HistoryCapacity;

        public EconomySettings(long startingMoney, long startingTokens, int entranceTicketPrice, int foodPrice, int drinkPrice,
            float refundRate, int historyCapacity)
        {
            StartingMoney = Math.Max(0, startingMoney);
            StartingConservationTokens = Math.Max(0, startingTokens);
            EntranceTicketPrice = Math.Max(0, entranceTicketPrice);
            FoodPrice = Math.Max(0, foodPrice);
            DrinkPrice = Math.Max(0, drinkPrice);
            RefundBasisPoints = (int)Math.Round(Mathf.Clamp01(refundRate) * BasisPointsScale);
            HistoryCapacity = Math.Min(MaxHistory, Math.Max(MinHistory, historyCapacity));
        }

        public static EconomySettings Default => new EconomySettings(25000, 0, 10, 6, 4, 0.5f, 100);

        /// <summary>floor(originalCost x refund rate), never more than originalCost, never negative.</summary>
        public long RefundFor(long originalCost)
        {
            if (originalCost <= 0 || RefundBasisPoints <= 0) return 0;
            long refund = originalCost * RefundBasisPoints / BasisPointsScale; // integer division floors non-negatives
            return refund > originalCost ? originalCost : refund;
        }
    }
}
