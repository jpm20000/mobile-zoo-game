using System;
using System.Collections.Generic;
using ZooGame.Animals;
using ZooGame.Placement;
using ZooGame.Visitors;

namespace ZooGame.Progression
{
    /// <summary>
    /// Gathers the five rating inputs from the existing systems and turns them into a <see cref="ZooRatingBreakdown"/>
    /// with <see cref="ZooRatingMath"/>. It owns only the rolling visitor happiness history; everything else is read
    /// from the authoritative registries, never copied. Plain C#: <see cref="ZooRatingDriver"/> pumps
    /// <see cref="Tick"/> from the GameClock, so the rating stands still while paused.
    ///
    /// Every collaborator except the settings is optional (a missing one contributes 0). Facilities count only when
    /// the destination is enabled (placed, and for stalls staffed by a working vendor through the staff gate) and a
    /// walkable path connects it to the entrance.
    /// </summary>
    public sealed class ZooRatingService
    {
        readonly ZooRatingSettings _settings;
        readonly IAnimalRegistry _animals;
        readonly IVisitorRegistry _visitors;
        readonly PlacementMap _placement;
        readonly VisitorDestinationService _destinations;

        readonly float[] _happinessSamples;
        int _sampleCount;
        int _sampleCursor;

        readonly HashSet<string> _species = new HashSet<string>();
        readonly int[] _facilityCounts = new int[ZooRatingSettings.FacilityCategoryCount];
        float _sinceRecalculate;

        public ZooRatingService(ZooRatingSettings settings, IAnimalRegistry animals = null, IVisitorRegistry visitors = null,
            PlacementMap placement = null, VisitorDestinationService destinations = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            _animals = animals;
            _visitors = visitors;
            _placement = placement;
            _destinations = destinations;
            _happinessSamples = new float[_settings.HappinessSampleCapacity];
            Recalculate();
        }

        /// <summary>The last calculated breakdown. Components and rating are unrounded.</summary>
        public ZooRatingBreakdown Current { get; private set; }

        /// <summary>The underlying 0-100 rating. Use this, not a rounded value, for unlocks.</summary>
        public double Rating => Current.Rating;
        public int Stars => Current.Stars;
        public ZooRatingSettings Settings => _settings;

        /// <summary>Samples currently in the rolling happiness window.</summary>
        public int HappinessSampleCount => _sampleCount;

        /// <summary>Raised after a recalculation whose result differs from the previous one.</summary>
        public event Action<ZooRatingBreakdown> Changed;

        /// <summary>Raised with (oldStars, newStars) when the star level changes.</summary>
        public event Action<int, int> StarsChanged;

        // ---- Visitor happiness history ---------------------------------------------------------------------

        /// <summary>Adds one sample (clamped to 0-100, NaN = 0), dropping the oldest once the window is full.</summary>
        public void RecordVisitorHappiness(float happiness)
        {
            float v = !(happiness > 0f) ? 0f : happiness > 100f ? 100f : happiness;
            _happinessSamples[_sampleCursor] = v;
            _sampleCursor = (_sampleCursor + 1) % _happinessSamples.Length;
            if (_sampleCount < _happinessSamples.Length) _sampleCount++;
        }

        /// <summary>Records a leaving visitor's final happiness. Hook this to <c>VisitorDecisionService.Left</c>.</summary>
        public void RecordVisitorDeparture(VisitorInstance visitor)
        {
            if (visitor != null) RecordVisitorHappiness(visitor.Happiness);
        }

        public void ClearHappinessHistory()
        {
            _sampleCount = 0;
            _sampleCursor = 0;
        }

        // ---- Calculation -----------------------------------------------------------------------------------

        /// <summary>Advances the recalculation timer by simulated seconds; recalculates when the interval has elapsed.</summary>
        public void Tick(float deltaSeconds)
        {
            if (!(deltaSeconds > 0f)) return;
            _sinceRecalculate += deltaSeconds;
            if (_sinceRecalculate < _settings.RecalculateIntervalSeconds) return;
            Recalculate();
        }

