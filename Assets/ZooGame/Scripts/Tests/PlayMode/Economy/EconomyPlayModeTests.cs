using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZooGame.Animals;
using ZooGame.Cameras;
using ZooGame.Core;
using ZooGame.Economy;
using ZooGame.Gameplay;
using ZooGame.World;

namespace ZooGame.Tests.PlayMode.Economy
{
    /// <summary>The economy in the real Zoo scene: starting balances, animal adoption charges and unaffordable blocking.</summary>
    public class EconomyPlayModeTests
    {
        ZooSceneBinder _binder;
        Camera _cam;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Bootstrap");
            float timeout = Time.realtimeSinceStartup + 10f;
            while ((GameManager.Instance == null || GameManager.Instance.State.Current != GameState.Playing)
                   && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State.Current);

            _binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            Assert.IsNotNull(_binder.Economy, "the economy must be created by the scene binder");
            _cam = Object.FindAnyObjectByType<ZooCameraController>().GetComponent<Camera>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameManager.Instance != null) Object.Destroy(GameManager.Instance.gameObject);
            yield return null;
        }

        static List<FenceEdge> RectEdges(int minX, int minZ, int w, int d)
        {
            var edges = new List<FenceEdge>();
            for (int x = minX; x < minX + w; x++)
            {
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.X, x, minZ), EdgeKind.Fence));
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.X, x, minZ + d), EdgeKind.Fence));
            }
            for (int z = minZ; z < minZ + d; z++)
            {
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.Z, minX, z), EdgeKind.Fence));
                edges.Add(new FenceEdge(new EdgeCoord(EdgeAxis.Z, minX + w, z), EdgeKind.Fence));
            }
            return edges;
        }

        Vector2 PenCentreOnScreen(int minX, int minZ, int size) =>
            _cam.WorldToScreenPoint(_binder.Grid.GridToWorld(new GridCoord(minX + size / 2, minZ + size / 2)));

        [UnityTest]
        public IEnumerator TheScene_StartsWithMoneyAndNoConservationTokens()
        {
            var e = _binder.Economy;
            Assert.AreEqual(e.Settings.StartingMoney, e.GetBalance(Currency.Money));
            Assert.AreEqual(0, e.GetBalance(Currency.ConservationTokens));
            Assert.AreEqual(0, e.HistoryCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlacingAnAnimal_ChargesItsAdoptionCost()
        {
            var e = _binder.Economy;
            _binder.Construction.BuildFences(RectEdges(30, 30, 6, 6));
            Assert.IsTrue(_binder.AnimalSpawner.Definitions.TryGet("rabbit", out var rabbit));

            long before = e.GetBalance(Currency.Money);
            var tool = _binder.AnimalPlacement;
            Assert.IsTrue(tool.Select(rabbit));
            tool.OnTapped(0, PenCentreOnScreen(30, 30, 6));
            yield return null;
            Assert.IsTrue(tool.Result.IsValid, tool.Result.Message);

            Assert.IsTrue(tool.Confirm());

            Assert.AreEqual(before - rabbit.AdoptionCost, e.GetBalance(Currency.Money));
            if (rabbit.AdoptionCost > 0)
            {
                e.TryGetLastTransaction(out var t);
                Assert.AreEqual(TransactionCategory.AnimalAdoption, t.Category);
                Assert.AreEqual(Currency.Money, t.Currency);
                Assert.AreEqual(rabbit.AdoptionCost, t.Amount);
            }
        }

        [UnityTest]
        public IEnumerator AnUnaffordableAnimal_IsNotPlaced_AndNotCharged()
        {
            var e = _binder.Economy;
            _binder.Construction.BuildFences(RectEdges(30, 30, 6, 6));
            Assert.IsTrue(_binder.AnimalSpawner.Definitions.TryGet("rabbit", out var rabbit));
            if (rabbit.AdoptionCost <= 0) Assert.Ignore("Rabbit is free: run ZooGame > M7 > Create Or Update Project Assets.");

            // Leave one less Money than the adoption costs.
            long target = rabbit.AdoptionCost - 1;
            long drain = e.GetBalance(Currency.Money) - target;
            Assert.IsTrue(e.DebugCharge(Currency.Money, drain).Success);

            int animals = _binder.Animals.Count;
            var tool = _binder.AnimalPlacement;
            Assert.IsTrue(tool.Select(rabbit));
            tool.OnTapped(0, PenCentreOnScreen(30, 30, 6));
            yield return null;
            Assert.IsFalse(tool.IsAffordable);

            Assert.IsFalse(tool.Confirm());

            Assert.AreEqual(animals, _binder.Animals.Count);
            Assert.AreEqual(target, e.GetBalance(Currency.Money));
        }
    }
}
