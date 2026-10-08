using System;
using ZooGame.Data;
using ZooGame.World;

namespace ZooGame.Placement
{
    public enum PlacementMode
    {
        /// <summary>Not placing. An existing object may be selected.</summary>
        Idle,
        /// <summary>Previewing a brand new object.</summary>
        Placing,
        /// <summary>Previewing a new position for an existing object (which stays untouched until confirmed).</summary>
        Moving
    }

    /// <summary>
    /// The placement state machine, free of input and rendering so it is fully testable. Holds the preview
    /// (definition, origin, rotation, validation result) and the selection, and commits changes to the
    /// <see cref="PlacementMap"/> only after validation. Moving an object never touches the map until the new
    /// footprint is confirmed valid: the object's own cells are simply ignored while validating, so cancelling needs
    /// no restore step and cannot lose the original placement.
    /// </summary>
    public sealed class PlacementSession
    {
        readonly PlacementMap _map;
        readonly PlacementValidator _validator;

        public PlacementSession(PlacementMap map, PlacementValidator validator)
        {
            _map = map;
            _validator = validator;
            _map.Removed += OnRemoved;
        }

        public PlacementMode Mode { get; private set; }
        public bool IsPlacing => Mode != PlacementMode.Idle;

        /// <summary>What is being previewed (the new object's definition, or the moved object's).</summary>
        public PlaceableDefinition Definition { get; private set; }
        public GridCoord Origin { get; private set; }
        public Rotation90 Rotation { get; private set; }
        /// <summary>Previewed footprint extents after rotation.</summary>
        public int Width { get; private set; }
        public int Height { get; private set; }
        public PlacementResult Result { get; private set; }

        /// <summary>The existing object being moved, in <see cref="PlacementMode.Moving"/>.</summary>
        public PlacedObject Moving { get; private set; }
        public PlacedObject Selected { get; private set; }

        /// <summary>Last result of a rejected rotate-in-place, for UI feedback.</summary>
        public PlacementResult LastRejection { get; private set; }

        /// <summary>Raised whenever anything the UI or views display changed.</summary>
        public event Action Changed;

        // ---- Selection -------------------------------------------------------------------------------------

        public bool Select(PlacedObject obj)
        {
            if (IsPlacing || !_map.Contains(obj) || Selected == obj) return false;
            Selected = obj;
            Changed?.Invoke();
            return true;
        }

        public bool ClearSelection()
        {
            if (Selected == null || IsPlacing) return false;
            Selected = null;
            Changed?.Invoke();
            return true;
        }

        // ---- Placing a new object --------------------------------------------------------------------------

        public bool BeginPlacing(PlaceableDefinition definition, GridCoord startOrigin)
        {
            if (definition == null) return false;
            Selected = null;
            Moving = null;
            Mode = PlacementMode.Placing;
            StartPreview(definition, startOrigin, Rotation90.Deg0);
            return true;
        }

        /// <summary>Starts moving the selected object. Its footprint stays occupied until the move is confirmed.</summary>
        public bool BeginMove()
        {
            if (IsPlacing || Selected == null) return false;
            Moving = Selected;
            Mode = PlacementMode.Moving;
            StartPreview(Moving.Definition, Moving.Origin, Moving.Rotation);
            return true;
        }

        public void SetOrigin(GridCoord origin)
        {
            if (!IsPlacing || origin == Origin) return;
            Origin = origin;
            Revalidate();
            Changed?.Invoke();
        }

        /// <summary>Rotates the preview a quarter turn about its origin cell. False if rotation is not supported or not placing.</summary>
        public bool Rotate()
        {
            if (!IsPlacing || !Definition.AllowRotation) return false;
            Rotation = Footprint.Next(Rotation);
            UpdateSize();
            Revalidate();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Commits the preview if valid. False (and no change) if invalid or not placing.</summary>
        public bool Confirm()
        {
            if (!IsPlacing || !Result.IsValid) return false;

            PlacedObject committed;
            if (Mode == PlacementMode.Placing)
            {
                committed = _map.Add(Definition, Origin, Rotation);
                if (committed == null) { Revalidate(); Changed?.Invoke(); return false; }
            }
            else
            {
                if (!_map.Move(Moving, Origin, Rotation)) { Revalidate(); Changed?.Invoke(); return false; }
                committed = Moving;
            }

            EndPreview();
            Selected = committed;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Abandons the preview. A moved object is left exactly where it was and stays selected.</summary>
        public void Cancel()
        {
            if (!IsPlacing) return;
            var keep = Moving;
            EndPreview();
            Selected = keep;
            Changed?.Invoke();
        }

        // ---- Editing the selection -------------------------------------------------------------------------

        /// <summary>Rotates the selected object in place (about its origin cell) if the rotated footprint is valid.</summary>
        public bool RotateSelected()
        {
            if (IsPlacing || Selected == null || !Selected.Definition.AllowRotation) return false;
            var rotation = Footprint.Next(Selected.Rotation);
            var result = _validator.Validate(Selected.Definition, Selected.Origin, rotation, Selected);
            if (!result.IsValid || !_map.Move(Selected, Selected.Origin, rotation))
            {
                LastRejection = result;
                Changed?.Invoke();
                return false;
            }
            LastRejection = PlacementResult.Valid;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Removes the selected object and frees its cells.</summary>
        public bool DeleteSelected()
        {
            if (IsPlacing || Selected == null) return false;
            return _map.Remove(Selected); // OnRemoved clears the selection and raises Changed
        }

        // ---- Internals -------------------------------------------------------------------------------------

        void StartPreview(PlaceableDefinition definition, GridCoord origin, Rotation90 rotation)
        {
            Definition = definition;
            Origin = origin;
            Rotation = rotation;
            UpdateSize();
            Revalidate();
            LastRejection = PlacementResult.Valid;
            Changed?.Invoke();
        }

        void EndPreview()
        {
            Mode = PlacementMode.Idle;
            Moving = null;
            Definition = null;
            Result = PlacementResult.Valid;
        }

        void UpdateSize()
        {
            Footprint.RotatedSize(Definition.FootprintWidth, Definition.FootprintHeight, Rotation, out int w, out int h);
            Width = w;
            Height = h;
        }

        void Revalidate() => Result = _validator.Validate(Definition, Origin, Rotation, Moving);

        void OnRemoved(PlacedObject obj)
        {
            bool relevant = false;
            if (Selected == obj) { Selected = null; relevant = true; }
            if (Moving == obj)
            {
                // The object vanished mid-move (e.g. removed by another system): fall back to a plain idle state.
                EndPreview();
                relevant = true;
            }
            if (relevant) Changed?.Invoke();
        }
    }
}
