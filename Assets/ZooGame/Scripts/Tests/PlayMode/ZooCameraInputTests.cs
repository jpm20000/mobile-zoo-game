using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZooGame.Cameras;
using ZooGame.Core;
using ZooGame.Gameplay;
using ZooGame.Input;
using ZooGame.World;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ZooGame.Tests.PlayMode
{
    /// <summary>
    /// Drives the real Zoo scene with simulated Touchscreen/Mouse devices to check pan, pinch, wheel zoom, bounds and
    /// UI input blocking end to end.
    /// </summary>
    public class ZooCameraInputTests
    {
        Touchscreen _touch;
        Mouse _mouse;
        ZooCameraController _camera;
        ZooSceneBinder _binder;
        PointerInputSource _source;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode _previousBehavior;
#endif

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Let simulated devices reach the game even when the Game view is not focused.
#if UNITY_EDITOR
            _previousBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif

            _touch = InputSystem.AddDevice<Touchscreen>();
            _mouse = InputSystem.AddDevice<Mouse>();

            SceneManager.LoadScene("Bootstrap");
            float timeout = Time.realtimeSinceStartup + 10f;
            while ((GameManager.Instance == null || GameManager.Instance.State.Current != GameState.Playing)
                   && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State.Current);

            _camera = Object.FindAnyObjectByType<ZooCameraController>();
            _binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            _source = Object.FindAnyObjectByType<PointerInputSource>();
            Assert.IsNotNull(_camera);
            Assert.IsNotNull(_binder);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_touch != null) InputSystem.RemoveDevice(_touch);
            if (_mouse != null) InputSystem.RemoveDevice(_mouse);
            if (GameManager.Instance != null) Object.Destroy(GameManager.Instance.gameObject);
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = _previousBehavior;
#endif
            yield return null;
        }

        // ---- Helpers ----

        void Touch(int id, TouchPhase phase, Vector2 position) =>
            InputSystem.QueueStateEvent(_touch, new TouchState
            {
                touchId = id,
                phase = phase,
                position = position,
                pressure = 1f
            });

        Vector2 _lastMousePosition;

        void Mouse(Vector2 position, bool left, float scrollY = 0f)
        {
            // Real mice report a per-event delta; simulated state must as well.
            var state = new MouseState { position = position, delta = position - _lastMousePosition, scroll = new Vector2(0f, scrollY) };
            _lastMousePosition = position;
            if (left) state = state.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(_mouse, state);
        }

        static Vector2 ScreenCentre => new Vector2(Screen.width * 0.5f, Screen.height * 0.6f);

        IEnumerator DragTouch(int id, Vector2 from, Vector2 to, int steps = 8)
        {
            Touch(id, TouchPhase.Began, from);
            yield return null;
            for (int i = 1; i <= steps; i++)
            {
                Touch(id, TouchPhase.Moved, Vector2.Lerp(from, to, i / (float)steps));
                yield return null;
            }
            Touch(id, TouchPhase.Ended, to);
            yield return null;
            yield return null;
        }

        static Vector2 ScreenPositionOf(string objectName)
        {
            var go = GameObject.Find(objectName);
            Assert.IsNotNull(go, objectName + " not found");
            return RectTransformUtility.WorldToScreenPoint(null, go.transform.position);
        }

        // ---- Tests ----

        [UnityTest]
        public IEnumerator Zoo_StartsWith64x64Grid_32x32Unlocked_AndCameraOnUnlockedArea()
        {
            var grid = _binder.Grid;
            Assert.AreEqual(64, grid.Width);
            Assert.AreEqual(64, grid.Depth);
            Assert.AreEqual(1024, grid.UnlockedCount);
            Assert.IsTrue(grid.IsUnlocked(new GridCoord(32, 32)));
            Assert.IsFalse(grid.IsUnlocked(new GridCoord(5, 5)));

            var cam = _camera.GetComponent<Camera>();
            Assert.IsTrue(cam.orthographic);
            Assert.AreEqual(32f, _camera.Focus.x, 0.01f);
            Assert.AreEqual(32f, _camera.Focus.z, 0.01f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OneFingerDrag_PansCameraWithGroundFollowingFinger()
        {
            var before = _camera.TargetFocus;
            var cam = _camera.GetComponent<Camera>();
            float upp = CameraMath.UnitsPerPixel(_camera.TargetZoom, cam.pixelHeight);

            yield return DragTouch(1, ScreenCentre, ScreenCentre + new Vector2(200f, 0f));

            var moved = _camera.TargetFocus - before;
            CameraMath.GroundAxes(45f, out var right, out _);
            // Finger moved right, so the camera moves left along the ground (minus the drag dead zone).
            float along = -Vector3.Dot(moved, right);
            Assert.Greater(along, 80f * upp, "camera should pan");
            Assert.LessOrEqual(along, 200f * upp + 0.001f);
            Assert.AreEqual(0f, Vector3.Dot(moved, Vector3.up), 1e-4f);
        }

        [UnityTest]
        public IEnumerator Tap_DoesNotMoveCamera()
        {
            var before = _camera.TargetFocus;
            Touch(1, TouchPhase.Began, ScreenCentre);
            yield return null;
            Touch(1, TouchPhase.Ended, ScreenCentre);
            yield return null;
            yield return null;
            Assert.AreEqual(before, _camera.TargetFocus);
        }

        [UnityTest]
        public IEnumerator TouchStartingOnUi_DoesNotMoveCamera_AndIsOwnedByUi()
        {
            var before = _camera.TargetFocus;
            var button = ScreenPositionOf("Pause");

            Touch(1, TouchPhase.Began, button);
            yield return null;
            yield return null;
            Assert.AreEqual(InputOwner.Ui, _source.Ownership.GetOwner(1), "UI should own the pointer");

            for (int i = 1; i <= 6; i++)
            {
                Touch(1, TouchPhase.Moved, button + new Vector2(60f * i, 80f * i));
                yield return null;
            }
            Touch(1, TouchPhase.Ended, button + new Vector2(360f, 480f));
            yield return null;
            yield return null;

            Assert.AreEqual(before, _camera.TargetFocus, "UI touches must not pan the camera");
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State.Current);
        }

        [UnityTest]
        public IEnumerator Pinch_ZoomsByFingerSpreadRatio_AndRespectsLimits()
        {
            float start = _camera.TargetZoom;
            var c = ScreenCentre;

            Touch(1, TouchPhase.Began, c + new Vector2(-50f, 0f));
            Touch(2, TouchPhase.Began, c + new Vector2(50f, 0f));
            yield return null;
            yield return null;

            Touch(2, TouchPhase.Moved, c + new Vector2(100f, 0f)); // distance 100 -> 150 (x1.5)
            yield return null;
            Assert.AreEqual(start / 1.5f, _camera.TargetZoom, start * 0.02f);

            Touch(2, TouchPhase.Moved, c + new Vector2(1000f, 0f)); // huge spread
            yield return null;
            var cam = _camera.GetComponent<Camera>();
            Assert.AreEqual(5f, _camera.TargetZoom, 1e-3f, "clamped to min zoom");

            Touch(2, TouchPhase.Moved, c + new Vector2(-45f, 0f)); // fingers nearly together
            yield return null;
            Assert.AreEqual(20f, _camera.TargetZoom, 1e-3f, "clamped to max zoom");

            Touch(1, TouchPhase.Ended, c + new Vector2(-50f, 0f));
            Touch(2, TouchPhase.Ended, c + new Vector2(-45f, 0f));
            yield return null;
            Assert.IsTrue(cam.orthographic);
        }

        [UnityTest]
        public IEnumerator Pan_StaysWithinMapBoundsPlusMargin()
        {
            for (int i = 0; i < 25; i++)
                yield return DragTouch(1, ScreenCentre, ScreenCentre + new Vector2(Screen.width * 0.4f, 0f), 4);

            var f = _camera.TargetFocus;
            var grid = _binder.Grid;
            Assert.GreaterOrEqual(f.x, grid.WorldMin.x - 4.001f);
            Assert.LessOrEqual(f.z, grid.WorldMax.z + 4.001f);
            Assert.AreEqual(grid.WorldMin.x - 4f, f.x, 0.01f, "reached the margin edge");
            Assert.AreEqual(grid.WorldMax.z + 4f, f.z, 0.01f, "reached the margin edge");
        }

        [UnityTest]
        public IEnumerator Camera_SmoothlyFollowsTarget_AndSettles()
        {
            yield return DragTouch(1, ScreenCentre, ScreenCentre + new Vector2(150f, 0f));
            for (int i = 0; i < 60; i++) yield return null;
            Assert.AreEqual(0f, Vector3.Distance(_camera.Focus, _camera.TargetFocus), 0.01f);
            Assert.AreEqual(_camera.TargetZoom, _camera.Zoom, 0.01f);
        }

        [UnityTest]
        public IEnumerator Mouse_DragPans_AndWheelZooms()
        {
            var before = _camera.TargetFocus;
            var c = ScreenCentre;

            Mouse(c, true);
            yield return null;
            for (int i = 1; i <= 6; i++)
            {
                Mouse(c + new Vector2(40f * i, 0f), true);
                yield return null;
            }
            Mouse(c + new Vector2(240f, 0f), false);
            yield return null;
            yield return null;
            Assert.Greater(Vector3.Distance(before, _camera.TargetFocus), 0.5f, "mouse drag should pan");

            float zoom = _camera.TargetZoom;
            Mouse(c, false, 120f);
            yield return null;
            Mouse(c, false, 0f);
            yield return null;
            Assert.Less(_camera.TargetZoom, zoom, "wheel up zooms in");
        }

        [UnityTest]
        public IEnumerator Mouse_PressOnUi_DoesNotMoveCamera()
        {
            var before = _camera.TargetFocus;
            var button = ScreenPositionOf("Pause");

            Mouse(button, true);
            yield return null;
            yield return null;
            for (int i = 1; i <= 6; i++)
            {
                Mouse(button + new Vector2(60f * i, 60f * i), true);
                yield return null;
            }
            Mouse(button + new Vector2(360f, 360f), false);
            yield return null;
            yield return null;

            Assert.AreEqual(before, _camera.TargetFocus, "UI mouse press must not pan the camera");
        }
    }
}