        /// <summary>Recomputes everything from the live systems now and raises events for what changed.</summary>
        public ZooRatingBreakdown Recalculate()
        {
            _sinceRecalculate = 0f;
            var s = _settings;
            var next = ZooRatingMath.Compose(
                CalculateWelfare(),
                CalculateHappiness(),
                ZooRatingMath.AnimalVariety(CountSpecies(), s),
                ZooRatingMath.ZooBeauty(CalculateBeauty(), s),
                CalculateFacilities(),
                s);

            var old = Current;
            bool first = _hasCalculated;
            Current = next;
            _hasCalculated = true;
            if (first && !SameScores(old, next)) Changed?.Invoke(next);
            if (first && old.Stars != next.Stars) StarsChanged?.Invoke(old.Stars, next.Stars);
            return next;
        }

        bool _hasCalculated;

        static bool SameScores(in ZooRatingBreakdown a, in ZooRatingBreakdown b) =>
            a.AnimalWelfare == b.AnimalWelfare && a.VisitorHappiness == b.VisitorHappiness
            && a.AnimalVariety == b.AnimalVariety && a.ZooBeauty == b.ZooBeauty
            && a.Facilities == b.Facilities && a.Rating == b.Rating;

        /// <summary>Whether the current underlying rating has reached the given star level (1 is always unlocked).</summary>
        public bool IsStarUnlocked(int stars) => ZooRatingMath.IsStarReached(Current.Rating, stars, _settings);

        // ---- Inputs ----------------------------------------------------------------------------------------

        // Animals without an enclosure or in open/invalid enclosures still count, using their current welfare.
        double CalculateWelfare()
        {
            if (_animals == null) return 0.0;
            double sum = 0.0;
            int count = 0;
            foreach (var a in _animals.GetAll())
            {
                if (a == null) continue;
                sum += ZooRatingMath.Clamp100(a.OverallWelfare);
                count++;
            }
            return ZooRatingMath.AverageScore(sum, count);
        }

        int CountSpecies()
        {
            if (_animals == null) return 0;
            _species.Clear();
            foreach (var a in _animals.GetAll())
                if (a != null && !string.IsNullOrEmpty(a.SpeciesId)) _species.Add(a.SpeciesId);
            return _species.Count;
        }

        double CalculateHappiness()
        {
            double historySum = 0.0;
            for (int i = 0; i < _sampleCount; i++) historySum += _happinessSamples[i];

            double activeSum = 0.0;
            int active = 0;
            if (_sampleCount == 0 && _visitors != null)
            {
                var all = _visitors.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] == null) continue;
                    activeSum += all[i].Happiness;
                    active++;
                }
            }
            return ZooRatingMath.VisitorHappiness(historySum, _sampleCount, activeSum, active);
        }

        // Only placed objects are in the map; a removed object is gone from it and so contributes nothing.
        double CalculateBeauty()
        {
            if (_placement == null) return 0.0;
            double total = 0.0;
            foreach (var obj in _placement.Objects)
            {
                if (obj == null || obj.Definition == null) continue;
                float b = obj.Definition.BeautyValue;
                if (b > 0f) total += b; // negative and NaN contribute 0
            }
            return total;
        }

        double CalculateFacilities()
        {
            Array.Clear(_facilityCounts, 0, _facilityCounts.Length);
            if (_destinations != null && _destinations.TryGetEntrance(out var entrance) && entrance.IsPathAccessible)
            {
                var from = entrance.InteractionPoint;
                var all = _destinations.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var d = all[i];
                    int category = CategoryOf(d.Kind);
                    if (category < 0) continue;
                    // IsReachable requires Enabled, so closed and unstaffed stalls are excluded here.
                    if (_destinations.IsReachable(d, from)) _facilityCounts[category]++;
                }
            }
            return ZooRatingMath.Facilities(_facilityCounts, _settings);
        }

        /// <summary>Per-category facility counts from the last calculation (for the HUD and diagnostics).</summary>
        public int FacilityCount(RatingFacility category) => _facilityCounts[(int)category];

        static int CategoryOf(VisitorDestinationKind kind)
        {
            switch (kind)
            {
                case VisitorDestinationKind.FoodStall: return (int)RatingFacility.Food;
                case VisitorDestinationKind.DrinkStall: return (int)RatingFacility.Drink;
                case VisitorDestinationKind.Toilet: return (int)RatingFacility.Toilets;
                case VisitorDestinationKind.Bench: return (int)RatingFacility.Benches;
                default: return -1;
            }
        }
    }
}
