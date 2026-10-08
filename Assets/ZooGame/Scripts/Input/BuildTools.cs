namespace ZooGame.Input
{
    /// <summary>What the player is currently doing with the world.</summary>
    public enum BuildMode
    {
        None = 0,
        ObjectPlacement,
        PathPlacement,
        FencePlacement,
        Demolition
    }

    /// <summary>
    /// One construction tool driven by the shared build-mode controller. The controller owns pointer claiming and
    /// gesture subscriptions; a tool only answers these calls. New construction systems add a tool and a
    /// <see cref="BuildMode"/> value, not another input system.
    /// </summary>
    public interface IBuildTool
    {
        BuildMode Mode { get; }

        /// <summary>The pointer owner this tool claims fingers as.</summary>
        InputOwner Owner { get; }

        /// <summary>True while the tool holds an uncommitted preview that Confirm would build.</summary>
        bool HasPending { get; }

        void Enter();

        /// <summary>Leaving the mode: discard any preview.</summary>
        void Exit();

        /// <summary>Called when an unowned pointer goes down in the world. Return true to claim it.</summary>
        bool WantsPointer(in PointerSample sample);

        /// <summary>Called for every sample of a pointer this tool claimed.</summary>
        void OnPointer(in PointerSample sample);

        /// <summary>A short tap that nobody claimed.</summary>
        void OnTapped(int pointerId, UnityEngine.Vector2 screenPosition);

        bool Confirm();

        /// <summary>Discards the preview; with nothing pending it does nothing.</summary>
        void Cancel();
    }
}
