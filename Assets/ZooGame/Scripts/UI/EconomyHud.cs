using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Economy;

namespace ZooGame.UI
{
    /// <summary>
    /// The main economy HUD: Money and Conservation Token balances plus short feedback for money gained, money spent and
    /// insufficient funds. Read only: it reacts to <see cref="EconomyService"/> events and never changes a balance. Not
    /// a finance screen. Updates only on events; the only per-frame work is fading the feedback line while it is visible.
    /// </summary>
    public sealed class EconomyHud : MonoBehaviour
    {
        const float FeedbackSeconds = 2.5f;
        /// <summary>Same-direction changes this close together (real seconds) merge into one running total.</summary>
        const float CoalesceSeconds = 1.0f;

        static readonly Color Gain = new Color(0.45f, 1f, 0.55f);
        static readonly Color Spend = new Color(1f, 0.55f, 0.45f);
        static readonly Color Warning = new Color(1f, 0.80f, 0.25f);

        [SerializeField] Text moneyLabel;
        [SerializeField] Text tokensLabel;
        [Tooltip("Optional one-line feedback under the balances.")]
        [SerializeField] Text feedbackLabel;

        readonly StringBuilder _sb = new StringBuilder(64);
        EconomyService _economy;

        // Running feedback entry (coalesced).
        Currency _fbCurrency;
        TransactionType _fbType;
        TransactionCategory _fbCategory;
        long _fbAmount;
        float _fbAge;
        bool _fbShowing;

        public void Bind(EconomyService economy)
        {
            Unbind();
            _economy = economy;
            _economy.TransactionRecorded += OnTransaction;
            _economy.InsufficientFunds += OnInsufficient;
            _economy.StateRestored += RefreshBalances;
            ClearFeedback();
            RefreshBalances();
        }

        void Unbind()
        {
            if (_economy == null) return;
            _economy.TransactionRecorded -= OnTransaction;
            _economy.InsufficientFunds -= OnInsufficient;
            _economy.StateRestored -= RefreshBalances;
            _economy = null;
        }

        void OnDestroy() => Unbind();

        void RefreshBalances()
        {
            if (_economy == null) return;
            if (moneyLabel != null) moneyLabel.text = "Money: " + Format(Currency.Money, _economy.GetBalance(Currency.Money));
            if (tokensLabel != null) tokensLabel.text = "Conservation Tokens: " + _economy.GetBalance(Currency.ConservationTokens);
        }

        void OnTransaction(TransactionRecord r)
        {
            RefreshBalances();
            if (feedbackLabel == null) return;

            bool merge = _fbShowing && _fbAge <= CoalesceSeconds && r.Currency == _fbCurrency && r.Type == _fbType;
            if (merge)
            {
                _fbAmount += r.Amount;
                if (_fbCategory != r.Category) _fbCategory = TransactionCategory.None; // mixed: no single reason to show
            }
            else
            {
                _fbCurrency = r.Currency;
                _fbType = r.Type;
                _fbCategory = r.Category;
                _fbAmount = r.Amount;
            }
            _fbAge = 0f;
            _fbShowing = true;

            _sb.Clear();
            _sb.Append(r.Type == TransactionType.Income ? '+' : '-').Append(Format(r.Currency, _fbAmount));
            if (_fbCategory != TransactionCategory.None) _sb.Append("  ").Append(Humanize(_fbCategory));
            Show(_sb.ToString(), r.Type == TransactionType.Income ? Gain : Spend);
        }

        void OnInsufficient(InsufficientFundsInfo info)
        {
            if (feedbackLabel == null) return;
            _fbShowing = false; // a warning never merges with the next gain
            _sb.Clear();
            _sb.Append("Not enough ").Append(info.Currency == Currency.Money ? "money" : "tokens")
               .Append(": need ").Append(Format(info.Currency, info.Needed)).Append(", have ").Append(Format(info.Currency, info.Available));
            _fbAge = 0f;
            Show(_sb.ToString(), Warning);
            enabled = true;
        }

        void Show(string text, Color color)
        {
            feedbackLabel.text = text;
            feedbackLabel.color = color;
            feedbackLabel.enabled = true;
            enabled = true; // Update fades the line out, then disables itself
        }

        void ClearFeedback()
        {
            _fbShowing = false;
            if (feedbackLabel != null) feedbackLabel.enabled = false;
            enabled = false;
        }

        void Update()
        {
            _fbAge += Time.unscaledDeltaTime;
            if (_fbAge >= FeedbackSeconds) ClearFeedback();
        }

        static string Format(Currency currency, long amount) =>
            currency == Currency.Money ? "$" + amount : amount + (amount == 1 ? " token" : " tokens");

        static string Humanize(TransactionCategory c)
        {
            string s = c.ToString();
            var sb = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
