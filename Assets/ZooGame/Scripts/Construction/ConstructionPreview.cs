using UnityEngine;
using ZooGame.Data;
using ZooGame.World;

namespace ZooGame.Construction
{
    /// <summary>
    /// The uncommitted-construction overlay: one mesh of tinted cell tiles and edge bars. Tools describe what they
    /// would build with AddCell/AddEdge and call Apply; nothing here touches grid or fence data.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ConstructionPreview : MonoBehaviour
    {
        readonly ColoredMeshBuilder _builder = new ColoredMeshBuilder();
        ZooGrid _grid;
        ConstructionConfig _config;
        Mesh _mesh;
        MeshRenderer _renderer;
        bool _dirty;

        /// <summary>Number of cells and edges currently shown.</summary>
        public int ItemCount { get; private set; }

        public void Bind(ZooGrid grid, ConstructionConfig config)
        {
            Unbind();
            _grid = grid;
            _config = config;
            _mesh = new Mesh { name = "Construction Preview" };
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = config.VertexColorMaterial;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
        }

        void Unbind()
        {
            if (_mesh != null) Destroy(_mesh);
            _mesh = null;
        }

        void OnDestroy() => Unbind();

        public void Clear()
        {
            _builder.Clear();
            ItemCount = 0;
            _dirty = true;
        }

        public void AddCell(GridCoord cell, Color color)
        {
            var corner = _grid.GridToWorldCorner(cell);
            float inset = 0.06f * _grid.CellSize, y = _grid.Origin.y + _config.PreviewHeight;
            _builder.FlatQuad(corner.x + inset, corner.z + inset, corner.x + _grid.CellSize - inset, corner.z + _grid.CellSize - inset, y, color);
            ItemCount++;
            _dirty = true;
        }

        public void AddEdge(EdgeCoord edge, Color color)
        {
            var p0 = _grid.GridToWorldCorner(edge.PointStart);
            var p1 = _grid.GridToWorldCorner(edge.PointEnd);
            float t = _config.FenceThickness * 0.9f, y0 = _grid.Origin.y + _config.PreviewHeight;
            _builder.Box(Mathf.Min(p0.x, p1.x) - t, Mathf.Min(p0.z, p1.z) - t, Mathf.Max(p0.x, p1.x) + t, Mathf.Max(p0.z, p1.z) + t, y0, y0 + 0.45f, color);
            ItemCount++;
            _dirty = true;
        }

        /// <summary>Uploads whatever was added since the last Clear. Call once per change, not per item.</summary>
        public void Apply()
        {
            if (!_dirty) return;
            _builder.Apply(_mesh);
            _renderer.enabled = ItemCount > 0;
            _dirty = false;
        }
    }
}
