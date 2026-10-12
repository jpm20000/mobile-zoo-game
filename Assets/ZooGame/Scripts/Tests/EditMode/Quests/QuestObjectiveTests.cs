using NUnit.Framework;
using ZooGame.Data;
using ZooGame.Quests;

namespace ZooGame.Tests.EditMode.Quests
{
    /// <summary>Objective progress: clamping, multi-objective completion, aggregates, cumulative types, event routing.</summary>
    public class QuestObjectiveTests : QuestTestBase
    {
        // ---- Progress ----

        [Test]
        public void Progress_IsCurrentOverTarget_AndClampedToTarget()
        {
            var service = Make(true, Simple("a", QuestObjectiveType.BuildPath, 5));
            var p = Get("a").Objectives[0];
            Assert.AreEqual(0, p.CurrentValue);
            Assert.AreEqual(5, p.TargetValue);

            World.Paths = 3;
            service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.AreEqual(3, p.CurrentValue);
            Assert.AreEqual(0.6f, p.Fraction, 0.0001f);
            Assert.IsFalse(p.IsComplete);

            World.Paths = 40; // far beyond the target
            service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.AreEqual(5, p.CurrentValue);
            Assert.IsTrue(p.IsComplete);
        }

        [Test]
        public void Progress_NeverGoesBackwards()
        {
            var service = Make(true, Simple("a", QuestObjectiveType.BuildPath, 10));
            World.Paths = 6;
            service.Notify(Ev(QuestEventKind.PathsChanged));
            World.Paths = 2; // paths demolished
            service.Notify(Ev(QuestEventKind.PathsChanged));
            Assert.AreEqual(6, Get("a").Objectives[0].CurrentValue);
        }

        [Test]
        public void CompletedQuest_StaysCompleted_WhenWorldRegresses()
        {
            var service = Make(true, Simple("a", QuestObjectiveType.ReachVisitorCount, 5));
            World.Visitors = 5;
            service.Notify(Ev(QuestEventKind.VisitorsChanged));
            Assert.AreEqual(QuestState.Completed, StateOf("a"));

            World.Visitors = 0;
            service.Notify(Ev(QuestEventKind.VisitorsChanged));
            Assert.AreEqual(QuestState.Completed, StateOf("a"));
        }

        [Test]
        public void ExistingProgress_IsPickedUp_WhenQuestBecomesActive()
        {
            World.Paths = 4;
            World.Placed["food_stall"] = 1;
            Make(true, Simple("a", QuestObjectiveType.BuildPath, 4));
            Assert.AreEqual(QuestState.Completed, StateOf("a"));
        }

        // ---- Multi objective ----

        [Test]
        public void MultiObjectiveQuest_CompletesOnlyWhenAllObjectivesAreDone()
        {
            var def = Def("multi", new[]
            {
                Obj("paths", QuestObjectiveType.BuildPath, 3),
                Obj("fences", QuestObjectiveType.BuildFence, 4),
                Obj("visitors", QuestObjectiveType.ReachVisitorCount, 2),
            });
            var service = Make(true, def);

            World.Paths = 3;
            service.Notify(Ev(QuestEventKind.PathsChanged));
            World.Fences = 4;
            service.Notify(Ev(QuestEventKind.FencesChanged));
            Assert.AreEqual(QuestState.Active, StateOf("multi"));
            Assert.IsTrue(Get("multi").Objectives[0].IsComplete);
            Assert.IsTrue(Get("multi").Objectives[1].IsComplete);
            Assert.IsFalse(Get("multi").Objectives[2].IsComplete);

            World.Visitors = 2;
            service.Notify(Ev(QuestEventKind.VisitorsChanged));
            Assert.AreEqual(QuestState.Completed, StateOf("multi"));
        }

        // ---- Aggregates ----

        [Test]
        public void AggregateObjectives_UpdateOnlyOnTheirOwnEvents()
        {
            var def = Def("agg", new[]
            {
                Obj("species", QuestObjectiveType.OwnSpeciesCount, 3),
                Obj("animals", QuestObjectiveType.OwnAnimalCount, 5),
            });
            var service = Make(true, def);
            World.Animals["lion"] = 2;
            World.Animals["zebra"] = 1;

            service.Notify(Ev(QuestEventKind.PathsChanged)); // unrelated
            Assert.AreEqual(0, Get("agg").Objectives[0].CurrentValue);

            service.Notify(Ev(QuestEventKind.AnimalsChanged));
            Assert.AreEqual(2, Get("agg").Objectives[0].CurrentValue);
            Assert.AreEqual(3, Get("agg").Objectives[1].CurrentValue);

            World.Animals["rabbit"] = 3;
            service.Notify(Ev(QuestEventKind.AnimalsChanged));
            Assert.AreEqual(QuestState.Completed, StateOf("agg"));
        }

        [Test]
        public void OwnAnimalCount_CanFilterBySpecies()
        {
            var def = Def("lions", new[] { Obj("o", QuestObjectiveType.OwnAnimalCount, 2, "lion") });
            var service = Make(true, def);
            World.Animals["zebra"] = 5;
            service.Notify(Ev(QuestEventKind.AnimalsChanged));
            Assert.AreEqual(0, Get("lions").Objectives[0].CurrentValue);
            World.Animals["lion"] = 2;
            service.Notify(Ev(QuestEventKind.AnimalsChanged));
            Assert.AreEqual(QuestState.Completed, StateOf("lions"));
        }

