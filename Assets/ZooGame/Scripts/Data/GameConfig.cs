using UnityEngine;
using ZooGame.Core;

namespace ZooGame.Data
{
    /// <summary>Global tuning and bootstrap configuration. Create via Assets > Create > ZooGame > Game Config.</summary>
    [CreateAssetMenu(menuName = "ZooGame/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("Scenes")]
        [SerializeField] string bootstrapSceneName = "Bootstrap";
        [SerializeField] string zooSceneName = "Zoo";

        [Header("Simulation")]
        [SerializeField, Min(0.01f)] float speed1xMultiplier = 1f;
        [SerializeField, Min(0.01f)] float speed2xMultiplier = 2f;
        [SerializeField, Min(0.01f)] float speed3xMultiplier = 3f;
        [Tooltip("Real frame delta is clamped to this before simulation scaling.")]
        [SerializeField, Min(0.001f)] float maxRealDeltaSeconds = 0.25f;

        [Header("Runtime")]
        [SerializeField, Range(30, 120)] int targetFrameRate = 60;
        [SerializeField] bool keepScreenAwake = true;
        [Tooltip("Enter Paused when the app is backgrounded.")]
        [SerializeField] bool pauseWhenAppBackgrounded = true;

        [Header("Debug")]
        [Tooltip("Debug HUD is only ever shown in the Editor and Development builds.")]
        [SerializeField] bool showDebugHud = true;

        public string BootstrapSceneName => bootstrapSceneName;
        public string ZooSceneName => zooSceneName;
        public int TargetFrameRate => targetFrameRate;
        public bool KeepScreenAwake => keepScreenAwake;
        public bool PauseWhenAppBackgrounded => pauseWhenAppBackgrounded;
        public bool ShowDebugHud => showDebugHud;

        public GameClockSettings ToClockSettings() =>
            new GameClockSettings(speed1xMultiplier, speed2xMultiplier, speed3xMultiplier, maxRealDeltaSeconds);
    }
}
