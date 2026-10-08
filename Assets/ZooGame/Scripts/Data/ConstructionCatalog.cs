using System.Collections.Generic;
using UnityEngine;
using ZooGame.World;

namespace ZooGame.Data
{
    /// <summary>The paths, fences and gates the player can build. Order is the toolbar order.</summary>
    [CreateAssetMenu(menuName = "ZooGame/Construction Catalog", fileName = "ConstructionCatalog")]
    public sealed class ConstructionCatalog : ScriptableObject
    {
        [SerializeField] List<PathDefinition> paths = new List<PathDefinition>();
        [SerializeField] List<FenceDefinition> fences = new List<FenceDefinition>();

        public IReadOnlyList<PathDefinition> Paths => paths;
        public IReadOnlyList<FenceDefinition> Fences => fences;

        public PathDefinition DefaultPath => paths.Count > 0 ? paths[0] : null;

        /// <summary>First fence definition of the given kind, or null.</summary>
        public FenceDefinition FirstOfKind(EdgeKind kind)
        {
            for (int i = 0; i < fences.Count; i++)
                if (fences[i] != null && fences[i].Kind == kind) return fences[i];
            return null;
        }
    }
}
