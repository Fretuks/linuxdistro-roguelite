using System;
using UnityEngine;

namespace KernelPanic.Meta
{
    public static class PackageTuning
    {
        public const int MaxPackageLevel = 5;
        public const float RefundFraction = 1f;
        public const float RarityCostMultiplier = 1f;

        private static readonly int[] CachePerRarityValues = { 5, 12, 30, 80, 200 };
        private static readonly int[] UpgradeCacheCostValues = { 10, 25, 50, 90, 150 };
        private static readonly int[] UpgradeBandwidthCostValues = { 20, 50, 100, 180, 300 };

        public static int GetCacheForRarity(int rarity)
        {
            return CachePerRarityValues[Mathf.Clamp(rarity, 1, CachePerRarityValues.Length) - 1];
        }

        public static int GetUpgradeCacheCost(int nextLevel, int rarity)
        {
            return ApplyRarityMultiplier(UpgradeCacheCostValues[Mathf.Clamp(nextLevel, 1, UpgradeCacheCostValues.Length) - 1], rarity);
        }

        public static int GetUpgradeBandwidthCost(int nextLevel, int rarity)
        {
            return ApplyRarityMultiplier(UpgradeBandwidthCostValues[Mathf.Clamp(nextLevel, 1, UpgradeBandwidthCostValues.Length) - 1], rarity);
        }

        public static int GetInvestedCache(int level, int rarity)
        {
            int total = 0;
            for (int nextLevel = 1; nextLevel <= Mathf.Clamp(level, 0, MaxPackageLevel); nextLevel++)
            {
                total += GetUpgradeCacheCost(nextLevel, rarity);
            }

            return total;
        }

        public static int GetInvestedBandwidth(int level, int rarity)
        {
            int total = 0;
            for (int nextLevel = 1; nextLevel <= Mathf.Clamp(level, 0, MaxPackageLevel); nextLevel++)
            {
                total += GetUpgradeBandwidthCost(nextLevel, rarity);
            }

            return total;
        }

        public static int GetRefundedInvestedCache(int level, int rarity)
        {
            return Mathf.RoundToInt(GetInvestedCache(level, rarity) * RefundFraction);
        }

        public static int GetRefundedInvestedBandwidth(int level, int rarity)
        {
            return Mathf.RoundToInt(GetInvestedBandwidth(level, rarity) * RefundFraction);
        }

        private static int ApplyRarityMultiplier(int baseCost, int rarity)
        {
            float multiplier = RarityCostMultiplier <= 0f ? 1f : RarityCostMultiplier;
            return Math.Max(0, Mathf.RoundToInt(baseCost * multiplier));
        }
    }
}
