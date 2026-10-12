using System;
using System.Collections.Generic;
using ZooGame.Animals;
using ZooGame.Data;
using ZooGame.Placement;
using ZooGame.Progression;
using ZooGame.Staff;
using ZooGame.Visitors;
using ZooGame.World;

namespace ZooGame.Quests
{
    /// <summary>
    /// <see cref="IQuestWorld"/> over the live systems. It reads the authoritative owners (placement map, grid,
    /// construction model, animal/visitor/staff registries, rating, progression) and copies nothing. Every collaborator
    /// is optional; a missing one reads as an empty zoo. Queries run only when a relevant quest event arrived.
    /// </summary>
    public sealed class QuestWorldQuery : IQuestWorld
    {
        readonly PlacementMap _placement;
        readonly ZooGrid _grid;
        readonly ConstructionModel _construction;
        readonly IAnimalRegistry _animals;
        readonly IVisitorRegistry _visitors;
        readonly IStaffRegistry _staff;
        readonly ZooRatingService _rating;
        readonly ProgressionService _progression;

        readonly HashSet<string> _species = new HashSet<string>();
        static readonly Dictionary<string, StaffRole> RoleByName = BuildRoleNames();

        public QuestWorldQuery(PlacementMap placement, ZooGrid grid, ConstructionModel construction, IAnimalRegistry animals,
            IVisitorRegistry visitors, IStaffRegistry staff, ZooRatingService rating, ProgressionService progression)
        {
            _placement = placement;
            _grid = grid;
            _construction = construction;
            _animals = animals;
            _visitors = visitors;
            _staff = staff;
            _rating = rating;
            _progression = progression;
        }

        public int CountPlaced(string placeableId, bool matchCategory, PlaceableCategory category)
        {
            if (_placement == null) return 0;
            int count = 0;
            foreach (var obj in _placement.Objects)
            {
                var def = obj != null ? obj.Definition : null;
                if (def == null) continue;
                if (matchCategory) { if (def.Category != category) continue; }
                else if (placeableId != null && def.Id != placeableId) continue;
                count++;
            }
            return count;
        }

        public int PathCellCount => _grid != null ? _grid.PathCount : 0;

        public int FenceSegmentCount => _construction != null ? _construction.Fences.Count : 0;

        public int EnclosureCount => _construction != null ? _construction.Enclosures.Count : 0;

        public int CountAnimals(string speciesId)
        {
            if (_animals == null) return 0;
            if (speciesId == null) return _animals.Count;
            int count = 0;
            foreach (var a in _animals.GetAll())
                if (a != null && a.SpeciesId == speciesId) count++;
            return count;
        }

        public int CountSpecies()
        {
            if (_animals == null) return 0;
            _species.Clear();
            foreach (var a in _animals.GetAll())
                if (a != null && !string.IsNullOrEmpty(a.SpeciesId)) _species.Add(a.SpeciesId);
            return _species.Count;
        }

        public int AverageWelfare => _rating != null ? WholePoints(_rating.Current.AnimalWelfare) : 0;

        public int VisitorCount => _visitors != null ? _visitors.Count : 0;

        public int VisitorHappiness => _rating != null ? WholePoints(_rating.Current.VisitorHappiness) : 0;

        public int CountStaff(string roleId)
        {
            if (_staff == null) return 0;
            if (roleId == null) return _staff.Count;
            return RoleByName.TryGetValue(roleId, out var role) ? _staff.CountOf(role) : 0;
        }

        public int Stars => _rating != null ? _rating.Stars : 1;

        public int Level => _progression != null ? _progression.Level : 1;

        // Whole points, rounded down: "reach 70" means the real value is at least 70.
        static int WholePoints(double value) => value > 0.0 ? (int)Math.Min(100.0, Math.Floor(value)) : 0;

        static Dictionary<string, StaffRole> BuildRoleNames()
        {
            var map = new Dictionary<string, StaffRole>(StringComparer.OrdinalIgnoreCase);
            foreach (StaffRole role in Enum.GetValues(typeof(StaffRole)))
                if (StaffRules.IsDefined(role)) map[role.ToString()] = role;
            return map;
        }
    }
}
