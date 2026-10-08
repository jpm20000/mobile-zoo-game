using UnityEngine;

namespace ZooGame.Data
{
    /// <summary>A kind of visitor path. Cells only store "has a path" today, so every path renders with the catalog's first definition.</summary>
    [CreateAssetMenu(menuName = "ZooGame/Path Definition", fileName = "Path")]
    public sealed class PathDefinition : ScriptableObject
    {
        [SerializeField] string id = "basic_path";
        [SerializeField] string displayName = "Basic Path";
        [SerializeField] Color color = new Color(0.78f, 0.68f, 0.50f);
        [Tooltip("Width of the path surface as a fraction of a cell.")]
        [SerializeField, Range(0.2f, 1f)] float width = 0.7f;
        [Tooltip("Reserved for the economy milestone; nothing deducts it yet.")]
        [SerializeField, Min(0)] int cost;

        public string Id => id;
        public string DisplayName => displayName;
        public Color Color => color;
        public float Width => width;
        public int Cost => cost;
    }
}
