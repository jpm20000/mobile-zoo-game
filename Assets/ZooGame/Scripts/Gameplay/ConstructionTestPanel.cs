using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Construction;
using ZooGame.Data;
using ZooGame.Input;
using ZooGame.World;

namespace ZooGame.Gameplay
{
    /// <summary>
    /// Temporary touch toolbar for path, fence, gate and demolish construction (replaced by the real build UI later):
    /// one button per catalog entry, Demolish, a Pan toggle (hands one-finger drag back to the camera), and
    /// Confirm / Cancel for the pending preview. All state is read from <see cref="BuildModeController"/>.
    /// </summary>
    public sealed class ConstructionTestPanel : MonoBehaviour
    {
        static readonly Color Idle = new Color(0f, 0f, 0f, 0.6f);
        static readonly Color Active = new Color(0.15f, 0.55f, 0.25f, 0.85f);

        [SerializeField] Text statusLabel;
        [SerializeField] RectTransform toolbarRoot;
        [Tooltip("Inactive button cloned once per tool.")]
        [SerializeField] Button toolButtonTemplate;
        [SerializeField] Button confirmButton;
        [SerializeField] Button cancelButton;

        struct ToolButton
        {
            public Button Button;
            public System.Func<bool> IsActive;
        }

        readonly List<ToolButton> _buttons = new List<ToolButton>(8);
        readonly StringBuilder _sb = new StringBuilder(128);
        BuildModeController _controller;
        ConstructionModel _model;
        Text _panLabel;

        public void Bind(BuildModeController controller, ConstructionModel model)
        {
            Unbind();
            _controller = controller;
            _model = model;
            var catalog = controller.Catalog;

            foreach (var def in catalog.Paths)
            {
                var d = def;
                AddTool(d.DisplayName, () => _controller.EnterPath(d),
                    () => _controller.Mode == BuildMode.PathPlacement);
            }
            foreach (var def in catalog.Fences)
            {
                var d = def;
                AddTool(d.DisplayName, () => _controller.EnterFence(d),
                    () => _controller.Mode == BuildMode.FencePlacement && _controller.Fence.Definition == d);
            }
            AddTool("Demolish", _controller.EnterDemolish, () => _controller.Mode == BuildMode.Demolition);
            var pan = AddTool("Pan: off", () => _controller.PanMode = !_controller.PanMode, () => _controller.PanMode, toggleOff: false);
            _panLabel = pan.GetComponentInChildren<Text>();

            confirmButton.onClick.AddListener(OnConfirm);
            cancelButton.onClick.AddListener(OnCancel);
            _controller.Changed += Refresh;
            _model.FencesChanged += Refresh;
            Refresh();
        }

        /// <param name="toggleOff">Pressing the already-active tool leaves the mode (deselects it).</param>
        Button AddTool(string label, UnityEngine.Events.UnityAction onClick, System.Func<bool> isActive, bool toggleOff = true)
        {
            var button = Instantiate(toolButtonTemplate, toolbarRoot);
            button.name = "Tool " + label;
            button.GetComponentInChildren<Text>().text = label;
            button.onClick.AddListener(() =>
            {
                if (toggleOff && isActive()) _controller.ExitMode();
                else onClick();
            });
            button.gameObject.SetActive(true);
            _buttons.Add(new ToolButton { Button = button, IsActive = isActive });
            return button;
        }

        void Unbind()
        {
            if (_controller == null) return;
            _controller.Changed -= Refresh;
            _model.FencesChanged -= Refresh;
            confirmButton.onClick.RemoveListener(OnConfirm);
            cancelButton.onClick.RemoveListener(OnCancel);
            for (int i = 0; i < _buttons.Count; i++)
                if (_buttons[i].Button != null) Destroy(_buttons[i].Button.gameObject);
            _buttons.Clear();
            _controller = null;
            _model = null;
        }

        void OnDestroy() => Unbind();

        void OnConfirm() => _controller.Confirm();
        void OnCancel() => _controller.Cancel();

        void Refresh()
        {
            var mode = _controller.Mode;
            bool building = mode == BuildMode.PathPlacement || mode == BuildMode.FencePlacement || mode == BuildMode.Demolition;

            for (int i = 0; i < _buttons.Count; i++)
                _buttons[i].Button.targetGraphic.color = _buttons[i].IsActive() ? Active : Idle;
            _panLabel.text = _controller.PanMode ? "Pan: ON" : "Pan: off";

            confirmButton.gameObject.SetActive(building);
            cancelButton.gameObject.SetActive(building);
            confirmButton.interactable = building && _controller.Active.HasPending;
            cancelButton.GetComponentInChildren<Text>().text = building && _controller.Active.HasPending ? "Cancel" : "Done";

            _sb.Clear();
            switch (mode)
            {
                case BuildMode.PathPlacement:
                    _sb.Append("Path: drag to paint, then Confirm\n").Append(_controller.Path.Pending.Count).Append(" cells pending");
                    break;
                case BuildMode.FencePlacement:
                    _sb.Append(_controller.Fence.Kind == EdgeKind.Gate ? "Gate" : "Fence")
                       .Append(": drag between corners or tap an edge, then Confirm\n").Append(_controller.Fence.Pending.Count).Append(" edges pending");
                    break;
                case BuildMode.Demolition:
                    _sb.Append("Demolish: touch paths or fences, then Confirm\n")
                       .Append(_controller.Demolish.PendingPaths.Count + _controller.Demolish.PendingEdges.Count).Append(" marked");
                    break;
                default:
                    _sb.Append("");
                    break;
            }
            if (building)
            {
                if (_controller.PanMode) _sb.Append("  (camera pan on)");
                int closed = _model.Enclosures.Count;
                _sb.Append("\nEnclosures: ").Append(closed).Append("  Fences: ").Append(_model.Fences.Count)
                   .Append("\nTwo fingers: move / zoom camera");
            }
            statusLabel.text = _sb.ToString();
        }
    }
}
