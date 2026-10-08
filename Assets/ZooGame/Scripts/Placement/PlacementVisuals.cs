using UnityEngine;
using ZooGame.Data;
using ZooGame.World;

namespace ZooGame.Placement
{
    /// <summary>Where a footprint's visual goes in the world. Prefabs are authored centred on their pivot, in cell units.</summary>
    public static class PlacementPose
    {
        /// <summary>Centre of the (already rotated) footprint rectangle at ground height.</summary>
        public static Vector3 Position(ZooGrid grid, GridCoord origin, int width, int height) =>
            Footprint.CentreWorld(grid, origin, width, height);

        /// <summary>
        /// Yaw about Y for the rotation step. Rotating the prefab a quarter turn about its centre produces exactly
        /// the swapped-size rectangle that <see cref="Footprint.RotatedSize"/> reports, so visual and cells agree.
        /// </summary>
        public static Quaternion Rotation(Rotation90 rotation) => Quaternion.Euler(0f, Footprint.Degrees(rotation), 0f);
    }

    /// <summary>One flat, tinted rectangle lying on the ground. Used for the ghost footprint and the selection marker.</summary>
    public sealed class FootprintPlate
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly GameObject _go;
        readonly Transform _transform;
        readonly MeshRenderer _renderer;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly float _lift;
        Color _color = Color.clear;

        public FootprintPlate(string name, Transform parent, Mesh unitQuad, Material material, float lift)
        {
            _go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            _transform = _go.transform;
            _transform.SetParent(parent, false);
            _go.GetComponent<MeshFilter>().sharedMesh = unitQuad;
            _renderer = _go.GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _lift = lift;
            _go.SetActive(false);
        }

        public bool Visible => _go.activeSelf;

        public void Show(ZooGrid grid, GridCoord origin, int width, int height, Color color)
        {
            var corner = grid.GridToWorldCorner(origin);
            _transform.position = new Vector3(corner.x, corner.y + _lift, corner.z);
            _transform.localScale = new Vector3(width * grid.CellSize, 1f, height * grid.CellSize);
            if (color != _color)
            {
                _color = color;
                _block.SetColor(ColorId, color);
                _renderer.SetPropertyBlock(_block);
            }
            if (!_go.activeSelf) _go.SetActive(true);
        }

        public void Hide()
        {
            if (_go.activeSelf) _go.SetActive(false);
        }

        public void Destroy()
        {
            if (_go != null) Object.Destroy(_go);
        }

        /// <summary>A 1x1 quad on the XZ plane from (0,0) to (1,1), facing up.</summary>
        public static Mesh CreateUnitQuad()
        {
            var mesh = new Mesh { name = "FootprintPlateQuad" };
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, 0) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.UploadMeshData(true);
            return mesh;
        }
    }

    /// <summary>Creates, moves and destroys the GameObjects for placed objects, following <see cref="PlacementMap"/> events.</summary>
    public sealed class PlacedObjectViews
    {
        readonly ZooGrid _grid;
        readonly Transform _parent;
        readonly PlacementMap _map;

        public PlacedObjectViews(PlacementMap map, Transform parent)
        {
            _map = map;
            _grid = map.Grid;
            _parent = parent;
            map.Placed += OnPlaced;
            map.Moved += Reposition;
            map.Removed += OnRemoved;
        }

        public void Dispose()
        {
            _map.Placed -= OnPlaced;
            _map.Moved -= Reposition;
            _map.Removed -= OnRemoved;
            foreach (var obj in _map.Objects)
                if (obj.View != null) Object.Destroy(obj.View);
        }

        void OnPlaced(PlacedObject obj)
        {
            obj.View = Instantiate(obj.Definition, "Placed " + obj.Definition.DisplayName + " #" + obj.Id, _parent, _grid.CellSize);
            Reposition(obj);
        }

        void Reposition(PlacedObject obj)
        {
            if (obj.View == null) return;
            obj.View.transform.SetPositionAndRotation(
                PlacementPose.Position(_grid, obj.Origin, obj.Width, obj.Height), PlacementPose.Rotation(obj.Rotation));
        }

        void OnRemoved(PlacedObject obj)
        {
            if (obj.View != null) Object.Destroy(obj.View);
            obj.View = null;
        }

        /// <summary>Spawns the definition's prefab (or a plain box sized to the footprint when it has none), without colliders.</summary>
        public static GameObject Instantiate(PlaceableDefinition definition, string name, Transform parent, float cellSize)
        {
            GameObject go;
            if (definition.Prefab != null)
            {
                go = Object.Instantiate(definition.Prefab, parent);
            }
            else
            {
                go = new GameObject();
                go.transform.SetParent(parent, false);
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.transform.SetParent(go.transform, false);
                box.transform.localScale = new Vector3(definition.FootprintWidth * 0.9f, 1f, definition.FootprintHeight * 0.9f);
                box.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            }
            go.name = name;
            go.transform.localScale *= cellSize;
            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) Object.Destroy(colliders[i]);
            return go;
        }
    }

    /// <summary>
    /// The placement preview: a tinted footprint plate plus a see-through copy of the prefab, green when the
    /// current position is valid and red when not. Rebuilt when the previewed definition changes; otherwise only
    /// repositioned and recoloured on change.
    /// </summary>
    public sealed class PlacementGhost
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly ZooGrid _grid;
        readonly PlacementConfig _config;
        readonly Transform _parent;
        readonly FootprintPlate _plate;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        GameObject _clone;
        Renderer[] _renderers;
        PlaceableDefinition _definition;
        Color _color = Color.clear;

        public PlacementGhost(ZooGrid grid, PlacementConfig config, Transform parent, Mesh unitQuad)
        {
            _grid = grid;
            _config = config;
            _parent = parent;
            _plate = new FootprintPlate("Placement Ghost Plate", parent, unitQuad, config.GhostMaterial, config.PlateHeight);
        }

        public GameObject Root => _clone;
        public bool Visible => _plate.Visible;

        public void Show(PlaceableDefinition definition, GridCoord origin, Rotation90 rotation, int width, int height, bool valid)
        {
            if (_definition != definition) Rebuild(definition);

            var color = valid ? _config.ValidColor : _config.InvalidColor;
            _plate.Show(_grid, origin, width, height, color);

            _clone.transform.SetPositionAndRotation(PlacementPose.Position(_grid, origin, width, height), PlacementPose.Rotation(rotation));
            if (color != _color)
            {
                _color = color;
                _block.SetColor(ColorId, color);
                for (int i = 0; i < _renderers.Length; i++) _renderers[i].SetPropertyBlock(_block);
            }
        }

        public void Hide()
        {
            _plate.Hide();
            DestroyClone();
        }

        public void Destroy()
        {
            _plate.Destroy();
            DestroyClone();
        }

        void Rebuild(PlaceableDefinition definition)
        {
            DestroyClone();
            _definition = definition;
            _clone = PlacedObjectViews.Instantiate(definition, "Placement Ghost", _parent, _grid.CellSize);
            _renderers = _clone.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var slots = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int s = 0; s < slots.Length; s++) slots[s] = _config.GhostMaterial;
                r.sharedMaterials = slots;
            }
            _color = Color.clear; // force the next Show to tint the new renderers
        }

        void DestroyClone()
        {
            if (_clone != null) Object.Destroy(_clone);
            _clone = null;
            _renderers = null;
            _definition = null;
        }
    }
}
