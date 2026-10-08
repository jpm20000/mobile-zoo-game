using System.Collections.Generic;
using ZooGame.Data;
using ZooGame.World;

namespace ZooGame.Placement
{
    public enum PlacementFailure
    {
        None = 0,
        InvalidDefinition,
        OutOfBounds,
        LockedLand,
        Occupied,
        /// <summary>Reported by rules added by later milestones (terrain, path access, prerequisites...). See <see cref="PlacementResult.Detail"/>.</summary>
        RuleFailed
    }

    public readonly struct PlacementResult
    {
        public readonly PlacementFailure Failure;
        /// <summary>The first offending cell, when the failure is cell-specific.</summary>
        public readonly GridCoord BlockingCell;
        /// <summary>Optional human-readable reason from custom rules.</summary>
        public readonly string Detail;

        PlacementResult(PlacementFailure failure, GridCoord cell, string detail)
        {
            Failure = failure;
            BlockingCell = cell;
            Detail = detail;
        }

        public bool IsValid => Failure == PlacementFailure.None;

        public static PlacementResult Valid => default;

        public static PlacementResult Fail(PlacementFailure failure, GridCoord cell = default, string detail = null) =>
            new PlacementResult(failure, cell, detail);

        public override string ToString() => IsValid ? "Valid" : Failure + (Detail != null ? " (" + Detail + ")" : " at " + BlockingCell);
    }

    /// <summary>Everything a rule needs to judge one candidate placement.</summary>
    public readonly struct PlacementQuery
    {
        public readonly ZooGrid Grid;
        public readonly PlacementMap Map;
        public readonly PlaceableDefinition Definition;
        public readonly GridCoord Origin;
        public readonly Rotation90 Rotation;
        /// <summary>Rotated footprint extent along X.</summary>
        public readonly int Width;
        /// <summary>Rotated footprint extent along Z.</summary>
        public readonly int Height;
        /// <summary>The object being moved or rotated; its own cells never block it. Null for new placements.</summary>
        public readonly PlacedObject Ignore;

        public PlacementQuery(PlacementMap map, PlaceableDefinition definition, GridCoord origin, Rotation90 rotation, PlacedObject ignore)
        {
            Map = map;
            Grid = map.Grid;
            Definition = definition;
            Origin = origin;
            Rotation = rotation;
            Ignore = ignore;
            Footprint.RotatedSize(definition.FootprintWidth, definition.FootprintHeight, rotation, out Width, out Height);
        }
    }

    /// <summary>
    /// One placement rule. Add new rules (terrain requirements, path access, enclosure requirements, building
    /// prerequisites) by implementing this and calling <see cref="PlacementValidator.AddRule"/>; nothing else changes.
    /// Rules should be allocation-free and must not throw for out-of-grid cells.
    /// </summary>
    public interface IPlacementRule
    {
        PlacementResult Evaluate(in PlacementQuery query);
    }

    /// <summary>Every footprint cell must be inside the grid.</summary>
    public sealed class BoundsRule : IPlacementRule
    {
        public PlacementResult Evaluate(in PlacementQuery q)
        {
            for (int z = 0; z < q.Height; z++)
            for (int x = 0; x < q.Width; x++)
            {
                var c = new GridCoord(q.Origin.X + x, q.Origin.Z + z);
                if (!q.Grid.IsInsideGrid(c)) return PlacementResult.Fail(PlacementFailure.OutOfBounds, c);
            }
            return PlacementResult.Valid;
        }
    }

    /// <summary>Every footprint cell must be unlocked.</summary>
    public sealed class UnlockedLandRule : IPlacementRule
    {
        public PlacementResult Evaluate(in PlacementQuery q)
        {
            for (int z = 0; z < q.Height; z++)
            for (int x = 0; x < q.Width; x++)
            {
                var c = new GridCoord(q.Origin.X + x, q.Origin.Z + z);
                if (!q.Grid.IsUnlocked(c)) return PlacementResult.Fail(PlacementFailure.LockedLand, c);
            }
            return PlacementResult.Valid;
        }
    }

    /// <summary>No footprint cell may be occupied, except by the object being moved.</summary>
    public sealed class UnoccupiedRule : IPlacementRule
    {
        public PlacementResult Evaluate(in PlacementQuery q)
        {
            int ignoreId = q.Ignore != null ? q.Ignore.Id : PlacementMap.NoOwner;
            for (int z = 0; z < q.Height; z++)
            for (int x = 0; x < q.Width; x++)
            {
                var c = new GridCoord(q.Origin.X + x, q.Origin.Z + z);
                if (!q.Grid.IsOccupied(c)) continue;
                if (ignoreId != PlacementMap.NoOwner && q.Map.OwnerIdAt(c) == ignoreId) continue;
                return PlacementResult.Fail(PlacementFailure.Occupied, c);
            }
            return PlacementResult.Valid;
        }
    }

    /// <summary>Objects cannot be built on path cells (remove the path first).</summary>
    public sealed class NoPathUnderObjectRule : IPlacementRule
    {
        public PlacementResult Evaluate(in PlacementQuery q)
        {
            for (int z = 0; z < q.Height; z++)
            for (int x = 0; x < q.Width; x++)
            {
                var c = new GridCoord(q.Origin.X + x, q.Origin.Z + z);
                if (q.Grid.HasPath(c)) return PlacementResult.Fail(PlacementFailure.RuleFailed, c, "path in the way");
            }
            return PlacementResult.Valid;
        }
    }

    /// <summary>
    /// Runs an ordered list of <see cref="IPlacementRule"/>s and reports the first failure. Defaults: bounds, locked
    /// land, occupancy, no path underneath. UI and input code ask this; they never inspect cells themselves.
    /// </summary>
    public sealed class PlacementValidator
    {
        readonly PlacementMap _map;
        readonly List<IPlacementRule> _rules = new List<IPlacementRule>(4);

        public PlacementValidator(PlacementMap map)
        {
            _map = map;
            _rules.Add(new BoundsRule());
            _rules.Add(new UnlockedLandRule());
            _rules.Add(new UnoccupiedRule());
            _rules.Add(new NoPathUnderObjectRule());
        }

        public int RuleCount => _rules.Count;

        public void AddRule(IPlacementRule rule)
        {
            if (rule != null) _rules.Add(rule);
        }

        public bool RemoveRule(IPlacementRule rule) => _rules.Remove(rule);

        /// <param name="ignore">The object being moved or rotated (its own cells do not block it), or null.</param>
        public PlacementResult Validate(PlaceableDefinition definition, GridCoord origin, Rotation90 rotation, PlacedObject ignore = null)
        {
            if (definition == null) return PlacementResult.Fail(PlacementFailure.InvalidDefinition);
            var query = new PlacementQuery(_map, definition, origin, rotation, ignore);
            for (int i = 0; i < _rules.Count; i++)
            {
                var result = _rules[i].Evaluate(query);
                if (!result.IsValid) return result;
            }
            return PlacementResult.Valid;
        }
    }
}
