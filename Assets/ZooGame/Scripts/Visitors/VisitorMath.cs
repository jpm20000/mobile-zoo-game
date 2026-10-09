using UnityEngine;

namespace ZooGame.Visitors
{
    /// <summary>
    /// The visitor formulas as pure functions (no scene, clock or randomness), so every number in the spec can be
    /// tested directly. <see cref="VisitorSimulationService"/> and <see cref="VisitorDecisionService"/> only call these.
    /// </summary>
    public static class VisitorMath
    {
        // Need penalty weights (sum 1.0), applied to the 0-100 "badness" of each need.
        public const float HungerPenaltyWeight = 0.25f;
        public const float ThirstPenaltyWeight = 0.30f;
        public const float ToiletPenaltyWeight = 0.25f;
        public const float EnergyPenaltyWeight = 0.20f;

        // Happiness = NeedsWeight * NeedsHappiness + SatisfactionWeight * VisitSatisfaction.
        public const float NeedsWeight = 0.65f;
        public const float SatisfactionWeight = 0.35f;

        // Critical need caps: at the threshold, happiness cannot exceed the cap. The lowest applicable cap wins.
        public const float ThirstCriticalAt = 90f, ThirstCap = 35f;
        public const float ToiletCriticalAt = 90f, ToiletCap = 40f;
        public const float HungerCriticalAt = 90f, HungerCap = 45f;
        public const float EnergyCriticalAt = 10f, EnergyCap = 40f;
        /// <summary>Returned by <see cref="CriticalCap"/> when no need is critical.</summary>
        public const float NoCap = 100f;

        public static float Clamp100(float v) => !(v > 0f) ? 0f : v > 100f ? 100f : v; // NaN becomes 0

        // ---- Needs over time ----------------------------------------------------------------------------

        /// <summary>Hunger, thirst and toilet rise and energy falls by rate x simulated minutes, clamped to 0-100.</summary>
        public static void ProgressNeeds(VisitorInstance v, VisitorConfig c, float simulatedMinutes)
        {
            if (!(simulatedMinutes > 0f)) return;
            v.Hunger += c.HungerIncreasePerMinute * simulatedMinutes;
            v.Thirst += c.ThirstIncreasePerMinute * simulatedMinutes;
            v.ToiletNeed += c.ToiletIncreasePerMinute * simulatedMinutes;
            v.Energy -= c.EnergyDecreasePerMinute * simulatedMinutes;
        }

        // ---- Urgency ------------------------------------------------------------------------------------

        /// <summary>0-1 urgency of one need (energy is inverted: tired is urgent).</summary>
        public static float Urgency(VisitorNeed need, VisitorInstance v)
        {
            switch (need)
            {
                case VisitorNeed.Food: return v.Hunger / 100f;
                case VisitorNeed.Drink: return v.Thirst / 100f;
                case VisitorNeed.Toilet: return v.ToiletNeed / 100f;
                case VisitorNeed.Rest: return (100f - v.Energy) / 100f;
                default: return 0f;
            }
        }

        /// <summary>Has the need crossed its configured threshold (food/drink >= x, toilet >= x, energy <= x)?</summary>
        public static bool IsUrgent(VisitorNeed need, VisitorInstance v, VisitorConfig c)
        {
            switch (need)
            {
                case VisitorNeed.Food: return v.Hunger >= c.HungerUrgentAt;
                case VisitorNeed.Drink: return v.Thirst >= c.ThirstUrgentAt;
                case VisitorNeed.Toilet: return v.ToiletNeed >= c.ToiletUrgentAt;
                case VisitorNeed.Rest: return v.Energy <= c.EnergyLowAt;
                default: return false;
            }
        }

        public static int Mask(VisitorNeed need) => 1 << (int)need;

        /// <summary>
        /// The urgent need with the highest urgency whose bit is not in <paramref name="excludedMask"/> (needs already
        /// tried and found to have no reachable facility), or <see cref="VisitorNeed.None"/>. Ties go to the earlier
        /// need in the order food, drink, toilet, rest.
        /// </summary>
        public static VisitorNeed MostUrgent(VisitorInstance v, VisitorConfig c, int excludedMask = 0)
        {
            VisitorNeed best = VisitorNeed.None;
            float bestUrgency = -1f;
            for (int n = (int)VisitorNeed.Food; n <= (int)VisitorNeed.Rest; n++)
            {
                var need = (VisitorNeed)n;
                if ((excludedMask & Mask(need)) != 0 || !IsUrgent(need, v, c)) continue;
                float u = Urgency(need, v);
                if (u > bestUrgency) { bestUrgency = u; best = need; }
            }
            return best;
        }

        // ---- Happiness ----------------------------------------------------------------------------------

