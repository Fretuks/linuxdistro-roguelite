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
        public const int EnemyUptimeGrowthPerWave = 1;

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
        public const int StatUpgradeMaxUptimePercent = 20;
        public const int StatUpgradeHealPercent = 30;
        public const int StatUpgradeRam = 1;
        public const int UpgradeMagnitudeBonus = 2;
        public const int FedoraBleedingEdgeBaseCrashChance = 5;
        public const int FedoraBleedingEdgeCrashChanceStep = 5;
        public const int FedoraBleedingEdgeMaxCrashChance = 90;
        public const int HitMagnitudeModeratePercent = 10;
        public const int HitMagnitudeMajorPercent = 25;
        public const int HitMagnitudeMassivePercent = 45;

        public static int ScaleStatUpgradeMaxUptime(int maxUptime)
        {
            return System.Math.Max(1, ((System.Math.Max(1, maxUptime) * StatUpgradeMaxUptimePercent) + 99) / 100);
        }

        public static int ScaleStatUpgradeHeal(int maxUptime)
        {
            return System.Math.Max(1, ((System.Math.Max(1, maxUptime) * StatUpgradeHealPercent) + 99) / 100);
        }

        // Combat pacing: gaps between phase transitions and per-item resolution steps so
        // players can follow drawing/queue/enemy resolution instead of seeing it all at once.
        // Skipped entirely when UIPreferences.ReducedMotion is set.
        public const float PhaseTransitionDelaySeconds = 0.35f;
        public const float CardDrawDelaySeconds = 0.12f;
        public const float QueueCardResolveDelaySeconds = 0.45f;
        public const float EnemyTelegraphDelaySeconds = 0.35f;
        public const float EnemyActionDelaySeconds = 0.45f;
    }

    public enum HitMagnitudeTier
    {
        Minor,
        Moderate,
        Major,
        Massive
    }

    public static class HitMagnitude
    {
        public static HitMagnitudeTier Classify(int damageAmount, int targetMaxUptime, bool targetDefeated)
        {
            int maxUptime = UnityEngine.Mathf.Max(1, targetMaxUptime);
            int amount = UnityEngine.Mathf.Max(0, damageAmount);
            int percent = UnityEngine.Mathf.FloorToInt((amount * 100f) / maxUptime);

            HitMagnitudeTier tier = percent >= CombatTuning.HitMagnitudeMassivePercent
                ? HitMagnitudeTier.Massive
                : percent >= CombatTuning.HitMagnitudeMajorPercent
                    ? HitMagnitudeTier.Major
                    : percent >= CombatTuning.HitMagnitudeModeratePercent
                        ? HitMagnitudeTier.Moderate
                        : HitMagnitudeTier.Minor;

            return targetDefeated && tier < HitMagnitudeTier.Major ? HitMagnitudeTier.Major : tier;
        }
    }
}
