namespace KernelPanic.Meta
{
    /// <summary>
    /// Tunable gacha progression amounts. Kept in code until designers need asset-driven economy tuning.
    /// </summary>
    public static class GachaTuning
    {
        public const int MaxVersion = 5;
        public const int MergesPerDupe = 50;
        public const int MergesOnCharacterBonus = 25;
        public const float MergesMaxVersionOverflowMultiplier = 1.5f;
        public static int DupeConsolationBandwidth => 0;
        public static readonly int[] VersionCosts = { 50, 120, 250, 500 };

        public static int GetVersionUpgradeCost(int targetVersion)
        {
            if (targetVersion <= 1 || targetVersion > MaxVersion)
            {
                return 0;
            }

            int index = targetVersion - 2;
            return index >= 0 && index < VersionCosts.Length ? VersionCosts[index] : VersionCosts[VersionCosts.Length - 1];
        }
    }
}
