using NUnit.Framework;
using ZooGame.Placement;
using ZooGame.World;

namespace ZooGame.Tests.EditMode
{
    public class PlacementSessionTests
    {
        PlacementFixture _f;
        PlacementSession S => _f.Session;

        [SetUp] public void SetUp() => _f = new PlacementFixture();
        [TearDown] public void TearDown() => _f.Dispose();

        [Test]
        public void BeginPlacing_ShowsAValidPreviewOnFreeUnlockedLand()
        {
            var def = _f.Define("a", 2, 2);
            Assert.IsTrue(S.BeginPlacing(def, new GridCoord(6, 6)));
            Assert.AreEqual(PlacementMode.Placing, S.Mode);
            Assert.IsTrue(S.Result.IsValid);
            Assert.AreEqual(0, _f.Map.Count, "previewing places nothing");
            Assert.AreEqual(0, _f.OccupiedCount());
        }

        [Test]
        public void Preview_TracksValidityAsItMovesAcrossTheGrid()
        {
            var def = _f.Define("a", 2, 2);
            S.BeginPlacing(def, new GridCoord(6, 6));
            S.SetOrigin(new GridCoord(2, 6));
            Assert.AreEqual(PlacementFailure.LockedLand, S.Result.Failure);
            S.SetOrigin(new GridCoord(15, 6));
            Assert.AreEqual(PlacementFailure.OutOfBounds, S.Result.Failure);
            S.SetOrigin(new GridCoord(7, 7));
            Assert.IsTrue(S.Result.IsValid);
        }

        [Test]
        public void Confirm_PlacesTheObjectAndSelectsIt()
        {
            var def = _f.Define("a", 2, 3);
            S.BeginPlacing(def, new GridCoord(6, 6));
            Assert.IsTrue(S.Confirm());

            Assert.AreEqual(PlacementMode.Idle, S.Mode);
            Assert.AreEqual(1, _f.Map.Count);
            Assert.AreEqual(6, _f.OccupiedCount());
            Assert.IsNotNull(S.Selected);
            Assert.AreEqual(new GridCoord(6, 6), S.Selected.Origin);
        }

        [Test]
        public void Confirm_WhenInvalid_DoesNothingAndKeepsPreviewing()
        {
            var def = _f.Define("a", 2, 2);
            S.BeginPlacing(def, new GridCoord(2, 2));
            Assert.IsFalse(S.Confirm());
            Assert.AreEqual(PlacementMode.Placing, S.Mode);
            Assert.AreEqual(0, _f.Map.Count);
            Assert.AreEqual(0, _f.OccupiedCount());
        }

        [Test]
        public void Confirm_OverAnExistingObject_IsRejected_SoObjectsCannotOverlap()
        {
            var def = _f.Define("a", 2, 2);
            S.BeginPlacing(def, new GridCoord(6, 6));
            S.Confirm();
            S.BeginPlacing(def, new GridCoord(7, 7));
            Assert.AreEqual(PlacementFailure.Occupied, S.Result.Failure);
            Assert.IsFalse(S.Confirm());
            Assert.AreEqual(1, _f.Map.Count);
            Assert.AreEqual(4, _f.OccupiedCount());
        }

        [Test]
        public void Cancel_WhilePlacing_LeavesNothingBehind()
        {
            var def = _f.Define("a", 2, 2);
            S.BeginPlacing(def, new GridCoord(6, 6));
            S.Cancel();
            Assert.AreEqual(PlacementMode.Idle, S.Mode);
            Assert.AreEqual(0, _f.Map.Count);
            Assert.AreEqual(0, _f.OccupiedCount());
            Assert.IsNull(S.Selected);
        }

        [Test]
        public void Rotate_SwapsWidthAndHeightOfTheNonSquarePreview()
        {
            var def = _f.Define("a", 2, 3);
            S.BeginPlacing(def, new GridCoord(6, 6));
            Assert.AreEqual((2, 3), (S.Width, S.Height));
            Assert.IsTrue(S.Rotate());
            Assert.AreEqual((3, 2), (S.Width, S.Height));
            Assert.AreEqual(Rotation90.Deg90, S.Rotation);
            Assert.IsTrue(S.Rotate());
            Assert.AreEqual((2, 3), (S.Width, S.Height));
        }

        [Test]
        public void Rotate_RevalidatesWithTheNewFootprint()
        {
            var def = _f.Define("a", 2, 3);
            S.BeginPlacing(def, new GridCoord(10, 4));
            Assert.IsTrue(S.Result.IsValid);
            S.Rotate(); // 3x2 reaches x = 12 (locked)
            Assert.AreEqual(PlacementFailure.LockedLand, S.Result.Failure);
            S.Rotate();
            S.Rotate();
            S.Rotate(); // four quarter turns: back to the unrotated 2x3
            Assert.IsTrue(S.Result.IsValid);
        }

