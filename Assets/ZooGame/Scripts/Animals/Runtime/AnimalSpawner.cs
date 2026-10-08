using System;
using System.Collections.Generic;
using UnityEngine;
using ZooGame.Core;

namespace ZooGame.Animals
{
    public enum AnimalSpawnFailure
    {
        None = 0,
        NotBound,
        UnknownSpecies,
        MissingPrefab,
        Placement,
        DuplicateId,
        AlreadySpawned,
        NotRegistered
    }

    public readonly struct AnimalSpawnResult
    {
        public readonly AnimalSpawnFailure Failure;
        public readonly AnimalPlacementFailure Placement;
        public readonly AnimalInstance Animal;
        public readonly AnimalController Controller;

        public AnimalSpawnResult(AnimalInstance animal, AnimalController controller)
        {
            Failure = AnimalSpawnFailure.None;
            Placement = AnimalPlacementFailure.None;
            Animal = animal;
            Controller = controller;
        }

        public AnimalSpawnResult(AnimalSpawnFailure failure, AnimalPlacementFailure placement = AnimalPlacementFailure.None)
        {
            Failure = failure;
            Placement = placement;
            Animal = null;
            Controller = null;
        }

        public bool Success => Failure == AnimalSpawnFailure.None;

        public string Message
        {
            get
            {
                if (Success) return "Spawned";
                if (Failure == AnimalSpawnFailure.Placement) return new AnimalPlacementResult(Placement).Message;
                return Failure.ToString();
            }
        }
    }

    /// <summary>
    /// Creates, reconstructs, despawns and deletes animals, and drives the one shared tick for every spawned animal.
    ///
    /// New animals and saved animals take different paths on purpose: <see cref="SpawnNew"/> / <see cref="SpawnUnhoused"/>
    /// generate an AnimalId, <see cref="Reconstruct"/> keeps the id it is given. <see cref="Despawn"/> removes only the
    /// GameObject (the record stays registered, so it can be reconstructed); <see cref="Delete"/> also removes the record.
    /// </summary>
    public sealed class AnimalSpawner : MonoBehaviour
    {
        IAnimalRegistry _registry;
        IAnimalDefinitionResolver _definitions;
        IAnimalEnclosureService _enclosures;
        AnimalPlacementValidator _validator;
        GameClock _clock;
        Transform _parent;
        System.Random _rng;
        int _nameCounter;

        readonly Dictionary<string, AnimalController> _byId = new Dictionary<string, AnimalController>();
        readonly List<AnimalController> _active = new List<AnimalController>(16);

        public bool IsBound => _registry != null;
        public IAnimalRegistry Registry => _registry;
        public IAnimalDefinitionResolver Definitions => _definitions;
        public IAnimalEnclosureService Enclosures => _enclosures;
        public IReadOnlyList<AnimalController> Active => _active;

        /// <summary>Raised after an animal's GameObject exists and is bound.</summary>
        public event Action<AnimalController> Spawned;

        /// <summary>Raised just before an animal's GameObject is destroyed (despawn or delete).</summary>
        public event Action<AnimalController> Despawning;

        /// <param name="clock">Simulation clock so animals respect pause and speed; null uses frame time.</param>
        public void Bind(IAnimalRegistry registry, IAnimalDefinitionResolver definitions, IAnimalEnclosureService enclosures,
            GameClock clock = null, Transform parent = null, System.Random rng = null)
        {
            Unbind();
            _registry = registry;
            _definitions = definitions;
            _enclosures = enclosures;
            _clock = clock;
            _parent = parent != null ? parent : transform;
            _rng = rng ?? new System.Random();
            _validator = new AnimalPlacementValidator(enclosures);
            _enclosures.EnclosuresChanged += OnEnclosuresChanged;
        }

        void Unbind()
        {
            if (_enclosures != null) _enclosures.EnclosuresChanged -= OnEnclosuresChanged;
        }

        void OnDestroy() => Unbind();

        // ---- Spawning --------------------------------------------------------------------------------------

        /// <summary>Creates a new animal in an enclosure: resolve, validate, new id, instance, prefab, bind, register.</summary>
        public AnimalSpawnResult SpawnNew(string speciesId, string enclosureId)
        {
            if (!IsBound) return new AnimalSpawnResult(AnimalSpawnFailure.NotBound);
            if (!_definitions.TryGet(speciesId, out var def)) return new AnimalSpawnResult(AnimalSpawnFailure.UnknownSpecies);
            if (def.Prefab == null) return new AnimalSpawnResult(AnimalSpawnFailure.MissingPrefab);

            var enclosureCheck = _validator.ValidateEnclosure(def, enclosureId);
            if (!enclosureCheck.IsValid) return new AnimalSpawnResult(AnimalSpawnFailure.Placement, enclosureCheck.Failure);
            if (!_enclosures.TryGetRandomValidPosition(enclosureId, out var position))
                return new AnimalSpawnResult(AnimalSpawnFailure.Placement, AnimalPlacementFailure.EnclosureInvalid);
            return SpawnAt(speciesId, enclosureId, position);
        }

