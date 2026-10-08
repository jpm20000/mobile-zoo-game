using NUnit.Framework;
using UnityEngine;
using ZooGame.Cameras;

namespace ZooGame.Tests.EditMode
{
    public class CameraMathTests
    {
        const float Yaw = 45f, Pitch = 30f;

        [Test]
        public void UnitsPerPixel_IsHeightOverPixels()
        {
            Assert.AreEqual(0.02f, CameraMath.UnitsPerPixel(10f, 1000f), 1e-6f);
            Assert.Greater(CameraMath.UnitsPerPixel(10f, 0f), 0f); // never divides by zero
        }

        [Test]
        public void ClampZoom_LimitsToRange()
        {
            Assert.AreEqual(5f, CameraMath.ClampZoom(1f, 5f, 20f));
            Assert.AreEqual(20f, CameraMath.ClampZoom(99f, 5f, 20f));
            Assert.AreEqual(12f, CameraMath.ClampZoom(12f, 5f, 20f));
        }

        [Test]
        public void ClampFocus_AllowsMarginBeyondMapAndKeepsY()
        {
            var min = new Vector3(0, 0, 0);
            var max = new Vector3(64, 0, 64);
            var inside = CameraMath.ClampFocus(new Vector3(30, 2, 30), min, max, 4f);
            Assert.AreEqual(new Vector3(30, 2, 30), inside);

            var clamped = CameraMath.ClampFocus(new Vector3(-100, 2, 500), min, max, 4f);
            Assert.AreEqual(-4f, clamped.x);
            Assert.AreEqual(68f, clamped.z);
            Assert.AreEqual(2f, clamped.y);
        }

        [Test]
        public void GroundAxes_AreOrthonormalHorizontalAndMatchCameraRotation()
        {
            CameraMath.GroundAxes(Yaw, out var right, out var forward);
            Assert.AreEqual(1f, right.magnitude, 1e-5f);
            Assert.AreEqual(1f, forward.magnitude, 1e-5f);
            Assert.AreEqual(0f, Vector3.Dot(right, forward), 1e-5f);
            Assert.AreEqual(0f, right.y);
            Assert.AreEqual(0f, forward.y);

            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            var camRight = rot * Vector3.right;
            Assert.AreEqual(camRight.x, right.x, 1e-5f);
            Assert.AreEqual(camRight.z, right.z, 1e-5f);
            var camForward = rot * Vector3.forward;
            var flat = new Vector3(camForward.x, 0f, camForward.z).normalized;
            Assert.AreEqual(flat.x, forward.x, 1e-5f);
            Assert.AreEqual(flat.z, forward.z, 1e-5f);
        }

        [Test]
        public void Pan_DragRight_MovesFocusAlongNegativeRight()
        {
            var d = CameraMath.PanFocusDelta(new Vector2(100f, 0f), 0.02f, Yaw, Pitch, 1f);
            CameraMath.GroundAxes(Yaw, out var right, out _);
            Assert.AreEqual(-2f, Vector3.Dot(d, right), 1e-4f);
            Assert.AreEqual(0f, d.y);
        }

        [Test]
        public void Pan_GroundPointFollowsFinger_OnScreen()
        {
            // Project a ground point to screen before and after the pan; it must move by exactly the finger delta.
            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            float upp = 0.02f;
            var focus = new Vector3(32, 0, 32);
            var ground = new Vector3(35, 0, 30);
            var delta = new Vector2(40f, -25f);

            Vector2 ToScreen(Vector3 f, Vector3 p)
            {
                var local = Quaternion.Inverse(rot) * (p - f);
                return new Vector2(local.x, local.y) / upp;
            }

            var before = ToScreen(focus, ground);
            var newFocus = focus + CameraMath.PanFocusDelta(delta, upp, Yaw, Pitch, 1f);
            var after = ToScreen(newFocus, ground);
            Assert.AreEqual(delta.x, (after - before).x, 1e-2f);
            Assert.AreEqual(delta.y, (after - before).y, 1e-2f);
        }

        [Test]
        public void Pan_SpeedScalesLinearly()
        {
            var one = CameraMath.PanFocusDelta(new Vector2(10, 10), 0.02f, Yaw, Pitch, 1f);
            var two = CameraMath.PanFocusDelta(new Vector2(10, 10), 0.02f, Yaw, Pitch, 2f);
            Assert.AreEqual(one.x * 2f, two.x, 1e-5f);
            Assert.AreEqual(one.z * 2f, two.z, 1e-5f);
        }

        [Test]
        public void ZoomAnchor_KeepsGroundPointUnderFingerFixed()
        {
            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            var focus = new Vector3(32, 0, 32);
            var offset = new Vector2(300f, -120f);   // pixels from screen centre
            float oldUpp = 0.02f, newUpp = 0.01f;

            // Ground point seen under a screen offset, via full camera-space projection.
            Vector3 GroundUnder(Vector3 f, float upp)
            {
                var ray = rot * Vector3.forward;
                var origin = f - ray * 100f + (rot * new Vector3(offset.x * upp, offset.y * upp, 0f));
                float t = -origin.y / ray.y;
                return origin + ray * t;
            }

            var before = GroundUnder(focus, oldUpp);
            var shifted = focus + CameraMath.ZoomAnchorShift(offset, oldUpp, newUpp, Yaw, Pitch);
            var after = GroundUnder(shifted, newUpp);
            Assert.AreEqual(before.x, after.x, 1e-3f);
            Assert.AreEqual(before.z, after.z, 1e-3f);
        }

        [Test]
        public void CameraPosition_LooksAtFocus()
        {
            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            var focus = new Vector3(10, 0, 20);
            var pos = CameraMath.CameraPosition(focus, rot, 100f);
            Assert.AreEqual(100f, Vector3.Distance(pos, focus), 1e-3f);
            var back = pos + rot * Vector3.forward * 100f;
            Assert.AreEqual(focus.x, back.x, 1e-3f);
            Assert.AreEqual(focus.z, back.z, 1e-3f);
            Assert.Greater(pos.y, 0f);
        }

        [Test]
        public void Smoothing_ZeroIsInstant_AndIsFrameRateIndependent()
        {
            Assert.AreEqual(1f, CameraMath.SmoothingFactor(0.016f, 0f));
            float oneStep = CameraMath.SmoothingFactor(0.032f, 0.1f);
            float keep = 1f - CameraMath.SmoothingFactor(0.016f, 0.1f);
            float twoSteps = 1f - keep * keep;
            Assert.AreEqual(oneStep, twoSteps, 1e-5f);
        }
    }
}
