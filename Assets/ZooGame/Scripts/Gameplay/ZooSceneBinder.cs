using UnityEngine;
using UnityEngine.SceneManagement;
using ZooGame.Input;
using ZooGame.UI;

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
        [Tooltip("Editor convenience: if this scene is entered directly, load the Bootstrap scene first.")]
        [SerializeField] string bootstrapSceneName = "Bootstrap";

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
            debugHud.Bind(game, game.Config.ShowDebugHud);
        }
    }
}
