using UnityEngine;
using UnityEngine.InputSystem;
using ZooGame.Data;
using ZooGame.Input;
using ZooGame.World;

namespace ZooGame.Placement
{
    /// <summary>
    /// Connects the pure placement logic to the scene: reads shared pointer input, drives the preview ghost,
    /// selection marker and placed-object views. Event driven; the only per-frame work is a few key checks in the
    /// Editor/standalone builds.
    ///
    /// Touch model: while placing, a press that lands on (or within a cell of) the ghost <i>grabs</i> it and drags it
    /// across the grid (the camera is not involved because the pointer is claimed as Placement before gestures are
    /// recognised). A tap anywhere else snaps the ghost there. A drag that starts away from the ghost pans the camera,
    /// and pinch zoom keeps working. UI presses are owned by UI and never reach this class. When idle, a tap selects
    /// the placed object under the finger (or clears the selection). Hover and right-click are never used.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlacementController : MonoBehaviour, IBuildTool
    {
        const int NoPointer = int.MinValue;

        Camera _camera;
        ZooGrid _grid;
        PlacementConfig _config;
        PlaceableCatalog _catalog;

        PlacementMap _map;
        PlacementValidator _validator;
        PlacementSession _session;
        PlacedObjectViews _views;
        PlacementGhost _ghost;
        FootprintPlate _selectionPlate;
        Mesh _quad;
        Transform _viewRoot;

        int _dragPointer = NoPointer;
        Vector3 _grabOffset;
        GameObject _hiddenView;

        public PlacementMap Map => _map;
        public PlacementValidator Validator => _validator;
        public PlacementSession Session => _session;
        public PlaceableCatalog Catalog => _catalog;
        public bool IsBound => _session != null;
        /// <summary>True while a pointer is dragging the ghost.</summary>
        public bool IsDraggingGhost => _dragPointer != NoPointer;
        /// <summary>The see-through copy of the previewed prefab, or null when not placing.</summary>
        public GameObject GhostObject => _ghost?.Root;

        // ---- IBuildTool (driven by the build-mode controller, which owns input) ----
        public BuildMode Mode => IsBound && _session.IsPlacing ? BuildMode.ObjectPlacement : BuildMode.None;
        public InputOwner Owner => InputOwner.Placement;
        public bool HasPending => IsBound && _session.IsPlacing;
        public void Enter() { }
        public void Exit()
        {
            if (!IsBound) return;
            if (_session.IsPlacing) _session.Cancel();
            _session.ClearSelection();
        }
        bool IBuildTool.Confirm() => Confirm();
        void IBuildTool.Cancel() { if (IsBound && _session.IsPlacing) _session.Cancel(); }

        public void Bind(Camera camera, ZooGrid grid, PlacementConfig config, PlaceableCatalog catalog)
        {
            Unbind();
            _camera = camera;
            _grid = grid;
            _config = config;
            _catalog = catalog;

            _map = new PlacementMap(grid);
            _validator = new PlacementValidator(_map);
            _session = new PlacementSession(_map, _validator);

            _viewRoot = new GameObject("Placed Objects").transform;
            _viewRoot.SetParent(transform, false);
            _quad = FootprintPlate.CreateUnitQuad();
            _views = new PlacedObjectViews(_map, _viewRoot);
            _ghost = new PlacementGhost(grid, config, _viewRoot, _quad);
            _selectionPlate = new FootprintPlate("Selection Plate", _viewRoot, _quad, config.GhostMaterial, config.PlateHeight);

            _session.Changed += Refresh;
        }

        void Unbind()
        {
            if (_session == null) return;
            _dragPointer = NoPointer;
            _session.Changed -= Refresh;
            _views.Dispose();
            _ghost.Destroy();
            _selectionPlate.Destroy();
            if (_viewRoot != null) Destroy(_viewRoot.gameObject);
            if (_quad != null) Destroy(_quad);
            _session = null;
        }

        void OnDestroy() => Unbind();

        // ---- Commands (UI buttons and Editor shortcuts call these) -----------------------------------------

        /// <summary>Starts placing a new object; the ghost appears at the centre of the screen.</summary>
        public bool BeginPlace(PlaceableDefinition definition)
        {
            if (!IsBound || definition == null) return false;
            if (_session.IsPlacing) _session.Cancel();
            Footprint.RotatedSize(definition.FootprintWidth, definition.FootprintHeight, Rotation90.Deg0, out int w, out int h);
            var centre = TryGroundPoint(new Vector2(_camera.pixelWidth * 0.5f, _camera.pixelHeight * 0.5f), out var p) ? p : _grid.WorldCenter;
            return _session.BeginPlacing(definition, Footprint.OriginForCentre(_grid, centre, w, h));
        }

        public bool BeginMoveSelected() => IsBound && _session.BeginMove();

        public bool Rotate() => IsBound && (_session.IsPlacing ? _session.Rotate() : _session.RotateSelected());

        public bool Confirm() => IsBound && _session.Confirm();

        public void Cancel()
        {
            if (!IsBound) return;
            if (_session.IsPlacing) _session.Cancel();
            else _session.ClearSelection();
        }

        public bool DeleteSelected() => IsBound && _session.DeleteSelected();

        // ---- Input -----------------------------------------------------------------------------------------

        public bool WantsPointer(in PointerSample s)
        {
            if (!IsBound || !_session.IsPlacing || _dragPointer != NoPointer) return false;
            if (!TryGroundPoint(s.ScreenPosition, out var ground) || !GhostContains(ground)) return false;

            _grabOffset = Footprint.CentreWorld(_grid, _session.Origin, _session.Width, _session.Height) - ground;
            _dragPointer = s.PointerId;
            return true;
        }

        public void OnPointer(in PointerSample s)
        {
            if (s.PointerId != _dragPointer) return;
            switch (s.Phase)
            {
                case PointerPhase.Moved:
                    if (_session.IsPlacing) MoveGhostTo(s.ScreenPosition, _grabOffset);
                    break;
                case PointerPhase.Ended:
                case PointerPhase.Canceled:
                    _dragPointer = NoPointer;
                    break;
            }
        }

        public void OnTapped(int pointerId, Vector2 position)
        {
            if (!IsBound) return;
            if (_session.IsPlacing)
            {
                MoveGhostTo(position, Vector3.zero);
                return;
            }

            if (TryGroundPoint(position, out var ground) && _grid.TryWorldToGrid(ground, out var cell))
            {
                var hit = _map.GetAt(cell);
                if (hit != null) _session.Select(hit);
                else _session.ClearSelection();
            }
            else
            {
                _session.ClearSelection();
            }
        }

        void MoveGhostTo(Vector2 screenPosition, Vector3 offset)
        {
            if (!TryGroundPoint(screenPosition, out var ground)) return;
            _session.SetOrigin(Footprint.OriginForCentre(_grid, ground + offset, _session.Width, _session.Height));
        }

        bool TryGroundPoint(Vector2 screenPosition, out Vector3 point) => GroundProbe.TryGroundPoint(_camera, _grid, screenPosition, out point);

        bool GhostContains(Vector3 ground)
        {
            var min = _grid.GridToWorldCorner(_session.Origin);
            float margin = _config.GrabMarginCells * _grid.CellSize;
            float maxX = min.x + _session.Width * _grid.CellSize;
            float maxZ = min.z + _session.Height * _grid.CellSize;
            return ground.x >= min.x - margin && ground.x <= maxX + margin
                && ground.z >= min.z - margin && ground.z <= maxZ + margin;
        }

        // ---- Visuals ---------------------------------------------------------------------------------------

        void Refresh()
        {
            // Hide the real object while its replacement ghost is shown; restore it as soon as the move ends.
            var moving = _session.Mode == PlacementMode.Moving ? _session.Moving?.View : null;
            if (_hiddenView != null && _hiddenView != moving) _hiddenView.SetActive(true);
            _hiddenView = moving;
            if (_hiddenView != null) _hiddenView.SetActive(false);

            if (_session.IsPlacing)
            {
                _ghost.Show(_session.Definition, _session.Origin, _session.Rotation, _session.Width, _session.Height, _session.Result.IsValid);
                _selectionPlate.Hide();
            }
            else
            {
                _ghost.Hide();
                var sel = _session.Selected;
                if (sel != null) _selectionPlate.Show(_grid, sel.Origin, sel.Width, sel.Height, _config.SelectionColor);
                else _selectionPlate.Hide();
            }
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        // Editor/desktop fallback so everything is testable without touching the test panel:
        // 1-9 start placing the catalog entry, R rotate, Enter/Space confirm, Esc cancel/deselect, M move, Delete remove.
        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !IsBound) return;

            if (kb.rKey.wasPressedThisFrame) Rotate();
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Confirm();
            if (kb.escapeKey.wasPressedThisFrame) Cancel();
            if (kb.mKey.wasPressedThisFrame) BeginMoveSelected();
            if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) DeleteSelected();

            if (_catalog == null || _session.IsPlacing) return;
            var defs = _catalog.Definitions;
            for (int i = 0; i < defs.Count && i < 9; i++)
            {
                if (kb[Key.Digit1 + i].wasPressedThisFrame) BeginPlace(defs[i]);
            }
        }
#endif
    }
}
