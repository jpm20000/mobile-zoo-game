using System.Collections.Generic;
using ZooGame.Data;
using ZooGame.Economy;
using ZooGame.World;

namespace ZooGame.Construction
{
    /// <summary>Outcome of a priced construction commit.</summary>
    public readonly struct ConstructionCommit
    {
        /// <summary>Units (cells or edges) actually built or removed.</summary>
        public readonly int Units;
        /// <summary>Money charged (builds) or refunded (demolition).</summary>
        public readonly long Money;
        /// <summary>The batch was refused for lack of Money and nothing was built.</summary>
        public readonly bool Unaffordable;

        public ConstructionCommit(int units, long money, bool unaffordable)
        {
            Units = units;
            Money = money;
            Unaffordable = unaffordable;
        }
    }

    /// <summary>
    /// Prices and commits unit-based construction (paths, fences and gates, demolition). The cost of a batch is the
    /// unit cost from the definition times the units that will really be built; only committed valid units are charged,
    /// and the charge happens after the build succeeded. An unaffordable batch builds nothing and charges nothing.
    /// Demolition returns floor(unit cost x refund rate) Money per removed unit. Pure logic (no scene objects), so the
    /// tools stay thin and this is directly testable. With no <see cref="PurchaseService"/> everything is free.
    /// </summary>
    public sealed class ConstructionPurchaser
    {
        public const string PathDemolitionSource = "path-demolition";
        public const string FenceDemolitionSource = "fence-demolition";

        readonly ConstructionModel _model;
        readonly PurchaseService _purchases;
        readonly ConstructionCatalog _catalog;

        /// <param name="purchases">Null makes construction free and demolition refund nothing.</param>
        /// <param name="catalog">Used to find the definition whose price a removed path/fence was bought at. Null = no refunds.</param>
        public ConstructionPurchaser(ConstructionModel model, PurchaseService purchases, ConstructionCatalog catalog)
        {
            _model = model;
            _purchases = purchases;
            _catalog = catalog;
        }

        // ---- Paths -----------------------------------------------------------------------------------------

        public PurchaseQuote QuotePaths(PathDefinition definition, IReadOnlyList<GridCoord> cells)
        {
            long unit = definition != null ? definition.CostPerCell : 0;
            return Quote(unit, _model.CountBuildablePaths(cells));
        }

        public ConstructionCommit TryBuildPaths(PathDefinition definition, IReadOnlyList<GridCoord> cells)
        {
            string source = definition != null ? definition.Id : "path";
            var quote = QuotePaths(definition, cells);
            if (_purchases != null && !_purchases.Authorize(quote.TotalCost, source))
                return new ConstructionCommit(0, 0, true);

            int built = _model.BuildPaths(cells);
            long charged = 0;
            if (built > 0 && _purchases != null && quote.UnitCost > 0)
            {
                _purchases.Charge(TransactionCategory.PathConstruction, source, quote.UnitCost, built);
                charged = quote.UnitCost * built;
            }
            return new ConstructionCommit(built, charged, false);
        }

        // ---- Fences and gates ------------------------------------------------------------------------------

        public PurchaseQuote QuoteFences(FenceDefinition definition, IReadOnlyList<FenceEdge> edges)
        {
            long unit = definition != null ? definition.CostPerSegment : 0;
            return Quote(unit, _model.CountBuildableFences(edges));
        }

        public ConstructionCommit TryBuildFences(FenceDefinition definition, IReadOnlyList<FenceEdge> edges)
        {
            string source = definition != null ? definition.Id : "fence";
            var quote = QuoteFences(definition, edges);
            if (_purchases != null && !_purchases.Authorize(quote.TotalCost, source))
                return new ConstructionCommit(0, 0, true);

            int built = _model.BuildFences(edges);
            long charged = 0;
            if (built > 0 && _purchases != null && quote.UnitCost > 0)
            {
                _purchases.Charge(TransactionCategory.FenceConstruction, source, quote.UnitCost, built);
                charged = quote.UnitCost * built;
            }
            return new ConstructionCommit(built, charged, false);
        }

        // ---- Demolition ------------------------------------------------------------------------------------

        /// <summary>Money the given paths and fences would return if demolished now.</summary>
        public long RefundQuote(IReadOnlyList<GridCoord> paths, IReadOnlyList<EdgeCoord> edges) =>
            PathRefund(paths) + FenceRefund(edges);

        /// <summary>Removes the paths and fences, then credits the refund for what was actually removed.</summary>
        public ConstructionCommit Demolish(IReadOnlyList<GridCoord> paths, IReadOnlyList<EdgeCoord> edges)
        {
            // Priced from what is built right now (before removal), credited only for what was removed.
            long pathRefund = PathRefund(paths);
            long fenceRefund = FenceRefund(edges);
            int pathsRemoved = _model.RemovePaths(paths);
            int edgesRemoved = _model.RemoveFences(edges);

            long refunded = 0;
            if (_purchases != null)
            {
                if (pathsRemoved > 0) refunded += _purchases.Refund(PathDemolitionSource, pathRefund);
                if (edgesRemoved > 0) refunded += _purchases.Refund(FenceDemolitionSource, fenceRefund);
            }
            return new ConstructionCommit(pathsRemoved + edgesRemoved, refunded, false);
        }

        long PathRefund(IReadOnlyList<GridCoord> paths)
        {
            var def = _catalog != null ? _catalog.DefaultPath : null;
            if (_purchases == null || def == null) return 0;
            long unit = _purchases.RefundAmount(def.CostPerCell);
            if (unit <= 0) return 0;
            int present = 0;
            for (int i = 0; i < paths.Count; i++)
                if (_model.Grid.HasPath(paths[i])) present++;
            return unit * present;
        }

        long FenceRefund(IReadOnlyList<EdgeCoord> edges)
        {
            if (_purchases == null || _catalog == null) return 0;
            long total = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                var kind = _model.Fences.Get(edges[i]);
                if (kind == EdgeKind.None) continue;
                var def = _catalog.FirstOfKind(kind);
                if (def != null) total += _purchases.RefundAmount(def.CostPerSegment);
            }
            return total;
        }

        PurchaseQuote Quote(long unitCost, int units) =>
            _purchases != null ? _purchases.Quote(unitCost, units) : new PurchaseQuote(unitCost, units, long.MaxValue);
    }
}
