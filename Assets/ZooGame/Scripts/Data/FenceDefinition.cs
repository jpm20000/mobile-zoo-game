using UnityEngine;
using ZooGame.World;

namespace ZooGame.Data
{
    /// <summary>A kind of fence-line construction: a plain fence or a gate. Edges store only the <see cref="EdgeKind"/>.</summary>
    [CreateAssetMenu(menuName = "ZooGame/Fence Definition", fileName = "Fence")]
    public sealed class FenceDefinition : ScriptableObject
    {
        [SerializeField] string id = "basic_fence";
        [SerializeField] string displayName = "Basic Fence";
        [SerializeField] EdgeKind kind = EdgeKind.Fence;
        [SerializeField] Color color = new Color(0.55f, 0.38f, 0.22f);
        [Tooltip("Height in world units.")]
        [SerializeField, Min(0.05f)] float height = 0.7f;
        [Tooltip("0 = freehand runs. N > 0 = a one-touch N x N cell square outline that follows the finger until confirmed.")]
        [SerializeField, Min(0)] int squareSize;
        [Tooltip("Reserved for the economy milestone; nothing deducts it yet.")]
        [SerializeField, Min(0)] int cost;

        public string Id => id;
        public string DisplayName => displayName;
        public EdgeKind Kind => kind;
        public Color Color => color;
        public float Height => height;
        public int SquareSize => squareSize;
        public int Cost => cost;

        void OnValidate()
        {
            if (kind == EdgeKind.None) kind = EdgeKind.Fence;
        }
    }
}
