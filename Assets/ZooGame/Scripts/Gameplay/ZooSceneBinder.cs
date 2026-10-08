using UnityEngine;
using UnityEngine.SceneManagement;
using ZooGame.Cameras;
using ZooGame.Data;
using ZooGame.Input;
using ZooGame.UI;
using ZooGame.World;

namespace ZooGame.Gameplay
{
    /// <summary>
    /// Composition point for the Zoo scene: hands the persistent game context to scene-level components once, at Awake.
    /// Add future scene-level systems here (or to a sibling binder) rather than letting them look up GameManager.
    /// </summary>
    public sealed class ZooSceneBinder : MonoBehaviour
    {
        [SerializeField] DebugHud debugHud;
        [SerializeField] PointerInputSource pointerInput;
        [SerializeField] WorldConfig worldConfig;
        [SerializeField] CameraConfig cameraConfig;
        [SerializeField] TerrainView terrainView;
        [SerializeField] GridDebugOverlay gridOverlay;
        [SerializeField] ZooCameraController cameraController;
        [Tooltip("Editor convenience: if this scene is entered directly, load the Bootstrap scene first.")]
        [SerializeField] string bootstrapSceneName = "Bootstrap";

        /// <summary>The authoritative zoo map for this scene. Hand it to systems from here; do not search for it.</summary>
        public ZooGrid Grid { get; private set; }

        void Awake()
        {
            var game = GameManager.Instance;
            if (game == null)
            {
#if UNITY_EDITOR
                SceneManager.LoadScene(bootstrapSceneName);
#else
                Debug.LogError("Zoo scene started without the Bootstrap scene.");
#endif
                return;
            }

            pointerInput.SetUiBlocker(new EventSystemPointerBlocker());

            Grid = new ZooGrid(worldConfig.ToGridSettings());
            terrainView.Bind(Grid, worldConfig.ToTerrainPalette());

            bool debugBuild = Application.isEditor || Debug.isDebugBuild;
            gridOverlay.Bind(Grid, worldConfig.OverlayMajorLineEvery, debugBuild && worldConfig.ShowGridOverlay);

            pointerInput.Gestures.Settings = new GestureSettings(
                DpToPixels(cameraConfig.DragThresholdDp), cameraConfig.MaxTapSeconds);
            cameraController.Bind(pointerInput, Grid, cameraConfig, UnlockedAreaCentre(worldConfig.ToGridSettings()));

            debugHud.Bind(game, game.Config.ShowDebugHud, gridOverlay.Toggle);
        }

        static float DpToPixels(float dp)
        {
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f; // some platforms/Editor report 0
            return dp * dpi / 160f;
        }

        static Vector3 UnlockedAreaCentre(GridSettings s)
        {
            float x = s.Origin.x + (s.UnlockedMinX + s.UnlockedWidth * 0.5f) * s.CellSize;
            float z = s.Origin.z + (s.UnlockedMinZ + s.UnlockedDepth * 0.5f) * s.CellSize;
            return new Vector3(x, s.Origin.y, z);
        }
    }
}
