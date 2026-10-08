using UnityEngine;
using ZooGame.Animals;
using ZooGame.World;

namespace ZooGame.Tests.EditMode.Animals
{
    /// <summary>Real construction model + real enclosure service, so animal tests exercise the actual M3 enclosures.</summary>
    public sealed class AnimalFixture
    {
        public readonly ConstructionFixture Construction = new ConstructionFixture();
        public readonly AnimalEnclosureService Enclosures;
        public readonly AnimalRegistry Registry = new AnimalRegistry();
        public readonly AnimalDefinition Rabbit = AnimalDefinition.Create("rabbit", "Rabbit", 4);
        public readonly AnimalDefinition Zebra = AnimalDefinition.Create("zebra", "Zebra", 16);
        public readonly AnimalDefinition Lion = AnimalDefinition.Create("lion", "Lion", 36);
        public readonly AnimalDefinitionResolver Resolver;

        public AnimalFixture(int seed = 7)
        {
            Enclosures = new AnimalEnclosureService(Construction.Grid, Construction.Model.Enclosures, new System.Random(seed));
            Resolver = new AnimalDefinitionResolver(new[] { Rabbit, Zebra, Lion });
        }

        /// <summary>Builds a closed w x d rectangle (cells; unlocked land is 4..11) and returns its enclosure id.</summary>
        public string BuildEnclosure(int minX, int minZ, int w, int d)
        {
            Construction.BuildRect(minX, minZ, w, d);
            return AnimalEnclosureService.ToId(Construction.EnclosureIdAt(minX, minZ));
        }

        public void Dispose()
        {
            Enclosures.Dispose();
            Object.DestroyImmediate(Rabbit);
            Object.DestroyImmediate(Zebra);
            Object.DestroyImmediate(Lion);
        }
    }
}
