using UnityEngine;
using ZooGame.Data;
using ZooGame.Input;
using ZooGame.World;

namespace ZooGame.Cameras
{
    /// <summary>
    /// Fixed-angle orthographic camera. One-finger drag pans, pinch zooms (Editor: mouse drag / wheel). Consumes the
    /// shared gesture events and claims pointers as <see cref="InputOwner.Camera"/> first-come-first-served, so a pointer
    /// owned by UI (or later placement/selection) never moves the camera. Works while the game is paused: it uses
    /// unscaled time and no simulation clock. Idle frames cost one bool check.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ZooCameraController : MonoBehaviour
    {
        const float CameraDistance = 100f;
        const float SettleEpsilon = 1e-3f;
        const int NoPointer = int.MinValue;

        Camera _camera;
        PointerInputSource _input;
        ZooGrid _grid;
        CameraConfig _config;

        Quaternion _rotation;
        Vector3 _focus, _targetFocus;
        float _zoom, _targetZoom;
        bool _settled = true;

        int _dragPointer = NoPointer;
        int _pinchA = NoPointer, _pinchB = NoPointer;
        Vector2 _lastPinchCentre;

        public Vector3 Focus => _focus;
        public float Zoom => _zoom;
        public Vector3 TargetFocus => _targetFocus;
        public float TargetZoom => _targetZoom;

        /// <summary>Called by the scene binder. Safe to call again to rebind.</summary>
        public void Bind(PointerInputSource input, ZooGrid grid, CameraConfig config, Vector3 initialFocus)
        {
            Unsubscribe();
            _input = input;
            _grid = grid;
            _config = config;

            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.nearClipPlane = 1f;
            _camera.farClipPlane = CameraDistance * 2f;
            _rotation = Quaternion.Euler(config.PitchDegrees, config.YawDegrees, 0f);

            _targetZoom = _zoom = config.DefaultZoom;
            _targetFocus = _focus = CameraMath.ClampFocus(initialFocus, grid.WorldMin, grid.WorldMax, config.BoundsMargin);
            Apply();

            if (isActiveAndEnabled) Subscribe();
        }

        void OnEnable() => Subscribe();
        void OnDisable() => Unsubscribe();

        void Subscribe()
        {
            if (_input == null) return;
            var g = _input.Gestures;
            g.DragStarted += OnDragStarted;
            g.DragMoved += OnDragMoved;
            g.DragEnded += OnDragEnded;
            g.PinchStarted += OnPinchStarted;
            g.PinchChanged += OnPinchChanged;
            g.PinchEnded += OnPinchEnded;
            _input.Scrolled += OnScrolled;
        }

        void Unsubscribe()
        {
            if (_input == null) return;
            var g = _input.Gestures;
            g.DragStarted -= OnDragStarted;
            g.DragMoved -= OnDragMoved;
            g.DragEnded -= OnDragEnded;
            g.PinchStarted -= OnPinchStarted;
            g.PinchChanged -= OnPinchChanged;
            g.PinchEnded -= OnPinchEnded;
            _input.Scrolled -= OnScrolled;
            ReleaseClaims();
        }

        // ---- Gesture handlers ------------------------------------------------------------------------------

        void OnDragStarted(int pointerId, Vector2 startPos, Vector2 currentPos)
        {
            _dragPointer = _input.Ownership.TryClaim(pointerId, InputOwner.Camera) ? pointerId : NoPointer;
        }

        void OnDragMoved(int pointerId, Vector2 position, Vector2 delta)
        {
            if (pointerId != _dragPointer) return;
            float upp = CameraMath.UnitsPerPixel(_targetZoom, _camera.pixelHeight);
            _targetFocus += CameraMath.PanFocusDelta(delta, upp, _config.YawDegrees, _config.PitchDegrees, _config.PanSpeed);
            ClampTargets();
        }

        void OnDragEnded(int pointerId)
        {
            if (pointerId != _dragPointer) return;
            _input.Ownership.Release(pointerId, InputOwner.Camera);
            _dragPointer = NoPointer;
        }

        void OnPinchStarted(int a, int b, Vector2 centre)
        {
            var own = _input.Ownership;
            if (own.TryClaim(a, InputOwner.Camera) & own.TryClaim(b, InputOwner.Camera))
            {
                _pinchA = a;
                _pinchB = b;
                _lastPinchCentre = centre;
            }
            else
            {
                // Another system owns one of the fingers; leave the camera alone.
                own.Release(a, InputOwner.Camera);
                own.Release(b, InputOwner.Camera);
            }
        }

        void OnPinchChanged(Vector2 centre, float ratio)
        {
            if (_pinchA == NoPointer) return;

            // Two fingers moving together pan exactly like one finger dragging.
            var delta = centre - _lastPinchCentre;
            _lastPinchCentre = centre;
            if (delta != Vector2.zero)
            {
                float upp = CameraMath.UnitsPerPixel(_targetZoom, _camera.pixelHeight);
                _targetFocus += CameraMath.PanFocusDelta(delta, upp, _config.YawDegrees, _config.PitchDegrees, _config.PanSpeed);
                ClampTargets();
            }

            float exponent = _config.PinchZoomSpeed;
            if (ratio != 1f) ZoomBy(Mathf.Pow(ratio, -exponent), centre); // fingers spreading (ratio > 1) zooms in = smaller ortho size
        }

        void OnPinchEnded()
        {
            ReleaseClaims();
        }

        void OnScrolled(Vector2 position, float notches)
        {
            ZoomBy(Mathf.Exp(-notches * _config.MouseWheelZoomSpeed), position);
        }

        // ---- Core operations -------------------------------------------------------------------------------

        /// <summary>Multiplies the target orthographic size, keeping the ground under <paramref name="screenAnchor"/> fixed.</summary>
        void ZoomBy(float factor, Vector2 screenAnchor)
        {
            float oldZoom = _targetZoom;
            float newZoom = CameraMath.ClampZoom(oldZoom * factor, _config.MinZoom, _config.MaxZoom);
            if (Mathf.Approximately(oldZoom, newZoom)) return;

            float h = _camera.pixelHeight;
            var offset = screenAnchor - new Vector2(_camera.pixelWidth, h) * 0.5f;
            _targetFocus += CameraMath.ZoomAnchorShift(offset,
                CameraMath.UnitsPerPixel(oldZoom, h), CameraMath.UnitsPerPixel(newZoom, h),
                _config.YawDegrees, _config.PitchDegrees);
            _targetZoom = newZoom;
            ClampTargets();
        }

        void ClampTargets()
        {
            _targetFocus = CameraMath.ClampFocus(_targetFocus, _grid.WorldMin, _grid.WorldMax, _config.BoundsMargin);
            _settled = false;
        }

        void ReleaseClaims()
        {
            if (_input == null) return;
            var own = _input.Ownership;
            if (_dragPointer != NoPointer) own.Release(_dragPointer, InputOwner.Camera);
            if (_pinchA != NoPointer) own.Release(_pinchA, InputOwner.Camera);
            if (_pinchB != NoPointer) own.Release(_pinchB, InputOwner.Camera);
            _dragPointer = _pinchA = _pinchB = NoPointer;
        }

        void LateUpdate()
        {
            if (_settled) return;

            float t = CameraMath.SmoothingFactor(Time.unscaledDeltaTime, _config.SmoothingSeconds);
            _focus = Vector3.Lerp(_focus, _targetFocus, t);
            _zoom = Mathf.Lerp(_zoom, _targetZoom, t);

            if ((_focus - _targetFocus).sqrMagnitude < SettleEpsilon * SettleEpsilon
                && Mathf.Abs(_zoom - _targetZoom) < SettleEpsilon)
            {
                _focus = _targetFocus;
                _zoom = _targetZoom;
                _settled = true;
            }
            Apply();
        }

        void Apply()
        {
            _camera.orthographicSize = _zoom;
            transform.SetPositionAndRotation(CameraMath.CameraPosition(_focus, _rotation, CameraDistance), _rotation);
        }
    }
}
