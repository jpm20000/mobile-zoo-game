using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ZooGame.Animals
{
    /// <summary>
    /// Shows the selected animal: identity, enclosure, state, its needs (hunger, thirst, comfort, social, enrichment),
    /// habitat suitability, overall welfare and band, and, in a second debug label, the six habitat factor scores.
    /// Event-driven (selection, the controller's StateChanged and the needs system's Evaluated), so it costs nothing
    /// while nothing changes. Display only; it never edits the animal.
    /// </summary>
    public sealed class AnimalInfoPanel : MonoBehaviour
    {
        [SerializeField] GameObject content;
        [SerializeField] Text label;
        [Tooltip("Debug: habitat factor scores. Optional.")]
        [SerializeField] Text factorsLabel;

        readonly StringBuilder _sb = new StringBuilder(200);
        readonly StringBuilder _factors = new StringBuilder(200);
        AnimalSelectionController _selection;
        AnimalNeedsSystem _needs;
        AnimalController _shown;

        public void Bind(AnimalSelectionController selection, AnimalNeedsSystem needs = null)
        {
            Unbind();
            _selection = selection;
            _needs = needs;
            _selection.SelectionChanged += OnSelectionChanged;
            if (_needs != null) _needs.Evaluated += OnEvaluated;
            OnSelectionChanged(selection.Selected);
        }

        void Unbind()
        {
            if (_selection == null) return;
            _selection.SelectionChanged -= OnSelectionChanged;
            if (_needs != null) _needs.Evaluated -= OnEvaluated;
            Watch(null);
            _selection = null;
            _needs = null;
        }

        void OnDestroy() => Unbind();

        void OnSelectionChanged(AnimalController animal)
        {
            Watch(animal);
            Refresh();
        }

        void Watch(AnimalController animal)
        {
            if (_shown != null) _shown.StateChanged -= OnStateChanged;
            _shown = animal;
            if (_shown != null) _shown.StateChanged += OnStateChanged;
        }

        void OnStateChanged(AnimalController _) => Refresh();

        void OnEvaluated(AnimalInstance animal)
        {
            if (_shown != null && ReferenceEquals(_shown.Instance, animal)) Refresh();
        }

        void Refresh()
        {
            if (content != null) content.SetActive(_shown != null);
            if (_shown == null || label == null) return;

            var a = _shown.Instance;
            _sb.Clear();
            _sb.Append(a.DisplayName).Append(" (").Append(_shown.Definition.DisplayName).Append(", ").Append(a.Sex).Append(")\n")
               .Append("ID: ").Append(a.AnimalId).Append('\n')
               .Append("Enclosure: ").Append(a.HasEnclosure ? a.EnclosureId : "none").Append('\n')
               .Append("State: ").Append(_shown.State);
            if (!_shown.EnclosureValid) _sb.Append("  (enclosure invalid)");
            _sb.Append('\n');

            AnimalWelfareReport r = null;
            bool hasReport = _needs != null && _needs.TryGetReport(a.AnimalId, out r);
            AppendNeed("Hunger", a.Hunger).Append("   ");
            AppendNeed("Thirst", a.Thirst).Append('\n');
            AppendNeed("Comfort", a.Comfort).Append("   ");
            AppendNeed("Social", a.Social).Append('\n');
            AppendNeed("Enrichment", a.Enrichment).Append('\n');
            if (hasReport)
                _sb.Append("Habitat Suitability: ").Append(Mathf.RoundToInt(r.HabitatSuitability)).Append('\n');
            _sb.Append("Overall Welfare: ").Append(Mathf.RoundToInt(a.OverallWelfare));
            if (hasReport)
            {
                _sb.Append("  ").Append(r.Band);
                if (r.Cap < AnimalWelfareMath.NoCap) _sb.Append("  (capped at ").Append(Mathf.RoundToInt(r.Cap)).Append(')');
            }
            label.text = _sb.ToString();

            if (factorsLabel == null) return;
            _factors.Clear();
            if (hasReport)
            {
                var f = r.Factors;
                _factors.Append("Habitat factors");
                if (!r.EnclosureValid) _factors.Append(" (no usable enclosure)");
                _factors.Append('\n');
                AppendFactor("Space", f.Space, true).Append('\n');
                AppendFactor("Social", f.Social, true).Append('\n');
                AppendFactor("Terrain", f.Terrain, f.TerrainApplicable).Append('\n');
                AppendFactor("Water", f.Water, f.WaterApplicable).Append('\n');
                AppendFactor("Shelter", f.Shelter, f.ShelterApplicable).Append('\n');
                AppendFactor("Enrichment", f.Enrichment, true).Append('\n');
                _factors.Append("Comfort target: ").Append(Mathf.RoundToInt(r.ComfortTarget));
            }
            factorsLabel.text = _factors.ToString();
        }

        StringBuilder AppendNeed(string name, float value) =>
            _sb.Append(name).Append(": ").Append(Mathf.RoundToInt(value));

        StringBuilder AppendFactor(string name, float value, bool applicable)
        {
            _factors.Append(name).Append(": ");
            return applicable ? _factors.Append(Mathf.RoundToInt(value)) : _factors.Append("n/a");
        }
    }
}
