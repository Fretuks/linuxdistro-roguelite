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
        public int rootCredits;
        public int standardPullCurrency;
        public int limitedPullCurrency;
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
            ownedUnitIds ??= new List<string>();
            bannerPoolIds ??= new List<string>();
            beginnerBannerState ??= new GachaBannerState(GachaService.BeginnerBannerId);
            beginnerBannerState.bannerId = GachaService.BeginnerBannerId;
            beginnerBannerState.EnsureLists();
            lastRunLoadout ??= new LastRunLoadoutSaveEntry();
            lastRunLoadout.EnsureLists();
        }
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
