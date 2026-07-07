using System;
using UnityEngine;

namespace KernelPanic.Meta
{
    public static class VersionUpgrader
    {
        public static event Action<VersionUpgradeResult> UnitUpgraded;

        public static bool TryUpgrade(string unitId, SaveData state, PlayerCollection collection, out VersionUpgradeResult result)
        {
            result = TryUpgrade(unitId, state, collection);
            return result.Success;
        }

        public static VersionUpgradeResult TryUpgradeAndSave(string unitId, SaveData state, PlayerCollection collection, SaveService saveService)
        {
            VersionUpgradeResult result = TryUpgrade(unitId, state, collection);
            if (result.Success)
            {
                saveService?.Save(state);
            }

            return result;
        }

        public static VersionUpgradeResult TryUpgrade(string unitId, SaveData state, PlayerCollection collection)
        {
            state ??= SaveData.CreateDefault();
            state.EnsureLists();

            OwnedUnitSaveEntry entry = state.FindOwnedUnit(unitId);
            if (entry == null && collection != null && collection.IsOwned(unitId))
            {
                entry = state.AddOwnedUnit(unitId, collection.GetVersion(unitId));
            }

            if (entry == null)
            {
                return VersionUpgradeResult.Failed(unitId, VersionUpgradeFailureReason.NotOwned);
            }

            int currentVersion = Mathf.Clamp(entry.version, 1, GachaTuning.MaxVersion);
            if (currentVersion >= GachaTuning.MaxVersion)
            {
                return VersionUpgradeResult.Failed(unitId, VersionUpgradeFailureReason.MaxVersion, currentVersion, currentVersion, 0);
            }

            int targetVersion = currentVersion + 1;
            int cost = GachaTuning.GetVersionUpgradeCost(targetVersion);
            int merges = Math.Max(0, entry.merges);
            if (merges < cost)
            {
                return VersionUpgradeResult.Failed(unitId, VersionUpgradeFailureReason.InsufficientMerges, currentVersion, targetVersion, cost);
            }

            entry.merges = merges - cost;
            entry.version = targetVersion;
            collection?.SetVersionSilently(unitId, targetVersion);

            VersionUpgradeResult result = VersionUpgradeResult.Upgraded(unitId, currentVersion, targetVersion, cost, entry.merges);
            UnitUpgraded?.Invoke(result);
            return result;
        }
    }

    public readonly struct VersionUpgradeResult
    {
        private VersionUpgradeResult(string unitId, bool success, VersionUpgradeFailureReason failureReason, int fromVersion, int toVersion, int cost, int mergesRemaining)
        {
            UnitId = unitId;
            Success = success;
            FailureReason = failureReason;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            Cost = cost;
            MergesRemaining = mergesRemaining;
        }

        public string UnitId { get; }
        public bool Success { get; }
        public VersionUpgradeFailureReason FailureReason { get; }
        public int FromVersion { get; }
        public int ToVersion { get; }
        public int Cost { get; }
        public int MergesRemaining { get; }

        public static VersionUpgradeResult Upgraded(string unitId, int fromVersion, int toVersion, int cost, int mergesRemaining)
        {
            return new VersionUpgradeResult(unitId, true, VersionUpgradeFailureReason.None, fromVersion, toVersion, cost, mergesRemaining);
        }

        public static VersionUpgradeResult Failed(string unitId, VersionUpgradeFailureReason reason, int fromVersion = 0, int toVersion = 0, int cost = 0)
        {
            return new VersionUpgradeResult(unitId, false, reason, fromVersion, toVersion, cost, 0);
        }
    }

    public enum VersionUpgradeFailureReason
    {
        None,
        NotOwned,
        MaxVersion,
        InsufficientMerges
    }
}
