using UnityEngine;
using ZooGame.Data;
using ZooGame.World;

namespace ZooGame.Construction
{
    /// <summary>
    /// Draws every path cell as one mesh: a centre tile plus an arm toward each connected neighbour, so straights,
    /// corners, T-junctions, crosses and dead ends fall out of the connection mask with no per-shape art and no object
    /// per cell. Rebuilt in LateUpdate, at most once per frame, and only after a path actually changed.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PathRenderer : MonoBehaviour
    {
        readonly ColoredMeshBuilder _builder = new ColoredMeshBuilder();
        ZooGrid _grid;
        Mesh _mesh;
        Color _color;
        float _width, _y;

        public int LastBuiltCellCount { get; private set; }
        public int RebuildCount { get; private set; }

        public void Bind(ZooGrid grid, ConstructionConfig config, PathDefinition definition)
        {
            Unbind();
            _grid = grid;
            _color = definition != null ? definition.Color : new Color(0.78f, 0.68f, 0.5f);
            _width = definition != null ? definition.Width : 0.7f;
            _y = grid.Origin.y + config.PathHeight;
            _mesh = new Mesh { name = "Paths" };
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            var r = GetComponent<MeshRenderer>();
            r.sharedMaterial = config.VertexColorMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            grid.PathsChanged += MarkDirty;
            Rebuild();
            enabled = false;
        }

        void Unbind()
        {
            if (_grid != null) _grid.PathsChanged -= MarkDirty;
            if (_mesh != null) Destroy(_mesh);
            _grid = null;
        }

        void OnDestroy() => Unbind();

        void MarkDirty() => enabled = true;

        void LateUpdate()
        {
            Rebuild();
            enabled = false;
        }

        void Rebuild()
        {
            _builder.Clear();
            float cs = _grid.CellSize;
            float half = _width * 0.5f * cs;
            int count = 0;
            for (int z = 0; z < _grid.Depth; z++)
            for (int x = 0; x < _grid.Width; x++)
            {
                var cell = new GridCoord(x, z);
                if (!_grid.HasPath(cell)) continue;
                count++;
                var c = _grid.GridToWorld(cell);
                int mask = PathConnectivity.GetMask(_grid, cell);
                float cx = c.x, cz = c.z, e = cs * 0.5f;
                _builder.FlatQuad(cx - half, cz - half, cx + half, cz + half, _y, _color);
                if ((mask & 1) != 0) _builder.FlatQuad(cx - half, cz + half, cx + half, cz + e, _y, _color); // north
                if ((mask & 2) != 0) _builder.FlatQuad(cx + half, cz - half, cx + e, cz + half, _y, _color); // east
                if ((mask & 4) != 0) _builder.FlatQuad(cx - half, cz - e, cx + half, cz - half, _y, _color); // south
                if ((mask & 8) != 0) _builder.FlatQuad(cx - e, cz - half, cx - half, cz + half, _y, _color); // west
            }
            _builder.Apply(_mesh);
            LastBuiltCellCount = count;
            RebuildCount++;
        }
    }
}
