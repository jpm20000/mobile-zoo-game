namespace ZooGame.Economy
{
    /// <summary>
    /// The zoo's currencies. One enum drives one generic transaction path, so a new currency is a new value here and a
    /// new pair of balance fields in <see cref="EconomyState"/>, not a new system. Append only; <see cref="None"/> is
    /// the "unset" sentinel and is always rejected.
    /// </summary>
    public enum Currency
    {
        None = 0,
        Money,
        ConservationTokens
    }

    public enum TransactionType
    {
        Income = 0,
        Expense
    }

    /// <summary>
    /// Why a transaction happened. Each value belongs to exactly one <see cref="TransactionType"/>
    /// (see <see cref="TransactionCategories"/>); the economy rejects a mismatch. Append only within each block.
    /// </summary>
    public enum TransactionCategory
    {
        None = 0,

        // ---- Income ----
        EntranceTicket,
        FoodSale,
        DrinkSale,
        Donation,
        DemolitionRefund,
        /// <summary>Reserved: awarded by the future animal release system (Conservation Tokens). Nothing triggers it yet.</summary>
        ConservationReward,
        /// <summary>Development/testing grants only.</summary>
        DebugGrant,

        // ---- Expense ----
        BuildingPurchase,
        DecorationPurchase,
        HabitatObjectPurchase,
        PathConstruction,
        FenceConstruction,
        AnimalAdoption,
        /// <summary>Reserved: future Conservation Token spending (unlocks). Nothing triggers it yet.</summary>
        ConservationUnlock,
        /// <summary>Hiring a staff member (M8).</summary>
        StaffHiring,
        /// <summary>Development/testing charges only.</summary>
        DebugCharge
    }

    public static class TransactionCategories
    {
        public static bool IsIncome(TransactionCategory c) => c >= TransactionCategory.EntranceTicket && c <= TransactionCategory.DebugGrant;

        public static bool IsExpense(TransactionCategory c) => c >= TransactionCategory.BuildingPurchase && c <= TransactionCategory.DebugCharge;

        /// <summary>False for <see cref="TransactionCategory.None"/> and undefined values.</summary>
        public static bool IsValidFor(TransactionCategory c, TransactionType type) =>
            type == TransactionType.Income ? IsIncome(c) : IsExpense(c);

        public static bool IsDefined(Currency c) => c == Currency.Money || c == Currency.ConservationTokens;
    }
}
