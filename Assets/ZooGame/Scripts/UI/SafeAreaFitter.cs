using UnityEngine;

namespace ZooGame.UI
{
    /// <summary>Pure safe-area math so it can be unit tested without a screen.</summary>
    public static class SafeAreaMath
    {
        public static bool TryGetAnchors(Rect safeArea, int screenWidth, int screenHeight,
            out Vector2 anchorMin, out Vector2 anchorMax)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                anchorMin = Vector2.zero;
                anchorMax = Vector2.one;
                return false;
            }

            anchorMin = new Vector2(Mathf.Clamp01(safeArea.xMin / screenWidth), Mathf.Clamp01(safeArea.yMin / screenHeight));
            anchorMax = new Vector2(Mathf.Clamp01(safeArea.xMax / screenWidth), Mathf.Clamp01(safeArea.yMax / screenHeight));
            return true;
        }
    }

    /// <summary>
    /// Fits this RectTransform to Screen.safeArea (iPhone notch/home indicator, Android display cutouts).
    /// Put it on a full-stretch child of the Canvas and parent all HUD elements to it.
    /// Re-applies only when the rect dimensions change (rotation, resize); no per-frame work.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [ExecuteAlways]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rect;
        Rect _lastSafeArea;
        Vector2Int _lastScreen;

        void OnEnable() => Apply();

        void OnRectTransformDimensionsChange() => Apply();

        void Apply()
        {
            if (_rect == null) _rect = (RectTransform)transform;

            var safe = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (safe == _lastSafeArea && screen == _lastScreen) return;
            _lastSafeArea = safe;
            _lastScreen = screen;

            if (!SafeAreaMath.TryGetAnchors(safe, screen.x, screen.y, out var min, out var max)) return;
            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
