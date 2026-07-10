using System;
using System.Collections.Generic;
using System.IO;
using KernelPanic.Data;
using UnityEngine;

namespace KernelPanic.Meta
{
    /// <summary>
    /// Provides save and load contracts for persistent meta progression.
    /// </summary>
    public sealed class SaveService
    {
        private const string SaveFileName = "kernel-panic-save.json";

        public string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public void Save(SaveData data)
        {
            data ??= new SaveData();
            data.EnsureLists();

            string directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(SavePath, json);
        }

        public SaveData Load()
        {
            if (!File.Exists(SavePath))
            {
                return SaveData.CreateDefault();
            }

            try
            {
                SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                data ??= SaveData.CreateDefault();
                data.EnsureLists();
                return data;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Save file could not be loaded; starting fresh. {ex.Message}");
                return SaveData.CreateDefault();
            }
        }
    }

    /// <summary>
    /// Stores persistent progression data for serialization.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        public bool starterChosen;
        public int entropyBalance;
        public int rootCredits = GachaService.TestRootCreditsBalance;
        public int commitsBalance;
        public int bandwidthBalance;
        // Legacy conflated balance. Older builds used this for both Commits and Bandwidth.
        // Migration preserves it as Bandwidth and starts Commits at 0 because the source is ambiguous.
        public int standardPullCurrency;
        // Legacy pull-token field kept only so older saves deserialize cleanly.
        public int limitedPullCurrency;
        public int cacheBalance;
        // Legacy per-pull package-dupe pool. Package dupes now auto-scrap directly to Cache.
        public int packageMerges;
        // Legacy global balance kept only so older saves deserialize cleanly.
        // New duplicate-pull merges live on OwnedUnitSaveEntry.merges.
        public int merges;
        public List<OwnedUnitSaveEntry> ownedUnits = new();
        public List<OwnedPackageSaveEntry> ownedPackages = new();
        public List<string> ownedPackageIds = new();
        public List<PackageLoadoutSaveEntry> packageLoadouts = new();
        public List<string> ownedUnitIds = new();
        public List<string> bannerPoolIds = new();
        public GachaBannerState beginnerBannerState = new(GachaService.BeginnerBannerId);
        public GachaBannerState standardBannerState = new(GachaService.StandardBannerId);
        public LastRunLoadoutSaveEntry lastRunLoadout = new();
        public string lastRunDistroId;
        public List<DistroBestWaveSaveEntry> distroBestWaves = new();

        // Populated by NormalizeOwnedPackages when a save contains more than one instance of the
        // same package id (packages are unique-per-type). Not serialized: the owner (bootstrap code
        // with access to PackageDatabase) consumes this once per load to grant Cache for the extras,
        // then clears it. See MainMenuController.LoadMetaState.
        [NonSerialized]
        public List<CollapsedPackageDuplicate> CollapsedPackageDuplicates = new();

        public static SaveData CreateDefault()
        {
            return new SaveData();
        }

        public void EnsureLists()
        {
            ownedUnits ??= new List<OwnedUnitSaveEntry>();
            ownedPackages ??= new List<OwnedPackageSaveEntry>();
            ownedPackageIds ??= new List<string>();
            packageLoadouts ??= new List<PackageLoadoutSaveEntry>();
            ownedUnitIds ??= new List<string>();
            bannerPoolIds ??= new List<string>();
            distroBestWaves ??= new List<DistroBestWaveSaveEntry>();
            beginnerBannerState ??= new GachaBannerState(GachaService.BeginnerBannerId);
            beginnerBannerState.bannerId = GachaService.BeginnerBannerId;
            beginnerBannerState.EnsureLists();
            standardBannerState ??= new GachaBannerState(GachaService.StandardBannerId);
            standardBannerState.bannerId = GachaService.StandardBannerId;
            standardBannerState.EnsureLists();
            lastRunLoadout ??= new LastRunLoadoutSaveEntry();
            lastRunLoadout.EnsureLists();
            if (string.IsNullOrWhiteSpace(lastRunDistroId) && !string.IsNullOrWhiteSpace(lastRunLoadout.distroId))
            {
                lastRunDistroId = lastRunLoadout.distroId;
            }

            MigrateLegacyConflatedCurrency();
            entropyBalance = Math.Max(0, entropyBalance);
            rootCredits = Math.Max(0, rootCredits);
            commitsBalance = Math.Max(0, commitsBalance);
            bandwidthBalance = Math.Max(0, bandwidthBalance);
            merges = Math.Max(0, merges);
            packageMerges = Math.Max(0, packageMerges);
            cacheBalance = Math.Max(0, cacheBalance);
            MigrateLegacyOwnedUnitIds();
            MigrateLegacyOwnedPackageIds();
            NormalizeOwnedUnits();
            NormalizeOwnedPackages();
            NormalizePackageLoadouts();
            NormalizeDistroBestWaves();
            MigrateLegacyGlobalMerges();
        }

