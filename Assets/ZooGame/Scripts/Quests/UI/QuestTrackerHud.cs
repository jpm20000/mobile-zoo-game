using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ZooGame.Quests
{
    /// <summary>
    /// One-quest HUD tracker: the pinned quest (or the next claimable/active one) with its objective progress, and a
    /// Claim button when its reward is ready. Pure display over <see cref="QuestService"/>; rebuilds at most once per
    /// frame and only after a change.
    /// </summary>
    public sealed class QuestTrackerHud : MonoBehaviour
    {
        [SerializeField] Text label;
        [SerializeField] Button claimButton;
        [SerializeField] Text claimLabel;

        readonly StringBuilder _sb = new StringBuilder(160);
        QuestService _quests;
        string _trackedId;
        bool _dirty;

        public void Bind(QuestService quests)
        {
            Unbind();
            _quests = quests;
            if (_quests == null) return;
            _quests.Changed += OnChanged;
            _quests.Restored += OnChanged;
            if (claimButton != null) claimButton.onClick.AddListener(OnClaim);
            _dirty = true;
        }

        void Unbind()
        {
            if (_quests == null) return;
            _quests.Changed -= OnChanged;
            _quests.Restored -= OnChanged;
            if (claimButton != null) claimButton.onClick.RemoveListener(OnClaim);
            _quests = null;
        }

        void OnDestroy() => Unbind();

        void OnChanged() => _dirty = true;

        void OnClaim()
        {
            if (_quests != null && _trackedId != null) _quests.Claim(_trackedId);
        }

        void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            Refresh();
        }

        void Refresh()
        {
            if (_quests == null) return;
            bool has = _quests.TryGetTracked(out var quest);
            _trackedId = has ? quest.QuestId : null;

            if (label != null)
            {
                _sb.Clear();
                if (!has)
                {
                    _sb.Append("No active quest");
                }
                else
                {
                    _sb.Append(quest.Definition.Title);
                    if (quest.State == QuestState.Completed) _sb.Append(" - ready to claim");
                    var objectives = quest.Objectives;
                    for (int i = 0; i < objectives.Count; i++)
                    {
                        _sb.Append('\n');
                        QuestText.AppendObjective(_sb, objectives[i]);
                    }
                }
                label.text = _sb.ToString();
            }

            bool claimable = has && quest.State == QuestState.Completed;
            if (claimButton != null) claimButton.gameObject.SetActive(claimable);
            if (claimLabel != null && claimable) claimLabel.text = "Claim " + QuestText.DescribeRewards(quest.Definition);
        }
    }
}
