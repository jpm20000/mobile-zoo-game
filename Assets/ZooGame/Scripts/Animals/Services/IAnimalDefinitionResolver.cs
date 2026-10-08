namespace ZooGame.Animals
{
    /// <summary>Turns a stable SpeciesId into its definition, so saved data never needs an asset reference.</summary>
    public interface IAnimalDefinitionResolver
    {
        bool TryGet(string speciesId, out AnimalDefinition definition);
    }
}
