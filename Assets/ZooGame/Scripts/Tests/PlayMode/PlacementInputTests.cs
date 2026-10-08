using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZooGame.Cameras;
using ZooGame.Core;
using ZooGame.Data;
using ZooGame.Gameplay;
using ZooGame.Input;
using ZooGame.Placement;
using ZooGame.World;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ZooGame.Tests.PlayMode
{
    /// <summary>Drives placement in the real Zoo scene with simulated touch: ghost grab, tap, UI, camera interplay.</summary>
    public class PlacementInputTests
    {
        Touchscreen _touch;
        ZooCameraController _camera;
        ZooSceneBinder _binder;
        PlacementController _placement;
        Camera _cam;
        PlaceableDefinition _stall, _utility, _decoration;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode _previousBehavior;
#endif

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            _previousBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            _touch = InputSystem.AddDevice<Touchscreen>();

            SceneManager.LoadScene("Bootstrap");
            float timeout = Time.realtimeSinceStartup + 10f;
            while ((GameManager.Instance == null || GameManager.Instance.State.Current != GameState.Playing)
                   && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State.Current);

            _camera = Object.FindAnyObjectByType<ZooCameraController>();
            _binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            _placement = _binder.PlacementController;
            Assert.IsNotNull(_placement, "run ZooGame > M2 > Create Or Update Project Assets");
            _cam = _camera.GetComponent<Camera>();

            var defs = _placement.Catalog.Definitions;
            Assert.IsTrue(_placement.Catalog.TryGet("small_decoration", out _decoration));
            Assert.IsTrue(_placement.Catalog.TryGet("food_stall", out _stall));
            Assert.IsTrue(_placement.Catalog.TryGet("utility_building", out _utility));
            Assert.GreaterOrEqual(defs.Count, 3);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_touch != null) InputSystem.RemoveDevice(_touch);
            if (GameManager.Instance != null) Object.Destroy(GameManager.Instance.gameObject);
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = _previousBehavior;
#endif
            yield return null;
        }

        // ---- Helpers ----

        void Touch(int id, TouchPhase phase, Vector2 position) =>
            InputSystem.QueueStateEvent(_touch, new TouchState { touchId = id, phase = phase, position = position, pressure = 1f });

        Vector2 ScreenOf(Vector3 world) => _cam.WorldToScreenPoint(world);

        Vector2 ScreenOfCell(int x, int z) => ScreenOf(_binder.Grid.GridToWorld(new GridCoord(x, z)));

        IEnumerator Tap(Vector2 position)
        {
            Touch(1, TouchPhase.Began, position);
            yield return null;
            Touch(1, TouchPhase.Ended, position);
            yield return null;
            yield return null;
        }

        IEnumerator Drag(Vector2 from, Vector2 to, int steps = 8)
        {
            Touch(1, TouchPhase.Began, from);
            yield return null;
            for (int i = 1; i <= steps; i++)
            {
                Touch(1, TouchPhase.Moved, Vector2.Lerp(from, to, i / (float)steps));
                yield return null;
            }
            Touch(1, TouchPhase.Ended, to);
            yield return null;
            yield return null;
        }

        /// <summary>Starts placing and taps so the ghost sits exactly on the given origin.</summary>
        IEnumerator BeginAt(PlaceableDefinition def, int ox, int oz)
        {
            Assert.IsTrue(_placement.BeginPlace(def));
            yield return MoveGhostByTap(ox, oz);
        }

        /// <summary>
        /// A press on or next to the ghost grabs it (drag), so a tap only repositions it when it lands further away.
        /// If the target is close to the ghost, tap a distant cell first.
        /// </summary>
        IEnumerator MoveGhostByTap(int ox, int oz)
        {
            var s = _placement.Session;
            if (Mathf.Abs(s.Origin.X - ox) <= 6 && Mathf.Abs(s.Origin.Z - oz) <= 6)
            {
                int hopX = ox + (ox < 32 ? 12 : -12);
                yield return Tap(ScreenOf(Footprint.CentreWorld(_binder.Grid, new GridCoord(hopX, oz), s.Width, s.Height)));
                Assert.AreEqual(hopX, s.Origin.X, "hop tap should reposition the ghost");
            }
            yield return Tap(ScreenOf(Footprint.CentreWorld(_binder.Grid, new GridCoord(ox, oz), s.Width, s.Height)));
            Assert.AreEqual(new GridCoord(ox, oz), s.Origin);
        }

        // ---- Tests ----

        [UnityTest]
        public IEnumerator BeginPlace_ShowsAGhostSnappedToTheGrid_ValidOnUnlockedLand()
        {
            Assert.IsTrue(_placement.BeginPlace(_stall));
            yield return null;

            var s = _placement.Session;
            Assert.AreEqual(PlacementMode.Placing, s.Mode);
            Assert.IsTrue(s.Result.IsValid);
            Assert.IsNotNull(_placement.GhostObject);
            Assert.AreEqual(0, _placement.Map.Count);

            // Ghost sits on the centre of its 2x2 footprint, i.e. on a cell corner.
            var pos = _placement.GhostObject.transform.position;
            Assert.AreEqual(0f, pos.x - Mathf.Round(pos.x), 1e-4f);
            Assert.AreEqual(0f, pos.z - Mathf.Round(pos.z), 1e-4f);
        }

        [UnityTest]
        public IEnumerator TapSnapsGhostToTheTappedCell_AndPlaceCommitsIt()
        {
            yield return BeginAt(_stall, 30, 30);

            Assert.IsTrue(_placement.Confirm());
            Assert.AreEqual(1, _placement.Map.Count);
            Assert.IsTrue(_binder.Grid.IsOccupied(new GridCoord(31, 31)));
            Assert.IsFalse(_binder.Grid.IsOccupied(new GridCoord(32, 32)));
            yield return null;
            Assert.IsNull(_placement.GhostObject);
            var placed = _placement.Map.GetAt(new GridCoord(30, 30));
            Assert.IsNotNull(placed.View);
            var viewPos = placed.View.transform.position;
            Assert.AreEqual(31f, viewPos.x, 1e-3f);
            Assert.AreEqual(31f, viewPos.z, 1e-3f);
        }

        [UnityTest]
        public IEnumerator DraggingTheGhost_MovesIt_AndDoesNotPanTheCamera()
        {
            yield return BeginAt(_stall, 30, 30);
            var focusBefore = _camera.TargetFocus;

            var from = ScreenOf(Footprint.CentreWorld(_binder.Grid, new GridCoord(30, 30), 2, 2));
            var to = ScreenOfCell(36, 30) + (from - ScreenOfCell(30, 30)); // keep grab offset
            yield return Drag(from, to);

            Assert.AreEqual(focusBefore, _camera.TargetFocus, "camera must not move while the ghost is dragged");
            Assert.AreNotEqual(new GridCoord(30, 30), _placement.Session.Origin, "ghost should have followed the finger");
            Assert.AreEqual(36, _placement.Session.Origin.X);
            Assert.AreEqual(30, _placement.Session.Origin.Z);
            Assert.IsFalse(_placement.IsDraggingGhost, "drag released with the finger");
        }

        [UnityTest]
        public IEnumerator DraggingAwayFromTheGhost_StillPansTheCamera()
        {
            yield return BeginAt(_stall, 30, 30);
            var focusBefore = _camera.TargetFocus;
            var originBefore = _placement.Session.Origin;

            // Start far from the ghost (more than the grab margin).
            var far = ScreenOfCell(40, 40);
            yield return Drag(far, far + new Vector2(150f, 0f));

            Assert.Greater(Vector3.Distance(focusBefore, _camera.TargetFocus), 0.5f, "camera should pan");
            Assert.AreEqual(originBefore, _placement.Session.Origin, "ghost must stay put");
        }

        [UnityTest]
        public IEnumerator InvalidPositions_AreRejected_LockedAndOccupied()
        {
            // Locked land: the unlocked block is cells 16..47. (Kept on screen and clear of the HUD button row.)
            yield return BeginAt(_stall, 15, 30); // x 15..16: column 15 is locked, 16 is the first unlocked
            Assert.AreEqual(PlacementFailure.LockedLand, _placement.Session.Result.Failure);
            Assert.IsFalse(_placement.Confirm());
            Assert.AreEqual(0, _placement.Map.Count);

            // Occupied: place one, then try to overlap it.
            yield return MoveGhostByTap(30, 30);
            Assert.IsTrue(_placement.Confirm());
            yield return BeginAt(_stall, 31, 31);
            Assert.AreEqual(PlacementFailure.Occupied, _placement.Session.Result.Failure);
            Assert.IsFalse(_placement.Confirm());
            Assert.AreEqual(1, _placement.Map.Count);
        }

        [UnityTest]
        public IEnumerator Rotate_SwapsGhostFootprint_AndPlacedObjectOccupiesRotatedCells()
        {
            yield return BeginAt(_utility, 30, 30); // 2x3
            Assert.AreEqual((2, 3), (_placement.Session.Width, _placement.Session.Height));
            Assert.IsTrue(_placement.Rotate());
            Assert.AreEqual((3, 2), (_placement.Session.Width, _placement.Session.Height));
            Assert.IsTrue(_placement.Confirm());

            Assert.IsTrue(_binder.Grid.IsOccupied(new GridCoord(32, 31)));
            Assert.IsFalse(_binder.Grid.IsOccupied(new GridCoord(30, 32)));
            yield return null;
            var view = _placement.Map.GetAt(new GridCoord(30, 30)).View;
            Assert.AreEqual(90f, view.transform.eulerAngles.y, 0.01f);
            Assert.AreEqual(31.5f, view.transform.position.x, 1e-3f);
            Assert.AreEqual(31f, view.transform.position.z, 1e-3f);
        }

        [UnityTest]
        public IEnumerator TapOnPlacedObject_Selects_AndTapOnEmptyGroundDeselects_WithoutMovingCamera()
        {
            yield return BeginAt(_stall, 30, 30);
            _placement.Confirm();
            _placement.Session.ClearSelection();
            var focusBefore = _camera.TargetFocus;

            yield return Tap(ScreenOfCell(31, 31));
            Assert.IsNotNull(_placement.Session.Selected);

            yield return Tap(ScreenOfCell(40, 20));
            Assert.IsNull(_placement.Session.Selected);
            Assert.AreEqual(focusBefore, _camera.TargetFocus);
        }

        [UnityTest]
        public IEnumerator Move_ThenCancel_RestoresOriginal_AndConfirmMovesOccupancy()
        {
            yield return BeginAt(_stall, 30, 30);
            _placement.Confirm();
            var obj = _placement.Session.Selected;

            Assert.IsTrue(_placement.BeginMoveSelected());
            yield return MoveGhostByTap(22, 26);
            Assert.IsTrue(_binder.Grid.IsOccupied(new GridCoord(30, 30)), "old cells stay occupied until the move is confirmed");
            _placement.Cancel();
            Assert.AreEqual(new GridCoord(30, 30), obj.Origin);
            Assert.IsFalse(_binder.Grid.IsOccupied(new GridCoord(22, 26)));
            yield return null;
            Assert.IsTrue(obj.View.activeSelf, "original object visible again after cancel");

            Assert.IsTrue(_placement.BeginMoveSelected());
            yield return MoveGhostByTap(22, 26);
            Assert.IsTrue(_placement.Confirm());
            Assert.AreEqual(new GridCoord(22, 26), obj.Origin);
            Assert.IsFalse(_binder.Grid.IsOccupied(new GridCoord(30, 30)));
            Assert.IsTrue(_binder.Grid.IsOccupied(new GridCoord(23, 27)));
        }

        [UnityTest]
        public IEnumerator Delete_RemovesViewAndFreesCells()
        {
            yield return BeginAt(_utility, 30, 30);
            _placement.Confirm();
            var view = _placement.Session.Selected.View;
            Assert.IsNotNull(view);

            Assert.IsTrue(_placement.DeleteSelected());
            yield return null;
            Assert.IsTrue(view == null, "view GameObject destroyed");
            Assert.AreEqual(0, _placement.Map.Count);
            Assert.IsFalse(_binder.Grid.IsOccupied(new GridCoord(30, 30)));
            Assert.IsFalse(_binder.Grid.IsOccupied(new GridCoord(31, 32)));
        }

        [UnityTest]
        public IEnumerator TouchOnPlacementButton_IsOwnedByUi_AndDoesNotMoveGhostOrCamera()
        {
            yield return BeginAt(_stall, 30, 30);
            yield return null; // let the panel switch to its previewing layout
            var focusBefore = _camera.TargetFocus;
            var originBefore = _placement.Session.Origin;

            var place = GameObject.Find("Place");
            Assert.IsNotNull(place, "Place button");
            var pos = RectTransformUtility.WorldToScreenPoint(null, place.transform.position);

            var source = Object.FindAnyObjectByType<PointerInputSource>();
            Touch(1, TouchPhase.Began, pos);
            yield return null;
            yield return null;
            Assert.AreEqual(InputOwner.Ui, source.Ownership.GetOwner(1));
            for (int i = 1; i <= 5; i++)
            {
                Touch(1, TouchPhase.Moved, pos + new Vector2(0f, 60f * i));
                yield return null;
            }
            Touch(1, TouchPhase.Ended, pos + new Vector2(0f, 300f));
            yield return null;
            yield return null;

            Assert.AreEqual(focusBefore, _camera.TargetFocus);
            Assert.AreEqual(originBefore, _placement.Session.Origin);
        }

        [UnityTest]
        public IEnumerator PinchZoom_StillWorksWhilePlacing()
        {
            yield return BeginAt(_stall, 30, 30);
            float zoom = _camera.TargetZoom;
            var c = new Vector2(Screen.width * 0.7f, Screen.height * 0.7f); // away from the ghost

            Touch(1, TouchPhase.Began, c + new Vector2(-50f, 0f));
            Touch(2, TouchPhase.Began, c + new Vector2(50f, 0f));
            yield return null;
            yield return null;
            Touch(2, TouchPhase.Moved, c + new Vector2(100f, 0f));
            yield return null;
            Touch(1, TouchPhase.Ended, c + new Vector2(-50f, 0f));
            Touch(2, TouchPhase.Ended, c + new Vector2(100f, 0f));
            yield return null;

            Assert.Less(_camera.TargetZoom, zoom, "pinch should zoom in");
            Assert.AreEqual(new GridCoord(30, 30), _placement.Session.Origin);
        }
    }
}
