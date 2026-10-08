using UnityEngine;

namespace ZooGame.World
{
    /// <summary>Per-terrain colours plus the tint that marks locked land. Built from WorldConfig.</summary>
    public readonly struct TerrainPalette
    {
        public readonly Color32 Grass, Dirt, Sand, Water, Rock;
        public readonly Color32 LockedTint;
        /// <summary>0 = locked land looks like normal terrain, 1 = fully <see cref="LockedTint"/>.</summary>
        public readonly float LockedBlend;

        public TerrainPalette(Color32 grass, Color32 dirt, Color32 sand, Color32 water, Color32 rock, Color32 lockedTint, float lockedBlend)
        {
            Grass = grass;
            Dirt = dirt;
            Sand = sand;
            Water = water;
            Rock = rock;
            LockedTint = lockedTint;
            LockedBlend = lockedBlend;
        }

        public Color32 Get(TerrainType t)
        {
            switch (t)
            {
                case TerrainType.Dirt: return Dirt;
                case TerrainType.Sand: return Sand;
                case TerrainType.Water: return Water;
                case TerrainType.Rock: return Rock;
                default: return Grass;
            }
        }
    }

    /// <summary>
    /// Placeholder terrain: ONE quad spanning the whole grid, textured with a point-filtered texture that has one
    /// texel per cell (colour = terrain, tinted when locked). Rebuilt only when the grid changes, in LateUpdate,
    /// so a batch of edits costs one texture upload. Assumes the Zoo-scene terrain material is a URP Lit/Unlit
    /// material (uses _BaseMap / _BaseColor through a property block, so the shared material is never instanced).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class TerrainView : MonoBehaviour
    {
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        ZooGrid _grid;
        TerrainPalette _palette;
        Texture2D _texture;
        Color32[] _pixels;
        Mesh _mesh;
        MaterialPropertyBlock _block;
        bool _dirty;

        public void Bind(ZooGrid grid, TerrainPalette palette)
        {
            Unbind();
            _grid = grid;
            _palette = palette;

            _texture = new Texture2D(grid.Width, grid.Depth, TextureFormat.RGBA32, false)
            {
                name = "Terrain Cells",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _pixels = new Color32[grid.CellCount];
            _mesh = BuildQuad(grid);

            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _block = new MaterialPropertyBlock();
            _block.SetTexture(BaseMapId, _texture);
            _block.SetColor(BaseColorId, Color.white);
            GetComponent<MeshRenderer>().SetPropertyBlock(_block);

            _grid.Changed += MarkDirty;
            Rebuild();
        }

        void Unbind()
        {
            if (_grid != null) _grid.Changed -= MarkDirty;
            _grid = null;
            if (_texture != null) Destroy(_texture);
            if (_mesh != null) Destroy(_mesh);
            _texture = null;
            _mesh = null;
        }

        void OnDestroy() => Unbind();

        void MarkDirty()
        {
            _dirty = true;
            enabled = true;
        }

        void LateUpdate()
        {
            if (_dirty) Rebuild();
            enabled = false; // idle until the next grid change
        }

        void Rebuild()
        {
            _dirty = false;
            int w = _grid.Width, d = _grid.Depth;
            for (int z = 0; z < d; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    var cell = _grid.GetCell(new GridCoord(x, z));
                    var c = _palette.Get(cell.Terrain);
                    if (!cell.IsUnlocked) c = Color32.Lerp(c, _palette.LockedTint, _palette.LockedBlend);
                    _pixels[z * w + x] = c;
                }
            }
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        /// <summary>Upward-facing quad covering the grid; texel (x, z) maps exactly onto cell (x, z).</summary>
        static Mesh BuildQuad(ZooGrid grid)
        {
            var min = grid.WorldMin;
            var max = grid.WorldMax;
            var mesh = new Mesh { name = "Terrain Quad" };
            mesh.vertices = new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, min.y, min.z)
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
