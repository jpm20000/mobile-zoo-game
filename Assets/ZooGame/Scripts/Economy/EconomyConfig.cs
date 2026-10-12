using UnityEngine;

namespace ZooGame.Economy
{
    /// <summary>Economy tuning asset. Create via Assets > Create > ZooGame > Economy Config. Optional: defaults apply when absent.</summary>
    [CreateAssetMenu(menuName = "ZooGame/Economy Config", fileName = "EconomyConfig")]
    public sealed class EconomyConfig : ScriptableObject
    {
        [Header("Starting balances")]
        [SerializeField, Min(0)] int startingMoney = 25000;
        [SerializeField, Min(0)] int startingConservationTokens;

        [Header("Visitor revenue (Money per successful interaction)")]
        [SerializeField, Min(0)] int entranceTicketPrice = 10;
        [SerializeField, Min(0)] int foodPrice = 6;
        [SerializeField, Min(0)] int drinkPrice = 4;

        [Header("Demolition")]
        [Tooltip("Share of the original purchase cost returned in Money when something is demolished (floor applied).")]
        [SerializeField, Range(0f, 1f)] float refundRate = 0.5f;

        [Header("History")]
        [Tooltip("Most recent transactions kept in memory; older ones are dropped.")]
        [SerializeField, Range(EconomySettings.MinHistory, EconomySettings.MaxHistory)] int transactionHistoryCapacity = 100;

        public EconomySettings ToSettings() => new EconomySettings(startingMoney, startingConservationTokens,
            entranceTicketPrice, foodPrice, drinkPrice, refundRate, transactionHistoryCapacity);

        public static EconomyConfig CreateDefault() => CreateInstance<EconomyConfig>();
    }
}
