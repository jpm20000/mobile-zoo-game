using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZooGame.Construction
{
    /// <summary>
    /// Accumulates flat quads and axis-aligned boxes with per-vertex colours (and baked face shading, since the
    /// material is unlit) into reusable lists, then uploads them to a mesh. Reused across rebuilds so rebuilding
    /// allocates nothing once the lists have grown.
    /// </summary>
    public sealed class ColoredMeshBuilder
    {
        readonly List<Vector3> _vertices = new List<Vector3>(1024);
        readonly List<Color32> _colors = new List<Color32>(1024);
        readonly List<int> _triangles = new List<int>(2048);

        public int VertexCount => _vertices.Count;

        public void Clear()
        {
            _vertices.Clear();
            _colors.Clear();
            _triangles.Clear();
        }

        /// <summary>A quad facing up on the XZ plane. Corners are (minX,minZ) to (maxX,maxZ) at height y.</summary>
        public void FlatQuad(float minX, float minZ, float maxX, float maxZ, float y, Color color)
        {
            Face(new Vector3(minX, y, minZ), new Vector3(minX, y, maxZ), new Vector3(maxX, y, maxZ), new Vector3(maxX, y, minZ), color);
        }

        /// <summary>An axis-aligned box from <paramref name="baseY"/> to <paramref name="topY"/>. Top is full colour; sides are darker.</summary>
        public void Box(float minX, float minZ, float maxX, float maxZ, float baseY, float topY, Color color)
        {
            Color sideX = Shade(color, 0.78f), sideZ = Shade(color, 0.62f);
            var a = new Vector3(minX, baseY, minZ); var b = new Vector3(minX, baseY, maxZ);
            var c = new Vector3(maxX, baseY, maxZ); var d = new Vector3(maxX, baseY, minZ);
            var e = new Vector3(minX, topY, minZ); var f = new Vector3(minX, topY, maxZ);
            var g = new Vector3(maxX, topY, maxZ); var h = new Vector3(maxX, topY, minZ);
            Face(e, f, g, h, color);       // top
            Face(a, e, h, d, sideZ);       // -Z side
            Face(c, g, f, b, sideZ);       // +Z side
            Face(b, f, e, a, sideX);       // -X side
            Face(d, h, g, c, sideX);       // +X side
        }

        public void Apply(Mesh mesh)
        {
            mesh.Clear();
            if (_vertices.Count == 0) return;
            mesh.indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
        }

        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            int i = _vertices.Count;
            _vertices.Add(a); _vertices.Add(b); _vertices.Add(c); _vertices.Add(d);
            Color32 col = color;
            _colors.Add(col); _colors.Add(col); _colors.Add(col); _colors.Add(col);
            _triangles.Add(i); _triangles.Add(i + 1); _triangles.Add(i + 2);
            _triangles.Add(i); _triangles.Add(i + 2); _triangles.Add(i + 3);
        }

        static Color Shade(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a);
    }
}
