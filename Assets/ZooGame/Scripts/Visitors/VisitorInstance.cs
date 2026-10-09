using System;
using UnityEngine;

namespace ZooGame.Visitors
{
    /// <summary>
    /// One visitor's runtime data: plain serializable state, not a MonoBehaviour, with no scene references. The
    /// <see cref="VisitorRegistry"/> owns these records while the visitor is in the zoo; the id never changes and is
    /// not derived from a GameObject or list position. All 0-100 values clamp on write.
    /// Hunger, thirst and toilet need are 0 = satisfied / 100 = urgent; energy is 0 = exhausted / 100 = rested;
    /// happiness is 0 = unhappy / 100 = delighted. Times are simulated minutes.
    /// </summary>
    [Serializable]
    public sealed class VisitorInstance
    {
        public const float Min = 0f;
        public const float Max = 100f;
        /// <summary>How many recently viewed enclosures a visitor remembers (for the variety penalty).</summary>
        public const int RecentCapacity = 3;
        public const int NoEnclosure = 0;

        [SerializeField] string visitorId;
        [SerializeField] VisitorState state = VisitorState.Entering;
        [SerializeField] Vector3 position;
        [SerializeField] string targetDestinationId;
        [SerializeField] float hunger;
        [SerializeField] float thirst;
        [SerializeField] float toiletNeed;
        [SerializeField] float energy = Max;
        [SerializeField] float happiness;
        [SerializeField] float needsHappiness;
        [SerializeField] float visitSatisfaction;
        [SerializeField] float visitTime;
        [SerializeField] float maximumVisitTime;
        [SerializeField] float unhappyTime;
        [SerializeField] float activityTimeLeft;
        [SerializeField] int currentEnclosureId;
        [SerializeField] int[] recentEnclosures = new int[RecentCapacity];
        [SerializeField] int recentCursor;

        public VisitorInstance(string visitorId, Vector3 position, float maximumVisitTime)
        {
            if (string.IsNullOrEmpty(visitorId)) throw new ArgumentException("A visitor needs an id.", nameof(visitorId));
            this.visitorId = visitorId;
            this.position = position;
            this.maximumVisitTime = Mathf.Max(0f, maximumVisitTime);
        }

        public static VisitorInstance CreateNew(Vector3 position, float maximumVisitTime) =>
            new VisitorInstance(NewId(), position, maximumVisitTime);

        public static string NewId() => "visitor-" + Guid.NewGuid().ToString("N");

        public string VisitorId => visitorId;
        public VisitorState State { get => state; set => state = value; }
        public Vector3 Position { get => position; set => position = value; }

        /// <summary>Id of the destination being walked to (see <c>VisitorDestination.Id</c>); null when there is none.</summary>
        public string TargetDestinationId { get => targetDestinationId; set => targetDestinationId = string.IsNullOrEmpty(value) ? null : value; }

        public float Hunger { get => hunger; set => hunger = Clamp(value); }
        public float Thirst { get => thirst; set => thirst = Clamp(value); }
        public float ToiletNeed { get => toiletNeed; set => toiletNeed = Clamp(value); }
        public float Energy { get => energy; set => energy = Clamp(value); }

        /// <summary>Overall happiness, after the critical-need caps.</summary>
        public float Happiness { get => happiness; set => happiness = Clamp(value); }

        /// <summary>Happiness from needs alone (before the visit satisfaction blend and caps).</summary>
        public float NeedsHappiness { get => needsHappiness; set => needsHappiness = Clamp(value); }

        /// <summary>Slow-moving score from recent successful activities (viewing, facility use).</summary>
        public float VisitSatisfaction { get => visitSatisfaction; set => visitSatisfaction = Clamp(value); }

        /// <summary>Simulated minutes spent in the zoo.</summary>
        public float VisitTime { get => visitTime; set => visitTime = Mathf.Max(0f, value); }
        public float MaximumVisitTime { get => maximumVisitTime; set => maximumVisitTime = Mathf.Max(0f, value); }

        /// <summary>Simulated minutes spent continuously at or below the unhappy threshold.</summary>
        public float UnhappyTime { get => unhappyTime; set => unhappyTime = Mathf.Max(0f, value); }

        /// <summary>Simulated minutes left in the current viewing / resting activity.</summary>
        public float ActivityTimeLeft { get => activityTimeLeft; set => activityTimeLeft = Mathf.Max(0f, value); }

        /// <summary>The enclosure being looked at (<see cref="NoEnclosure"/> when not viewing).</summary>
        public int CurrentEnclosureId { get => currentEnclosureId; set => currentEnclosureId = value; }

        public bool HasVisitTimeLeft => visitTime < maximumVisitTime;

        public bool RecentlyViewed(int enclosureId)
        {
            if (enclosureId == NoEnclosure) return false;
            for (int i = 0; i < recentEnclosures.Length; i++)
                if (recentEnclosures[i] == enclosureId) return true;
            return false;
        }

        public void RememberViewed(int enclosureId)
        {
            if (enclosureId == NoEnclosure) return;
            recentEnclosures[recentCursor] = enclosureId;
            recentCursor = (recentCursor + 1) % recentEnclosures.Length;
        }

        public void ForgetRecent()
        {
            Array.Clear(recentEnclosures, 0, recentEnclosures.Length);
            recentCursor = 0;
        }

        // NaN becomes Min
        static float Clamp(float v) => !(v > Min) ? Min : v > Max ? Max : v;
    }
}
