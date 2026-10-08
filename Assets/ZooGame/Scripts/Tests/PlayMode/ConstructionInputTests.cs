using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZooGame.Cameras;
using ZooGame.Construction;
using ZooGame.Core;
using ZooGame.Data;
using ZooGame.Gameplay;
using ZooGame.Input;
using ZooGame.Placement;
using ZooGame.World;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ZooGame.Tests.PlayMode
{
    /// <summary>Drives path, fence, gate and demolish construction in the real Zoo scene with simulated touch.</summary>
    public class ConstructionInputTests
    {
        Touchscreen _touch;
        ZooCameraController _camera;
        ZooSceneBinder _binder;
        BuildModeController _build;
        ConstructionModel _model;
        PointerInputSource _source;
        Camera _cam;
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
            _source = Object.FindAnyObjectByType<PointerInputSource>();
            _build = _binder.Builder;
            Assert.IsNotNull(_build, "run ZooGame > M3 > Create Or Update Project Assets");
            _model = _binder.Construction;
            _cam = _camera.GetComponent<Camera>();
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

        /// <summary>Screen position of a grid point (cell corner).</summary>
        Vector2 ScreenOfPoint(int x, int z) => ScreenOf(_binder.Grid.GridToWorldCorner(new GridCoord(x, z)));

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

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        // ---- Paths ----

        [UnityTest]
        public IEnumerator PathDrag_PreviewsWithoutBuilding_ThenConfirmBuilds_WithoutPanningTheCamera()
        {
            _build.EnterPath();
            Assert.AreEqual(BuildMode.PathPlacement, _build.Mode);
            var focusBefore = _camera.TargetFocus;

            yield return Drag(ScreenOfCell(28, 30), ScreenOfCell(34, 30));

            Assert.AreEqual(focusBefore, _camera.TargetFocus, "camera must not pan while a path is drawn");
            Assert.GreaterOrEqual(_build.Path.Pending.Count, 7);
            Assert.AreEqual(_build.Path.Pending.Count, FindPreview().ItemCount, "every pending cell is previewed");
            Assert.IsFalse(_binder.Grid.HasPath(C(30, 30)), "a preview never changes grid data");
            Assert.IsTrue(_build.Active.HasPending);

            Assert.IsTrue(_build.Confirm());
            for (int x = 28; x <= 34; x++) Assert.IsTrue(_binder.Grid.HasPath(C(x, 30)), "cell " + x);
            Assert.AreEqual(0, _build.Path.Pending.Count);
            Assert.AreEqual(PathShape.DeadEnd, PathConnectivity.Describe(_binder.Grid, C(28, 30)).Shape);
            Assert.AreEqual(PathShape.Straight, PathConnectivity.Describe(_binder.Grid, C(31, 30)).Shape);
            Assert.IsTrue(_build.Mode == BuildMode.PathPlacement, "stays in path mode for the next stroke");
        }

        [UnityTest]
        public IEnumerator PathDrag_CancelDiscardsThePreview()
        {
            _build.EnterPath();
            yield return Drag(ScreenOfCell(28, 30), ScreenOfCell(31, 30));
            Assert.IsTrue(_build.Path.HasPending);
            _build.Cancel();
            Assert.IsFalse(_build.Path.HasPending);
            Assert.AreEqual(0, FindPreview().ItemCount);
            Assert.AreEqual(BuildMode.PathPlacement, _build.Mode, "first Cancel only discards the preview");
            _build.Cancel();
            Assert.AreEqual(BuildMode.None, _build.Mode, "second Cancel leaves the mode");
        }

        [UnityTest]
        public IEnumerator PathDrag_AcrossLockedLand_SkipsLockedCellsOnConfirm()
        {
            _binder.Grid.SetUnlocked(C(32, 30), false);
            _build.EnterPath();
            yield return Drag(ScreenOfCell(30, 30), ScreenOfCell(34, 30));
            CollectionAssert.Contains(_build.Path.Pending, C(32, 30), "locked cells are previewed (red) so the player sees why");
            _build.Confirm();
            Assert.IsFalse(_binder.Grid.HasPath(C(32, 30)));
            Assert.IsTrue(_binder.Grid.HasPath(C(31, 30)));
            Assert.IsTrue(_binder.Grid.HasPath(C(33, 30)));
        }

        [UnityTest]
        public IEnumerator PanMode_HandsTheDragBackToTheCamera()
        {
            _build.EnterPath();
            _build.PanMode = true;
            var focusBefore = _camera.TargetFocus;
            yield return Drag(ScreenOfCell(30, 30), ScreenOfCell(30, 30) + new Vector2(150f, 0f));
            Assert.Greater(Vector3.Distance(focusBefore, _camera.TargetFocus), 0.5f, "camera should pan");
            Assert.IsFalse(_build.Path.HasPending);
        }

        // ---- Fences and gates ----

        [UnityTest]
        public IEnumerator FenceDrags_AccumulateIntoARectangle_AndConfirmCreatesAnEnclosure()
        {
            var fence = _build.Catalog.FirstOfKind(EdgeKind.Fence);
            _build.EnterFence(fence);
            var focusBefore = _camera.TargetFocus;

            // Four straight runs around cells x 30..33, z 30..32 (corner points 30..34 by 30..33).
            yield return Drag(ScreenOfPoint(30, 30), ScreenOfPoint(34, 30));
            yield return Drag(ScreenOfPoint(34, 30), ScreenOfPoint(34, 33));
            yield return Drag(ScreenOfPoint(34, 33), ScreenOfPoint(30, 33));
            yield return Drag(ScreenOfPoint(30, 33), ScreenOfPoint(30, 30));

            Assert.AreEqual(focusBefore, _camera.TargetFocus, "camera must not pan while fencing");
            Assert.AreEqual(14, _build.Fence.Pending.Count);
            Assert.AreEqual(0, _model.Fences.Count, "the preview has not touched fence data");
            Assert.AreEqual(0, _model.Enclosures.Count);

            Assert.IsTrue(_build.Confirm());
            Assert.AreEqual(14, _model.Fences.Count);
            Assert.AreEqual(1, _model.Enclosures.Count);
            Assert.AreEqual(12, _model.Enclosures.GetAt(C(31, 31)).Area);
            Assert.AreNotEqual(0, _binder.Grid.GetCell(C(31, 31)).EnclosureId);
        }

        [UnityTest]
        public IEnumerator OpenFenceLayout_ConfirmsButCreatesNoEnclosure()
        {
            _build.EnterFence(_build.Catalog.FirstOfKind(EdgeKind.Fence));
            yield return Drag(ScreenOfPoint(30, 30), ScreenOfPoint(34, 30));
            yield return Drag(ScreenOfPoint(34, 30), ScreenOfPoint(34, 33));
            _build.Confirm();
            Assert.AreEqual(7, _model.Fences.Count);
            Assert.AreEqual(0, _model.Enclosures.Count);
        }

        [UnityTest]
        public IEnumerator GateTap_PlacesAGateOnTheNearestEdge()
        {
            _build.EnterFence(_build.Catalog.FirstOfKind(EdgeKind.Gate));
            Assert.AreEqual(EdgeKind.Gate, _build.Fence.Kind);

            // A point just above the middle of the south edge of cell (31, 30).
            yield return Tap(ScreenOf(new Vector3(31.5f, 0f, 30.08f)));
            Assert.AreEqual(1, _build.Fence.Pending.Count);
            _build.Confirm();

            Assert.AreEqual(EdgeKind.Gate, _model.Fences.Get(new EdgeCoord(EdgeAxis.X, 31, 30)));
            Assert.AreEqual(1, _model.Fences.Count);
        }

        [UnityTest]
        public IEnumerator FenceOverLockedLand_IsPreviewedButNotBuilt()
        {
            // Make a 3x3 locked patch so edges inside it have no unlocked neighbour.
            for (int x = 30; x < 33; x++)
            for (int z = 30; z < 33; z++) _binder.Grid.SetUnlocked(C(x, z), false);
            _build.EnterFence(_build.Catalog.FirstOfKind(EdgeKind.Fence));
            yield return Drag(ScreenOfPoint(30, 31), ScreenOfPoint(33, 31)); // runs through the patch
            Assert.AreEqual(3, _build.Fence.Pending.Count);
            _build.Confirm();
            Assert.AreEqual(0, _model.Fences.Count);
        }

        [UnityTest]
        public IEnumerator SquareFence_OneTouchPlacesAnEightByEightOutline_AndDragRepositionsIt()
        {
            FenceDefinition square = null;
            foreach (var d in _build.Catalog.Fences) if (d.SquareSize == 8) square = d;
            Assert.IsNotNull(square, "Fence 8x8 Square definition");
            _build.EnterFence(square);

            yield return Tap(ScreenOfPoint(28, 28));
            Assert.AreEqual(32, _build.Fence.Pending.Count);

            // A second touch moves the square rather than adding another.
            yield return Drag(ScreenOfPoint(28, 28), ScreenOfPoint(32, 33));
            Assert.AreEqual(32, _build.Fence.Pending.Count);
            bool moved = false; // the square is now centred on point (32,33): its minimum corner is (28,29)
            foreach (var fe in _build.Fence.Pending)
                if (fe.Edge == new EdgeCoord(EdgeAxis.X, 28, 29)) moved = true;
            Assert.IsTrue(moved, "dragging repositions the square");

            Assert.IsTrue(_build.Confirm());
            Assert.AreEqual(32, _model.Fences.Count);
            Assert.AreEqual(1, _model.Enclosures.Count);
            Assert.AreEqual(64, _model.Enclosures.GetAt(C(32, 33)).Area);
        }

        [UnityTest]
        public IEnumerator Gate_BuiltOverAFence_ReplacesIt()
        {
            _model.BuildFences(new[] { new FenceEdge(new EdgeCoord(EdgeAxis.X, 31, 30), EdgeKind.Fence) });
            _build.EnterFence(_build.Catalog.FirstOfKind(EdgeKind.Gate));
            yield return Tap(ScreenOf(new Vector3(31.5f, 0f, 30.08f)));
            Assert.AreEqual(1, _build.Fence.Pending.Count, "a gate over a fence is a valid pending change");
            _build.Confirm();
            Assert.AreEqual(EdgeKind.Gate, _model.Fences.Get(new EdgeCoord(EdgeAxis.X, 31, 30)));
            Assert.AreEqual(1, _model.Fences.Count);
        }

        [UnityTest]
        public IEnumerator PressingTheActiveToolButtonAgain_Deselects()
        {
            var button = GameObject.Find("Tool Basic Path").GetComponent<UnityEngine.UI.Button>();
            button.onClick.Invoke();
            Assert.AreEqual(BuildMode.PathPlacement, _build.Mode);
            button.onClick.Invoke();
            Assert.AreEqual(BuildMode.None, _build.Mode);

            var fence = GameObject.Find("Tool Basic Fence").GetComponent<UnityEngine.UI.Button>();
            var gate = GameObject.Find("Tool Basic Gate").GetComponent<UnityEngine.UI.Button>();
            fence.onClick.Invoke();
            gate.onClick.Invoke();
            Assert.AreEqual(EdgeKind.Gate, _build.Fence.Kind, "a different tool selects instead of toggling");
            gate.onClick.Invoke();
            Assert.AreEqual(BuildMode.None, _build.Mode);
            yield return null;
        }

        // ---- Demolition ----

        [UnityTest]
        public IEnumerator Demolish_RemovesMarkedPathsAndFences_OnConfirm()
        {
            _model.BuildPaths(new[] { C(30, 30), C(31, 30) });
            _model.BuildFences(new[] { new FenceEdge(new EdgeCoord(EdgeAxis.X, 33, 33), EdgeKind.Fence) });
            _build.EnterDemolish();

            yield return Tap(ScreenOfCell(30, 30));                                    // centre of a path cell
            yield return Tap(ScreenOf(new Vector3(33.5f, 0f, 33.05f)));                // on the fence line
            Assert.AreEqual(1, _build.Demolish.PendingPaths.Count);
            Assert.AreEqual(1, _build.Demolish.PendingEdges.Count);
            Assert.IsTrue(_binder.Grid.HasPath(C(30, 30)), "marking does not remove yet");

            _build.Confirm();
            Assert.IsFalse(_binder.Grid.HasPath(C(30, 30)));
            Assert.IsTrue(_binder.Grid.HasPath(C(31, 30)), "unmarked path survives");
            Assert.AreEqual(0, _model.Fences.Count);
            Assert.AreEqual(PathShape.Isolated, PathConnectivity.Describe(_binder.Grid, C(31, 30)).Shape);
        }

        [UnityTest]
        public IEnumerator Demolish_DragSweepsAcrossSeveralPathCells()
        {
            _model.BuildPaths(new[] { C(29, 30), C(30, 30), C(31, 30), C(32, 30) });
            _build.EnterDemolish();
            yield return Drag(ScreenOfCell(29, 30), ScreenOfCell(32, 30));
            Assert.AreEqual(4, _build.Demolish.PendingPaths.Count);
            _build.Confirm();
            for (int x = 29; x <= 32; x++) Assert.IsFalse(_binder.Grid.HasPath(C(x, 30)));
        }

        // ---- Modes and input isolation ----

        [UnityTest]
        public IEnumerator StartingObjectPlacement_LeavesConstructionMode_AndDiscardsItsPreview()
        {
            _build.EnterPath();
            yield return Drag(ScreenOfCell(28, 30), ScreenOfCell(31, 30));
            Assert.IsTrue(_build.Path.HasPending);

            var stall = FindDefinition("food_stall");
            Assert.IsTrue(_binder.PlacementController.BeginPlace(stall));

            Assert.AreEqual(BuildMode.ObjectPlacement, _build.Mode);
            Assert.IsFalse(_build.Path.HasPending);
            Assert.AreEqual(0, FindPreview().ItemCount);
            Assert.IsTrue(_binder.PlacementController.Session.IsPlacing);
        }

        [UnityTest]
        public IEnumerator EnteringConstruction_CancelsObjectPlacement()
        {
            Assert.IsTrue(_binder.PlacementController.BeginPlace(FindDefinition("food_stall")));
            yield return null;
            _build.EnterFence(_build.Catalog.FirstOfKind(EdgeKind.Fence));
            Assert.AreEqual(BuildMode.FencePlacement, _build.Mode);
            Assert.IsFalse(_binder.PlacementController.Session.IsPlacing);
        }

        [UnityTest]
        public IEnumerator ObjectPlacement_StillWorksAfterConstruction()
        {
            _build.EnterPath();
            _build.ExitMode();
            Assert.AreEqual(BuildMode.None, _build.Mode);

            var placement = _binder.PlacementController;
            Assert.IsTrue(placement.BeginPlace(FindDefinition("small_decoration")));
            yield return null;
            Assert.AreEqual(BuildMode.ObjectPlacement, _build.Mode);
            Assert.IsTrue(placement.Confirm());
            Assert.AreEqual(1, placement.Map.Count);
            Assert.AreEqual(BuildMode.None, _build.Mode, "back to idle after placing");
        }

        [UnityTest]
        public IEnumerator PathIsRejectedUnderAnObject_AndObjectIsRejectedOnAPath()
        {
            var placement = _binder.PlacementController;
            placement.BeginPlace(FindDefinition("food_stall"));
            var origin = placement.Session.Origin;
            Assert.IsTrue(placement.Confirm());

            Assert.AreEqual(BuildFailure.Occupied, _model.CanBuildPath(origin).Failure);

            _model.BuildPaths(new[] { C(origin.X + 5, origin.Z + 5) });
            placement.BeginPlace(FindDefinition("small_decoration"));
            placement.Session.SetOrigin(C(origin.X + 5, origin.Z + 5));
            Assert.IsFalse(placement.Session.Result.IsValid, "objects cannot stand on paths");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TouchOnConstructionButton_IsOwnedByUi_AndDrawsNothing()
        {
            _build.EnterPath();
            yield return null;
            var confirm = GameObject.Find("Confirm");
            Assert.IsNotNull(confirm, "Confirm button");
            var pos = RectTransformUtility.WorldToScreenPoint(null, confirm.transform.position);
            var focusBefore = _camera.TargetFocus;

            Touch(1, TouchPhase.Began, pos);
            yield return null;
            yield return null;
            Assert.AreEqual(InputOwner.Ui, _source.Ownership.GetOwner(1));
            for (int i = 1; i <= 5; i++)
            {
                Touch(1, TouchPhase.Moved, pos + new Vector2(-60f * i, 0f));
                yield return null;
            }
            Touch(1, TouchPhase.Ended, pos + new Vector2(-300f, 0f));
            yield return null;
            yield return null;

            Assert.IsFalse(_build.Path.HasPending, "UI touches must not draw");
            Assert.AreEqual(focusBefore, _camera.TargetFocus);
        }

        [UnityTest]
        public IEnumerator PinchZoom_WorksInPanMode_WhileAToolIsSelected()
        {
            _build.EnterPath();
            _build.PanMode = true;
            float zoom = _camera.TargetZoom;
            var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.6f);

            Touch(1, TouchPhase.Began, c + new Vector2(-50f, 0f));
            Touch(2, TouchPhase.Began, c + new Vector2(50f, 0f));
            yield return null;
            yield return null;
            Touch(2, TouchPhase.Moved, c + new Vector2(100f, 0f));
            yield return null;
            Touch(1, TouchPhase.Ended, c + new Vector2(-50f, 0f));
            Touch(2, TouchPhase.Ended, c + new Vector2(100f, 0f));
            yield return null;

            Assert.Less(_camera.TargetZoom, zoom);
            Assert.IsFalse(_build.Path.HasPending);
        }

        [UnityTest]
        public IEnumerator TwoFingers_MoveAndZoomTheCamera_WhileAToolIsSelected_WithoutPanMode()
        {
            _build.EnterPath();
            Assert.IsFalse(_build.PanMode);
            float zoom = _camera.TargetZoom;
            var focus = _camera.TargetFocus;
            var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.6f);
            var a = c + new Vector2(-60f, 0f);
            var b = c + new Vector2(60f, 0f);

            Touch(1, TouchPhase.Began, a);
            yield return null;                       // finger 1 starts a stroke
            Touch(2, TouchPhase.Began, b);
            yield return null;                       // finger 2 turns it into a camera gesture
            yield return null;

            Assert.IsFalse(_build.Path.IsStroking);
            Assert.IsFalse(_build.Path.HasPending, "a barely-moved first finger must not leave a stray cell");

            // Spread (zoom in), then move both fingers together (pan).
            Touch(1, TouchPhase.Moved, a + new Vector2(-40f, 0f));
            Touch(2, TouchPhase.Moved, b + new Vector2(40f, 0f));
            yield return null;
            Assert.Less(_camera.TargetZoom, zoom, "spreading zooms in");

            var panFrom = _camera.TargetFocus;
            for (int i = 1; i <= 4; i++)
            {
                Touch(1, TouchPhase.Moved, a + new Vector2(-40f, 0f) + new Vector2(0f, 30f * i));
                Touch(2, TouchPhase.Moved, b + new Vector2(40f, 0f) + new Vector2(0f, 30f * i));
                yield return null;
            }
            Assert.AreNotEqual(panFrom, _camera.TargetFocus, "two fingers moving together pan the camera");

            Touch(1, TouchPhase.Ended, a);
            Touch(2, TouchPhase.Ended, b);
            yield return null;
            yield return null;
            Assert.IsFalse(_build.Path.HasPending, "nothing was drawn");
            Assert.AreEqual(BuildMode.PathPlacement, _build.Mode, "the tool stays selected");
        }

        [UnityTest]
        public IEnumerator SecondFinger_AbortsAFenceRun_ButKeepsEarlierPendingEdges()
        {
            _build.EnterFence(_build.Catalog.FirstOfKind(EdgeKind.Fence));
            yield return Drag(ScreenOfPoint(30, 30), ScreenOfPoint(34, 30));
            int pending = _build.Fence.Pending.Count;
            Assert.AreEqual(4, pending);

            // A long drag interrupted by a second finger: the live (unlifted) run is dropped.
            Touch(1, TouchPhase.Began, ScreenOfPoint(34, 33));
            yield return null;
            Touch(1, TouchPhase.Moved, ScreenOfPoint(34, 35));
            yield return null;
            Touch(2, TouchPhase.Began, ScreenOfPoint(31, 33));
            yield return null;
            yield return null;
            Touch(1, TouchPhase.Ended, ScreenOfPoint(34, 35));
            Touch(2, TouchPhase.Ended, ScreenOfPoint(31, 33));
            yield return null;
            yield return null;

            Assert.AreEqual(pending, _build.Fence.Pending.Count, "earlier edges kept, interrupted run not added");
            Assert.IsFalse(_build.Fence.IsStroking);
        }

        [UnityTest]
        public IEnumerator OneFingerDrawingStillWorks_AfterATwoFingerCameraMove()
        {
            _build.EnterPath();
            var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.6f);
            Touch(1, TouchPhase.Began, c + new Vector2(-60f, 0f));
            Touch(2, TouchPhase.Began, c + new Vector2(60f, 0f));
            yield return null;
            yield return null;
            Touch(1, TouchPhase.Ended, c + new Vector2(-60f, 0f));
            Touch(2, TouchPhase.Ended, c + new Vector2(60f, 0f));
            yield return null;
            yield return null;

            yield return Drag(ScreenOfCell(28, 30), ScreenOfCell(31, 30));
            Assert.GreaterOrEqual(_build.Path.Pending.Count, 4);
        }

        // ---- Lookups ----

        ConstructionPreview FindPreview() => Object.FindAnyObjectByType<ConstructionPreview>();

        PlaceableDefinition FindDefinition(string id)
        {
            Assert.IsTrue(_binder.PlacementController.Catalog.TryGet(id, out var def), id);
            return def;
        }
    }
}
