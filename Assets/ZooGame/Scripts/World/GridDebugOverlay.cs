using UnityEngine;

namespace ZooGame.World
{
    /// <summary>
    /// Optional debug grid: every cell boundary drawn as GPU line segments from ONE static mesh and one draw call.
    /// Lines sit slightly above the terrain; every Nth line is brighter and the map border brightest.
    /// Hidden = renderer disabled (no cost). Needs a vertex-colour unlit material such as Sprites/Default.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class GridDebugOverlay : MonoBehaviour
    {
        const float LiftAboveTerrain = 0.02f;

        Mesh _mesh;
        MeshRenderer _renderer;

        public bool Visible
        {
            get => _renderer != null && _renderer.enabled;
            set { if (_renderer != null) _renderer.enabled = value; }
        }

        public void Toggle() => Visible = !Visible;

        public void Bind(ZooGrid grid, int majorLineEvery, bool visible)
        {
            if (_mesh != null) Destroy(_mesh);
            _mesh = BuildLines(grid, Mathf.Max(1, majorLineEvery));
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = GetComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = visible;
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        static Mesh BuildLines(ZooGrid grid, int majorEvery)
        {
            int w = grid.Width, d = grid.Depth;
            int lineCount = (w + 1) + (d + 1);
            var verts = new Vector3[lineCount * 2];
            var colors = new Color32[lineCount * 2];
            var indices = new int[lineCount * 2];

            var min = grid.WorldMin;
            var max = grid.WorldMax;
            float y = min.y + LiftAboveTerrain;

            var minor = new Color32(255, 255, 255, 40);
            var major = new Color32(255, 255, 255, 110);
            var border = new Color32(255, 230, 80, 220);

            int n = 0;
            for (int x = 0; x <= w; x++, n += 2)
            {
                float wx = min.x + x * grid.CellSize;
                verts[n] = new Vector3(wx, y, min.z);
                verts[n + 1] = new Vector3(wx, y, max.z);
                colors[n] = colors[n + 1] = x == 0 || x == w ? border : x % majorEvery == 0 ? major : minor;
            }
            for (int z = 0; z <= d; z++, n += 2)
            {
                float wz = min.z + z * grid.CellSize;
                verts[n] = new Vector3(min.x, y, wz);
                verts[n + 1] = new Vector3(max.x, y, wz);
                colors[n] = colors[n + 1] = z == 0 || z == d ? border : z % majorEvery == 0 ? major : minor;
            }
            for (int i = 0; i < indices.Length; i++) indices[i] = i;

            var mesh = new Mesh { name = "Grid Debug Lines" };
            mesh.vertices = verts;
            mesh.colors32 = colors;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
