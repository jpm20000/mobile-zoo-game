using UnityEngine;

namespace ZooGame.Animals
{
    public enum AnimalPlacementFailure
    {
        None = 0,
        MissingDefinition,
        NoEnclosure,
        EnclosureInvalid,
        EnclosureTooSmall,
        PositionOutsideEnclosure
    }

    public readonly struct AnimalPlacementResult
    {
        public readonly AnimalPlacementFailure Failure;
        public bool IsValid => Failure == AnimalPlacementFailure.None;

        public AnimalPlacementResult(AnimalPlacementFailure failure) => Failure = failure;

        public static readonly AnimalPlacementResult Valid = new AnimalPlacementResult(AnimalPlacementFailure.None);

        public string Message
        {
            get
            {
                switch (Failure)
                {
                    case AnimalPlacementFailure.None: return "OK";
                    case AnimalPlacementFailure.MissingDefinition: return "Unknown species";
                    case AnimalPlacementFailure.NoEnclosure: return "Must be inside a closed enclosure";
                    case AnimalPlacementFailure.EnclosureInvalid: return "Enclosure is missing or not closed";
                    case AnimalPlacementFailure.EnclosureTooSmall: return "Enclosure is too small for this species";
                    case AnimalPlacementFailure.PositionOutsideEnclosure: return "Position is outside the enclosure";
                    default: return Failure.ToString();
                }
            }
        }
    }

    /// <summary>Decides whether a species may be put in an enclosure at a position, with a reason when it may not.</summary>
    public sealed class AnimalPlacementValidator
    {
        readonly IAnimalEnclosureService _enclosures;

        public AnimalPlacementValidator(IAnimalEnclosureService enclosures) => _enclosures = enclosures;

        /// <summary>Enclosure-level checks only (exists, closed, big enough).</summary>
        public AnimalPlacementResult ValidateEnclosure(AnimalDefinition definition, string enclosureId)
        {
            if (definition == null) return new AnimalPlacementResult(AnimalPlacementFailure.MissingDefinition);
            if (string.IsNullOrEmpty(enclosureId)) return new AnimalPlacementResult(AnimalPlacementFailure.NoEnclosure);
            if (!_enclosures.IsValidEnclosure(enclosureId)) return new AnimalPlacementResult(AnimalPlacementFailure.EnclosureInvalid);
            if (!_enclosures.MeetsMinimumArea(enclosureId, definition.MinimumEnclosureArea))
                return new AnimalPlacementResult(AnimalPlacementFailure.EnclosureTooSmall);
            return AnimalPlacementResult.Valid;
        }

        /// <summary>Validates a bare position: finds the enclosure under it (NoEnclosure if there is none), then runs the full check.</summary>
        public AnimalPlacementResult ValidateAt(AnimalDefinition definition, Vector3 position, out string enclosureId)
        {
            enclosureId = null;
            if (definition == null) return new AnimalPlacementResult(AnimalPlacementFailure.MissingDefinition);
            if (!_enclosures.TryGetEnclosureAt(position, out enclosureId))
                return new AnimalPlacementResult(AnimalPlacementFailure.NoEnclosure);
            return Validate(definition, enclosureId, position);
        }

        public AnimalPlacementResult Validate(AnimalDefinition definition, string enclosureId, Vector3 position)
        {
            var result = ValidateEnclosure(definition, enclosureId);
            if (!result.IsValid) return result;
            return _enclosures.ContainsPosition(enclosureId, position)
                ? AnimalPlacementResult.Valid
                : new AnimalPlacementResult(AnimalPlacementFailure.PositionOutsideEnclosure);
        }
    }
}