        /// <summary>Creates a new animal at a chosen position (what the player's placement does). Same checks as <see cref="SpawnNew"/>.</summary>
        public AnimalSpawnResult SpawnAt(string speciesId, string enclosureId, Vector3 position)
        {
            if (!IsBound) return new AnimalSpawnResult(AnimalSpawnFailure.NotBound);
            if (!_definitions.TryGet(speciesId, out var def)) return new AnimalSpawnResult(AnimalSpawnFailure.UnknownSpecies);
            if (def.Prefab == null) return new AnimalSpawnResult(AnimalSpawnFailure.MissingPrefab);
            var placement = _validator.Validate(def, enclosureId, position);
            if (!placement.IsValid) return new AnimalSpawnResult(AnimalSpawnFailure.Placement, placement.Failure);

            var instance = AnimalInstance.CreateNew(def.SpeciesId, NextName(def), RandomSex(), enclosureId, position);
            return Materialise(instance, def, register: true);
        }

        /// <summary>Creates a new animal that belongs to no enclosure. It stands where it is placed.</summary>
        public AnimalSpawnResult SpawnUnhoused(string speciesId, Vector3 position)
        {
            if (!IsBound) return new AnimalSpawnResult(AnimalSpawnFailure.NotBound);
            if (!_definitions.TryGet(speciesId, out var def)) return new AnimalSpawnResult(AnimalSpawnFailure.UnknownSpecies);
            if (def.Prefab == null) return new AnimalSpawnResult(AnimalSpawnFailure.MissingPrefab);
            var instance = AnimalInstance.CreateNew(def.SpeciesId, NextName(def), RandomSex(), null, position);
            return Materialise(instance, def, register: true);
        }

        /// <summary>
        /// Gives an existing record (for example one loaded from a save) a GameObject, keeping its AnimalId. The record is
        /// registered if it is not already. No enclosure check: an animal whose enclosure is gone is simply stranded.
        /// </summary>
        public AnimalSpawnResult Reconstruct(AnimalInstance instance)
        {
            if (!IsBound) return new AnimalSpawnResult(AnimalSpawnFailure.NotBound);
            if (instance == null) return new AnimalSpawnResult(AnimalSpawnFailure.NotRegistered);
            if (_byId.ContainsKey(instance.AnimalId)) return new AnimalSpawnResult(AnimalSpawnFailure.AlreadySpawned);
            if (!_definitions.TryGet(instance.SpeciesId, out var def)) return new AnimalSpawnResult(AnimalSpawnFailure.UnknownSpecies);
            if (def.Prefab == null) return new AnimalSpawnResult(AnimalSpawnFailure.MissingPrefab);

            bool register = true;
            if (_registry.TryGet(instance.AnimalId, out var existing))
            {
                if (!ReferenceEquals(existing, instance)) return new AnimalSpawnResult(AnimalSpawnFailure.DuplicateId);
                register = false;
            }
            return Materialise(instance, def, register);
        }

        AnimalSpawnResult Materialise(AnimalInstance instance, AnimalDefinition def, bool register)
        {
            if (register && !_registry.Register(instance)) return new AnimalSpawnResult(AnimalSpawnFailure.DuplicateId);

            var go = Instantiate(def.Prefab, instance.Position, Quaternion.identity, _parent);
            go.name = instance.DisplayName + " (" + def.DisplayName + ")";
            var controller = go.GetComponent<AnimalController>();
            if (controller == null)
            {
                Destroy(go);
                if (register) _registry.Unregister(instance.AnimalId);
                return new AnimalSpawnResult(AnimalSpawnFailure.MissingPrefab);
            }

            controller.Bind(instance, def, _enclosures, _rng);
            _byId.Add(instance.AnimalId, controller);
            _active.Add(controller);
            Spawned?.Invoke(controller);
            return new AnimalSpawnResult(instance, controller);
        }

        // ---- Despawn vs delete -----------------------------------------------------------------------------

        /// <summary>Removes the GameObject only. The AnimalInstance stays registered and can be reconstructed.</summary>
        public bool Despawn(string animalId)
        {
            if (animalId == null || !_byId.TryGetValue(animalId, out var controller)) return false;
            Despawning?.Invoke(controller);
            _byId.Remove(animalId);
            _active.Remove(controller);
            if (controller.Instance != null) controller.Instance.Position = controller.transform.position;
            Destroy(controller.gameObject);
            return true;
        }

        /// <summary>Ends the animal: removes its GameObject (if spawned) and its record.</summary>
        public bool Delete(string animalId)
        {
            bool had = Despawn(animalId);
            return _registry != null && _registry.Unregister(animalId) || had;
        }

        public bool TryGetController(string animalId, out AnimalController controller)
        {
            if (animalId == null) { controller = null; return false; }
            return _byId.TryGetValue(animalId, out controller);
        }

        // ---- Per-frame -------------------------------------------------------------------------------------

        void Update()
        {
            if (_active.Count == 0) return;
            float dt = _clock != null ? _clock.DeltaTime : Time.deltaTime;
            for (int i = 0; i < _active.Count; i++) _active[i].Tick(dt);
        }

        void OnEnclosuresChanged()
        {
            for (int i = 0; i < _active.Count; i++) _active[i].RefreshEnclosure();
        }

        // ---- Helpers ---------------------------------------------------------------------------------------

        string NextName(AnimalDefinition def) => def.DisplayName + " " + (++_nameCounter);

        AnimalSex RandomSex() => _rng.Next(2) == 0 ? AnimalSex.Female : AnimalSex.Male;
    }
}
