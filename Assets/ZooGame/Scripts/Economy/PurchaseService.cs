using System;
using ZooGame.Data;

namespace ZooGame.Economy
{
    /// <summary>What a pending purchase would cost, for showing price and affordability before committing.</summary>
    public readonly struct PurchaseQuote
    {
        public readonly long UnitCost;
        public readonly int Units;
        public readonly long TotalCost;
        public readonly long Balance;

        public PurchaseQuote(long unitCost, int units, long balance)
        {
            UnitCost = Math.Max(0, unitCost);
            Units = Math.Max(0, units);
            TotalCost = UnitCost * Units;
            Balance = balance;
        }

        public bool CanAfford => TotalCost <= Balance;
        public long Shortfall => CanAfford ? 0 : TotalCost - Balance;
    }

    /// <summary>
    /// Money purchase validation and execution for gameplay actions. The flow every caller follows:
    /// <list type="number">
    /// <item><see cref="Quote"/> to show the cost and affordability;</item>
    /// <item><see cref="Authorize"/> at confirm time (refuses and reports when unaffordable, changes nothing);</item>
    /// <item>perform the gameplay action;</item>
    /// <item><see cref="Charge"/> for what was actually committed, only after the action succeeded.</item>
    /// </list>
    /// Failed or cancelled actions never reach step 4, so they cannot charge. Static prices stay in definition data; this
    /// class only multiplies and spends. Refunds return Money only.
    /// </summary>
    public sealed class PurchaseService
    {
        readonly EconomyService _economy;

        public PurchaseService(EconomyService economy)
        {
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        }

        public EconomyService Economy => _economy;

        // ---- Before the action -----------------------------------------------------------------------------

        public PurchaseQuote Quote(long unitCost, int units) =>
            new PurchaseQuote(unitCost, units, _economy.GetBalance(Currency.Money));

        public bool CanAfford(long totalCost) => _economy.CanAfford(Currency.Money, totalCost);

        /// <summary>
        /// True when Money covers <paramref name="totalCost"/> (a free purchase always passes). When it does not, raises the
        /// insufficient-funds feedback event and returns false without changing anything.
        /// </summary>
        public bool Authorize(long totalCost, string sourceId)
        {
            if (totalCost <= 0 || _economy.CanAfford(Currency.Money, totalCost)) return true;
            _economy.ReportInsufficientFunds(Currency.Money, totalCost, sourceId);
            return false;
        }

        // ---- After the action ------------------------------------------------------------------------------

        /// <summary>
        /// Spends unitCost x units of Money. Zero cost or zero units succeeds without a transaction. Returns false if the
        /// spend was refused (callers authorize first, so this only happens if funds changed in between).
        /// </summary>
        public bool Charge(TransactionCategory category, string sourceId, long unitCost, int units)
        {
            if (unitCost <= 0 || units <= 0) return true;
            return _economy.TrySpend(Currency.Money, unitCost * units, category, sourceId).Success;
        }

        // ---- Refunds ---------------------------------------------------------------------------------------

        /// <summary>floor(originalCost x refund rate), never above originalCost.</summary>
        public long RefundAmount(long originalCost) => _economy.Settings.RefundFor(originalCost);

        /// <summary>Credits <paramref name="amount"/> Money as a demolition refund. Returns the amount credited (0 if none).</summary>
        public long Refund(string sourceId, long amount)
        {
            if (amount <= 0) return 0;
            var result = _economy.AddIncome(Currency.Money, amount, TransactionCategory.DemolitionRefund, sourceId);
            return result.Success ? amount : 0;
        }

        // ---- Categories ------------------------------------------------------------------------------------

        public static TransactionCategory CategoryFor(PlaceableCategory category)
        {
            switch (category)
            {
                case PlaceableCategory.Decoration: return TransactionCategory.DecorationPurchase;
                case PlaceableCategory.Habitat: return TransactionCategory.HabitatObjectPurchase;
                default: return TransactionCategory.BuildingPurchase; // Building and Service structures
            }
        }
    }
}
