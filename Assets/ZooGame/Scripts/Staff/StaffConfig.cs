using UnityEngine;

namespace ZooGame.Staff
{
    /// <summary>
    /// Staff tuning in one asset. Create via Assets > Create > ZooGame > Staff Config. Optional: defaults apply when absent.
    /// Times are GameClock seconds (they stop on pause and scale with 1x/2x/3x).
    /// </summary>
    [CreateAssetMenu(menuName = "ZooGame/Staff Config", fileName = "StaffConfig")]
    public sealed class StaffConfig : ScriptableObject
    {
        [Header("Hiring")]
        [Tooltip("Most staff members employed at once.")]
        [SerializeField, Min(1)] int maxStaff = 20;
        [SerializeField, Min(0)] int vendorHireCost = 200;
        [SerializeField, Min(0)] int keeperHireCost = 300;
        [SerializeField, Min(0)] int maintenanceHireCost = 250;
        [SerializeField, Min(0)] int veterinarianHireCost = 500;

        [Header("Movement")]
        [Tooltip("Walking speed in grid cells per simulation second.")]
        [SerializeField, Min(0.1f)] float walkSpeedCellsPerSecond = 2.5f;

        [Header("Tasks")]
        [Tooltip("Work time of a task posted without an explicit duration.")]
        [SerializeField, Min(0f)] float defaultTaskSeconds = 6f;

        public int MaxStaff => maxStaff;
        public float WalkSpeedCellsPerSecond => walkSpeedCellsPerSecond;
        public float DefaultTaskSeconds => defaultTaskSeconds;

        public int HireCost(StaffRole role)
        {
            switch (role)
            {
                case StaffRole.Vendor: return vendorHireCost;
                case StaffRole.Keeper: return keeperHireCost;
                case StaffRole.MaintenanceWorker: return maintenanceHireCost;
                case StaffRole.Veterinarian: return veterinarianHireCost;
                default: return 0;
            }
        }

        public static StaffConfig CreateDefault() => CreateInstance<StaffConfig>();
    }
}