        [Test]
        public void Rotate_PlacedObjectOccupiesTheRotatedCells()
        {
            var def = _f.Define("a", 2, 3);
            S.BeginPlacing(def, new GridCoord(5, 5));
            S.Rotate();
            S.Confirm();
            Assert.AreEqual((3, 2), (S.Selected.Width, S.Selected.Height));
            Assert.IsTrue(_f.Grid.IsOccupied(new GridCoord(7, 6)));
            Assert.IsFalse(_f.Grid.IsOccupied(new GridCoord(5, 7)));
        }

        [Test]
        public void Rotate_IsIgnoredWhenTheDefinitionDoesNotSupportIt()
        {
            var def = _f.Define("a", 2, 3, rotatable: false);
            S.BeginPlacing(def, new GridCoord(6, 6));
            Assert.IsFalse(S.Rotate());
            Assert.AreEqual(Rotation90.Deg0, S.Rotation);
            Assert.AreEqual((2, 3), (S.Width, S.Height));
        }

        [Test]
        public void Select_OnlyWorksOnPlacedObjectsWhileIdle()
        {
            var def = _f.Define("a", 1, 1);
            var obj = _f.Map.Add(def, new GridCoord(6, 6), Rotation90.Deg0);
            Assert.IsTrue(S.Select(obj));
            Assert.AreSame(obj, S.Selected);
            Assert.IsTrue(S.ClearSelection());
            Assert.IsNull(S.Selected);

            S.BeginPlacing(def, new GridCoord(8, 8));
            Assert.IsFalse(S.Select(obj), "no selecting mid-placement");
        }

        // ---- Moving ----

        PlacedObject PlaceSelected(int w, int h, GridCoord at)
        {
            var def = _f.Define("m" + w + h, w, h);
            S.BeginPlacing(def, at);
            S.Confirm();
            return S.Selected;
        }

        [Test]
        public void Move_PreviewStartsAtTheCurrentPlacementAndKeepsCellsOccupiedUntilConfirmed()
        {
            var obj = PlaceSelected(2, 2, new GridCoord(5, 5));
            Assert.IsTrue(S.BeginMove());

            Assert.AreEqual(PlacementMode.Moving, S.Mode);
            Assert.AreEqual(new GridCoord(5, 5), S.Origin);
            Assert.IsTrue(S.Result.IsValid, "an object does not block its own footprint");
            Assert.AreEqual(4, _f.OccupiedCount());
            Assert.AreSame(obj, _f.Map.GetAt(new GridCoord(5, 5)));
        }

        [Test]
        public void Move_ConfirmReleasesOldCellsAndOccupiesNewOnes()
        {
            var obj = PlaceSelected(2, 3, new GridCoord(4, 4));
            S.BeginMove();
            S.SetOrigin(new GridCoord(8, 6));
            Assert.IsTrue(S.Confirm());

            Assert.AreEqual(PlacementMode.Idle, S.Mode);
            Assert.AreEqual(new GridCoord(8, 6), obj.Origin);
            Assert.AreEqual(6, _f.OccupiedCount());
            Assert.IsFalse(_f.Grid.IsOccupied(new GridCoord(4, 4)), "old cells are free again");
            Assert.IsTrue(_f.Grid.IsOccupied(new GridCoord(9, 8)));
            Assert.AreSame(obj, S.Selected);
        }

        [Test]
        public void Move_CancelRestoresTheOriginalPlacementExactly()
        {
            var obj = PlaceSelected(2, 2, new GridCoord(5, 5));
            S.BeginMove();
            S.SetOrigin(new GridCoord(9, 9));
            S.Rotate();
            S.Cancel();

            Assert.AreEqual(PlacementMode.Idle, S.Mode);
            Assert.AreEqual(new GridCoord(5, 5), obj.Origin);
            Assert.AreEqual(Rotation90.Deg0, obj.Rotation);
            Assert.AreEqual(4, _f.OccupiedCount());
            Assert.IsTrue(_f.Grid.IsOccupied(new GridCoord(5, 5)));
            Assert.IsFalse(_f.Grid.IsOccupied(new GridCoord(9, 9)));
            Assert.AreSame(obj, S.Selected, "the object stays selected after a cancelled move");
        }

