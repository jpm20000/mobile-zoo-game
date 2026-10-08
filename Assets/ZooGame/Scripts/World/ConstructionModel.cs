using System;
using System.Collections.Generic;

namespace ZooGame.World
{
    /// <summary>
    /// The logical construction state of the zoo beyond placed objects: path cells (stored as grid flags), fences and
    /// gates (stored as cell-edge data) and the enclosures they form. All changes go through here so that
    /// validation runs, enclosures are recalculated once per committed batch, and views get one notification.
    /// Pure data and logic; no scene objects. Previews never touch it.
    /// </summary>
    public sealed class ConstructionModel
    {
        readonly List<EdgeCoord> _changedEdges = new List<EdgeCoord>(32);

        public ConstructionModel(ZooGrid grid)
        {
            Grid = grid;
            Fences = new FenceMap(grid.Width, grid.Depth);
            Enclosures = new EnclosureMap(grid, Fences);
            PathRules = new PathValidator(grid);
            FenceRules = new FenceValidator(grid, Fences);
        }

        public ZooGrid Grid { get; }
        public FenceMap Fences { get; }
        public EnclosureMap Enclosures { get; }
        public PathValidator PathRules { get; }
        public FenceValidator FenceRules { get; }

        /// <summary>Raised once after a batch changed fences/gates, after enclosures were recalculated.</summary>
        public event Action FencesChanged;

        // ---- Paths -----------------------------------------------------------------------------------------

        public BuildResult CanBuildPath(GridCoord cell) => PathRules.Validate(cell);

        /// <summary>Builds a path on every valid cell that does not already have one. Returns how many cells were built.</summary>
        public int BuildPaths(IReadOnlyList<GridCoord> cells)
        {
            int built = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                if (Grid.HasPath(cells[i]) || !PathRules.Validate(cells[i]).IsValid) continue;
                if (Grid.SetPath(cells[i], true)) built++;
            }
            return built;
        }

        /// <summary>Clears the path flag only; occupancy, enclosure ids and fences are untouched.</summary>
        public bool RemovePath(GridCoord cell) => Grid.HasPath(cell) && Grid.SetPath(cell, false);

        public int RemovePaths(IReadOnlyList<GridCoord> cells)
        {
            int removed = 0;
            for (int i = 0; i < cells.Count; i++)
                if (RemovePath(cells[i])) removed++;
            return removed;
        }

        // ---- Fences and gates ------------------------------------------------------------------------------

        public BuildResult CanBuildFence(EdgeCoord edge, EdgeKind kind) => FenceRules.Validate(edge, kind);

        /// <summary>
        /// Builds every valid fence/gate in the batch (a gate replaces a fence on the same edge and vice versa), then
        /// recalculates the affected enclosures once. Returns the number of edges that changed.
        /// </summary>
        public int BuildFences(IReadOnlyList<FenceEdge> edges)
        {
            _changedEdges.Clear();
            for (int i = 0; i < edges.Count; i++)
            {
                var fe = edges[i];
                if (fe.Kind == EdgeKind.None || Fences.Get(fe.Edge) == fe.Kind) continue;
                if (!FenceRules.Validate(fe.Edge, fe.Kind).IsValid) continue;
                if (Fences.Set(fe.Edge, fe.Kind)) _changedEdges.Add(fe.Edge);
            }
            return Commit();
        }

        public int RemoveFences(IReadOnlyList<EdgeCoord> edges)
        {
            _changedEdges.Clear();
            for (int i = 0; i < edges.Count; i++)
                if (Fences.Remove(edges[i])) _changedEdges.Add(edges[i]);
            return Commit();
        }

        public bool RemoveFence(EdgeCoord edge) => RemoveFences(new[] { edge }) > 0;

        int Commit()
        {
            int changed = _changedEdges.Count;
            if (changed == 0) return 0;
            Enclosures.Recalculate(_changedEdges);
            FencesChanged?.Invoke();
            return changed;
        }
    }
}
