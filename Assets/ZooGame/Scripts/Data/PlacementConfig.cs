using UnityEngine;

namespace ZooGame.Data
{
    /// <summary>Visual and interaction tuning for the placement system.</summary>
    [CreateAssetMenu(menuName = "ZooGame/Placement Config", fileName = "PlacementConfig")]
    public sealed class PlacementConfig : ScriptableObject
    {
        [Tooltip("Unlit transparent material (Sprites/Default) used for the ghost and footprint plates; colour is set per renderer.")]
        [SerializeField] Material ghostMaterial;
        [SerializeField] Color validColor = new Color(0.20f, 0.90f, 0.30f, 0.55f);
        [SerializeField] Color invalidColor = new Color(0.95f, 0.20f, 0.20f, 0.55f);
        [SerializeField] Color selectionColor = new Color(1.00f, 0.85f, 0.20f, 0.50f);
        [Tooltip("A press this many cells outside the ghost's footprint still grabs the ghost (fingers are imprecise). Presses further away pan the camera.")]
        [SerializeField, Min(0f)] float grabMarginCells = 1f;
        [Tooltip("Height of footprint plates above the ground, to avoid z-fighting.")]
        [SerializeField, Min(0.001f)] float plateHeight = 0.03f;

        public Material GhostMaterial => ghostMaterial;
        public Color ValidColor => validColor;
        public Color InvalidColor => invalidColor;
        public Color SelectionColor => selectionColor;
        public float GrabMarginCells => grabMarginCells;
        public float PlateHeight => plateHeight;
    }
}
