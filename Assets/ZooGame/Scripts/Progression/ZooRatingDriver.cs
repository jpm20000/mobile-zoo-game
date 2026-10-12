using UnityEngine;
using ZooGame.Core;

namespace ZooGame.Progression
{
    /// <summary>
    /// The single per-frame hook for the rating. It forwards GameClock simulation time (zero while paused, scaled by
    /// 1x/2x/3x) to <see cref="ZooRatingService.Tick"/>; all logic lives in the service.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZooRatingDriver : MonoBehaviour
    {
        GameClock _clock;
        ZooRatingService _rating;

        public void Bind(GameClock clock, ZooRatingService rating)
        {
            _clock = clock;
            _rating = rating;
        }

        void Update()
        {
            if (_rating == null) return;
            _rating.Tick(_clock != null ? _clock.DeltaTime : Time.deltaTime);
        }
    }
}
