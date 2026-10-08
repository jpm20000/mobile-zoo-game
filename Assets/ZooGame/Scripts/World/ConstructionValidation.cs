using System.Collections.Generic;

namespace ZooGame.World
{
    public enum BuildFailure : byte
    {
        None = 0,
        OutOfBounds,
        LockedLand,
        Occupied,
        /// <summary>Reported by rules added by later milestones. See <see cref="BuildResult.Detail"/>.</summary>
        RuleFailed
    }

    public readonly struct BuildResult
    {
        public readonly BuildFailure Failure;
        public readonly string Detail;

        BuildResult(BuildFailure failure, string detail)
        {
            Failure = failure;
            Detail = detail;
        }

        public bool IsValid => Failure == BuildFailure.None;
        public static BuildResult Valid => default;
        public static BuildResult Fail(BuildFailure failure, string detail = null) => new BuildResult(failure, detail);
        public override string ToString() => IsValid ? "Valid" : Failure + (Detail != null ? " (" + Detail + ")" : "");
    }

    /// <summary>A rule deciding whether a path may be built on a cell. Add new rules to <see cref="PathValidator"/> without touching the tool.</summary>
    public interface IPathRule
    {
        BuildResult Evaluate(ZooGrid grid, GridCoord cell);
    }

    /// <summary>A rule deciding whether a fence or gate may be built on an edge.</summary>
    public interface IFenceRule
    {
        BuildResult Evaluate(ZooGrid grid, FenceMap fences, EdgeCoord edge, EdgeKind kind);
    }

    public sealed class PathBoundsRule : IPathRule
    {
        public BuildResult Evaluate(ZooGrid grid, GridCoord cell) =>
            grid.IsInsideGrid(cell) ? BuildResult.Valid : BuildResult.Fail(BuildFailure.OutOfBounds);
    }

    public sealed class PathUnlockedRule : IPathRule
    {
        public BuildResult Evaluate(ZooGrid grid, GridCoord cell) =>
            grid.IsUnlocked(cell) ? BuildResult.Valid : BuildResult.Fail(BuildFailure.LockedLand);
    }

    /// <summary>Paths cannot be built under placed objects (any occupied cell).</summary>
    public sealed class PathNotOccupiedRule : IPathRule
    {
        public BuildResult Evaluate(ZooGrid grid, GridCoord cell) =>
            grid.IsOccupied(cell) ? BuildResult.Fail(BuildFailure.Occupied) : BuildResult.Valid;
    }

    /// <summary>Ordered path rules; the first failure wins. Defaults: inside the grid, unlocked, not occupied.</summary>
    public sealed class PathValidator
    {
        readonly ZooGrid _grid;
        readonly List<IPathRule> _rules = new List<IPathRule>(4);

        public PathValidator(ZooGrid grid)
        {
            _grid = grid;
            _rules.Add(new PathBoundsRule());
            _rules.Add(new PathUnlockedRule());
            _rules.Add(new PathNotOccupiedRule());
        }

        public void AddRule(IPathRule rule)
        {
            if (rule != null) _rules.Add(rule);
        }

        public bool RemoveRule(IPathRule rule) => _rules.Remove(rule);

        public BuildResult Validate(GridCoord cell)
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                var r = _rules[i].Evaluate(_grid, cell);
                if (!r.IsValid) return r;
            }
            return BuildResult.Valid;
        }
    }

    public sealed class FenceBoundsRule : IFenceRule
    {
        public BuildResult Evaluate(ZooGrid grid, FenceMap fences, EdgeCoord edge, EdgeKind kind) =>
            fences.IsValidEdge(edge) ? BuildResult.Valid : BuildResult.Fail(BuildFailure.OutOfBounds);
    }

    /// <summary>An edge needs at least one unlocked neighbouring cell, so the border of the unlocked area can be fenced but locked land cannot.</summary>
    public sealed class FenceUnlockedRule : IFenceRule
    {
        public BuildResult Evaluate(ZooGrid grid, FenceMap fences, EdgeCoord edge, EdgeKind kind) =>
            grid.IsUnlocked(edge.CellA) || grid.IsUnlocked(edge.CellB) ? BuildResult.Valid : BuildResult.Fail(BuildFailure.LockedLand);
    }

    /// <summary>Ordered fence rules; the first failure wins. Defaults: valid edge, touches unlocked land.</summary>
    public sealed class FenceValidator
    {
        readonly ZooGrid _grid;
        readonly FenceMap _fences;
        readonly List<IFenceRule> _rules = new List<IFenceRule>(4);

        public FenceValidator(ZooGrid grid, FenceMap fences)
        {
            _grid = grid;
            _fences = fences;
            _rules.Add(new FenceBoundsRule());
            _rules.Add(new FenceUnlockedRule());
        }

        public void AddRule(IFenceRule rule)
        {
            if (rule != null) _rules.Add(rule);
        }

        public bool RemoveRule(IFenceRule rule) => _rules.Remove(rule);

        public BuildResult Validate(EdgeCoord edge, EdgeKind kind)
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                var r = _rules[i].Evaluate(_grid, _fences, edge, kind);
                if (!r.IsValid) return r;
            }
            return BuildResult.Valid;
        }
    }
}
