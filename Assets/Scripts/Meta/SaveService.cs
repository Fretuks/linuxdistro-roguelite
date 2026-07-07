using System;
using System.Collections.Generic;
using System.IO;
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
        public int standardPullCurrency;
        public int limitedPullCurrency;
        // Legacy global balance kept only so older saves deserialize cleanly.
        // New duplicate-pull merges live on OwnedUnitSaveEntry.merges.
        public int merges;
        public List<OwnedUnitSaveEntry> ownedUnits = new();
        public List<string> ownedUnitIds = new();
        public List<string> bannerPoolIds = new();
        public GachaBannerState beginnerBannerState = new(GachaService.BeginnerBannerId);
        public LastRunLoadoutSaveEntry lastRunLoadout = new();

        public static SaveData CreateDefault()
        {
            return new SaveData();
        }

        public void EnsureLists()
        {
            ownedUnits ??= new List<OwnedUnitSaveEntry>();
            ownedUnitIds ??= new List<string>();
            bannerPoolIds ??= new List<string>();
            beginnerBannerState ??= new GachaBannerState(GachaService.BeginnerBannerId);
            beginnerBannerState.bannerId = GachaService.BeginnerBannerId;
            beginnerBannerState.EnsureLists();
            lastRunLoadout ??= new LastRunLoadoutSaveEntry();
            lastRunLoadout.EnsureLists();
            merges = Math.Max(0, merges);
            MigrateLegacyOwnedUnitIds();
            NormalizeOwnedUnits();
            MigrateLegacyGlobalMerges();
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
    public sealed class LastRunLoadoutSaveEntry
    {
        public string distroId;
        public List<string> cardIds = new();

        public void EnsureLists()
        {
            cardIds ??= new List<string>();
        }
    }
}
