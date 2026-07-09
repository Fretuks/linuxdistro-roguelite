using System;
using KernelPanic.Data;

namespace KernelPanic.Meta
{
    public enum PackageUpgradeFailureReason
    {
        None,
        NotOwned,
        MaxLevel,
        InsufficientCache,
        InsufficientBandwidth
    }

    public readonly struct PackageUpgradeResult
    {
        public PackageUpgradeResult(bool success, PackageUpgradeFailureReason failureReason, int cacheCost, int bandwidthCost, int newLevel)
        {
            Success = success;
            FailureReason = failureReason;
            CacheCost = cacheCost;
            BandwidthCost = bandwidthCost;
            NewLevel = newLevel;
        }

        public bool Success { get; }
        public PackageUpgradeFailureReason FailureReason { get; }
        public int CacheCost { get; }
        public int BandwidthCost { get; }
        public int NewLevel { get; }
    }

    public static class PackageUpgrader
    {
        public static event Action<OwnedPackageInstance> Upgraded;

        public static PackageUpgradeResult TryUpgrade(OwnedPackageInstance packageInstance, SaveData wallet)
        {
            if (packageInstance == null || wallet == null)
            {
                return new PackageUpgradeResult(false, PackageUpgradeFailureReason.NotOwned, 0, 0, 0);
            }

            int currentLevel = Math.Max(0, packageInstance.UpgradeLevel);
            if (currentLevel >= PackageTuning.MaxPackageLevel)
            {
                return new PackageUpgradeResult(false, PackageUpgradeFailureReason.MaxLevel, 0, 0, currentLevel);
            }

            int nextLevel = currentLevel + 1;
            PackageDefinition definition = packageInstance.Definition;
            int rarity = definition == null ? 1 : definition.Rarity;
            int cacheCost = PackageTuning.GetUpgradeCacheCost(nextLevel, rarity);
            int bandwidthCost = PackageTuning.GetUpgradeBandwidthCost(nextLevel, rarity);
            if (wallet.cacheBalance < cacheCost)
            {
                return new PackageUpgradeResult(false, PackageUpgradeFailureReason.InsufficientCache, cacheCost, bandwidthCost, currentLevel);
            }

            if (wallet.bandwidthBalance < bandwidthCost)
            {
                return new PackageUpgradeResult(false, PackageUpgradeFailureReason.InsufficientBandwidth, cacheCost, bandwidthCost, currentLevel);
            }

            wallet.cacheBalance -= cacheCost;
            wallet.bandwidthBalance -= bandwidthCost;
            packageInstance.SetUpgradeLevel(nextLevel);
            Upgraded?.Invoke(packageInstance);
            return new PackageUpgradeResult(true, PackageUpgradeFailureReason.None, cacheCost, bandwidthCost, nextLevel);
        }
    }
}