        private void MigrateLegacyConflatedCurrency()
        {
            if (standardPullCurrency <= 0)
            {
                return;
            }

            if (bandwidthBalance <= 0)
            {
                bandwidthBalance = Math.Max(0, standardPullCurrency);
                Debug.Log("Migrated legacy standardPullCurrency to Bandwidth. Commits defaulted to 0 because the old field conflated pull tokens and upgrade currency.");
            }

            standardPullCurrency = 0;
            limitedPullCurrency = 0;
        }

        public OwnedUnitSaveEntry FindOwnedUnit(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            EnsureLists();
            for (int i = 0; i < ownedUnits.Count; i++)
            {
                OwnedUnitSaveEntry entry = ownedUnits[i];
                if (entry != null && string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        public bool IsUnitOwned(string id)
        {
            return FindOwnedUnit(id) != null;
        }

        public OwnedUnitSaveEntry AddOwnedUnit(string id, int version)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            EnsureLists();
            OwnedUnitSaveEntry existing = FindOwnedUnit(id);
            if (existing != null)
            {
                existing.version = Math.Max(1, Math.Min(GachaTuning.MaxVersion, existing.version));
                return existing;
            }

            OwnedUnitSaveEntry entry = new()
            {
                id = id,
                version = Math.Max(1, Math.Min(GachaTuning.MaxVersion, version))
            };
            ownedUnits.Add(entry);
            return entry;
        }

        private void MigrateLegacyOwnedUnitIds()
        {
            for (int i = 0; i < ownedUnitIds.Count; i++)
            {
                string id = ownedUnitIds[i];
                if (string.IsNullOrWhiteSpace(id) || HasOwnedUnitEntry(id))
                {
                    continue;
                }

                ownedUnits.Add(new OwnedUnitSaveEntry
                {
                    id = id,
                    version = 1
                });
            }
        }

        private void NormalizeOwnedUnits()
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            for (int i = ownedUnits.Count - 1; i >= 0; i--)
            {
                OwnedUnitSaveEntry entry = ownedUnits[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || !seen.Add(entry.id))
                {
                    ownedUnits.RemoveAt(i);
                    continue;
                }

                entry.version = Math.Max(1, Math.Min(GachaTuning.MaxVersion, entry.version));
                entry.merges = Math.Max(0, entry.merges);
            }
        }

        private void NormalizeOwnedPackages()
        {
            CollapsedPackageDuplicates.Clear();
            Dictionary<string, OwnedPackageSaveEntry> highestById = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> extraCountById = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ownedPackages.Count; i++)
            {
                OwnedPackageSaveEntry entry = ownedPackages[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                {
                    continue;
                }

                entry.upgradeLevel = Math.Max(0, Math.Min(PackageTuning.MaxPackageLevel, entry.upgradeLevel));
                if (highestById.TryGetValue(entry.id, out OwnedPackageSaveEntry existing))
                {
                    extraCountById[entry.id] = extraCountById.TryGetValue(entry.id, out int count) ? count + 1 : 1;
                    if (entry.upgradeLevel > existing.upgradeLevel)
                    {
                        highestById[entry.id] = entry;
                    }
                }
                else
                {
                    highestById[entry.id] = entry;
                }
            }

            ownedPackages = new List<OwnedPackageSaveEntry>(highestById.Values);
            foreach (KeyValuePair<string, int> extra in extraCountById)
            {
                CollapsedPackageDuplicates.Add(new CollapsedPackageDuplicate(extra.Key, extra.Value));
            }

            ownedPackageIds.Clear();
            for (int i = 0; i < ownedPackages.Count; i++)
            {
                ownedPackageIds.Add(ownedPackages[i].id);
            }
        }

        public int GetBestWave(string distroId)
        {
            if (string.IsNullOrWhiteSpace(distroId))
            {
                return 0;
            }

            EnsureLists();
            for (int i = 0; i < distroBestWaves.Count; i++)
            {
                DistroBestWaveSaveEntry entry = distroBestWaves[i];
                if (entry != null && string.Equals(entry.distroId, distroId, StringComparison.OrdinalIgnoreCase))
                {
                    return Math.Max(0, entry.bestWave);
                }
            }

            return 0;
        }

        public void RecordRunStats(string distroId, int waveReached)
        {
            if (string.IsNullOrWhiteSpace(distroId))
            {
                return;
            }

            EnsureLists();
            lastRunDistroId = distroId;
            lastRunLoadout ??= new LastRunLoadoutSaveEntry();
            lastRunLoadout.distroId = distroId;
            int safeWave = Math.Max(0, waveReached);
            for (int i = 0; i < distroBestWaves.Count; i++)
            {
                DistroBestWaveSaveEntry entry = distroBestWaves[i];
                if (entry != null && string.Equals(entry.distroId, distroId, StringComparison.OrdinalIgnoreCase))
                {
                    entry.bestWave = Math.Max(entry.bestWave, safeWave);
                    return;
                }
            }

            distroBestWaves.Add(new DistroBestWaveSaveEntry { distroId = distroId, bestWave = safeWave });
        }

        private void NormalizeDistroBestWaves()
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            for (int i = distroBestWaves.Count - 1; i >= 0; i--)
            {
                DistroBestWaveSaveEntry entry = distroBestWaves[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.distroId) || !seen.Add(entry.distroId))
                {
                    distroBestWaves.RemoveAt(i);
                    continue;
                }

                entry.bestWave = Math.Max(0, entry.bestWave);
            }
        }

        private void MigrateLegacyOwnedPackageIds()
        {
            for (int i = 0; i < ownedPackageIds.Count; i++)
            {
                string id = ownedPackageIds[i];
                if (string.IsNullOrWhiteSpace(id) || HasOwnedPackageEntry(id))
                {
                    continue;
                }

                ownedPackages.Add(new OwnedPackageSaveEntry { id = id, upgradeLevel = 0 });
            }
        }

        private bool HasOwnedPackageEntry(string id)
        {
            for (int i = 0; i < ownedPackages.Count; i++)
            {
                OwnedPackageSaveEntry entry = ownedPackages[i];
                if (entry != null && string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void NormalizePackageLoadouts()
        {
            HashSet<string> seenDistros = new(StringComparer.OrdinalIgnoreCase);
            for (int i = packageLoadouts.Count - 1; i >= 0; i--)
            {
                PackageLoadoutSaveEntry entry = packageLoadouts[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.distroId) || !seenDistros.Add(entry.distroId))
                {
                    packageLoadouts.RemoveAt(i);
                    continue;
                }

                entry.EnsureLists();
            }
        }

        private void MigrateLegacyGlobalMerges()
        {
            if (merges <= 0 || ownedUnits.Count == 0)
            {
                return;
            }

            bool hasSpecificMerges = false;
            for (int i = 0; i < ownedUnits.Count; i++)
            {
                if (ownedUnits[i] != null && ownedUnits[i].merges > 0)
                {
                    hasSpecificMerges = true;
                    break;
                }
            }

            if (!hasSpecificMerges)
            {
                ownedUnits[0].merges = Math.Max(0, ownedUnits[0].merges) + merges;
                merges = 0;
            }
        }

        private bool HasOwnedUnitEntry(string id)
        {
            for (int i = 0; i < ownedUnits.Count; i++)
            {
                OwnedUnitSaveEntry entry = ownedUnits[i];
                if (entry != null && string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    public sealed class OwnedUnitSaveEntry
    {
        public string id;
        public int version = 1;
        public int merges;
    }

    [Serializable]
    public sealed class OwnedPackageSaveEntry
    {
        public string id;
        public int upgradeLevel;
    }

    public sealed class CollapsedPackageDuplicate
    {
        public CollapsedPackageDuplicate(string id, int extraCount)
        {
            Id = id;
            ExtraCount = extraCount;
        }

        public string Id { get; }
        public int ExtraCount { get; }
    }

    [Serializable]
    public sealed class LastRunLoadoutSaveEntry
    {
        public string distroId;
        public List<string> cardIds = new();

        public void EnsureLists()
        {
            cardIds ??= new List<string>();
        }
    }

    [Serializable]
    public sealed class DistroBestWaveSaveEntry
    {
        public string distroId;
        public int bestWave;
    }

    [Serializable]
    public sealed class PackageLoadoutSaveEntry
    {
        public string distroId;
        public List<PackageLoadoutSlotSaveEntry> slots = new();

        public void EnsureLists()
        {
            slots ??= new List<PackageLoadoutSlotSaveEntry>();
            HashSet<PackageSlot> seen = new();
            for (int i = slots.Count - 1; i >= 0; i--)
            {
                PackageLoadoutSlotSaveEntry slot = slots[i];
                if (slot == null || string.IsNullOrWhiteSpace(slot.packageId) || !seen.Add(slot.slot))
                {
                    slots.RemoveAt(i);
                }
            }
        }
    }

    [Serializable]
    public sealed class PackageLoadoutSlotSaveEntry
    {
        public PackageSlot slot;
        public string packageId;
    }
}
