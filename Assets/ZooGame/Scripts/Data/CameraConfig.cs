using UnityEngine;

namespace ZooGame.Data
{
    /// <summary>Mobile camera and touch-gesture tuning. Create via Assets > Create > ZooGame > Camera Config.</summary>
    [CreateAssetMenu(menuName = "ZooGame/Camera Config", fileName = "CameraConfig")]
    public sealed class CameraConfig : ScriptableObject
    {
        [Header("View (fixed angle)")]
        [Range(20f, 80f)] [SerializeField] float pitchDegrees = 30f;
        [SerializeField] float yawDegrees = 45f;
        [Tooltip("Orthographic size (half the visible height in world units) when the zoo loads.")]
        [SerializeField, Min(0.5f)] float defaultZoom = 10f;
        [SerializeField, Min(0.5f)] float minZoom = 5f;
        [SerializeField, Min(0.5f)] float maxZoom = 20f;

        [Header("Pan")]
        [Tooltip("1 = the ground follows the finger exactly.")]
        [SerializeField, Min(0.01f)] float panSpeed = 1f;

        [Header("Zoom")]
        [Tooltip("Pinch: 1 = the view scales exactly with finger spread; higher is more sensitive.")]
        [SerializeField, Min(0.01f)] float pinchZoomSpeed = 1f;
        [Tooltip("Mouse wheel (Editor/desktop): zoom factor exponent per wheel notch.")]
        [SerializeField, Min(0.001f)] float mouseWheelZoomSpeed = 0.12f;

        [Header("Feel")]
        [Tooltip("Smoothing time constant in seconds; 0 = camera follows input instantly.")]
        [SerializeField, Min(0f)] float smoothingSeconds = 0.08f;

        [Header("Bounds")]
        [Tooltip("How far (world units) the view centre may move beyond the edge of the map.")]
        [SerializeField, Min(0f)] float boundsMargin = 4f;

        [Header("Gestures")]
        [Tooltip("Movement (density-independent pixels, 160 per inch) before a press becomes a drag instead of a tap.")]
        [SerializeField, Min(1f)] float dragThresholdDp = 12f;
        [Tooltip("A press released within this time, without exceeding the drag threshold, is a tap.")]
        [SerializeField, Min(0.05f)] float maxTapSeconds = 0.5f;

        public float PitchDegrees => pitchDegrees;
        public float YawDegrees => yawDegrees;
        public float DefaultZoom => Mathf.Clamp(defaultZoom, MinZoom, MaxZoom);
        public float MinZoom => Mathf.Min(minZoom, maxZoom);
        public float MaxZoom => Mathf.Max(minZoom, maxZoom);
        public float PanSpeed => panSpeed;
        public float PinchZoomSpeed => pinchZoomSpeed;
        public float MouseWheelZoomSpeed => mouseWheelZoomSpeed;
        public float SmoothingSeconds => smoothingSeconds;
        public float BoundsMargin => boundsMargin;
        public float DragThresholdDp => dragThresholdDp;
        public float MaxTapSeconds => maxTapSeconds;
    }
}
