using System.Collections.Generic;
using UnityEngine;
using ZooGame.World;

namespace ZooGame.Animals
{
    public enum WelfareBand
    {
        Critical = 0,
        Poor,
        Fair,
        Good,
        Excellent
    }

    /// <summary>
    /// The six habitat factor scores (0-100) for one animal in one enclosure. Terrain, water and shelter can be "not
    /// applicable" for a species; those scores are then ignored and the other weights are normalised.
    /// Space, social and enrichment always apply.
    /// </summary>
    public struct HabitatFactors
    {
        public float Space;
        public float Social;
        public float Terrain;
        public float Water;
        public float Shelter;
        public float Enrichment;
        public bool TerrainApplicable;
        public bool WaterApplicable;
        public bool ShelterApplicable;
    }

    /// <summary>
    /// Every M5 formula as a pure static function (no state, no allocation), so the rules can be unit-tested directly
    /// and tuned in one place. Weights are fixed by the design; per-species numbers come from <see cref="AnimalDefinition"/>.
    /// </summary>
    public static class AnimalWelfareMath
    {
        // Comfort target weights.
        public const float ComfortSpaceWeight = 0.35f;
        public const float ComfortTerrainWeight = 0.35f;
        public const float ComfortWaterWeight = 0.15f;
        public const float ComfortShelterWeight = 0.15f;

        // Habitat suitability weights.
        public const float SuitSpaceWeight = 0.30f;
        public const float SuitTerrainWeight = 0.25f;
        public const float SuitSocialWeight = 0.15f;
        public const float SuitWaterWeight = 0.10f;
        public const float SuitShelterWeight = 0.10f;
        public const float SuitEnrichmentWeight = 0.10f;

        // Basic needs weights.
        public const float NeedHunger = 0.35f;
        public const float NeedThirst = 0.30f;
        public const float NeedSocial = 0.15f;
        public const float NeedEnrichment = 0.10f;
        public const float NeedComfort = 0.10f;

        // Overall welfare blend.
        public const float WelfareBasicNeeds = 0.70f;
        public const float WelfareHabitat = 0.30f;

        // Critical caps.
        public const float LowThreshold = 20f;
        public const float HungerLowCap = 40f;
        public const float ThirstLowCap = 35f;
        public const float HungerEmptyCap = 20f;
        public const float ThirstEmptyCap = 15f;
        public const float InvalidEnclosureCap = 50f;
        public const float NoCap = 100f;

        public static float Clamp100(float v) => float.IsNaN(v) ? 0f : Mathf.Clamp(v, 0f, 100f);

        // ---- Needs over time ----

        /// <summary>Hunger/Thirst: value - decay x minutes (+ recovery x minutes when usable food/water exists), clamped once to 0-100.</summary>
        public static float DecayAndRecover(float value, float decayPerMinute, float recoveryPerMinute, bool recovers, float minutes) =>
            Clamp100(value - decayPerMinute * minutes + (recovers ? recoveryPerMinute * minutes : 0f));

        /// <summary>Moves current towards target by at most maxStep (never overshoots).</summary>
        public static float MoveToward(float current, float target, float maxStep) =>
            Clamp100(Mathf.MoveTowards(current, target, Mathf.Max(0f, maxStep)));

        // ---- Factor scores ----

        /// <summary>100 x clamp01(area / (minimumArea x animals of the species)). Needs at least one animal.</summary>
        public static float SpaceScore(int enclosureArea, int minimumAreaPerAnimal, int sameSpeciesCount)
        {
            float required = (float)minimumAreaPerAnimal * Mathf.Max(1, sameSpeciesCount);
            if (required <= 0f) return 100f;
            return Mathf.Clamp01(enclosureArea / required) * 100f;
        }

        /// <summary>Inside the preferred group size: 100. Below it: 100 x count / min. Above it: 100 - (count - max) x penalty, floored at 0.</summary>
        public static float SocialScore(int sameSpeciesCount, int groupMin, int groupMax, float overcrowdingPenaltyPerAnimal)
        {
            if (sameSpeciesCount < groupMin) return Clamp100(100f * sameSpeciesCount / Mathf.Max(1, groupMin));
            if (sameSpeciesCount > groupMax) return Mathf.Max(0f, Clamp100(100f - (sameSpeciesCount - groupMax) * overcrowdingPenaltyPerAnimal));
            return 100f;
        }

        /// <summary>100 x clamp01(available / required); 100 when nothing is required.</summary>
        public static float EnrichmentScore(float availableValue, float requiredValue) =>
            requiredValue <= 0f ? 100f : Mathf.Clamp01(availableValue / requiredValue) * 100f;

