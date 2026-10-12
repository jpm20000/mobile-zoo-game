using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ZooGame.Quests
{
    /// <summary>One quest row of the panel. Authored by the editor setup; the panel only fills text and toggles buttons.</summary>
    [Serializable]
    public sealed class QuestRowView
    {
        public GameObject root;
        public Text titleLabel;
        public Text objectivesLabel;
        public Text rewardLabel;
        public Button claimButton;
        public Text claimLabel;
        public Button pinButton;
        public Text pinLabel;
    }

    /// <summary>
    /// Compact quest list: claimable quests first, then active, then available (claimed ones are only counted). Shows each
    /// quest's objectives with progress, its rewards and a Claim button, plus a Pin button for the HUD tracker. It reads
    /// <see cref="QuestService"/> and calls only <see cref="QuestService.Claim"/>, <see cref="QuestService.Start"/> and
    /// <see cref="QuestService.SetPinned"/>; it holds no quest logic. Changes mark it dirty and it rebuilds its text at most
    /// once per frame, only when something changed.
    /// </summary>
    public sealed class QuestPanel : MonoBehaviour
    {
        [SerializeField] GameObject body;
        [SerializeField] Button toggleButton;
        [SerializeField] Text toggleLabel;
        [SerializeField] Text headerLabel;
        [SerializeField] QuestRowView[] rows = new QuestRowView[0];
        [SerializeField] Button pageButton;
        [SerializeField] Text pageLabel;

        readonly StringBuilder _sb = new StringBuilder(256);
        readonly List<QuestInstance> _visible = new List<QuestInstance>(8);
        readonly List<UnityEngine.Events.UnityAction> _rowClaim = new List<UnityEngine.Events.UnityAction>();
        readonly List<UnityEngine.Events.UnityAction> _pinActions = new List<UnityEngine.Events.UnityAction>();
        QuestService _quests;
        bool _dirty;
        bool _open;
        int _page;
        string _message;

        public void Bind(QuestService quests)
        {
            Unbind();
            _quests = quests;
            if (_quests == null) return;

            if (toggleButton != null) toggleButton.onClick.AddListener(OnToggle);
            if (pageButton != null) pageButton.onClick.AddListener(OnPage);
            for (int i = 0; i < rows.Length; i++)
            {
                int index = i;
                var row = rows[i];
                UnityEngine.Events.UnityAction onClaim = () => OnRowAction(index, isClaim: true);
                UnityEngine.Events.UnityAction onPin = () => OnRowAction(index, isClaim: false);
                _rowClaim.Add(onClaim);
                _pinActions.Add(onPin);
                if (row.claimButton != null) row.claimButton.onClick.AddListener(onClaim);
                if (row.pinButton != null) row.pinButton.onClick.AddListener(onPin);
            }
            _quests.Changed += OnChanged;
            _quests.Restored += OnChanged;
            if (body != null) body.SetActive(_open);
            _dirty = true;
        }

        void Unbind()
        {
            if (_quests == null) return;
            _quests.Changed -= OnChanged;
            _quests.Restored -= OnChanged;
            if (toggleButton != null) toggleButton.onClick.RemoveListener(OnToggle);
            if (pageButton != null) pageButton.onClick.RemoveListener(OnPage);
            for (int i = 0; i < rows.Length && i < _rowClaim.Count; i++)
            {
                if (rows[i].claimButton != null) rows[i].claimButton.onClick.RemoveListener(_rowClaim[i]);
                if (rows[i].pinButton != null) rows[i].pinButton.onClick.RemoveListener(_pinActions[i]);
            }
            _rowClaim.Clear();
            _pinActions.Clear();
            _quests = null;
        }

        void OnDestroy() => Unbind();

        void OnChanged() => _dirty = true;

        void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            Refresh();
        }

        // ---- Input -----------------------------------------------------------------------------------------

        void OnToggle()
        {
            _open = !_open;
            if (body != null) body.SetActive(_open);
            _dirty = true;
        }

        void OnPage()
        {
            _page++;
            _dirty = true;
        }

        void OnRowAction(int rowIndex, bool isClaim)
        {
            if (_quests == null) return;
            int index = _page * rows.Length + rowIndex;
            if (index < 0 || index >= _visible.Count) return;
            var quest = _visible[index];

            if (!isClaim)
            {
                _quests.SetPinned(_quests.PinnedQuestId == quest.QuestId ? null : quest.QuestId);
                return;
            }

            if (quest.State == QuestState.Available)
            {
                _quests.Start(quest.QuestId);
                return;
            }
            var result = _quests.Claim(quest.QuestId);
            _message = result.Success ? "Reward claimed: " + QuestText.DescribeRewards(quest.Definition)
                : result.Failure == QuestClaimFailure.AlreadyClaimed ? "Already claimed"
                : "Cannot claim yet";
            _dirty = true;
        }

        // ---- Display ---------------------------------------------------------------------------------------

        void Refresh()
        {
            if (_quests == null) return;
            CollectVisible(out int claimable, out int active, out int claimed);

            if (toggleLabel != null)
                toggleLabel.text = claimable > 0 ? "Quests (" + claimable + " ready)" : "Quests";

            if (headerLabel != null)
            {
                _sb.Clear();
                _sb.Append(active).Append(" active, ").Append(claimable).Append(" ready, ").Append(claimed).Append(" done");
                if (!string.IsNullOrEmpty(_message)) _sb.Append("\n").Append(_message);
                headerLabel.text = _sb.ToString();
            }

            int perPage = Mathf.Max(1, rows.Length);
            int pages = Mathf.Max(1, (_visible.Count + perPage - 1) / perPage);
            if (_page >= pages) _page = 0;

            for (int i = 0; i < rows.Length; i++)
            {
                int index = _page * perPage + i;
                var row = rows[i];
                bool has = index < _visible.Count;
                if (row.root != null) row.root.SetActive(has && _open);
                if (has) FillRow(row, _visible[index]);
            }

            if (pageButton != null) pageButton.gameObject.SetActive(pages > 1);
            if (pageLabel != null) pageLabel.text = "Page " + (_page + 1) + "/" + pages;
        }

        // Claimable first, then active, then available; the list order within each group is the catalogue order.
        void CollectVisible(out int claimable, out int active, out int claimed)
        {
            _visible.Clear();
            claimable = active = claimed = 0;
            var all = _quests.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].State == QuestState.Completed) { _visible.Add(all[i]); claimable++; }
            for (int i = 0; i < all.Count; i++)
                if (all[i].State == QuestState.Active) { _visible.Add(all[i]); active++; }
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].State == QuestState.Available) _visible.Add(all[i]);
                else if (all[i].State == QuestState.RewardClaimed) claimed++;
            }
        }

        void FillRow(QuestRowView row, QuestInstance quest)
        {
            var def = quest.Definition;
            bool pinned = _quests.PinnedQuestId == quest.QuestId;

            if (row.titleLabel != null)
                row.titleLabel.text = def.Title + (quest.State == QuestState.Completed ? "  - COMPLETE" : string.Empty);

            if (row.objectivesLabel != null)
            {
                _sb.Clear();
                if (!string.IsNullOrEmpty(def.Description)) _sb.Append(def.Description).Append('\n');
                var objectives = quest.Objectives;
                for (int i = 0; i < objectives.Count; i++)
                {
                    if (i > 0) _sb.Append('\n');
                    QuestText.AppendObjective(_sb, objectives[i]);
                }
                row.objectivesLabel.text = _sb.ToString();
            }

            if (row.rewardLabel != null) row.rewardLabel.text = "Reward: " + QuestText.DescribeRewards(def);

            bool canClaim = quest.State == QuestState.Completed;
            bool canStart = quest.State == QuestState.Available;
            if (row.claimButton != null) row.claimButton.gameObject.SetActive(canClaim || canStart);
            if (row.claimLabel != null) row.claimLabel.text = canStart ? "Start" : "Claim";
            if (row.pinLabel != null) row.pinLabel.text = pinned ? "Unpin" : "Pin";
        }
    }
}
