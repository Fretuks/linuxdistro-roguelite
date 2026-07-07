namespace KernelPanic.Combat
{
    /// <summary>
    /// Central combat economy/scaffold tuning. Kept in code while the combat scaffold is still volatile;
    /// promote to a ScriptableObject once designers need per-run or per-encounter tuning without recompiles.
    /// </summary>
    public static class CombatTuning
    {
        public const int OpeningHandSize = 5;
        public const int DrawPerTurn = 1;
        public const int MinimumHandFloor = 0;

        public const int BaseEnemyUptimeMin = 5;
        public const int BaseEnemyUptimeMax = 10;
        public const int EnemyUptimeGrowthPerWave = 2;

        public const int BaseEnemyAttack = 3;
        public const int EnemyAttackGrowthEveryWaves = 2;
        public const int EnemySlotAttackVariance = 1;
        public const int EnemyStatusAttackDamage = 1;
        public const int EnemyStatusDuration = 3;

        public const int BaseEnemiesPerWave = 2;
        public const int AdditionalEnemiesPerWave = 1;
        public const int MaxEnemiesPerWave = 5;

        public const int BitsPerKill = 1;
        public const int ShopSize = 5;
        public const int RerollBaseCost = 1;
        public const int RerollCostStep = 1;
        public const int RemoveCardCost = 1;
        public const int NewCardOfferCost = 2;
        public const int UpgradeOfferCost = 2;
        public const int StatUpgradeCost = 3;
        public const int BandwidthBase = 10;
        public const int BandwidthPerWaveStep = 5;
        public const int EntropyStartWave = 5;
        public const int EntropyPerWave = 3;
        public const int StatUpgradeMaxCycles = 1;
        public const int StatUpgradeMaxUptime = 4;
        public const int StatUpgradeHeal = 5;
        public const int StatUpgradeRam = 1;
        public const int UpgradeMagnitudeBonus = 2;
    }
}
