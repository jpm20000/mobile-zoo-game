using UnityEngine;

namespace ZooGame.Cameras
{
    /// <summary>
    /// Pure maths for the fixed-angle orthographic zoo camera. The camera looks down at <c>pitch</c> degrees, so a
    /// screen-vertical movement of <c>dy</c> pixels corresponds to <c>dy / sin(pitch)</c> along the ground, while
    /// screen-horizontal movement maps 1:1 onto the ground's "right" axis.
    /// </summary>
    public static class CameraMath
    {
        /// <summary>World units covered by one screen pixel at the given orthographic size.</summary>
        public static float UnitsPerPixel(float orthoSize, float pixelHeight) =>
            2f * orthoSize / Mathf.Max(pixelHeight, 1f);

        /// <summary>Horizontal (ground-plane) unit vectors for a camera with the given yaw.</summary>
        public static void GroundAxes(float yawDegrees, out Vector3 right, out Vector3 forward)
        {
            float yaw = yawDegrees * Mathf.Deg2Rad;
            forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            right = new Vector3(forward.z, 0f, -forward.x);
        }

        /// <summary>
        /// How far the camera focus must move so the ground follows a finger that moved
        /// <paramref name="screenDelta"/> pixels (drag right => ground moves right => camera moves left).
        /// </summary>
        public static Vector3 PanFocusDelta(Vector2 screenDelta, float unitsPerPixel, float yawDegrees, float pitchDegrees, float panSpeed)
        {
            GroundAxes(yawDegrees, out var right, out var forward);
            float sinPitch = Mathf.Max(Mathf.Sin(pitchDegrees * Mathf.Deg2Rad), 0.05f);
            return -(right * screenDelta.x + forward * (screenDelta.y / sinPitch)) * (unitsPerPixel * panSpeed);
        }

        /// <summary>
        /// Focus shift that keeps the ground point under <paramref name="pixelOffsetFromCentre"/> fixed on screen
        /// while the units-per-pixel changes from <paramref name="oldUpp"/> to <paramref name="newUpp"/>.
        /// </summary>
        public static Vector3 ZoomAnchorShift(Vector2 pixelOffsetFromCentre, float oldUpp, float newUpp, float yawDegrees, float pitchDegrees)
        {
            GroundAxes(yawDegrees, out var right, out var forward);
            float sinPitch = Mathf.Max(Mathf.Sin(pitchDegrees * Mathf.Deg2Rad), 0.05f);
            var k = right * pixelOffsetFromCentre.x + forward * (pixelOffsetFromCentre.y / sinPitch);
            return k * (oldUpp - newUpp);
        }

        public static float ClampZoom(float zoom, float min, float max) => Mathf.Clamp(zoom, min, max);

        /// <summary>Clamps X/Z of the focus to the map rectangle expanded by <paramref name="margin"/>. Y is kept.</summary>
        public static Vector3 ClampFocus(Vector3 focus, Vector3 worldMin, Vector3 worldMax, float margin)
        {
            focus.x = Mathf.Clamp(focus.x, worldMin.x - margin, worldMax.x + margin);
            focus.z = Mathf.Clamp(focus.z, worldMin.z - margin, worldMax.z + margin);
            return focus;
        }

        /// <summary>Camera world position for a focus point on the ground.</summary>
        public static Vector3 CameraPosition(Vector3 focus, Quaternion rotation, float distance) =>
            focus - rotation * Vector3.forward * distance;

        /// <summary>Frame-rate independent exponential smoothing factor; time constant 0 means "jump".</summary>
        public static float SmoothingFactor(float deltaTime, float timeConstant) =>
            timeConstant <= 0f ? 1f : 1f - Mathf.Exp(-deltaTime / timeConstant);
    }
}
