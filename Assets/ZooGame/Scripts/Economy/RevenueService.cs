using System;
using ZooGame.Data;

namespace ZooGame.Economy
{
    /// <summary>
    /// Visitor-generated Money income. The visitor systems report completed interactions (a successful admission, a
    /// completed facility use); this converts them to Money at the configured prices. It is called only after the
    /// interaction succeeded, never on destination selection. Toilets and benches are free, so they produce nothing.
    /// There are no visitor wallets in this milestone: visitors always pay.
    /// </summary>
    public sealed class RevenueService
    {
        readonly EconomyService _economy;

        public RevenueService(EconomyService economy)
        {
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        }

        /// <summary>A visitor was admitted through the entrance.</summary>
        public bool RecordAdmission(string entranceId) =>
            Earn(_economy.Settings.EntranceTicketPrice, TransactionCategory.EntranceTicket, entranceId);

        /// <summary>A visitor completed using a facility. Returns true when Money was earned.</summary>
        public bool RecordFacilityUse(VisitorFacilityKind kind, string facilityId)
        {
            switch (kind)
            {
                case VisitorFacilityKind.FoodStall:
                    return Earn(_economy.Settings.FoodPrice, TransactionCategory.FoodSale, facilityId);
                case VisitorFacilityKind.DrinkStall:
                    return Earn(_economy.Settings.DrinkPrice, TransactionCategory.DrinkSale, facilityId);
                default:
                    return false; // toilets, benches and anything else are free
            }
        }

        bool Earn(long amount, TransactionCategory category, string sourceId) =>
            amount > 0 && _economy.AddIncome(Currency.Money, amount, category, sourceId).Success;
    }
}
