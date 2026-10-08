using System.Collections.Generic;

namespace ZooGame.Animals
{
    public sealed class AnimalRegistry : IAnimalRegistry
    {
        const string NoEnclosureKey = "";
        static readonly AnimalInstance[] None = new AnimalInstance[0];

        readonly Dictionary<string, AnimalInstance> _byId = new Dictionary<string, AnimalInstance>();
        readonly Dictionary<string, List<AnimalInstance>> _byEnclosure = new Dictionary<string, List<AnimalInstance>>();

        public int Count => _byId.Count;

        public event System.Action<string> MembershipChanged;
        public event System.Action<string> Unregistered;

        public bool Register(AnimalInstance animal)
        {
            if (animal == null || string.IsNullOrEmpty(animal.AnimalId) || _byId.ContainsKey(animal.AnimalId)) return false;
            _byId.Add(animal.AnimalId, animal);
            Index(animal);
            MembershipChanged?.Invoke(animal.EnclosureId);
            return true;
        }

        public bool Unregister(string animalId)
        {
            if (animalId == null || !_byId.TryGetValue(animalId, out var animal)) return false;
            _byId.Remove(animalId);
            Unindex(animal);
            MembershipChanged?.Invoke(animal.EnclosureId);
            Unregistered?.Invoke(animalId);
            return true;
        }

        public bool TryGet(string animalId, out AnimalInstance animal)
        {
            if (animalId == null) { animal = null; return false; }
            return _byId.TryGetValue(animalId, out animal);
        }

        public IReadOnlyCollection<AnimalInstance> GetAll() => _byId.Values;

        public IReadOnlyCollection<AnimalInstance> GetByEnclosure(string enclosureId) =>
            _byEnclosure.TryGetValue(Key(enclosureId), out var list) ? (IReadOnlyCollection<AnimalInstance>)list : None;

        public bool SetEnclosure(string animalId, string enclosureId)
        {
            if (animalId == null || !_byId.TryGetValue(animalId, out var animal)) return false;
            string before = animal.EnclosureId;
            Unindex(animal);
            animal.SetEnclosure(enclosureId);
            Index(animal);
            MembershipChanged?.Invoke(before);
            if (animal.EnclosureId != before) MembershipChanged?.Invoke(animal.EnclosureId);
            return true;
        }

        void Index(AnimalInstance animal)
        {
            string key = Key(animal.EnclosureId);
            if (!_byEnclosure.TryGetValue(key, out var list)) _byEnclosure[key] = list = new List<AnimalInstance>(4);
            list.Add(animal);
        }

        void Unindex(AnimalInstance animal)
        {
            string key = Key(animal.EnclosureId);
            if (!_byEnclosure.TryGetValue(key, out var list)) return;
            list.Remove(animal);
            if (list.Count == 0) _byEnclosure.Remove(key);
        }

        static string Key(string enclosureId) => string.IsNullOrEmpty(enclosureId) ? NoEnclosureKey : enclosureId;
    }
}
