using UnityEngine;
using ZooGame.Data;
using ZooGame.World;

namespace ZooGame.Construction
{
    /// <summary>
    /// Draws every fence and gate edge as one mesh. Each rail is lengthened by half its thickness at both ends so
    /// rails meeting at a corner or junction join cleanly. Gates are shorter, a different colour, and have taller
    /// posts. Fences that are not part of a closed enclosure are tinted so open layouts are visible at a glance.
    /// Rebuilt in LateUpdate after the model reports a fence change.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class FenceRenderer : MonoBehaviour
    {
        readonly ColoredMeshBuilder _builder = new ColoredMeshBuilder();
        ConstructionModel _model;
        ZooGrid _grid;
        ConstructionConfig _config;
        Mesh _mesh;
        FenceDefinition _fence, _gate;

        public int RebuildCount { get; private set; }

        public void Bind(ConstructionModel model, ConstructionConfig config, FenceDefinition fence, FenceDefinition gate)
        {
            Unbind();
            _model = model;
            _grid = model.Grid;
            _config = config;
            _fence = fence;
            _gate = gate;
            _mesh = new Mesh { name = "Fences" };
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            var r = GetComponent<MeshRenderer>();
            r.sharedMaterial = config.VertexColorMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            model.FencesChanged += MarkDirty;
            Rebuild();
            enabled = false;
        }

        void Unbind()
        {
            if (_model != null) _model.FencesChanged -= MarkDirty;
            if (_mesh != null) Destroy(_mesh);
            _model = null;
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
            _model.Fences.ForEach(AddEdge);
            _builder.Apply(_mesh);
            RebuildCount++;
        }

        void AddEdge(EdgeCoord edge, EdgeKind kind)
        {
            bool gate = kind == EdgeKind.Gate;
            var def = gate ? _gate : _fence;
            Color color = def != null ? def.Color : (gate ? new Color(0.3f, 0.5f, 0.85f) : new Color(0.55f, 0.38f, 0.22f));
            float height = def != null ? def.Height : 0.7f;
            if (!_model.Enclosures.IsClosedBoundary(edge, out _)) color *= _config.OpenFenceTint;
            color.a = 1f;

            var p0 = _grid.GridToWorldCorner(edge.PointStart);
            var p1 = _grid.GridToWorldCorner(edge.PointEnd);
            float y0 = _grid.Origin.y;
            float t = _config.FenceThickness * 0.5f;
            if (gate) t *= 0.6f;

            float minX = Mathf.Min(p0.x, p1.x) - t, maxX = Mathf.Max(p0.x, p1.x) + t;
            float minZ = Mathf.Min(p0.z, p1.z) - t, maxZ = Mathf.Max(p0.z, p1.z) + t;
            if (gate)
            {
                // A shorter, raised rail between two taller posts.
                float post = _config.FenceThickness * 0.9f;
                _builder.Box(minX, minZ, maxX, maxZ, y0 + height * 0.25f, y0 + height * _config.GateHeightFraction, color);
                AddPost(p0, post, y0 + height * 1.15f, color);
                AddPost(p1, post, y0 + height * 1.15f, color);
            }
            else
            {
                _builder.Box(minX, minZ, maxX, maxZ, y0, y0 + height, color);
            }
        }

        void AddPost(Vector3 centre, float halfSize, float top, Color color)
        {
            var c = new Color(color.r * 0.85f, color.g * 0.85f, color.b * 0.85f, 1f);
            _builder.Box(centre.x - halfSize, centre.z - halfSize, centre.x + halfSize, centre.z + halfSize, _grid.Origin.y, top, c);
        }
    }
}