        /// <summary>100 x clamp01(capacity / required); 100 when nothing is required.</summary>
        public static float ShelterScore(float availableCapacity, float requiredCapacity) =>
            requiredCapacity <= 0f ? 100f : Mathf.Clamp01(availableCapacity / requiredCapacity) * 100f;

        /// <summary>Habitat water is all-or-nothing: 100 when the enclosure has standing water, else 0.</summary>
        public static float WaterScore(bool satisfied) => satisfied ? 100f : 0f;

        /// <summary>
        /// Mean over the species' preferences of: 100 inside the preferred share range, falling linearly to 0 at
        /// <paramref name="tolerance"/> (a share of the enclosure) outside it. Not applicable without preferences or cells.
        /// </summary>
        public static float TerrainScore(IReadOnlyList<TerrainPreference> prefs, int[] terrainCells, int area, float tolerance, out bool applicable)
        {
            applicable = prefs != null && prefs.Count > 0 && area > 0;
            if (!applicable) return 100f;
            tolerance = Mathf.Max(0.0001f, tolerance);
            float sum = 0f;
            for (int i = 0; i < prefs.Count; i++)
            {
                var p = prefs[i];
                int idx = (int)p.terrain;
                float share = terrainCells != null && idx >= 0 && idx < terrainCells.Length ? (float)terrainCells[idx] / area : 0f;
                float lo = Mathf.Min(p.minShare, p.maxShare), hi = Mathf.Max(p.minShare, p.maxShare);
                float distance = share < lo ? lo - share : share > hi ? share - hi : 0f;
                sum += Mathf.Clamp01(1f - distance / tolerance) * 100f;
            }
            return sum / prefs.Count;
        }

        // ---- Aggregates ----

        public static float ComfortTarget(in HabitatFactors f)
        {
            float w = ComfortSpaceWeight, s = ComfortSpaceWeight * f.Space;
            if (f.TerrainApplicable) { w += ComfortTerrainWeight; s += ComfortTerrainWeight * f.Terrain; }
            if (f.WaterApplicable) { w += ComfortWaterWeight; s += ComfortWaterWeight * f.Water; }
            if (f.ShelterApplicable) { w += ComfortShelterWeight; s += ComfortShelterWeight * f.Shelter; }
            return Clamp100(s / w);
        }

        public static float HabitatSuitability(in HabitatFactors f)
        {
            float w = SuitSpaceWeight + SuitSocialWeight + SuitEnrichmentWeight;
            float s = SuitSpaceWeight * f.Space + SuitSocialWeight * f.Social + SuitEnrichmentWeight * f.Enrichment;
            if (f.TerrainApplicable) { w += SuitTerrainWeight; s += SuitTerrainWeight * f.Terrain; }
            if (f.WaterApplicable) { w += SuitWaterWeight; s += SuitWaterWeight * f.Water; }
            if (f.ShelterApplicable) { w += SuitShelterWeight; s += SuitShelterWeight * f.Shelter; }
            return Clamp100(s / w);
        }

        public static float BasicNeeds(float hunger, float thirst, float social, float enrichment, float comfort) =>
            Clamp100(NeedHunger * hunger + NeedThirst * thirst + NeedSocial * social + NeedEnrichment * enrichment + NeedComfort * comfort);

        /// <summary>The lowest cap that applies (<see cref="NoCap"/> when none does).</summary>
        public static float WelfareCap(float hunger, float thirst, bool enclosureValid)
        {
            float cap = NoCap;
            if (hunger < LowThreshold) cap = Mathf.Min(cap, HungerLowCap);
            if (thirst < LowThreshold) cap = Mathf.Min(cap, ThirstLowCap);
            if (hunger <= 0f) cap = Mathf.Min(cap, HungerEmptyCap);
            if (thirst <= 0f) cap = Mathf.Min(cap, ThirstEmptyCap);
            if (!enclosureValid) cap = Mathf.Min(cap, InvalidEnclosureCap);
            return cap;
        }

        /// <summary>0.70 x basic needs + 0.30 x habitat suitability, clamped to 0-100, then limited by <paramref name="cap"/>.</summary>
        public static float Welfare(float basicNeeds, float habitatSuitability, float cap) =>
            Mathf.Min(Clamp100(WelfareBasicNeeds * basicNeeds + WelfareHabitat * habitatSuitability), cap);

        public static WelfareBand Band(float welfare)
        {
            if (welfare >= 90f) return WelfareBand.Excellent;
            if (welfare >= 75f) return WelfareBand.Good;
            if (welfare >= 50f) return WelfareBand.Fair;
            if (welfare >= 25f) return WelfareBand.Poor;
            return WelfareBand.Critical;
        }
    }
}
