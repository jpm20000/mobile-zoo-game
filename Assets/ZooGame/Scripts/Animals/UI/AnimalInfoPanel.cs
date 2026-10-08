using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ZooGame.Animals
{
    /// <summary>
    /// Shows the selected animal: name, species, AnimalId, EnclosureId and current state. Event-driven (selection and
    /// the controller's StateChanged), so it costs nothing while nothing changes. Display only; it never edits the animal.
    /// </summary>
    public sealed class AnimalInfoPanel : MonoBehaviour
    {
        [SerializeField] GameObject content;
        [SerializeField] Text label;

        readonly StringBuilder _sb = new StringBuilder(160);
        AnimalSelectionController _selection;
        AnimalController _shown;

        public void Bind(AnimalSelectionController selection)
        {
            Unbind();
            _selection = selection;
            _selection.SelectionChanged += OnSelectionChanged;
            OnSelectionChanged(selection.Selected);
        }

        void Unbind()
        {
            if (_selection == null) return;
            _selection.SelectionChanged -= OnSelectionChanged;
            Watch(null);
            _selection = null;
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
            label.text = _sb.ToString();
        }
    }
}
