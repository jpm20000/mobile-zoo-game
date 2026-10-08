using NUnit.Framework;
using UnityEngine;
using ZooGame.UI;

namespace ZooGame.Tests.EditMode
{
    public class SafeAreaMathTests
    {
        [Test]
        public void FullScreenSafeArea_GivesFullAnchors()
        {
            Assert.IsTrue(SafeAreaMath.TryGetAnchors(new Rect(0, 0, 2400, 1080), 2400, 1080, out var min, out var max));
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }

        [Test]
        public void LandscapeNotchInsets_AreNormalised()
        {
            // 2436x1125 with 132px side insets and a 63px bottom inset (iPhone X style landscape).
            Assert.IsTrue(SafeAreaMath.TryGetAnchors(new Rect(132, 63, 2172, 1062), 2436, 1125, out var min, out var max));
            Assert.AreEqual(132f / 2436f, min.x, 1e-5f);
            Assert.AreEqual(63f / 1125f, min.y, 1e-5f);
            Assert.AreEqual(1f - 132f / 2436f, max.x, 1e-5f);
            Assert.AreEqual(1f, max.y, 1e-5f);
        }

        [Test]
        public void ZeroSizedScreen_IsRejected()
        {
            Assert.IsFalse(SafeAreaMath.TryGetAnchors(new Rect(0, 0, 0, 0), 0, 0, out _, out _));
        }
    }
}
