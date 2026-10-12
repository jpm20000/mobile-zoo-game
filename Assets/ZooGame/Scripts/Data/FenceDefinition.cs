using UnityEngine;
using UnityEngine.Serialization;
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
        [Tooltip("Money charged per fence or gate segment that is actually built.")]
        [FormerlySerializedAs("cost")]
        [SerializeField, Min(0)] int costPerSegment;

        public string Id => id;
        public string DisplayName => displayName;
        public EdgeKind Kind => kind;
        public Color Color => color;
        public float Height => height;
        public int SquareSize => squareSize;
        public int CostPerSegment => costPerSegment;

        /// <summary>Sets kind and cost from code (tests, procedural content).</summary>
        public FenceDefinition Configure(EdgeKind newKind, int newCostPerSegment)
        {
            kind = newKind;
            costPerSegment = Mathf.Max(0, newCostPerSegment);
            OnValidate();
            return this;
        }

        void OnValidate()
        {
            if (kind == EdgeKind.None) kind = EdgeKind.Fence;
        }
    }
}