        [Test]
        public void Move_ToAnInvalidSpotCannotBeConfirmed_AndLeavesTheObjectWhereItWas()
        {
            var obj = PlaceSelected(2, 2, new GridCoord(4, 4));
            var other = _f.Define("o", 2, 2);
            _f.Map.Add(other, new GridCoord(8, 4), Rotation90.Deg0);

            S.Select(obj);
            S.BeginMove();
            S.SetOrigin(new GridCoord(7, 4));
            Assert.AreEqual(PlacementFailure.Occupied, S.Result.Failure);
            Assert.IsFalse(S.Confirm());
            Assert.AreEqual(PlacementMode.Moving, S.Mode);
            Assert.AreEqual(new GridCoord(4, 4), obj.Origin);
            Assert.AreEqual(8, _f.OccupiedCount());
        }

        [Test]
        public void Move_CanRotateWhileMovingAndCommitTheRotation()
        {
            var obj = PlaceSelected(2, 3, new GridCoord(5, 5));
            S.BeginMove();
            S.Rotate();
            S.Confirm();
            Assert.AreEqual(Rotation90.Deg90, obj.Rotation);
            Assert.AreEqual((3, 2), (obj.Width, obj.Height));
            Assert.AreEqual(6, _f.OccupiedCount());
            Assert.IsFalse(_f.Grid.IsOccupied(new GridCoord(5, 7)));
        }

        [Test]
        public void BeginMove_NeedsASelection()
        {
            Assert.IsFalse(S.BeginMove());
        }

        // ---- Rotate in place / delete ----

        [Test]
        public void RotateSelected_RotatesInPlaceWhenThereIsRoom()
        {
            var obj = PlaceSelected(2, 3, new GridCoord(5, 5));
            Assert.IsTrue(S.RotateSelected());
            Assert.AreEqual((3, 2), (obj.Width, obj.Height));
            Assert.AreEqual(6, _f.OccupiedCount());
            Assert.IsTrue(_f.Grid.IsOccupied(new GridCoord(7, 6)));
            Assert.IsFalse(_f.Grid.IsOccupied(new GridCoord(5, 7)));
        }

        [Test]
        public void RotateSelected_IsRefusedWhenTheRotatedFootprintWouldCollide()
        {
            var obj = PlaceSelected(2, 3, new GridCoord(5, 5));
            var blocker = _f.Define("b", 1, 1);
            _f.Map.Add(blocker, new GridCoord(7, 5), Rotation90.Deg0);

            S.Select(obj);
            Assert.IsFalse(S.RotateSelected());
            Assert.AreEqual(PlacementFailure.Occupied, S.LastRejection.Failure);
            Assert.AreEqual(Rotation90.Deg0, obj.Rotation);
            Assert.AreEqual(7, _f.OccupiedCount());
        }

        [Test]
        public void RotateSelected_FourTimesReturnsToTheStart()
        {
            var obj = PlaceSelected(2, 3, new GridCoord(5, 5));
            for (int i = 0; i < 4; i++) Assert.IsTrue(S.RotateSelected());
            Assert.AreEqual(Rotation90.Deg0, obj.Rotation);
            Assert.AreEqual(new GridCoord(5, 5), obj.Origin);
            Assert.AreEqual((2, 3), (obj.Width, obj.Height));
        }

        [Test]
        public void DeleteSelected_FreesAllCellsAndClearsTheSelection()
        {
            var obj = PlaceSelected(3, 4, new GridCoord(4, 4));
            Assert.AreEqual(12, _f.OccupiedCount());

            Assert.IsTrue(S.DeleteSelected());
            Assert.AreEqual(0, _f.OccupiedCount());
            Assert.AreEqual(0, _f.Map.Count);
            Assert.IsNull(S.Selected);
            Assert.IsFalse(_f.Map.Contains(obj));

            // The space can be reused straight away.
            S.BeginPlacing(_f.Define("again", 3, 4), new GridCoord(4, 4));
            Assert.IsTrue(S.Confirm());
        }

        [Test]
        public void DeleteSelected_WithoutSelection_DoesNothing()
        {
            Assert.IsFalse(S.DeleteSelected());
        }

        [Test]
        public void Changed_FiresWhenPreviewOrSelectionChanges()
        {
            int count = 0;
            S.Changed += () => count++;
            var def = _f.Define("a", 1, 1);
            S.BeginPlacing(def, new GridCoord(6, 6));
            int afterBegin = count;
            S.SetOrigin(new GridCoord(6, 6)); // unchanged: no event
            Assert.AreEqual(afterBegin, count);
            S.SetOrigin(new GridCoord(7, 6));
            Assert.Greater(count, afterBegin);
        }
    }
}
