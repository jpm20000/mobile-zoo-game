using System;
using System.Collections.Generic;

namespace ZooGame.Economy
{
    /// <summary>
    /// The only writer of currency balances. Every change is one validated transaction: the currency must be defined,
    /// the amount positive, the category valid for the direction, an expense affordable and the numbers in range.
    /// A successful change updates <see cref="EconomyState"/>, appends an immutable <see cref="TransactionRecord"/> to a
    /// bounded history and raises <see cref="TransactionRecorded"/>; a refused one changes nothing.
    ///
    /// Both currencies run through the same code, so Conservation Tokens need nothing extra when a future system
    /// starts awarding them. Event driven: nothing here runs per frame.
    /// </summary>
    public sealed class EconomyService
    {
        readonly EconomySettings _settings;
        readonly Func<double> _time;
        readonly TransactionRecord[] _history;
        EconomyState _state;
        int _historyStart;
        int _historyCount;
        long _nextId = 1;

        /// <param name="time">Simulation time source (GameClock.ElapsedTime). Null stamps every record with 0.</param>
        public EconomyService(EconomySettings settings, Func<double> time = null)
        {
            _settings = settings;
            _time = time;
            _history = new TransactionRecord[settings.HistoryCapacity];
            _state = new EconomyState(settings.StartingMoney, settings.StartingConservationTokens);
        }

        public EconomySettings Settings => _settings;

        /// <summary>Read-only view for persistence and display. Do not mutate: the mutators are internal.</summary>
        public EconomyState State => _state;

        public int HistoryCapacity => _history.Length;
        public int HistoryCount => _historyCount;

        /// <summary>Raised after every successful transaction (either currency).</summary>
        public event Action<TransactionRecord> TransactionRecorded;

        /// <summary>Raised when a spend was refused for lack of funds, so the UI can say so.</summary>
        public event Action<InsufficientFundsInfo> InsufficientFunds;

        /// <summary>Raised after <see cref="Restore"/> replaced the state.</summary>
        public event Action StateRestored;

        // ---- Queries ---------------------------------------------------------------------------------------

        public long GetBalance(Currency currency) => _state.BalanceOf(currency);

        public long GetLifetimeIncome(Currency currency) => _state.LifetimeIncomeOf(currency);

        public long GetLifetimeExpenses(Currency currency) => _state.LifetimeExpensesOf(currency);

        /// <summary>True for a defined currency, a non-negative amount and a balance that covers it.</summary>
        public bool CanAfford(Currency currency, long amount) =>
            TransactionCategories.IsDefined(currency) && amount >= 0 && _state.BalanceOf(currency) >= amount;

        // ---- Transactions ----------------------------------------------------------------------------------

        public TransactionResult TrySpend(Currency currency, long amount, TransactionCategory category, string sourceId)
        {
            var failure = Validate(currency, amount, category, TransactionType.Expense);
            if (failure != TransactionFailure.None) return new TransactionResult(failure);

            long balance = _state.BalanceOf(currency);
            if (balance < amount)
            {
                InsufficientFunds?.Invoke(new InsufficientFundsInfo(currency, amount, balance, sourceId));
                return new TransactionResult(TransactionFailure.InsufficientFunds);
            }
            if (_state.LifetimeExpensesOf(currency) > long.MaxValue - amount) return new TransactionResult(TransactionFailure.Overflow);

            _state.Debit(currency, amount);
            return Record(currency, TransactionType.Expense, category, amount, sourceId);
        }

        public TransactionResult AddIncome(Currency currency, long amount, TransactionCategory category, string sourceId)
        {
            var failure = Validate(currency, amount, category, TransactionType.Income);
            if (failure != TransactionFailure.None) return new TransactionResult(failure);

            if (_state.BalanceOf(currency) > long.MaxValue - amount || _state.LifetimeIncomeOf(currency) > long.MaxValue - amount)
                return new TransactionResult(TransactionFailure.Overflow);

            _state.Credit(currency, amount);
            return Record(currency, TransactionType.Income, category, amount, sourceId);
        }

        /// <summary>
        /// Reports a failed affordability pre-check (the purchase flow checks before acting and spends only after the
        /// action succeeded). Changes nothing; only raises <see cref="InsufficientFunds"/>.
        /// </summary>
        public void ReportInsufficientFunds(Currency currency, long needed, string sourceId) =>
            InsufficientFunds?.Invoke(new InsufficientFundsInfo(currency, needed, _state.BalanceOf(currency), sourceId));

        static TransactionFailure Validate(Currency currency, long amount, TransactionCategory category, TransactionType type)
        {
            if (!TransactionCategories.IsDefined(currency)) return TransactionFailure.InvalidCurrency;
            if (amount <= 0) return TransactionFailure.InvalidAmount;
            if (!TransactionCategories.IsValidFor(category, type)) return TransactionFailure.InvalidCategory;
            return TransactionFailure.None;
        }

        TransactionResult Record(Currency currency, TransactionType type, TransactionCategory category, long amount, string sourceId)
        {
            var record = new TransactionRecord(_nextId++, currency, type, category, amount, sourceId, _time != null ? _time() : 0d);
            int capacity = _history.Length;
            if (_historyCount < capacity)
            {
                _history[(_historyStart + _historyCount) % capacity] = record;
                _historyCount++;
            }
            else
            {
                _history[_historyStart] = record; // overwrite the oldest
                _historyStart = (_historyStart + 1) % capacity;
            }
            TransactionRecorded?.Invoke(record);
            return new TransactionResult(record);
        }

        // ---- History ---------------------------------------------------------------------------------------

        /// <summary>Copies the retained history, oldest first, into <paramref name="into"/> (cleared first).</summary>
        public void CopyHistoryTo(List<TransactionRecord> into)
        {
            into.Clear();
            for (int i = 0; i < _historyCount; i++) into.Add(_history[(_historyStart + i) % _history.Length]);
        }

        public bool TryGetLastTransaction(out TransactionRecord record)
        {
            if (_historyCount == 0) { record = default; return false; }
            record = _history[(_historyStart + _historyCount - 1) % _history.Length];
            return true;
        }

        // ---- Persistence -----------------------------------------------------------------------------------

        /// <summary>A copy of the persistent numbers (balances and lifetime totals). History is not persisted.</summary>
        public EconomyState Snapshot() => _state.Clone();

        /// <summary>
        /// Replaces the balances and totals with loaded ones. Rejects null or negative data. Creates no transactions and
        /// clears the (unpersisted) history.
        /// </summary>
        public bool Restore(EconomyState state)
        {
            if (state == null || !state.IsValid) return false;
            _state = state.Clone();
            _historyStart = 0;
            _historyCount = 0;
            Array.Clear(_history, 0, _history.Length);
            StateRestored?.Invoke();
            return true;
        }

        // ---- Development -----------------------------------------------------------------------------------

        /// <summary>Development/testing only: grants an amount of any currency (for example tokens, which nothing awards yet).</summary>
        public TransactionResult DebugGrant(Currency currency, long amount) =>
            AddIncome(currency, amount, TransactionCategory.DebugGrant, "debug");

        /// <summary>Development/testing only: removes an amount of any currency if affordable.</summary>
        public TransactionResult DebugCharge(Currency currency, long amount) =>
            TrySpend(currency, amount, TransactionCategory.DebugCharge, "debug");
    }
}
