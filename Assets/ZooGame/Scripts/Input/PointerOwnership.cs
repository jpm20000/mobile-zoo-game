using System.Collections.Generic;

namespace ZooGame.Input
{
    /// <summary>Which system currently owns a pointer (finger/mouse) for the duration of a gesture.</summary>
    public enum InputOwner
    {
        None = 0,
        Ui,
        Camera,
        World,
        Placement
    }

    /// <summary>
    /// First-claim-wins arbitration so two systems never both act on the same touch.
    /// A system claims a pointer id when it starts handling a gesture and releases it when done.
    /// The input source releases every claim automatically when the pointer ends or is cancelled.
    /// Pure C#; no UnityEngine dependency.
    /// </summary>
    public sealed class PointerOwnershipTracker
    {
        readonly Dictionary<int, InputOwner> _owners = new Dictionary<int, InputOwner>(8);

        public InputOwner GetOwner(int pointerId) =>
            _owners.TryGetValue(pointerId, out var owner) ? owner : InputOwner.None;

        /// <summary>True if <paramref name="owner"/> now owns the pointer (newly claimed or already owned).</summary>
        public bool TryClaim(int pointerId, InputOwner owner)
        {
            if (owner == InputOwner.None) return false;
            if (_owners.TryGetValue(pointerId, out var current)) return current == owner;
            _owners.Add(pointerId, owner);
            return true;
        }

        /// <summary>Releases only if <paramref name="owner"/> is the current owner.</summary>
        public bool Release(int pointerId, InputOwner owner)
        {
            if (!_owners.TryGetValue(pointerId, out var current) || current != owner) return false;
            _owners.Remove(pointerId);
            return true;
        }

        public void ReleaseAll(int pointerId) => _owners.Remove(pointerId);
    }
}
