using UnityEngine;

namespace ZooGame.Quests
{
    /// <summary>
    /// The tutorial chain, as code: the single source the editor setup turns into <see cref="QuestDefinition"/> assets
    /// and the tests run against. Ids are stable; the ids of placeables and the role are the ones in the project assets.
    /// </summary>
    public static class StarterQuests
    {
        public const string PlaceFoodStall = "starter-place-food-stall";
        public const string BuildPath = "starter-build-path";
        public const string CreateEnclosure = "starter-create-enclosure";
        public const string AdoptAnimal = "starter-adopt-animal";
        public const string ReachVisitors = "starter-reach-visitors";
        public const string HireKeeper = "starter-hire-keeper";
        public const string ReachTwoStars = "starter-two-stars";

        /// <summary>The placeable id of the food stall asset.</summary>
        public const string FoodStallId = "food_stall";

        /// <summary>Creates the seven definitions in chain order. The caller owns the created objects.</summary>
        public static QuestDefinition[] Create()
        {
            return new[]
            {
                Make(PlaceFoodStall, "Open a food stall", "Visitors get hungry. Place a food stall.",
                    new[] { new QuestObjectiveDefinition("place-food-stall", QuestObjectiveType.PlaceObject, 1, FoodStallId, "Place a food stall") },
                    new[] { new QuestRewardDefinition(QuestRewardType.Money, 500), new QuestRewardDefinition(QuestRewardType.Xp, 25) },
                    null),

                Make(BuildPath, "Lay some paths", "Visitors walk on paths.",
                    new[] { new QuestObjectiveDefinition("build-path", QuestObjectiveType.BuildPath, 5, null, "Build 5 path tiles") },
                    new[] { new QuestRewardDefinition(QuestRewardType.Money, 500), new QuestRewardDefinition(QuestRewardType.Xp, 25) },
                    new[] { PlaceFoodStall }),

                Make(CreateEnclosure, "Build a home", "Animals need a fenced enclosure.",
                    new[] { new QuestObjectiveDefinition("create-enclosure", QuestObjectiveType.CreateEnclosure, 1, null, "Create an enclosure") },
                    new[] { new QuestRewardDefinition(QuestRewardType.Money, 750), new QuestRewardDefinition(QuestRewardType.Xp, 50) },
                    new[] { BuildPath }),

                Make(AdoptAnimal, "Welcome an animal", "Adopt your first animal into the enclosure.",
                    new[] { new QuestObjectiveDefinition("adopt-animal", QuestObjectiveType.AdoptAnimal, 1, null, "Adopt an animal") },
                    new[] { new QuestRewardDefinition(QuestRewardType.Money, 1000), new QuestRewardDefinition(QuestRewardType.Xp, 75) },
                    new[] { CreateEnclosure }),

                Make(ReachVisitors, "Draw a crowd", "Get visitors through the gate.",
                    new[] { new QuestObjectiveDefinition("reach-visitors", QuestObjectiveType.ReachVisitorCount, 10, null, "Have 10 visitors in the zoo") },
                    new[] { new QuestRewardDefinition(QuestRewardType.Money, 1000), new QuestRewardDefinition(QuestRewardType.Xp, 100) },
                    new[] { AdoptAnimal }),

                Make(HireKeeper, "Hire help", "Keepers look after the animals.",
                    new[] { new QuestObjectiveDefinition("hire-keeper", QuestObjectiveType.HireStaff, 1, "Keeper", "Hire a Keeper") },
                    new[] { new QuestRewardDefinition(QuestRewardType.Money, 1000), new QuestRewardDefinition(QuestRewardType.Xp, 100) },
                    new[] { ReachVisitors }),

                Make(ReachTwoStars, "Rising star", "Improve the zoo until it earns two stars.",
                    new[] { new QuestObjectiveDefinition("two-stars", QuestObjectiveType.ReachZooRating, 2, null, "Reach a 2-star zoo rating") },
                    new[] { new QuestRewardDefinition(QuestRewardType.Money, 2500), new QuestRewardDefinition(QuestRewardType.ConservationTokens, 5),
                            new QuestRewardDefinition(QuestRewardType.Xp, 200) },
                    new[] { HireKeeper }),
            };
        }

        static QuestDefinition Make(string id, string title, string description, QuestObjectiveDefinition[] objectives,
            QuestRewardDefinition[] rewards, string[] prerequisites)
        {
            var def = ScriptableObject.CreateInstance<QuestDefinition>();
            def.name = id;
            def.Configure(id, title, objectives, rewards, prerequisites, 0, 0, description);
            return def;
        }
    }
}
