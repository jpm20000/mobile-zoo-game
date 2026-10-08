using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZooGame.Animals
{
    /// <summary>
    /// The welfare simulation. Plain C#, no per-animal MonoBehaviour and no Update: <see cref="AnimalNeedsDriver"/> feeds
    /// it GameClock time through <see cref="Advance"/>, which accumulates simulation seconds and runs one scheduled tick
    /// per <see cref="AnimalNeedsConfig.TickIntervalSeconds"/>. A paused clock supplies zero time, so nothing advances;
    /// 2x/3x supply proportionally more, so the same number of ticks happens in less real time.
    ///
    /// Each tick, for every registered animal (spawned or not):
    ///  1. hunger/thirst decay, plus recovery when the enclosure has usable food/drinking water;
    ///  2. habitat conditions are read from the cached <see cref="EnclosureHabitat"/>;
    ///  3. social is set from group size, enrichment and comfort move gradually to their targets;
    ///  4. habitat suitability is recalculated from the factor scores;
    ///  5. welfare is recalculated and capped.
    /// When the habitat changes (fence, terrain, objects, residents) animals are re-evaluated once, with zero elapsed
    /// time, on the next <see cref="Advance"/>, even while paused: that is recalculation, not simulation.
    /// </summary>
    public sealed class AnimalNeedsSystem : IDisposable
    {
        readonly IAnimalRegistry _registry;
        readonly IAnimalDefinitionResolver _definitions;
        readonly IAnimalEnclosureService _enclosures;
        readonly IEnclosureHabitatService _habitats;
        readonly AnimalNeedsConfig _config;
        readonly Dictionary<string, AnimalWelfareReport> _reports = new Dictionary<string, AnimalWelfareReport>();
        float _accumulated;
        bool _refresh = true;

        public AnimalNeedsSystem(IAnimalRegistry registry, IAnimalDefinitionResolver definitions, IAnimalEnclosureService enclosures,
            IEnclosureHabitatService habitats, AnimalNeedsConfig config)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _enclosures = enclosures ?? throw new ArgumentNullException(nameof(enclosures));
            _habitats = habitats ?? throw new ArgumentNullException(nameof(habitats));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _habitats.Changed += MarkRefresh;
            _enclosures.EnclosuresChanged += MarkRefresh;
            _registry.MembershipChanged += OnMembership;
            _registry.Unregistered += OnUnregistered;
        }

        /// <summary>Raised after an animal was evaluated (its needs and report are current).</summary>
        public event Action<AnimalInstance> Evaluated;

        /// <summary>Scheduled ticks run so far (diagnostics and tests).</summary>
        public int TicksRun { get; private set; }

        public AnimalNeedsConfig Config => _config;

        public void Dispose()
        {
            _habitats.Changed -= MarkRefresh;
            _enclosures.EnclosuresChanged -= MarkRefresh;
            _registry.MembershipChanged -= OnMembership;
            _registry.Unregistered -= OnUnregistered;
        }

        void MarkRefresh() => _refresh = true;
        void OnMembership(string _) => _refresh = true;
        void OnUnregistered(string animalId) => _reports.Remove(animalId);

        public bool TryGetReport(string animalId, out AnimalWelfareReport report)
        {
            if (animalId == null) { report = null; return false; }
            return _reports.TryGetValue(animalId, out report);
        }

        // ---- Scheduling ------------------------------------------------------------------------------------

        /// <summary>Adds simulation seconds (GameClock.DeltaTime). Runs every tick that is now due, then any pending zero-time re-evaluation.</summary>
        public void Advance(float simulatedSeconds)
        {
            if (simulatedSeconds > 0f)
            {
                _accumulated += simulatedSeconds;
                float interval = _config.TickIntervalSeconds;
                int ran = 0;
                while (_accumulated >= interval && ran < _config.MaxTicksPerAdvance)
                {
                    _accumulated -= interval;
                    Step(_config.MinutesPerTick);
                    ran++;
                }
                if (_accumulated >= interval) _accumulated = 0f; // hitch: drop what the cap could not process
            }
            if (_refresh) Step(0f);
        }

        /// <summary>Runs one tick covering the given simulated minutes for every animal. <see cref="Advance"/> calls this on schedule.</summary>
        public void Step(float simulatedMinutes)
        {
            _refresh = false;
            if (simulatedMinutes > 0f) TicksRun++;
            foreach (var animal in _registry.GetAll())
            {
                if (_definitions.TryGet(animal.SpeciesId, out var def)) Evaluate(animal, def, simulatedMinutes);
            }
        }

        // ---- One animal ------------------------------------------------------------------------------------

        void Evaluate(AnimalInstance a, AnimalDefinition def, float minutes)
        {
            if (!_reports.TryGetValue(a.AnimalId, out var r)) _reports[a.AnimalId] = r = new AnimalWelfareReport();

            // Is the animal in a closed enclosure that contains it, and is that enclosure summarised?
            EnclosureHabitat hab = null;
            bool valid = a.HasEnclosure && _enclosures.IsValidEnclosure(a.EnclosureId) && _enclosures.ContainsPosition(a.EnclosureId, a.Position)
                         && _habitats.TryGet(a.EnclosureId, out hab);

            // 1. Hunger / thirst. Animals do not need to reach the food or water: having it in the enclosure is enough.
            a.Hunger = AnimalWelfareMath.DecayAndRecover(a.Hunger, def.HungerDecayPerMinute, def.HungerRecoveryPerMinute,
                valid && hab.HasFood, minutes);
            a.Thirst = AnimalWelfareMath.DecayAndRecover(a.Thirst, def.ThirstDecayPerMinute, def.ThirstRecoveryPerMinute,
                valid && hab.HasDrinkingWater, minutes);

            // 2. Habitat conditions -> factor scores.
            var f = new HabitatFactors();
            int count = 0;
            if (valid)
            {
                count = Mathf.Max(1, hab.CountOf(a.SpeciesId));
                f.Space = AnimalWelfareMath.SpaceScore(hab.Area, def.MinimumEnclosureArea, count);
                f.Social = AnimalWelfareMath.SocialScore(count, def.PreferredGroupMin, def.PreferredGroupMax, _config.OvercrowdingPenaltyPerAnimal);
                f.Terrain = AnimalWelfareMath.TerrainScore(def.TerrainPreferences, hab.TerrainCells, hab.Area, def.TerrainTolerance, out f.TerrainApplicable);
                f.WaterApplicable = def.RequiresHabitatWater;
                f.Water = f.WaterApplicable ? AnimalWelfareMath.WaterScore(hab.HasHabitatWater) : 100f;
                f.ShelterApplicable = def.RequiresShelter;
                f.Shelter = f.ShelterApplicable
                    ? AnimalWelfareMath.ShelterScore(hab.ShelterCapacity, def.ShelterPerAnimal * count)
                    : 100f;
                f.Enrichment = AnimalWelfareMath.EnrichmentScore(hab.EnrichmentValue, def.RequiredEnrichmentValue);
            }
            // else: no usable habitat, every factor stays 0 (and the invalid-enclosure cap applies below).

            // 3. Social, enrichment, comfort.
            float comfortTarget = AnimalWelfareMath.ComfortTarget(f);
            a.Social = f.Social;
            a.Enrichment = AnimalWelfareMath.MoveToward(a.Enrichment, f.Enrichment, _config.EnrichmentChangePerMinute * minutes);
            a.Comfort = AnimalWelfareMath.MoveToward(a.Comfort, comfortTarget, _config.ComfortChangePerMinute * minutes);

            // 4. Habitat suitability.
            float suitability = AnimalWelfareMath.HabitatSuitability(f);

            // 5. Welfare and caps.
            float basic = AnimalWelfareMath.BasicNeeds(a.Hunger, a.Thirst, a.Social, a.Enrichment, a.Comfort);
            float cap = AnimalWelfareMath.WelfareCap(a.Hunger, a.Thirst, valid);
            float welfare = AnimalWelfareMath.Welfare(basic, suitability, cap);
            a.OverallWelfare = welfare;

            r.Factors = f;
            r.ComfortTarget = comfortTarget;
            r.HabitatSuitability = suitability;
            r.BasicNeeds = basic;
            r.UncappedWelfare = AnimalWelfareMath.Welfare(basic, suitability, AnimalWelfareMath.NoCap);
            r.Cap = cap;
            r.Welfare = welfare;
            r.Band = AnimalWelfareMath.Band(welfare);
            r.EnclosureValid = valid;
            r.SameSpeciesCount = count;
            r.EnclosureArea = valid ? hab.Area : 0;

            Evaluated?.Invoke(a);
        }
    }
}
