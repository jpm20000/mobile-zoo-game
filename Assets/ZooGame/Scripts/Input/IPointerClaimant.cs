namespace ZooGame.Input
{
    /// <summary>
    /// A system that must decide, at the moment a pointer goes down, whether it takes that pointer for itself
    /// (for example placement grabbing its ghost). The decision is made before gesture recognition, so a claimed
    /// pointer never becomes a camera pan; the claimant then follows it through <see cref="PointerInputSource.PointerChanged"/>.
    /// UI has already had its chance by then, so a claimant never sees pointers that began over UI.
    /// </summary>
    public interface IPointerClaimant
    {
        InputOwner Owner { get; }

        /// <summary>Called once per pointer press that nobody owns yet. Return true to claim it as <see cref="Owner"/>.</summary>
        bool WantsPointer(in PointerSample sample);
    }
}
