using System;
using KernelPanic.Data;

namespace KernelPanic.Meta
{
    public enum PackageScrapFailureReason
    {
        None,
        NotOwned,
        Equipped
    }

    public readonly struct PackageScrapResult
    {
        public PackageScrapResult(bool success, PackageScrapFailureReason failureReason, int cacheGranted, int bandwidthRefunded)
        {
            Success = success;
            FailureReason = failureReason;
            CacheGranted = cacheGranted;
            BandwidthRefunded = bandwidthRefunded;
        }

        public bool Success { get; }
        public PackageScrapFailureReason FailureReason { get; }
        public int CacheGranted { get; }
        public int BandwidthRefunded { get; }
    }

    public static class PackageScrapper
    {
        public static PackageScrapResult Scrap(PlayerCollection collection, PackageLoadout loadout, SaveData wallet, string packageId)
        {
            if (collection == null || wallet == null || string.IsNullOrWhiteSpace(packageId))
            {
                return new PackageScrapResult(false, PackageScrapFailureReason.NotOwned, 0, 0);
            }

            OwnedPackageInstance package = collection.GetOwnedPackage(packageId);
            if (package == null)
            {
                return new PackageScrapResult(false, PackageScrapFailureReason.NotOwned, 0, 0);
            }

            if (loadout != null && loadout.IsEquipped(packageId))
            {
                return new PackageScrapResult(false, PackageScrapFailureReason.Equipped, 0, 0);
            }

            PackageDefinition definition = package.Definition;
            int rarity = definition == null ? 1 : definition.Rarity;
            int cache = PackageTuning.GetCacheForRarity(rarity) + PackageTuning.GetRefundedInvestedCache(package.UpgradeLevel, rarity);
            int bandwidth = PackageTuning.GetRefundedInvestedBandwidth(package.UpgradeLevel, rarity);

            if (!collection.RemovePackage(packageId))
            {
                return new PackageScrapResult(false, PackageScrapFailureReason.NotOwned, 0, 0);
            }

            wallet.cacheBalance += Math.Max(0, cache);
            wallet.bandwidthBalance += Math.Max(0, bandwidth);
            return new PackageScrapResult(true, PackageScrapFailureReason.None, cache, bandwidth);
        }
    }
}
