using UnityEngine;
using ZooGame.World;

namespace ZooGame.Data
{
    /// <summary>Grid layout and placeholder terrain look. Create via Assets > Create > ZooGame > World Config.</summary>
    [CreateAssetMenu(menuName = "ZooGame/World Config", fileName = "WorldConfig")]
    public sealed class WorldConfig : ScriptableObject
    {
        [Header("Grid")]
        [SerializeField, Min(1)] int gridWidth = 64;
        [SerializeField, Min(1)] int gridDepth = 64;
        [Tooltip("World units per cell edge.")]
        [SerializeField, Min(0.01f)] float cellSize = 1f;
        [Tooltip("World position of the grid's minimum corner (cell 0,0).")]
        [SerializeField] Vector3 origin = Vector3.zero;

        [Header("Initially unlocked land (centred in the grid)")]
        [SerializeField, Min(0)] int unlockedWidth = 32;
        [SerializeField, Min(0)] int unlockedDepth = 32;

        [Header("Terrain colours")]
        [SerializeField] Color grass = new Color(0.36f, 0.62f, 0.33f);
        [SerializeField] Color dirt = new Color(0.50f, 0.38f, 0.25f);
        [SerializeField] Color sand = new Color(0.82f, 0.76f, 0.52f);
        [SerializeField] Color water = new Color(0.25f, 0.50f, 0.75f);
        [SerializeField] Color rock = new Color(0.50f, 0.50f, 0.52f);
        [SerializeField] Color lockedTint = new Color(0.20f, 0.22f, 0.24f);
        [SerializeField, Range(0f, 1f)] float lockedBlend = 0.65f;

        [Header("Debug grid overlay")]
        [Tooltip("Shown on start in the Editor and Development builds only.")]
        [SerializeField] bool showGridOverlay = true;
        [SerializeField, Min(1)] int overlayMajorLineEvery = 8;

        public int GridWidth => gridWidth;
        public int GridDepth => gridDepth;
        public bool ShowGridOverlay => showGridOverlay;
        public int OverlayMajorLineEvery => overlayMajorLineEvery;

        public GridSettings ToGridSettings() =>
            GridSettings.Centered(gridWidth, gridDepth, cellSize, origin, unlockedWidth, unlockedDepth);

        public TerrainPalette ToTerrainPalette() =>
            new TerrainPalette(grass, dirt, sand, water, rock, lockedTint, lockedBlend);
    }
}
