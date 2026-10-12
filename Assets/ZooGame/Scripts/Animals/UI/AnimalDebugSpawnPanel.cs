using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Construction;
using ZooGame.Economy;
using ZooGame.Input;

namespace ZooGame.Animals
{
    /// <summary>
    /// TEMPORARY development palette: one button per species. Pressing one starts placing that animal (see
    /// <see cref="AnimalPlacementTool"/>); pressing the active one again deselects it. Place / Cancel appear while
    /// placing, and Place is only enabled over a legal spot. No creation logic lives here. Replaced by the real
    /// acquisition UI in a later milestone.
    /// </summary>
    public sealed class AnimalDebugSpawnPanel : MonoBehaviour
    {
        static readonly Color Idle = new Color(0f, 0f, 0f, 0.6f);
        static readonly Color Active = new Color(0.15f, 0.55f, 0.25f, 0.85f);

        [SerializeField] Text statusLabel;
        [SerializeField] RectTransform buttonRoot;
        [Tooltip("Inactive button cloned once per species.")]
        [SerializeField] Button buttonTemplate;
        [SerializeField] Button placeButton;
        [SerializeField] Button cancelButton;

        struct SpeciesButton
        {
            public Button Button;
            public AnimalDefinition Definition;
        }

        readonly List<SpeciesButton> _buttons = new List<SpeciesButton>(4);
        AnimalPlacementTool _tool;
        BuildModeController _builder;
        EconomyService _economy;

        /// <param name="economy">Optional: re-checks affordability when Money changes.</param>
        public void Bind(AnimalPlacementTool tool, BuildModeController builder, IReadOnlyList<AnimalDefinition> species,
            EconomyService economy = null)
        {
            Unbind();
            _tool = tool;
            _builder = builder;
            _economy = economy;
            for (int i = 0; i < species.Count; i++)
            {
                var def = species[i];
                if (def == null) continue;
                var b = Instantiate(buttonTemplate, buttonRoot);
                b.name = "Spawn " + def.DisplayName;
                b.GetComponentInChildren<Text>().text = def.DisplayName + " (min " + def.MinimumEnclosureArea + ")"
                    + (def.AdoptionCost > 0 ? "  $" + def.AdoptionCost : string.Empty);
                b.onClick.AddListener(() => OnSpecies(def));
                b.gameObject.SetActive(true);
                _buttons.Add(new SpeciesButton { Button = b, Definition = def });
            }
            placeButton.transform.SetAsLastSibling(); // species buttons were appended; Place / Cancel go underneath
            cancelButton.transform.SetAsLastSibling();
            placeButton.onClick.AddListener(OnPlace);
            cancelButton.onClick.AddListener(OnCancel);
            _tool.Changed += Refresh;
            _builder.Changed += Refresh;
            if (_economy != null) _economy.TransactionRecorded += OnTransaction;
            Refresh();
        }

        void Unbind()
        {
            if (_tool == null) return;
            _tool.Changed -= Refresh;
            _builder.Changed -= Refresh;
            if (_economy != null) _economy.TransactionRecorded -= OnTransaction;
            _economy = null;
            placeButton.onClick.RemoveListener(OnPlace);
            cancelButton.onClick.RemoveListener(OnCancel);
            for (int i = 0; i < _buttons.Count; i++) if (_buttons[i].Button != null) Destroy(_buttons[i].Button.gameObject);
            _buttons.Clear();
            _tool = null;
            _builder = null;
        }

        void OnDestroy() => Unbind();

        void OnSpecies(AnimalDefinition def)
        {
            if (_builder.Mode == BuildMode.AnimalPlacement && _tool.Definition == def)
            {
                _builder.ExitMode();
                return;
            }
            if (_tool.Select(def)) _builder.Enter(BuildMode.AnimalPlacement);
        }

        void OnTransaction(TransactionRecord _) => Refresh();

        void OnPlace() => _builder.Confirm();
        void OnCancel() => _builder.Cancel();

        void Refresh()
        {
            bool placing = _tool.Definition != null;
            for (int i = 0; i < _buttons.Count; i++)
                _buttons[i].Button.targetGraphic.color = placing && _buttons[i].Definition == _tool.Definition ? Active : Idle;

            placeButton.gameObject.SetActive(placing);
            cancelButton.gameObject.SetActive(placing);
            placeButton.interactable = placing && _tool.Result.IsValid && _tool.IsAffordable;

            if (statusLabel == null) return;
            if (placing)
                statusLabel.text = "Placing " + _tool.Definition.DisplayName + ": "
                                   + (!_tool.Result.IsValid ? _tool.Result.Message
                                      : !_tool.IsAffordable ? "not enough money (need $" + _tool.Cost + ")"
                                      : _tool.Cost > 0 ? "$" + _tool.Cost + " - drag into position, then Place"
                                      : "drag into position, then Place");
            else statusLabel.text = _tool.LastMessage.Length > 0 ? _tool.LastMessage : "Pick an animal to place in an enclosure";
        }
    }
}
