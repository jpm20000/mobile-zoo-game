using NUnit.Framework;
using ZooGame.Input;

namespace ZooGame.Tests.EditMode
{
    public class PointerOwnershipTests
    {
        [Test]
        public void FirstClaimWins()
        {
            var t = new PointerOwnershipTracker();
            Assert.IsTrue(t.TryClaim(1, InputOwner.Ui));
            Assert.IsFalse(t.TryClaim(1, InputOwner.World));
            Assert.AreEqual(InputOwner.Ui, t.GetOwner(1));
        }

        [Test]
        public void SameOwnerCanReclaim()
        {
            var t = new PointerOwnershipTracker();
            t.TryClaim(1, InputOwner.Camera);
            Assert.IsTrue(t.TryClaim(1, InputOwner.Camera));
        }

        [Test]
        public void PointersAreIndependent()
        {
            var t = new PointerOwnershipTracker();
            t.TryClaim(1, InputOwner.Camera);
            Assert.IsTrue(t.TryClaim(2, InputOwner.Placement));
        }

        [Test]
        public void OnlyOwnerCanRelease()
        {
            var t = new PointerOwnershipTracker();
            t.TryClaim(1, InputOwner.World);
            Assert.IsFalse(t.Release(1, InputOwner.Camera));
            Assert.AreEqual(InputOwner.World, t.GetOwner(1));
            Assert.IsTrue(t.Release(1, InputOwner.World));
            Assert.AreEqual(InputOwner.None, t.GetOwner(1));
        }

        [Test]
        public void ReleaseAll_FreesPointerForNewOwner()
        {
            var t = new PointerOwnershipTracker();
            t.TryClaim(1, InputOwner.Ui);
            t.ReleaseAll(1);
            Assert.IsTrue(t.TryClaim(1, InputOwner.World));
        }

        [Test]
        public void ClaimingNone_IsRejected()
        {
            Assert.IsFalse(new PointerOwnershipTracker().TryClaim(1, InputOwner.None));
        }
    }
}
