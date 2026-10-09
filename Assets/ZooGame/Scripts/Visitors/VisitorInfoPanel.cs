using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ZooGame.Visitors
{
    /// <summary>
    /// Debug readout of the selected visitor: state, target, the four needs, needs happiness, visit satisfaction,
    /// overall happiness and visit time. Event-driven (selection, the simulation's Evaluated and the decision service's
    /// StateChanged), so it costs nothing while nothing changes. Display only; it never edits the visitor.
    /// </summary>
    public sealed class VisitorInfoPanel : MonoBehaviour
    {
        [SerializeField] GameObject content;
        [SerializeField] Text label;

        readonly StringBuilder _sb = new StringBuilder(256);
        VisitorSelectionController _selection;
        VisitorSimulationService _simulation;
        VisitorDecisionService _decisions;
        VisitorInstance _shown;

        public void Bind(VisitorSelectionController selection, VisitorSimulationService simulation, VisitorDecisionService decisions)
        {
            Unbind();
            _selection = selection;
            _simulation = simulation;
            _decisions = decisions;
            _selection.SelectionChanged += OnSelectionChanged;
            _simulation.Evaluated += OnChanged;
            _decisions.StateChanged += OnChanged;
            OnSelectionChanged(selection.Selected);
        }

        void Unbind()
        {
            if (_selection == null) return;
            _selection.SelectionChanged -= OnSelectionChanged;
            _simulation.Evaluated -= OnChanged;
            _decisions.StateChanged -= OnChanged;
            _selection = null;
            _simulation = null;
            _decisions = null;
        }

        void OnDestroy() => Unbind();

        void OnSelectionChanged(VisitorController visitor)
        {
            _shown = visitor != null ? visitor.Instance : null;
            Refresh();
        }

        void OnChanged(VisitorInstance v)
        {
            if (ReferenceEquals(v, _shown)) Refresh();
        }

        void Refresh()
        {
            if (content != null) content.SetActive(_shown != null);
            if (_shown == null || label == null) return;

            var v = _shown;
            string id = v.VisitorId;
            _sb.Clear();
            _sb.Append("Visitor ").Append(id.Length > 6 ? id.Substring(id.Length - 6) : id).Append('\n')
               .Append("State: ").Append(v.State).Append('\n')
               .Append("Target: ").Append(v.TargetDestinationId ?? "none").Append('\n');
            Need("Hunger", v.Hunger).Append("   ");
            Need("Thirst", v.Thirst).Append('\n');
            Need("Toilet", v.ToiletNeed).Append("   ");
            Need("Energy", v.Energy).Append('\n');
            Need("Needs Happiness", v.NeedsHappiness).Append('\n');
            Need("Visit Satisfaction", v.VisitSatisfaction).Append('\n');
            Need("Happiness", v.Happiness).Append('\n');
            _sb.Append("Visit time: ").Append(Mathf.RoundToInt(v.VisitTime)).Append(" / ")
               .Append(Mathf.RoundToInt(v.MaximumVisitTime)).Append(" min");
            label.text = _sb.ToString();
        }

        StringBuilder Need(string name, float value) => _sb.Append(name).Append(": ").Append(Mathf.RoundToInt(value));
    }
}
