namespace ZooGame.Economy
{
    /// <summary>One immutable entry of the transaction history. Created only by <see cref="EconomyService"/>.</summary>
    public readonly struct TransactionRecord
    {
        public readonly long TransactionId;
        public readonly Currency Currency;
        public readonly TransactionType Type;
        public readonly TransactionCategory Category;
        /// <summary>Always positive; <see cref="Type"/> gives the direction.</summary>
        public readonly long Amount;
        /// <summary>What caused it: a definition id, a facility id, a tool name. Never null.</summary>
        public readonly string SourceId;
        /// <summary>Simulation seconds (GameClock.ElapsedTime) when it happened.</summary>
        public readonly double SimulationTime;

        internal TransactionRecord(long id, Currency currency, TransactionType type, TransactionCategory category,
            long amount, string sourceId, double simulationTime)
        {
            TransactionId = id;
            Currency = currency;
            Type = type;
            Category = category;
            Amount = amount;
            SourceId = sourceId ?? string.Empty;
            SimulationTime = simulationTime;
        }

        /// <summary>Positive for income, negative for expenses.</summary>
        public long SignedAmount => Type == TransactionType.Income ? Amount : -Amount;
    }

    public enum TransactionFailure
    {
        None = 0,
        InvalidCurrency,
        /// <summary>Zero or negative.</summary>
        InvalidAmount,
        /// <summary>None, undefined, or an income category used for an expense (or the reverse).</summary>
        InvalidCategory,
        InsufficientFunds,
        /// <summary>The balance or lifetime total would exceed the numeric range.</summary>
        Overflow
    }

    public readonly struct TransactionResult
    {
        public readonly TransactionFailure Failure;
        public readonly TransactionRecord Record;

        internal TransactionResult(TransactionRecord record)
        {
            Failure = TransactionFailure.None;
            Record = record;
        }

        internal TransactionResult(TransactionFailure failure)
        {
            Failure = failure;
            Record = default;
        }

        public bool Success => Failure == TransactionFailure.None;
    }

    /// <summary>Raised when a spend was refused for lack of funds (feedback only; nothing changed).</summary>
    public readonly struct InsufficientFundsInfo
    {
        public readonly Currency Currency;
        public readonly long Needed;
        public readonly long Available;
        public readonly string SourceId;

        public InsufficientFundsInfo(Currency currency, long needed, long available, string sourceId)
        {
            Currency = currency;
            Needed = needed;
            Available = available;
            SourceId = sourceId ?? string.Empty;
        }

        public long Shortfall => Needed - Available;
    }
}
