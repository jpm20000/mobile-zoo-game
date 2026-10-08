using System;
using UnityEngine;
using ZooGame.Input;
using ZooGame.World;

namespace ZooGame.Animals
{
    /// <summary>
    /// Places a new animal the way a building is placed: choose a species, a see-through copy appears (green when the
    /// spot is legal, red when not), grab it to drag it, tap to move it, then Confirm to spawn or Cancel. A spot is legal
    /// only inside a closed enclosure that is big enough for the species. Driven by the shared build-mode controller, so
    /// it follows the same input rules as every other tool (UI-owned touches never arrive, a claimed finger never pans the
    /// camera, a press away from the ghost still moves the camera). The actual creation is <see cref="AnimalSpawner.SpawnAt"/>.
    /// </summary>
    public sealed class AnimalPlacementTool : IBuildTool
    {
        const int NoPointer = int.MinValue;
        static readonly Color ValidTint = new Color(0.35f, 1f, 0.45f, 1f);
        static readonly Color InvalidTint = new Color(1f, 0.35f, 0.35f, 1f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly Camera _camera;
        readonly ZooGrid _grid;
        readonly AnimalSpawner _spawner;
        readonly AnimalPlacementValidator _validator;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        AnimalDefinition _definition;
        GameObject _ghost;
        Renderer[] _renderers;
        Vector3 _position;
        string _enclosureId;
        int _dragPointer = NoPointer;
        Vector3 _grabOffset;

        public AnimalPlacementTool(Camera camera, ZooGrid grid, AnimalSpawner spawner)
        {
            _camera = camera;
            _grid = grid;
            _spawner = spawner;
            _validator = new AnimalPlacementValidator(spawner.Enclosures);
        }

        /// <summary>The species being placed, or null.</summary>
        public AnimalDefinition Definition => _definition;
        public Vector3 Position => _position;
        public AnimalPlacementResult Result { get; private set; }
        /// <summary>The enclosure under the ghost (null when it is over open ground).</summary>
        public string EnclosureId => _enclosureId;
        public GameObject GhostObject => _ghost;
        /// <summary>Outcome of the last Confirm, for the UI.</summary>
        public string LastMessage { get; private set; } = string.Empty;

        /// <summary>Raised when the species, the ghost position/validity or the last message changes.</summary>
        public event Action Changed;

        // ---- IBuildTool ------------------------------------------------------------------------------------

        public BuildMode Mode => BuildMode.AnimalPlacement;
        public InputOwner Owner => InputOwner.AnimalPlacement;
        public bool HasPending => _definition != null;
        public void Enter() { }
        public void Exit() => Clear();
        public void Cancel() => Clear();

        /// <summary>Starts placing a species; the ghost appears at the centre of the screen.</summary>
        public bool Select(AnimalDefinition definition)
        {
            if (definition == null || definition.Prefab == null) return false;
            DestroyGhost();
            _definition = definition;
            LastMessage = string.Empty;

            _ghost = UnityEngine.Object.Instantiate(definition.Prefab);
            _ghost.name = definition.DisplayName + " (ghost)";
            var selectable = _ghost.GetComponent<AnimalSelectable>();
            if (selectable != null)
            {
                selectable.SetSelected(true); // shows the footprint ring
                UnityEngine.Object.Destroy(selectable);
            }
            var controller = _ghost.GetComponent<AnimalController>();
            if (controller != null) UnityEngine.Object.Destroy(controller);
            _renderers = _ghost.GetComponentsInChildren<Renderer>();

            Vector3 start = TryGround(new Vector2(_camera.pixelWidth * 0.5f, _camera.pixelHeight * 0.5f), out var p) ? p : _grid.WorldCenter;
            MoveTo(start);
            return true;
        }

        public bool Confirm()
        {
            if (_definition == null) return false;
            if (!Result.IsValid)
            {
                LastMessage = Result.Message;
                Changed?.Invoke();
                return false;
            }
            var spawned = _spawner.SpawnAt(_definition.SpeciesId, _enclosureId, _position);
            if (!spawned.Success)
            {
                LastMessage = spawned.Message;
                Changed?.Invoke();
                return false;
            }
            string message = "Placed " + spawned.Animal.DisplayName;
            Clear();
            LastMessage = message;
            Changed?.Invoke();
            return true;
        }

        // ---- Input -----------------------------------------------------------------------------------------

        public bool WantsPointer(in PointerSample s)
        {
            if (_definition == null || _dragPointer != NoPointer) return false;
            if (!TryGround(s.ScreenPosition, out var ground)) return false;
            float reach = _grid.CellSize * 1.5f;
            var flat = ground - _position;
            flat.y = 0f;
            if (flat.sqrMagnitude > reach * reach) return false;
            _grabOffset = _position - ground;
            _dragPointer = s.PointerId;
            return true;
        }

        public void OnPointer(in PointerSample s)
        {
            if (s.PointerId != _dragPointer) return;
            if (s.Phase == PointerPhase.Moved)
            {
                if (_definition != null && TryGround(s.ScreenPosition, out var ground)) MoveTo(ground + _grabOffset);
            }
            else if (s.Phase == PointerPhase.Ended || s.Phase == PointerPhase.Canceled) _dragPointer = NoPointer;
        }

        public void OnTapped(int pointerId, Vector2 screenPosition)
        {
            if (_definition != null && TryGround(screenPosition, out var ground)) MoveTo(ground);
        }

        // ---- Internals -------------------------------------------------------------------------------------

        void MoveTo(Vector3 ground)
        {
            _position = new Vector3(ground.x, _grid.Origin.y, ground.z);
            Result = _validator.ValidateAt(_definition, _position, out _enclosureId);
            if (_ghost != null)
            {
                _ghost.transform.position = _position;
                var tint = Result.IsValid ? ValidTint : InvalidTint;
                _block.SetColor(BaseColorId, tint);
                _block.SetColor(ColorId, tint);
                for (int i = 0; i < _renderers.Length; i++) _renderers[i].SetPropertyBlock(_block);
            }
            Changed?.Invoke();
        }

        void Clear()
        {
            bool had = _definition != null;
            _definition = null;
            _dragPointer = NoPointer;
            _enclosureId = null;
            Result = default;
            DestroyGhost();
            if (had) Changed?.Invoke();
        }

        void DestroyGhost()
        {
            if (_ghost != null) UnityEngine.Object.Destroy(_ghost);
            _ghost = null;
            _renderers = null;
        }

        bool TryGround(Vector2 screen, out Vector3 point) => GroundProbe.TryGroundPoint(_camera, _grid, screen, out point);
    }
}
