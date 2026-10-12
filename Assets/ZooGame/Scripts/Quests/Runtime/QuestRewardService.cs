using System;
using System.Collections.Generic;
using ZooGame.Economy;
using ZooGame.Progression;

namespace ZooGame.Quests
{
    public enum QuestRewardFailure
    {
        None = 0,
        /// <summary>A Money or Conservation Token reward but no <see cref="EconomyService"/>.</summary>
        NoEconomy,
        /// <summary>An XP reward but no <see cref="ProgressionService"/>.</summary>
        NoProgression,
        InvalidAmount,
        /// <summary>A balance or total would exceed the numeric range.</summary>
        Overflow
    }

    /// <summary>
    /// Applies quest rewards through the authoritative services: Money and Conservation Tokens as income transactions on
    /// <see cref="EconomyService"/>, XP through <see cref="ProgressionService"/>. It never edits a balance itself and
    /// holds no state. It does not know about claiming: <see cref="QuestService"/> decides when a reward may be granted
    /// and guarantees once-only.
    /// </summary>
    public sealed class QuestRewardService
    {
        readonly EconomyService _economy;
        readonly ProgressionService _progression;

        public QuestRewardService(EconomyService economy, ProgressionService progression)
        {
            _economy = economy;
            _progression = progression;
        }

        /// <summary>Checks that every reward could be granted right now, so a grant is all-or-nothing.</summary>
        public QuestRewardFailure Validate(IReadOnlyList<QuestRewardDefinition> rewards)
        {
            long money = 0, tokens = 0, xp = 0;
            for (int i = 0; i < rewards.Count; i++)
            {
                var r = rewards[i];
                if (r == null || r.Amount <= 0) return QuestRewardFailure.InvalidAmount;
                switch (r.Type)
                {
                    case QuestRewardType.Money: money += r.Amount; break;
                    case QuestRewardType.ConservationTokens: tokens += r.Amount; break;
                    case QuestRewardType.Xp: xp += r.Amount; break;
                    default: return QuestRewardFailure.InvalidAmount;
                }
            }

            if (money > 0 && !HasHeadroom(Currency.Money, money, out var f)) return f;
            if (tokens > 0 && !HasHeadroom(Currency.ConservationTokens, tokens, out f)) return f;
            if (xp > 0)
            {
                if (_progression == null) return QuestRewardFailure.NoProgression;
                if (!_progression.CanAddXp(xp)) return QuestRewardFailure.Overflow;
            }
            return QuestRewardFailure.None;
        }

        bool HasHeadroom(Currency currency, long amount, out QuestRewardFailure failure)
        {
            failure = QuestRewardFailure.None;
            if (_economy == null) { failure = QuestRewardFailure.NoEconomy; return false; }
            if (_economy.GetBalance(currency) > long.MaxValue - amount
                || _economy.GetLifetimeIncome(currency) > long.MaxValue - amount)
            {
                failure = QuestRewardFailure.Overflow;
                return false;
            }
            return true;
        }

        /// <summary>Grants every reward (validated first). Nothing is granted when validation fails.</summary>
        public QuestRewardFailure Grant(string questId, IReadOnlyList<QuestRewardDefinition> rewards)
        {
            var failure = Validate(rewards);
            if (failure != QuestRewardFailure.None) return failure;

            for (int i = 0; i < rewards.Count; i++)
            {
                var r = rewards[i];
                switch (r.Type)
                {
                    case QuestRewardType.Money:
                        _economy.AddIncome(Currency.Money, r.Amount, TransactionCategory.QuestReward, questId);
                        break;
                    case QuestRewardType.ConservationTokens:
                        _economy.AddIncome(Currency.ConservationTokens, r.Amount, TransactionCategory.QuestReward, questId);
                        break;
                    case QuestRewardType.Xp:
                        _progression.TryAddXp(r.Amount, questId);
                        break;
                }
            }
            return QuestRewardFailure.None;
        }
    }
}
