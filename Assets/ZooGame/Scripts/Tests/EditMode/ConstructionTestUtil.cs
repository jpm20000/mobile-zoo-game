using System.Collections.Generic;
using UnityEngine;
using ZooGame.World;

namespace ZooGame.Tests.EditMode
{
    /// <summary>Shared fixture: a 16x16 grid whose unlocked block is the centred 8x8 (cells 4..11 on both axes).</summary>
    public sealed class ConstructionFixture
    {
        public ZooGrid Grid { get; }
        public ConstructionModel Model { get; }

        public ConstructionFixture()
        {
            Grid = new ZooGrid(GridSettings.Centered(16, 16, 1f, Vector3.zero, 8, 8));
            Model = new ConstructionModel(Grid);
        }

        public int BuildPaths(params (int x, int z)[] cells)
        {
            var list = new List<GridCoord>();
            foreach (var c in cells) list.Add(new GridCoord(c.x, c.z));
            return Model.BuildPaths(list);
        }

        public int PathCount()
        {
            int n = 0;
            for (int z = 0; z < Grid.Depth; z++)
            for (int x = 0; x < Grid.Width; x++)
                if (Grid.HasPath(new GridCoord(x, z))) n++;
            return n;
        }

        /// <summary>Edges of the w x d cell rectangle whose minimum corner cell is (minX, minZ).</summary>
        public static List<FenceEdge> RectEdges(int minX, int minZ, int w, int d, EdgeKind kind = EdgeKind.Fence)
        {
            var edges = new List<FenceEdge>();
            for (int x = minX; x < minX + w; x++)
            {
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.X, x, minZ), kind));
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.X, x, minZ + d), kind));
            }
            for (int z = minZ; z < minZ + d; z++)
            {
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.Z, minX, z), kind));
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.Z, minX + w, z), kind));
            }
            return edges;
        }

        public int BuildRect(int minX, int minZ, int w, int d) => Model.BuildFences(RectEdges(minX, minZ, w, d));

        public int EnclosureIdAt(int x, int z) => Grid.GetCell(new GridCoord(x, z)).EnclosureId;
    }
}
