using UnityEngine;

namespace ZooGame.World
{
    /// <summary>Screen point to ground point, by intersecting the camera ray with the horizontal plane at the grid's height. No physics.</summary>
    public static class GroundProbe
    {
        public static bool TryGroundPoint(Camera camera, ZooGrid grid, Vector2 screenPosition, out Vector3 point)
        {
            var ray = camera.ScreenPointToRay(screenPosition);
            float dy = ray.direction.y;
            if (Mathf.Abs(dy) < 1e-6f)
            {
                point = default;
                return false;
            }
            float t = (grid.Origin.y - ray.origin.y) / dy;
            if (t < 0f)
            {
                point = default;
                return false;
            }
            point = ray.origin + ray.direction * t;
            return true;
        }
    }
}
