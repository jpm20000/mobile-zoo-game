using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ZooGame.Progression
{
    /// <summary>
    /// Read-only display of the zoo's star level, rating and component scores. Rounds only here; the service keeps the
    /// underlying values. Refreshes on <see cref="ZooRatingService.Changed"/>, never per frame.
    /// </summary>
    public sealed class ZooRatingHud : MonoBehaviour
    {
        [SerializeField] Text summaryLabel;
        [Tooltip("Optional component breakdown under the summary.")]
        [SerializeField] Text detailLabel;

        readonly StringBuilder _sb = new StringBuilder(160);
        ZooRatingService _rating;

        public void Bind(ZooRatingService rating)
        {
            Unbind();
            _rating = rating;
            if (_rating == null) return;
            _rating.Changed += OnChanged;
            Refresh();
        }

        void Unbind()
        {
            if (_rating == null) return;
            _rating.Changed -= OnChanged;
            _rating = null;
        }

        void OnDestroy() => Unbind();

        void OnChanged(ZooRatingBreakdown _) => Refresh();

        void Refresh()
        {
            if (_rating == null) return;
            var b = _rating.Current;

            if (summaryLabel != null)
            {
                _sb.Clear();
                _sb.Append("Zoo rating: ");
                for (int i = 1; i <= ZooRatingSettings.MaxStars; i++) _sb.Append(i <= b.Stars ? '*' : '-');
                _sb.Append("  ").Append(b.Rating.ToString("0.0"));
                summaryLabel.text = _sb.ToString();
            }

            if (detailLabel != null)
            {
                _sb.Clear();
                _sb.Append("Welfare ").Append(b.AnimalWelfare.ToString("0"))
                   .Append("  Happy ").Append(b.VisitorHappiness.ToString("0"))
                   .Append("  Variety ").Append(b.AnimalVariety.ToString("0"))
                   .Append("\nBeauty ").Append(b.ZooBeauty.ToString("0"))
                   .Append("  Facilities ").Append(b.Facilities.ToString("0"));
                detailLabel.text = _sb.ToString();
            }
        }
    }
}