        public static float TotalNeedPenalty(float hunger, float thirst, float toiletNeed, float energy) =>
            hunger * HungerPenaltyWeight + thirst * ThirstPenaltyWeight
            + toiletNeed * ToiletPenaltyWeight + (100f - energy) * EnergyPenaltyWeight;

        /// <summary>100 minus the weighted need penalties, clamped to 0-100.</summary>
        public static float NeedsHappiness(float hunger, float thirst, float toiletNeed, float energy) =>
            Clamp100(100f - TotalNeedPenalty(hunger, thirst, toiletNeed, energy));

        /// <summary>The lowest cap among the critical needs, or <see cref="NoCap"/>.</summary>
        public static float CriticalCap(float hunger, float thirst, float toiletNeed, float energy)
        {
            float cap = NoCap;
            if (thirst >= ThirstCriticalAt && ThirstCap < cap) cap = ThirstCap;
            if (toiletNeed >= ToiletCriticalAt && ToiletCap < cap) cap = ToiletCap;
            if (hunger >= HungerCriticalAt && HungerCap < cap) cap = HungerCap;
            if (energy <= EnergyCriticalAt && EnergyCap < cap) cap = EnergyCap;
            return cap;
        }

        /// <summary>0.65 x needs happiness + 0.35 x visit satisfaction, clamped, then limited by the critical cap.</summary>
        public static float FinalHappiness(float needsHappiness, float visitSatisfaction, float cap)
        {
            float h = Clamp100(NeedsWeight * needsHappiness + SatisfactionWeight * visitSatisfaction);
            return h > cap ? cap : h;
        }

        /// <summary>Recomputes NeedsHappiness and Happiness from the visitor's current needs and satisfaction.</summary>
        public static void RecalculateHappiness(VisitorInstance v)
        {
            float needs = NeedsHappiness(v.Hunger, v.Thirst, v.ToiletNeed, v.Energy);
            v.NeedsHappiness = needs;
            v.Happiness = FinalHappiness(needs, v.VisitSatisfaction, CriticalCap(v.Hunger, v.Thirst, v.ToiletNeed, v.Energy));
        }

        // ---- Viewing and attraction choice --------------------------------------------------------------

        /// <summary>clamp(visibleAppeal / appealForMax, 0, 1) x 100.</summary>
        public static float ViewingScore(float totalVisibleAppeal, float appealForMaxViewingScore)
        {
            if (!(appealForMaxViewingScore > 0f)) return 100f;
            float t = totalVisibleAppeal / appealForMaxViewingScore;
            return !(t > 0f) ? 0f : t > 1f ? 100f : t * 100f;
        }

        /// <summary>One viewing nudges satisfaction towards the viewing score by at most <paramref name="adjustment"/>.</summary>
        public static float ViewingSatisfaction(float current, float viewingScore, float adjustment) =>
            Clamp100(Mathf.MoveTowards(current, viewingScore, adjustment));

        /// <summary>max(1, appeal), times the repeat multiplier when the visitor recently viewed the same enclosure.</summary>
        public static float AttractionWeight(float totalAnimalAppeal, bool recentlyViewed, float recentMultiplier)
        {
            float w = totalAnimalAppeal > 1f ? totalAnimalAppeal : 1f;
            return recentlyViewed ? w * recentMultiplier : w;
        }

        /// <summary>
        /// Index picked by a roll in [0, 1) over weights (negative / NaN weights count as 0). Falls back to the last
        /// positive weight when rounding pushes the roll past the end, and to -1 when nothing has weight.
        /// </summary>
        public static int PickWeighted(System.Collections.Generic.IReadOnlyList<float> weights, int count, double roll)
        {
            double total = 0.0;
            for (int i = 0; i < count; i++) if (weights[i] > 0f) total += weights[i];
            if (!(total > 0.0)) return -1;
            double target = roll * total;
            int lastPositive = -1;
            double acc = 0.0;
            for (int i = 0; i < count; i++)
            {
                if (!(weights[i] > 0f)) continue;
                lastPositive = i;
                acc += weights[i];
                if (target < acc) return i;
            }
            return lastPositive;
        }

        // ---- Leaving ------------------------------------------------------------------------------------

        /// <summary>Has the visitor stayed their maximum time?</summary>
        public static bool VisitTimeUp(VisitorInstance v) => v.VisitTime >= v.MaximumVisitTime;

        /// <summary>Has happiness stayed at or below the threshold for the configured sustained period?</summary>
        public static bool SustainedUnhappy(VisitorInstance v, VisitorConfig c) =>
            v.Happiness <= c.UnhappyAtOrBelow && v.UnhappyTime >= c.UnhappyDurationMinutes;

        /// <summary>Visit time is up, or unhappy for long enough. (Having nowhere useful to go is decided by the decision service.)</summary>
        public static bool ShouldExit(VisitorInstance v, VisitorConfig c) => VisitTimeUp(v) || SustainedUnhappy(v, c);
    }
}
