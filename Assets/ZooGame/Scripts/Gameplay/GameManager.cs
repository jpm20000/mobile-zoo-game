using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZooGame.Core;
using ZooGame.Data;
using ZooGame.Platform;

namespace ZooGame.Gameplay
{
    /// <summary>
    /// Persistent host created in the Bootstrap scene. Owns the shared services (event bus, game state, game clock),
    /// drives the single per-frame clock tick, and runs the boot flow. Deliberately holds no gameplay logic:
    /// future systems get these services through <see cref="IGameContext"/> and register on the event bus.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameManager : MonoBehaviour, IGameContext
    {
        [SerializeField] GameConfig config;

        /// <summary>
        /// Used only by scene binders to obtain the context once at scene start. Do not poll this in gameplay code.
        /// </summary>
        public static GameManager Instance { get; private set; }

        public GameConfig Config => config;
        public EventBus Events { get; private set; }
        public GameStateMachine State { get; private set; }
        public GameClock Clock { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            if (config == null)
            {
                Debug.LogError("GameManager has no GameConfig assigned.", this);
                enabled = false;
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            Events = new EventBus();
            State = new GameStateMachine(Events);
            Clock = new GameClock(config.ToClockSettings(), Events);
            RuntimeDeviceSettings.Apply(config.TargetFrameRate, config.KeepScreenAwake);
        }

        IEnumerator Start()
        {
            if (Instance != this) yield break;

            State.TryTransition(GameState.Loading);
            var load = SceneManager.LoadSceneAsync(config.ZooSceneName);
            yield return load;
            State.TryTransition(GameState.Playing);
        }

        void Update()
        {
            // The only per-frame simulation driver. Real (unscaled) time is used so Time.timeScale is never touched.
            Clock.Tick(State.Current == GameState.Playing ? Time.unscaledDeltaTime : 0f);
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && Instance == this && config.PauseWhenAppBackgrounded)
                SetPaused(true);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetPaused(bool paused)
        {
            if (paused) State.TryTransition(GameState.Paused);
            else if (State.Current == GameState.Paused) State.TryTransition(GameState.Playing);
        }

        public void SetSimulationSpeed(SimulationSpeed speed) => Clock.SetSpeed(speed);
    }
}
