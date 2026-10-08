using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooGame.Input;

namespace ZooGame.Tests.EditMode
{
    public class PointerGestureRecognizerTests
    {
        PointerOwnershipTracker _owner;
        PointerGestureRecognizer _g;
        List<string> _log;
        List<float> _ratios;

        [SetUp]
        public void SetUp()
        {
            _owner = new PointerOwnershipTracker();
            _g = new PointerGestureRecognizer(_owner) { Settings = new GestureSettings(10f, 0.5f) };
            _log = new List<string>();
            _ratios = new List<float>();
            _g.Tapped += (id, p) => _log.Add("tap" + id);
            _g.DragStarted += (id, s, c) => _log.Add("dragStart" + id);
            _g.DragMoved += (id, p, d) => _log.Add("dragMove" + id);
            _g.DragEnded += id => _log.Add("dragEnd" + id);
            _g.PinchStarted += (a, b, c) => _log.Add("pinchStart");
            _g.PinchChanged += (c, r) => { _log.Add("pinch"); _ratios.Add(r); };
            _g.PinchEnded += () => _log.Add("pinchEnd");
        }

        void Send(int id, PointerPhase phase, float x, float y, float t = 0f) =>
            _g.Process(new PointerSample(id, phase, new Vector2(x, y), Vector2.zero), t);

        [Test]
        public void ShortPressWithoutMovement_IsATap()
        {
            Send(1, PointerPhase.Began, 100, 100, 0f);
            Send(1, PointerPhase.Ended, 100, 100, 0.1f);
            CollectionAssert.AreEqual(new[] { "tap1" }, _log);
        }

        [Test]
        public void SmallJitterBelowThreshold_StillATap()
        {
            Send(1, PointerPhase.Began, 100, 100);
            Send(1, PointerPhase.Moved, 105, 104, 0.05f);
            Send(1, PointerPhase.Ended, 105, 104, 0.1f);
            CollectionAssert.AreEqual(new[] { "tap1" }, _log);
        }

        [Test]
        public void LongPress_IsNotATap()
        {
            Send(1, PointerPhase.Began, 100, 100, 0f);
            Send(1, PointerPhase.Ended, 100, 100, 0.9f);
            Assert.IsEmpty(_log);
        }

        [Test]
        public void MovementBeyondThreshold_StartsDragAndSuppressesTap()
        {
            Send(1, PointerPhase.Began, 100, 100);
            Send(1, PointerPhase.Moved, 120, 100, 0.02f);
            Send(1, PointerPhase.Moved, 140, 100, 0.04f);
            Send(1, PointerPhase.Ended, 140, 100, 0.06f);
            CollectionAssert.AreEqual(new[] { "dragStart1", "dragMove1", "dragEnd1" }, _log);
        }

        [Test]
        public void DragDelta_IsMeasuredFromPreviousPosition_AfterDeadZone()
        {
            Vector2 total = Vector2.zero;
            _g.DragMoved += (id, p, d) => total += d;
            Send(1, PointerPhase.Began, 0, 0);
            Send(1, PointerPhase.Moved, 15, 0);   // crosses threshold: dead zone swallowed
            Send(1, PointerPhase.Moved, 25, 5);
            Send(1, PointerPhase.Moved, 30, 5);
            Assert.AreEqual(new Vector2(15f, 5f), total);
        }

        [Test]
        public void CanceledPress_IsNeitherTapNorLeftDragging()
        {
            Send(1, PointerPhase.Began, 0, 0);
            Send(1, PointerPhase.Canceled, 0, 0, 0.05f);
            Assert.IsEmpty(_log);

            Send(2, PointerPhase.Began, 0, 0);
            Send(2, PointerPhase.Moved, 50, 0);
            Send(2, PointerPhase.Canceled, 50, 0);
            CollectionAssert.AreEqual(new[] { "dragStart2", "dragEnd2" }, _log);
        }

        [Test]
        public void UiOwnedPointer_ProducesNoGestures()
        {
            _owner.TryClaim(1, InputOwner.Ui);
            Send(1, PointerPhase.Began, 0, 0);
            Send(1, PointerPhase.Moved, 100, 0);
            Send(1, PointerPhase.Ended, 100, 0);
            Assert.IsEmpty(_log);
        }

        [Test]
        public void SecondFinger_ConvertsDragToPinch_AndReportsSpreadRatio()
        {
            Send(1, PointerPhase.Began, 100, 100);
            Send(1, PointerPhase.Moved, 130, 100);        // dragging
            Send(2, PointerPhase.Began, 230, 100);        // second finger: pinch (distance 100)
            Send(2, PointerPhase.Moved, 330, 100);        // distance 200
            CollectionAssert.AreEqual(new[] { "dragStart1", "dragEnd1", "pinchStart", "pinch" }, _log);
            Assert.AreEqual(2f, _ratios[0], 1e-4f);
        }

        [Test]
        public void PinchFingersMovingTogether_ReportsRatioBelowOne()
        {
            Send(1, PointerPhase.Began, 0, 0);
            Send(2, PointerPhase.Began, 200, 0);
            Send(2, PointerPhase.Moved, 100, 0);
            Assert.AreEqual(0.5f, _ratios[0], 1e-4f);
        }

        [Test]
        public void PinchFingersMovingInParallel_StillReportsTheMovingCentre()
        {
            var centres = new List<Vector2>();
            _g.PinchChanged += (c, r) => centres.Add(c);
            Send(1, PointerPhase.Began, 0, 0);
            Send(2, PointerPhase.Began, 100, 0);
            Send(1, PointerPhase.Moved, 0, 40);
            Send(2, PointerPhase.Moved, 100, 40);          // both moved: distance back to 100, centre moved
            Assert.AreEqual(new Vector2(50f, 40f), centres[centres.Count - 1]);

            _ratios.Clear();
            centres.Clear();
            Send(1, PointerPhase.Moved, 0, 40);            // no change at all: nothing raised
            Assert.IsEmpty(centres);
        }

        [Test]
        public void AfterPinch_RemainingFingerCannotPanOrTapUntilItLifts()
        {
            Send(1, PointerPhase.Began, 0, 0);
            Send(2, PointerPhase.Began, 100, 0);
            Send(2, PointerPhase.Ended, 100, 0);
            _log.Clear();

            Send(1, PointerPhase.Moved, 200, 200);
            Send(1, PointerPhase.Ended, 200, 200, 0.1f);
            Assert.IsEmpty(_log);

            Send(1, PointerPhase.Began, 0, 0, 1f); // fresh press works again
            Send(1, PointerPhase.Ended, 0, 0, 1.1f);
            CollectionAssert.AreEqual(new[] { "tap1" }, _log);
        }

        [Test]
        public void ThirdFinger_IsIgnored()
        {
            Send(1, PointerPhase.Began, 0, 0);
            Send(2, PointerPhase.Began, 100, 0);
            Send(3, PointerPhase.Began, 50, 50);
            Send(3, PointerPhase.Moved, 500, 500);
            Send(3, PointerPhase.Ended, 500, 500);
            CollectionAssert.AreEqual(new[] { "pinchStart" }, _log);
        }

        [Test]
        public void UiFingerPlusWorldFinger_BehavesAsSinglePointer()
        {
            _owner.TryClaim(1, InputOwner.Ui);
            Send(1, PointerPhase.Began, 0, 0);     // ignored (UI)
            Send(2, PointerPhase.Began, 100, 100);
            Send(2, PointerPhase.Moved, 140, 100);
            CollectionAssert.AreEqual(new[] { "dragStart2" }, _log);
        }
    }
}
