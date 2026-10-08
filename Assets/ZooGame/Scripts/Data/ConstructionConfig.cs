using UnityEngine;

namespace ZooGame.Data
{
    /// <summary>Look and feel of path, fence and preview rendering.</summary>
    [CreateAssetMenu(menuName = "ZooGame/Construction Config", fileName = "ConstructionConfig")]
    public sealed class ConstructionConfig : ScriptableObject
    {
        [Tooltip("Unlit material using vertex colours (Sprites/Default).")]
        [SerializeField] Material vertexColorMaterial;
        [SerializeField] Color validPreview = new Color(0.20f, 0.90f, 0.30f, 0.60f);
        [SerializeField] Color invalidPreview = new Color(0.95f, 0.20f, 0.20f, 0.60f);
        [SerializeField] Color demolishPreview = new Color(1.00f, 0.55f, 0.10f, 0.70f);
        [Tooltip("Fences that are not part of a closed enclosure are tinted with this colour (multiplied).")]
        [SerializeField] Color openFenceTint = new Color(1.0f, 0.85f, 0.45f);
        [SerializeField, Min(0.02f)] float fenceThickness = 0.12f;
        [SerializeField, Min(0.1f)] float gateHeightFraction = 0.8f;
        [Tooltip("Height of paths above the terrain.")]
        [SerializeField, Min(0.001f)] float pathHeight = 0.02f;
        [Tooltip("Height of preview markers above the terrain.")]
        [SerializeField, Min(0.001f)] float previewHeight = 0.05f;
        [Tooltip("Pointer must be this close (in cells) to a fence line for demolish to target the fence rather than the path under it.")]
        [SerializeField, Range(0.05f, 0.5f)] float fencePickRadius = 0.25f;

        public Material VertexColorMaterial => vertexColorMaterial;
        public Color ValidPreview => validPreview;
        public Color InvalidPreview => invalidPreview;
        public Color DemolishPreview => demolishPreview;
        public Color OpenFenceTint => openFenceTint;
        public float FenceThickness => fenceThickness;
        public float GateHeightFraction => gateHeightFraction;
        public float PathHeight => pathHeight;
        public float PreviewHeight => previewHeight;
        public float FencePickRadius => fencePickRadius;
    }
}
