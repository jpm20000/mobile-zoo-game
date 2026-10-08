using System.Collections.Generic;
using UnityEngine;
using ZooGame.Data;
using ZooGame.Placement;
using ZooGame.World;

namespace ZooGame.Tests.EditMode
{
    /// <summary>Shared fixture: a 16x16 grid whose unlocked block is the centred 8x8 (cells 4..11 on both axes).</summary>
    public sealed class PlacementFixture
    {
        readonly List<Object> _created = new List<Object>();

        public ZooGrid Grid { get; }
        public PlacementMap Map { get; }
        public PlacementValidator Validator { get; }
        public PlacementSession Session { get; }

        public PlacementFixture()
        {
            Grid = new ZooGrid(GridSettings.Centered(16, 16, 1f, Vector3.zero, 8, 8));
            Map = new PlacementMap(Grid);
            Validator = new PlacementValidator(Map);
            Session = new PlacementSession(Map, Validator);
        }

        public PlaceableDefinition Define(string id, int width, int height, bool rotatable = true)
        {
            var def = ScriptableObject.CreateInstance<PlaceableDefinition>();
            def.Configure(id, id, width, height, rotatable);
            _created.Add(def);
            return def;
        }

        public void Dispose()
        {
            for (int i = 0; i < _created.Count; i++) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        public int OccupiedCount()
        {
            int n = 0;
            for (int z = 0; z < Grid.Depth; z++)
            for (int x = 0; x < Grid.Width; x++)
                if (Grid.IsOccupied(new GridCoord(x, z))) n++;
            return n;
        }
    }
}
