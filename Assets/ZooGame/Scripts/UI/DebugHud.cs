using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZooGame.Core;

namespace ZooGame.UI
{
    /// <summary>
    /// Development HUD: game state, simulation speed, FPS, plus touch buttons for pause and 1x/2x/3x.
    /// Reads and commands only through <see cref="IGameContext"/>. Hidden outside the Editor and Development builds.
    /// Disable by unticking showDebugHud in GameConfig or by deleting the HUD object; nothing depends on it.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        const float FpsRefreshSeconds = 0.5f;

        [SerializeField] GameObject root;
        [SerializeField] Text infoLabel;
        [SerializeField] Text pauseLabel;
        [SerializeField] Button pauseButton;
        [SerializeField] Button speedPausedButton;
        [SerializeField] Button speed1xButton;
        [SerializeField] Button speed2xButton;
        [SerializeField] Button speed3xButton;
        [Tooltip("Optional: toggles the debug grid overlay.")]
        [SerializeField] Button gridButton;

        readonly StringBuilder _sb = new StringBuilder(96);
        IGameContext _context;
        System.Action _toggleGrid;
        float _fpsTimer;
        int _fpsFrames;
        float _fps;

        /// <summary>Called by the scene binder once the game context exists.</summary>
        public void Bind(IGameContext context, bool visible, System.Action toggleGrid = null)
        {
            Unbind();

#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
            visible = false;
#endif
            root.SetActive(visible);
            enabled = visible;
            if (!visible) return;

            _context = context;
            _toggleGrid = toggleGrid;
            _context.Events.Subscribe<GameStateChanged>(OnStateChanged);
            _context.Events.Subscribe<SimulationSpeedChanged>(OnSpeedChanged);

            pauseButton.onClick.AddListener(OnPauseClicked);
            speedPausedButton.onClick.AddListener(OnSpeedPausedClicked);
            speed1xButton.onClick.AddListener(OnSpeed1xClicked);
            speed2xButton.onClick.AddListener(OnSpeed2xClicked);
            speed3xButton.onClick.AddListener(OnSpeed3xClicked);
            if (gridButton != null) gridButton.onClick.AddListener(OnGridClicked);
            Refresh();
        }

        void Unbind()
        {
            if (_context == null) return;
            _context.Events.Unsubscribe<GameStateChanged>(OnStateChanged);
            _context.Events.Unsubscribe<SimulationSpeedChanged>(OnSpeedChanged);
            pauseButton.onClick.RemoveListener(OnPauseClicked);
            speedPausedButton.onClick.RemoveListener(OnSpeedPausedClicked);
            speed1xButton.onClick.RemoveListener(OnSpeed1xClicked);
            speed2xButton.onClick.RemoveListener(OnSpeed2xClicked);
            speed3xButton.onClick.RemoveListener(OnSpeed3xClicked);
            if (gridButton != null) gridButton.onClick.RemoveListener(OnGridClicked);
            _toggleGrid = null;
            _context = null;
        }

        void OnDestroy() => Unbind();

        void Update()
        {
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer < FpsRefreshSeconds) return;
            _fps = _fpsFrames / _fpsTimer;
            _fpsFrames = 0;
            _fpsTimer = 0f;
            Refresh();
        }

        void OnStateChanged(GameStateChanged _) => Refresh();
        void OnSpeedChanged(SimulationSpeedChanged _) => Refresh();

        void OnPauseClicked() => _context.SetPaused(_context.State.Current == GameState.Playing);
        void OnSpeedPausedClicked() => _context.SetSimulationSpeed(SimulationSpeed.Paused);
        void OnSpeed1xClicked() => _context.SetSimulationSpeed(SimulationSpeed.X1);
        void OnSpeed2xClicked() => _context.SetSimulationSpeed(SimulationSpeed.X2);
        void OnSpeed3xClicked() => _context.SetSimulationSpeed(SimulationSpeed.X3);

        void OnGridClicked() => _toggleGrid?.Invoke();

        void Refresh()
        {
            if (_context == null) return;
            var state = _context.State.Current;
            _sb.Clear();
            _sb.Append("State: ").Append(state).Append('\n');
            _sb.Append("Speed: ").Append(_context.Clock.Speed).Append(" (x").Append(_context.Clock.Multiplier).Append(")\n");
            _sb.Append("FPS: ").Append(Mathf.RoundToInt(_fps));
            infoLabel.text = _sb.ToString();
            pauseLabel.text = state == GameState.Paused ? "Resume" : "Pause";
        }
    }
}
