using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZooGame.Core;
using ZooGame.Gameplay;

namespace ZooGame.Tests.PlayMode
{
    public class BootstrapSmokeTests
    {
        [UnityTest]
        public IEnumerator Bootstrap_LoadsZooScene_AndEntersPlaying()
        {
            SceneManager.LoadScene("Bootstrap");

            float timeout = Time.realtimeSinceStartup + 10f;
            while ((GameManager.Instance == null || GameManager.Instance.State.Current != GameState.Playing)
                   && Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.IsNotNull(GameManager.Instance, "GameManager should exist after bootstrap");
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State.Current);
            Assert.AreEqual("Zoo", SceneManager.GetActiveScene().name);
            Assert.IsNotNull(Camera.main, "Zoo scene should contain a main camera");

            var game = GameManager.Instance;
            game.SetPaused(true);
            Assert.AreEqual(GameState.Paused, game.State.Current);
            game.SetPaused(false);
            Assert.AreEqual(GameState.Playing, game.State.Current);

            game.SetSimulationSpeed(SimulationSpeed.X3);
            Assert.AreEqual(3f, game.Clock.Multiplier);
            game.SetSimulationSpeed(SimulationSpeed.X1);

            Object.Destroy(game.gameObject);
        }
    }
}
