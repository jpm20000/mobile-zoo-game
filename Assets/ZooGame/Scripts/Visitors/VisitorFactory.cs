using UnityEngine;

namespace ZooGame.Visitors
{
    /// <summary>Creates new visitors with the configured starting needs. Randomness comes from the supplied generator, so a seed reproduces a crowd.</summary>
    public static class VisitorFactory
    {
        public static VisitorInstance CreateNew(VisitorConfig config, System.Random rng, Vector3 position)
        {
            var v = VisitorInstance.CreateNew(position, Range(rng, config.MinVisitMinutes, config.MaxVisitMinutes));
            Initialise(v, config, rng);
            return v;
        }

        /// <summary>Sets the starting needs and satisfaction (hunger 5-20, thirst 5-20, toilet 0-15, energy 80-100, satisfaction 60 by default) and calculates happiness.</summary>
        public static void Initialise(VisitorInstance v, VisitorConfig config, System.Random rng)
        {
            v.Hunger = Range(rng, config.HungerMin, config.HungerMax);
            v.Thirst = Range(rng, config.ThirstMin, config.ThirstMax);
            v.ToiletNeed = Range(rng, config.ToiletMin, config.ToiletMax);
            v.Energy = Range(rng, config.EnergyMin, config.EnergyMax);
            v.VisitSatisfaction = config.StartingVisitSatisfaction;
            v.VisitTime = 0f;
            v.UnhappyTime = 0f;
            v.ActivityTimeLeft = 0f;
            v.CurrentEnclosureId = VisitorInstance.NoEnclosure;
            v.ForgetRecent();
            VisitorMath.RecalculateHappiness(v);
        }

        static float Range(System.Random rng, float min, float max) =>
            max <= min ? min : min + (float)rng.NextDouble() * (max - min);
    }
}
