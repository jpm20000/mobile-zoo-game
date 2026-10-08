using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Data;
using ZooGame.Placement;

namespace ZooGame.Gameplay
{
    /// <summary>
    /// Temporary touch UI for exercising placement (replaced by the real build menu in a later milestone):
    /// a palette of placeables, Move / Rotate / Delete for the selected object, and Rotate / Place / Cancel while
    /// previewing. All state comes from <see cref="PlacementSession"/>; this class only shows it and forwards taps.
    /// </summary>
    public sealed class PlacementTestPanel : MonoBehaviour
    {
        [SerializeField] Text statusLabel;
        [SerializeField] RectTransform paletteRoot;
        [Tooltip("Inactive button cloned once per catalog entry.")]
        [SerializeField] Button paletteButtonTemplate;
        [SerializeField] Button moveButton;
        [SerializeField] Button rotateButton;
        [SerializeField] Button deleteButton;
        [SerializeField] Button placeButton;
        [SerializeField] Button cancelButton;

        readonly StringBuilder _sb = new StringBuilder(128);
        PlacementController _controller;
        PlacementSession _session;
        GameObject[] _paletteButtons;

        public void Bind(PlacementController controller, PlaceableCatalog catalog)
        {
            Unbind();
            _controller = controller;
            _session = controller.Session;

            var defs = catalog.Definitions;
            _paletteButtons = new GameObject[defs.Count];
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                var button = Instantiate(paletteButtonTemplate, paletteRoot);
                button.name = "Palette " + def.DisplayName;
                button.GetComponentInChildren<Text>().text = def.DisplayName + "\n" + def.FootprintWidth + "x" + def.FootprintHeight;
                button.onClick.AddListener(() => _controller.BeginPlace(def));
                button.gameObject.SetActive(true);
                _paletteButtons[i] = button.gameObject;
            }

            moveButton.onClick.AddListener(OnMove);
            rotateButton.onClick.AddListener(OnRotate);
            deleteButton.onClick.AddListener(OnDelete);
            placeButton.onClick.AddListener(OnPlace);
            cancelButton.onClick.AddListener(OnCancel);
            _session.Changed += Refresh;
            Refresh();
        }

        void Unbind()
        {
            if (_session == null) return;
            _session.Changed -= Refresh;
            moveButton.onClick.RemoveListener(OnMove);
            rotateButton.onClick.RemoveListener(OnRotate);
            deleteButton.onClick.RemoveListener(OnDelete);
            placeButton.onClick.RemoveListener(OnPlace);
            cancelButton.onClick.RemoveListener(OnCancel);
            if (_paletteButtons != null)
                for (int i = 0; i < _paletteButtons.Length; i++)
                    if (_paletteButtons[i] != null) Destroy(_paletteButtons[i]);
            _paletteButtons = null;
            _session = null;
        }

        void OnDestroy() => Unbind();

        void OnMove() => _controller.BeginMoveSelected();
        void OnRotate() => _controller.Rotate();
        void OnDelete() => _controller.DeleteSelected();
        void OnPlace() => _controller.Confirm();
        void OnCancel() => _controller.Cancel();

        void Refresh()
        {
            bool previewing = _session.IsPlacing;
            bool selected = !previewing && _session.Selected != null;

            paletteRoot.gameObject.SetActive(!previewing);
            moveButton.gameObject.SetActive(selected);
            deleteButton.gameObject.SetActive(selected);
            rotateButton.gameObject.SetActive(previewing ? _session.Definition.AllowRotation : selected && _session.Selected.Definition.AllowRotation);
            placeButton.gameObject.SetActive(previewing);
            cancelButton.gameObject.SetActive(previewing || selected);
            cancelButton.GetComponentInChildren<Text>().text = previewing ? "Cancel" : "Deselect";
            placeButton.interactable = previewing && _session.Result.IsValid;

            _sb.Clear();
            if (previewing)
            {
                _sb.Append(_session.Mode == PlacementMode.Moving ? "Moving " : "Placing ").Append(_session.Definition.DisplayName)
                   .Append(' ').Append(_session.Width).Append('x').Append(_session.Height).Append('\n');
                if (_session.Result.IsValid) _sb.Append("Valid - drag or tap to position, then Place");
                else _sb.Append("Blocked: ").Append(Describe(_session.Result));
            }
            else if (selected)
            {
                var sel = _session.Selected;
                _sb.Append("Selected ").Append(sel.Definition.DisplayName).Append(' ').Append(sel.Width).Append('x').Append(sel.Height)
                   .Append(" at ").Append(sel.Origin).Append('\n');
                if (!_session.LastRejection.IsValid) _sb.Append("Cannot rotate: ").Append(Describe(_session.LastRejection));
                else _sb.Append("Move, Rotate or Delete");
            }
            else
            {
                _sb.Append("Choose something to place, or tap a placed object");
            }
            statusLabel.text = _sb.ToString();
        }

        static string Describe(PlacementResult r)
        {
            switch (r.Failure)
            {
                case PlacementFailure.OutOfBounds: return "outside the map";
                case PlacementFailure.LockedLand: return "locked land";
                case PlacementFailure.Occupied: return "space is occupied";
                case PlacementFailure.RuleFailed: return r.Detail ?? "rule failed";
                default: return r.Failure.ToString();
            }
        }
    }
}