        [Test]
        public void PlaceObject_MatchesByIdOrByCategory()
        {
            var byId = Def("by-id", new[] { Obj("o", QuestObjectiveType.PlaceObject, 1, "food_stall") });
            var byCategory = Def("by-cat", new[]
            {
                Obj("o", QuestObjectiveType.PlaceObject, 2).WithCategory(PlaceableCategory.Building)
            });
            var service = Make(true, byId, byCategory);

            World.Placed["bench"] = 1;
            World.PlacedByCategory[PlaceableCategory.Decoration] = 1;
            service.Notify(Ev(QuestEventKind.ObjectsChanged));
            Assert.AreEqual(QuestState.Active, StateOf("by-id"));
            Assert.AreEqual(0, Get("by-cat").Objectives[0].CurrentValue);

            World.Placed["food_stall"] = 1;
            World.PlacedByCategory[PlaceableCategory.Building] = 2;
            service.Notify(Ev(QuestEventKind.ObjectsChanged));
            Assert.AreEqual(QuestState.Completed, StateOf("by-id"));
            Assert.AreEqual(QuestState.Completed, StateOf("by-cat"));
        }

        [Test]
        public void StaffObjective_CountsTheRequestedRole()
        {
            var def = Def("hire", new[] { Obj("o", QuestObjectiveType.HireStaff, 1, "Keeper") });
            var service = Make(true, def);
            World.Staff["Vendor"] = 3;
            service.Notify(Ev(QuestEventKind.StaffChanged));
            Assert.AreEqual(QuestState.Active, StateOf("hire"));
            World.Staff["Keeper"] = 1;
            service.Notify(Ev(QuestEventKind.StaffChanged));
            Assert.AreEqual(QuestState.Completed, StateOf("hire"));
        }

        [Test]
        public void ScoreObjectives_UseWholePointsFromTheWorld()
        {
            var def = Def("scores", new[]
            {
                Obj("welfare", QuestObjectiveType.ReachAnimalWelfare, 70),
                Obj("happy", QuestObjectiveType.ReachVisitorHappiness, 60),
                Obj("stars", QuestObjectiveType.ReachZooRating, 3),
                Obj("level", QuestObjectiveType.ReachZooLevel, 2),
            });
            var service = Make(true, def);

            World.Welfare = 71; World.Happiness = 59; World.StarCount = 3;
            service.Notify(Ev(QuestEventKind.RatingChanged));
            Assert.IsTrue(Get("scores").Objectives[0].IsComplete);
            Assert.IsFalse(Get("scores").Objectives[1].IsComplete);
            Assert.IsTrue(Get("scores").Objectives[2].IsComplete);

            World.Happiness = 60;
            service.Notify(Ev(QuestEventKind.RatingChanged));
            Assert.AreEqual(QuestState.Active, StateOf("scores"), "level still missing");

            World.LevelCount = 2;
            service.Notify(Ev(QuestEventKind.LevelChanged));
            Assert.AreEqual(QuestState.Completed, StateOf("scores"));
        }

        [Test]
        public void ConstructionObjectives_ReadPathsFencesAndEnclosures()
        {
            var def = Def("build", new[]
            {
                Obj("p", QuestObjectiveType.BuildPath, 2),
                Obj("f", QuestObjectiveType.BuildFence, 8),
                Obj("e", QuestObjectiveType.CreateEnclosure, 1),
            });
            var service = Make(true, def);
            World.Paths = 2; service.Notify(Ev(QuestEventKind.PathsChanged));
            World.Fences = 8; service.Notify(Ev(QuestEventKind.FencesChanged));
            Assert.AreEqual(QuestState.Active, StateOf("build"));
            World.Enclosures = 1; service.Notify(Ev(QuestEventKind.EnclosuresChanged));
            Assert.AreEqual(QuestState.Completed, StateOf("build"));
        }

        // ---- Cumulative objectives ----

        [Test]
        public void AdoptAnimal_CountsAdoptionsAfterStart_AndFiltersBySpecies()
        {
            var def = Def("adopt", new[] { Obj("o", QuestObjectiveType.AdoptAnimal, 2, "lion") });
            var service = Make(true, def);
            World.Animals["lion"] = 4; // owned before: does not count

            service.Notify(Ev(QuestEventKind.AnimalAdopted, "zebra", 1));
            Assert.AreEqual(0, Get("adopt").Objectives[0].CurrentValue);
            service.Notify(Ev(QuestEventKind.AnimalAdopted, "lion", 1));
            Assert.AreEqual(1, Get("adopt").Objectives[0].CurrentValue);
            service.Notify(Ev(QuestEventKind.AnimalAdopted, "lion", 1));
            Assert.AreEqual(QuestState.Completed, StateOf("adopt"));
        }

        [Test]
        public void EarnMoney_AccumulatesEventAmounts_AndClamps()
        {
            var service = Make(true, Def("earn", new[] { Obj("o", QuestObjectiveType.EarnMoney, 100) }));
            service.Notify(Ev(QuestEventKind.MoneyEarned, null, 40));
            Assert.AreEqual(40, Get("earn").Objectives[0].CurrentValue);
            service.Notify(Ev(QuestEventKind.MoneyEarned, null, long.MaxValue / 2));
            Assert.AreEqual(100, Get("earn").Objectives[0].CurrentValue);
            Assert.AreEqual(QuestState.Completed, StateOf("earn"));
        }

        [Test]
        public void IrrelevantEvents_DoNotChangeAnything()
        {
            var service = Make(true, Simple("a", QuestObjectiveType.BuildPath, 3));
            int changes = 0;
            service.Changed += () => changes++;
            World.Paths = 3; // would complete it, but no PathsChanged event arrives
            service.Notify(Ev(QuestEventKind.FencesChanged));
            service.Notify(Ev(QuestEventKind.VisitorsChanged));
            Assert.AreEqual(0, changes);
            Assert.AreEqual(QuestState.Active, StateOf("a"));
        }
    }
}
