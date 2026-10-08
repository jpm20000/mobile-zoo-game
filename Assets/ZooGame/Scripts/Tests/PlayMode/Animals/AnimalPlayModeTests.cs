using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ZooGame.Animals;
using ZooGame.Cameras;
using ZooGame.Core;
using ZooGame.Gameplay;
using ZooGame.Input;
using ZooGame.World;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ZooGame.Tests.PlayMode.Animals
{
    /// <summary>Animals in the real Zoo scene: spawning, enclosure association, roaming, invalid enclosures and tap selection.</summary>
    public class AnimalPlayModeTests
    {
        Touchscreen _touch;
        ZooSceneBinder _binder;
        ConstructionModel _model;
        AnimalSpawner _spawner;
        Camera _cam;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode _previousBehavior;
#endif

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            _previousBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            _touch = InputSystem.AddDevice<Touchscreen>();

            SceneManager.LoadScene("Bootstrap");
            float timeout = Time.realtimeSinceStartup + 10f;
            while ((GameManager.Instance == null || GameManager.Instance.State.Current != GameState.Playing)
                   && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State.Current);

            _binder = Object.FindAnyObjectByType<ZooSceneBinder>();
            _spawner = _binder.AnimalSpawner;
            Assert.IsNotNull(_spawner, "run ZooGame > M4 > Create Or Update Project Assets");
            Assert.IsTrue(_spawner.IsBound);
            _model = _binder.Construction;
            _cam = Object.FindAnyObjectByType<ZooCameraController>().GetComponent<Camera>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_touch != null) InputSystem.RemoveDevice(_touch);
            if (GameManager.Instance != null) Object.Destroy(GameManager.Instance.gameObject);
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = _previousBehavior;
#endif
            yield return null;
        }

        // ---- Helpers ----

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

        /// <summary>Builds a closed pen and returns its animal-facing enclosure id.</summary>
        string BuildPen(int minX, int minZ, int w, int d)
        {
            _model.BuildFences(RectEdges(minX, minZ, w, d));
            var e = _model.Enclosures.GetAt(new GridCoord(minX, minZ));
            Assert.IsNotNull(e, "pen should be closed");
            return AnimalEnclosureService.ToId(e.Id);
        }

        void Touch(int id, TouchPhase phase, Vector2 position) =>
            InputSystem.QueueStateEvent(_touch, new TouchState { touchId = id, phase = phase, position = position, pressure = 1f });

        IEnumerator Tap(Vector2 screen)
        {
            Touch(1, TouchPhase.Began, screen);
            yield return null;
            Touch(1, TouchPhase.Ended, screen);
            yield return null;
            yield return null;
        }

        Vector2 ScreenOf(AnimalController a) => _cam.WorldToScreenPoint(a.Selectable.PickPoint);

        // ---- Spawning ----

        [UnityTest]
        public IEnumerator SpawnNew_IntoAValidEnclosure_CreatesRegisteredAnimalAssociatedWithIt()
        {
            string pen = BuildPen(30, 30, 6, 6);
            var result = _spawner.SpawnNew("rabbit", pen);
            yield return null;

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(pen, result.Animal.EnclosureId);
            Assert.IsTrue(_binder.Animals.TryGet(result.Animal.AnimalId, out var registered));
            Assert.AreSame(result.Animal, registered);
            Assert.AreEqual(1, _binder.Animals.GetByEnclosure(pen).Count);
            Assert.IsTrue(_binder.AnimalEnclosures.ContainsPosition(pen, result.Controller.transform.position));
            Assert.AreEqual("rabbit", result.Controller.Definition.SpeciesId);
            Assert.AreSame(result.Animal, result.Controller.Instance);
        }

        [UnityTest]
        public IEnumerator SpawnNew_RejectsTooSmallInvalidAndMissing_WithoutCreatingAnything()
        {
            string small = BuildPen(30, 30, 4, 4); // 16 cells: fine for a zebra, too small for a lion
            Assert.AreEqual(AnimalPlacementFailure.EnclosureTooSmall, _spawner.SpawnNew("lion", small).Placement);
            Assert.AreEqual(AnimalPlacementFailure.EnclosureInvalid, _spawner.SpawnNew("rabbit", "enclosure-999").Placement);
            Assert.AreEqual(AnimalPlacementFailure.NoEnclosure, _spawner.SpawnNew("rabbit", null).Placement);
            Assert.AreEqual(AnimalSpawnFailure.UnknownSpecies, _spawner.SpawnNew("dodo", small).Failure);
            yield return null;

            Assert.AreEqual(0, _binder.Animals.Count);
            Assert.AreEqual(0, _spawner.Active.Count);
            Assert.IsTrue(_spawner.SpawnNew("zebra", small).Success);
        }

        [UnityTest]
        public IEnumerator Animals_MayExistWithoutAnEnclosure_AndStandStill()
        {
            var result = _spawner.SpawnUnhoused("zebra", new Vector3(32.5f, 0f, 32.5f));
            Assert.IsTrue(result.Success);
            Assert.IsNull(result.Animal.EnclosureId);
            Vector3 start = result.Controller.transform.position;
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until) yield return null;

            Assert.AreEqual(start, result.Controller.transform.position);
            Assert.AreEqual(AnimalState.Idle, result.Controller.State);
            Assert.IsTrue(result.Controller.EnclosureValid, "no enclosure is not an invalid enclosure");
        }

        [UnityTest]
        public IEnumerator Despawn_KeepsTheRecord_Reconstruct_KeepsTheId_Delete_RemovesIt()
        {
            string pen = BuildPen(30, 30, 6, 6);
            var spawned = _spawner.SpawnNew("rabbit", pen);
            string id = spawned.Animal.AnimalId;
            var instance = spawned.Animal;

            Assert.IsTrue(_spawner.Despawn(id));
            yield return null;
            Assert.IsTrue(spawned.Controller == null, "GameObject is gone");
            Assert.AreEqual(0, _spawner.Active.Count);
            Assert.IsTrue(_binder.Animals.TryGet(id, out _), "despawn retains the AnimalInstance");

            var back = _spawner.Reconstruct(instance);
            Assert.IsTrue(back.Success, back.Message);
            Assert.AreEqual(id, back.Animal.AnimalId, "reconstruction never regenerates the id");
            Assert.AreEqual(1, _binder.Animals.Count);
            Assert.AreEqual(AnimalSpawnFailure.AlreadySpawned, _spawner.Reconstruct(instance).Failure);

            Assert.IsTrue(_spawner.Delete(id));
            yield return null;
            Assert.IsFalse(_binder.Animals.TryGet(id, out _));
            Assert.AreEqual(0, _spawner.Active.Count);
            Assert.IsFalse(_spawner.Despawn(id));
        }

        [UnityTest]
        public IEnumerator Reconstruct_OfARecordWithAStaleEnclosure_StrandsInsteadOfFailing()
        {
            var saved = new AnimalInstance("animal-from-save", "zebra", "Old Friend", AnimalSex.Male, "enclosure-777", new Vector3(32.5f, 0f, 32.5f));
            var result = _spawner.Reconstruct(saved);
            yield return null;

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual("animal-from-save", result.Animal.AnimalId);
            Assert.AreEqual(AnimalState.Stranded, result.Controller.State);
            Assert.IsFalse(result.Controller.EnclosureValid);
        }

        // ---- Roaming ----

        [UnityTest]
        public IEnumerator Animals_RoamAndNeverLeaveTheirEnclosure()
        {
            string pen = BuildPen(30, 30, 5, 5);
            var a = _spawner.SpawnNew("rabbit", pen).Controller;
            var start = a.transform.position;
            bool moved = false;
            bool sawMoving = false;

            float until = Time.realtimeSinceStartup + 14f;
            while (Time.realtimeSinceStartup < until && !(moved && sawMoving && Time.realtimeSinceStartup > until - 10f))
            {
                yield return null;
                Assert.IsTrue(_binder.AnimalEnclosures.ContainsPosition(pen, a.transform.position),
                    "left the pen at " + a.transform.position);
                if ((a.transform.position - start).sqrMagnitude > 0.01f) moved = true;
                if (a.State == AnimalState.Moving) sawMoving = true;
            }
            Assert.IsTrue(moved, "the animal should wander");
            Assert.IsTrue(sawMoving, "and report the Moving state");
            Assert.AreEqual(a.transform.position, a.Instance.Position, "position is mirrored into the persistent record");
        }

        // ---- Enclosure changes ----

        [UnityTest]
        public IEnumerator OpeningAnEnclosure_StrandsTheAnimal_WithoutDeletingIt_AndReassignmentRecoversIt()
        {
            string pen = BuildPen(30, 30, 5, 5);
            var spawned = _spawner.SpawnNew("rabbit", pen);
            string id = spawned.Animal.AnimalId;
            var a = spawned.Controller;
            yield return null;
            Assert.IsTrue(a.EnclosureValid);

            var gap = new EdgeCoord(EdgeAxis.X, 32, 30);
            _model.RemoveFence(gap);
            yield return null;

            Assert.IsTrue(_binder.Animals.TryGet(id, out var kept), "record survives");
            Assert.AreEqual(id, kept.AnimalId);
            Assert.AreEqual(pen, kept.EnclosureId, "EnclosureId is kept while the pen is open");
            Assert.IsFalse(a.EnclosureValid);
            Assert.AreEqual(AnimalState.Stranded, a.State);
            Assert.IsFalse(_binder.AnimalEnclosures.IsValidEnclosure(pen));
            Assert.IsTrue(a != null, "GameObject is not destroyed");

            Vector3 held = a.transform.position;
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual(held, a.transform.position, "a stranded animal does not move (no escape behaviour yet)");

            _model.BuildFences(new[] { new FenceEdge(gap, EdgeKind.Fence) });
            yield return null;
            // M3 never reuses the id of an enclosure that stopped existing, so the repaired pen is a new enclosure and the
            // animal stays stranded until something reassigns it (future milestone). Identity is untouched either way.
            string repaired = AnimalEnclosureService.ToId(_model.Enclosures.GetAt(new GridCoord(30, 30)).Id);
            Assert.AreNotEqual(pen, repaired);
            Assert.AreEqual(AnimalState.Stranded, a.State);
            Assert.AreEqual(pen, kept.EnclosureId);

            Assert.IsTrue(_binder.Animals.SetEnclosure(id, repaired));
            a.RefreshEnclosure();
            Assert.AreEqual(id, a.Instance.AnimalId, "reassignment keeps the AnimalId");
            Assert.AreEqual(1, _binder.Animals.GetByEnclosure(repaired).Count);
            Assert.IsTrue(a.EnclosureValid);
            Assert.AreNotEqual(AnimalState.Stranded, a.State);
        }

        // ---- Selection ----

        [UnityTest]
        public IEnumerator TappingAnAnimal_SelectsIt_AndShowsItsDetails()
        {
            string pen = BuildPen(30, 30, 6, 6);
            var a = _spawner.SpawnNew("zebra", pen).Controller;
            yield return null;

            yield return Tap(ScreenOf(a));

            Assert.AreSame(a, _binder.AnimalSelection.Selected);
            var panel = Object.FindAnyObjectByType<AnimalInfoPanel>(FindObjectsInactive.Include);
            string text = panel.GetComponentInChildren<Text>(true).text;
            StringAssert.Contains(a.Instance.DisplayName, text);
            StringAssert.Contains(a.Instance.AnimalId, text);
            StringAssert.Contains(pen, text);
            StringAssert.Contains("Zebra", text);
            StringAssert.Contains("State:", text);

            yield return Tap(_cam.WorldToScreenPoint(_binder.Grid.GridToWorld(new GridCoord(40, 40))));
            Assert.IsNull(_binder.AnimalSelection.Selected, "tapping empty ground clears the selection");
        }

        [UnityTest]
        public IEnumerator DraggingAcrossAnAnimal_PansTheCamera_AndDoesNotSelect()
        {
            string pen = BuildPen(30, 30, 6, 6);
            var a = _spawner.SpawnNew("zebra", pen).Controller;
            yield return null;
            var cam = Object.FindAnyObjectByType<ZooCameraController>().transform;
            Vector3 before = cam.position;

            Vector2 p = ScreenOf(a);
            Touch(1, TouchPhase.Began, p);
            yield return null;
            for (int i = 1; i <= 6; i++)
            {
                Touch(1, TouchPhase.Moved, p + new Vector2(40f * i, 0f));
                yield return null;
            }
            Touch(1, TouchPhase.Ended, p + new Vector2(240f, 0f));
            yield return null;
            yield return null;

            Assert.IsNull(_binder.AnimalSelection.Selected);
            Assert.AreNotEqual(before, cam.position, "the drag moved the camera");
        }

        [UnityTest]
        public IEnumerator WhileABuildToolIsActive_TapsBelongToTheTool_NotToSelection()
        {
            string pen = BuildPen(30, 30, 6, 6);
            var a = _spawner.SpawnNew("zebra", pen).Controller;
            yield return null;

            _binder.Builder.EnterPath();
            yield return Tap(ScreenOf(a));
            Assert.IsNull(_binder.AnimalSelection.Selected);

            _binder.Builder.ExitMode();
            yield return null;
            yield return Tap(ScreenOf(a));
            Assert.AreSame(a, _binder.AnimalSelection.Selected, "selection works again once the tool is left");
        }

        [UnityTest]
        public IEnumerator DeletingTheSelectedAnimal_ClearsTheSelection()
        {
            string pen = BuildPen(30, 30, 6, 6);
            var spawned = _spawner.SpawnNew("zebra", pen);
            yield return null;
            _binder.AnimalSelection.Select(spawned.Controller);
            Assert.IsNotNull(_binder.AnimalSelection.Selected);

            _spawner.Delete(spawned.Animal.AnimalId);
            yield return null;
            Assert.IsNull(_binder.AnimalSelection.Selected);
        }

        // ---- Placing animals like buildings ----

        Button PanelButton(string name)
        {
            var panel = Object.FindAnyObjectByType<AnimalDebugSpawnPanel>(FindObjectsInactive.Include);
            foreach (var b in panel.GetComponentsInChildren<Button>(true))
                if (b.name == name) return b;
            return null;
        }

        Vector2 ScreenOfCell(int x, int z) => _cam.WorldToScreenPoint(_binder.Grid.GridToWorld(new GridCoord(x, z)));

        [UnityTest]
        public IEnumerator SelectingASpecies_ShowsAGhost_RedUntilItIsInsideASuitableEnclosure()
        {
            string pen = BuildPen(30, 30, 6, 6);
            var tool = _binder.AnimalPlacement;
            yield return null;

            PanelButton("Spawn Zebra").onClick.Invoke();
            yield return null;
            Assert.AreEqual(BuildMode.AnimalPlacement, _binder.Builder.Mode);
            Assert.IsNotNull(tool.GhostObject, "a see-through copy follows the player's choice");
            Assert.AreEqual(0, _binder.Animals.Count, "selecting does not spawn");

            yield return Tap(ScreenOfCell(45, 45));
            Assert.IsFalse(tool.Result.IsValid);
            Assert.AreEqual(AnimalPlacementFailure.NoEnclosure, tool.Result.Failure);
            Assert.IsFalse(PanelButton("Place").interactable);

            yield return Tap(ScreenOfCell(32, 32));
            Assert.IsTrue(tool.Result.IsValid, tool.Result.Message);
            Assert.AreEqual(pen, tool.EnclosureId);
            Assert.IsTrue(PanelButton("Place").interactable);
        }

        [UnityTest]
        public IEnumerator Place_SpawnsWhereTheGhostIs_InsideTheEnclosure_AndEndsPlacing()
        {
            string pen = BuildPen(30, 30, 6, 6);
            var tool = _binder.AnimalPlacement;
            yield return null;

            PanelButton("Spawn Lion").onClick.Invoke();
            yield return Tap(ScreenOfCell(33, 31));
            Vector3 at = tool.Position;
            PanelButton("Place").onClick.Invoke();
            yield return null;

            Assert.AreEqual(1, _binder.Animals.Count);
            var animal = new List<AnimalInstance>(_binder.Animals.GetByEnclosure(pen))[0];
            Assert.AreEqual("lion", animal.SpeciesId);
            Assert.AreEqual(at.x, animal.Position.x, 0.01f);
            Assert.AreEqual(at.z, animal.Position.z, 0.01f);
            Assert.AreEqual(BuildMode.None, _binder.Builder.Mode, "placing ends, as with buildings");
            Assert.IsNull(tool.GhostObject);
            Assert.IsFalse(PanelButton("Place").gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator Place_IsRefused_OutsideAnEnclosure_OrInOneThatIsTooSmall()
        {
            BuildPen(30, 30, 4, 4); // 16 cells
            var tool = _binder.AnimalPlacement;
            yield return null;

            PanelButton("Spawn Lion").onClick.Invoke(); // needs 36
            yield return Tap(ScreenOfCell(31, 31));
            Assert.AreEqual(AnimalPlacementFailure.EnclosureTooSmall, tool.Result.Failure);
            Assert.IsFalse(_binder.Builder.Confirm());

            yield return Tap(ScreenOfCell(45, 45));
            Assert.IsFalse(_binder.Builder.Confirm());
            Assert.AreEqual(0, _binder.Animals.Count);
            Assert.AreEqual(BuildMode.AnimalPlacement, _binder.Builder.Mode, "still placing after a refused confirm");

            PanelButton("Cancel").onClick.Invoke();
            yield return null;
            Assert.AreEqual(BuildMode.None, _binder.Builder.Mode);
            Assert.IsNull(tool.GhostObject);
            Assert.AreEqual(0, _binder.Animals.Count);
        }

        [UnityTest]
        public IEnumerator PressingTheActiveSpecies_Deselects_AndOtherToolsReplaceIt()
        {
            BuildPen(30, 30, 6, 6);
            yield return null;
            PanelButton("Spawn Rabbit").onClick.Invoke();
            Assert.AreEqual(BuildMode.AnimalPlacement, _binder.Builder.Mode);
            PanelButton("Spawn Rabbit").onClick.Invoke();
            Assert.AreEqual(BuildMode.None, _binder.Builder.Mode);
            Assert.IsNull(_binder.AnimalPlacement.GhostObject);

            PanelButton("Spawn Rabbit").onClick.Invoke();
            _binder.Builder.EnterPath();
            Assert.IsNull(_binder.AnimalPlacement.GhostObject, "starting a construction tool drops the ghost");
            Assert.AreEqual(BuildMode.PathPlacement, _binder.Builder.Mode);
        }

        [UnityTest]
        public IEnumerator DraggingTheGhost_MovesIt_AndAPressAwayFromItStillPansTheCamera()
        {
            BuildPen(30, 30, 8, 8);
            var tool = _binder.AnimalPlacement;
            yield return null;
            PanelButton("Spawn Rabbit").onClick.Invoke();
            yield return Tap(ScreenOfCell(31, 31));
            Vector3 start = tool.Position;

            Vector2 from = _cam.WorldToScreenPoint(start);
            Vector2 to = ScreenOfCell(35, 34);
            Touch(1, TouchPhase.Began, from);
            yield return null;
            for (int i = 1; i <= 5; i++)
            {
                Touch(1, TouchPhase.Moved, Vector2.Lerp(from, to, i / 5f));
                yield return null;
            }
            Touch(1, TouchPhase.Ended, to);
            yield return null;
            Assert.Greater(Vector3.Distance(start, tool.Position), 2f, "grabbed the ghost and dragged it");
            Assert.IsTrue(tool.Result.IsValid);

            var cam = Object.FindAnyObjectByType<ZooCameraController>().transform;
            Vector3 camBefore = cam.position;
            Vector2 away = _cam.WorldToScreenPoint(_binder.Grid.GridToWorld(new GridCoord(44, 44)));
            Touch(2, TouchPhase.Began, away);
            yield return null;
            for (int i = 1; i <= 5; i++)
            {
                Touch(2, TouchPhase.Moved, away + new Vector2(40f * i, 0f));
                yield return null;
            }
            Touch(2, TouchPhase.Ended, away + new Vector2(200f, 0f));
            yield return null;
            Assert.AreNotEqual(camBefore, cam.position);
        }
    }
}
