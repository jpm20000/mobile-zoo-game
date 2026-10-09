using UnityEngine;

namespace ZooGame.Data
{
    /// <summary>Broad grouping of placeables. Append only: values may be serialised in assets.</summary>
    public enum PlaceableCategory
    {
        Decoration = 0,
        Building,
        Habitat,
        Service
    }

    /// <summary>
    /// Data for one kind of placeable object (building, decoration, habitat object, service structure).
    /// Create via Assets > Create > ZooGame > Placeable Definition. The prefab is authored for the unrotated
    /// footprint, centred on its pivot, with the pivot at ground level, in cell units (1 unit = 1 cell).
    /// </summary>
    [CreateAssetMenu(menuName = "ZooGame/Placeable Definition", fileName = "Placeable")]
    public sealed class PlaceableDefinition : ScriptableObject
    {
        [SerializeField] string id = "placeable";
        [SerializeField] string displayName = "Placeable";
        [SerializeField] PlaceableCategory category;
        [SerializeField] GameObject prefab;
        [Tooltip("Footprint extent along the grid X axis, in cells.")]
        [SerializeField, Min(1)] int footprintWidth = 1;
        [Tooltip("Footprint extent along the grid Z axis, in cells.")]
        [SerializeField, Min(1)] int footprintHeight = 1;
        [SerializeField] bool allowRotation = true;
        [SerializeField] Sprite icon;
        [Tooltip("Reserved for the economy milestone; nothing deducts it yet.")]
        [SerializeField, Min(0)] int cost;
        [Tooltip("Optional: what this object provides to the enclosure it stands in (food, water, shelter, enrichment).")]
        [SerializeField] HabitatResourceInfo habitatResource;
        [Tooltip("Optional: what this object is to visitors (entrance, food stall, drink stall, toilet, bench).")]
        [SerializeField] VisitorFacilityInfo visitorFacility;

        public string Id => id;
        public string DisplayName => displayName;
        public PlaceableCategory Category => category;
        public GameObject Prefab => prefab;
        public int FootprintWidth => footprintWidth;
        public int FootprintHeight => footprintHeight;
        public bool AllowRotation => allowRotation;
        public Sprite Icon => icon;
        public int Cost => cost;
        public HabitatResourceInfo HabitatResource => habitatResource;
        public VisitorFacilityInfo VisitorFacility => visitorFacility;

        /// <summary>Sets the visitor role from code (tests, procedural content).</summary>
        public void ConfigureVisitorFacility(VisitorFacilityKind kind) => visitorFacility = new VisitorFacilityInfo(kind);

        /// <summary>Sets the habitat role from code (tests, procedural content).</summary>
        public void ConfigureHabitatResource(HabitatResourceKind kind, float amount = 1f) =>
            habitatResource = new HabitatResourceInfo(kind, amount);

        void OnValidate()
        {
            footprintWidth = Mathf.Max(1, footprintWidth);
            footprintHeight = Mathf.Max(1, footprintHeight);
            cost = Mathf.Max(0, cost);
        }

        /// <summary>Fills a definition from code (tests, procedural content). Values are clamped to valid ranges.</summary>
        public void Configure(string newId, string newDisplayName, int width, int height, bool rotatable = true,
            PlaceableCategory newCategory = PlaceableCategory.Decoration, GameObject newPrefab = null, int newCost = 0)
        {
            id = newId;
            displayName = newDisplayName;
            footprintWidth = width;
            footprintHeight = height;
            allowRotation = rotatable;
            category = newCategory;
            prefab = newPrefab;
            cost = newCost;
            OnValidate();
        }
    }
}
