using System.Collections.Generic;
using UnityEngine;
using ZooGame.Data;
using ZooGame.World;

namespace ZooGame.Placement
{
    /// <summary>
    /// Runtime record of one placed object. This, not the scene transform, is the source of truth for where the
    /// object is; <see cref="View"/> is just its visual. Created and mutated only by <see cref="PlacementMap"/>.
    /// </summary>
    public sealed class PlacedObject
    {
        readonly GridCoord[] _cells;

        public int Id { get; }
        public PlaceableDefinition Definition { get; }
        /// <summary>Minimum-corner cell of the (rotated) footprint.</summary>
        public GridCoord Origin { get; private set; }
        public Rotation90 Rotation { get; private set; }
        /// <summary>Footprint extent along X after rotation.</summary>
        public int Width { get; private set; }
        /// <summary>Footprint extent along Z after rotation.</summary>
        public int Height { get; private set; }
        /// <summary>Every cell this object occupies. Constant length (width * height of the definition).</summary>
        public IReadOnlyList<GridCoord> Cells => _cells;
        /// <summary>Visual representation, if one has been spawned. Owned by the view layer.</summary>
        public GameObject View { get; set; }

        internal PlacedObject(int id, PlaceableDefinition definition, GridCoord origin, Rotation90 rotation)
        {
            Id = id;
            Definition = definition;
            _cells = new GridCoord[definition.FootprintWidth * definition.FootprintHeight];
            Set(origin, rotation);
        }

        internal void Set(GridCoord origin, Rotation90 rotation)
        {
            Origin = origin;
            Rotation = rotation;
            int w, h;
            Footprint.RotatedSize(Definition.FootprintWidth, Definition.FootprintHeight, rotation, out w, out h);
            Width = w;
            Height = h;
            Footprint.Fill(origin, w, h, _cells);
        }
    }
}
