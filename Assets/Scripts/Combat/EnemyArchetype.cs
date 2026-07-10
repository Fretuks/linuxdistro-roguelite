using System;
using System.Collections.Generic;
using KernelPanic.Core;
using UnityEngine;

namespace KernelPanic.Combat
{
    [Flags]
    public enum EnemyBehaviorFlags
    {
        None = 0,
        Revive = 1 << 0,
        Split = 1 << 1,
        Grow = 1 << 2,
        ObfuscateIntent = 1 << 3,
        DefendAllies = 1 << 4,
        Countdown = 1 << 5,
        LeavesOrphan = 1 << 6,
        SegfaultOnDeath = 1 << 7,
        RacePair = 1 << 8,
        RootkitMasked = 1 << 9,
        Elite = 1 << 10,
        RamPressure = 1 << 11,
        TelemetryCollector = 1 << 12,
        CardLocker = 1 << 13
    }

    public enum EnemyTier
    {
        Fodder,
        Elite
    }

    public sealed class EnemyArchetypeDescriptor
    {
        public EnemyArchetypeDescriptor(
            string id,
            string displayName,
            int baseUptimeMin,
            int baseUptimeMax,
            EnemyBehaviorFlags behaviorFlags,
            IReadOnlyList<EnemyIntent> intentPool,
            EnemyTier tier = EnemyTier.Fodder)
        {
            Id = string.IsNullOrWhiteSpace(id) ? "unknown_process" : id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            BaseUptimeMin = Mathf.Max(1, baseUptimeMin);
            BaseUptimeMax = Mathf.Max(BaseUptimeMin, baseUptimeMax);
            BehaviorFlags = behaviorFlags;
            IntentPool = intentPool ?? Array.Empty<EnemyIntent>();
            Tier = tier;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public int BaseUptimeMin { get; }
        public int BaseUptimeMax { get; }
        public EnemyBehaviorFlags BehaviorFlags { get; }
        public IReadOnlyList<EnemyIntent> IntentPool { get; }
        public EnemyTier Tier { get; }
    }

    /// <summary>
    /// Runtime archetype construction site. Future EnemyDefinition assets should replace this table
    /// by providing the same descriptor data: id, display name, base Uptime range, intents, and flags.
    /// </summary>
    public static class EnemyArchetypeCatalog
    {
        public const int ZombieReapThresholdPercent = 30;
        public const int MemoryLeakAttackGrowthPerTurn = 1;
        public const int MemoryLeakAttackCapMin = 4;
        public const int MemoryLeakAttackCapMax = 5;
        public const int ForkBombTotalCap = 5;
        public const int RootkitMaskedDamagePercent = 10;
        public const int RacePairDamageBonus = 2;
        public const int KernelPanicRamPenalty = 2;
        public const int TelemetryDamageGrowthPerCard = 1;
        public const int DrmUnlockCycleCost = 1;
        public const int CronInitialCountdown = 2;
        public const int SegfaultInitialCountdown = 2;

        private static readonly EnemyArchetypeDescriptor ZombieProcess = new(
            "zombie_process",
            "Zombie Process",
            6,
            10,
            EnemyBehaviorFlags.Revive | EnemyBehaviorFlags.LeavesOrphan,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.Attack, 2, 3, Language.C, "attack", "!")
            });

        private static readonly EnemyArchetypeDescriptor MemoryLeak = new(
            "memory_leak",
            "Memory Leak",
            4,
            7,
            EnemyBehaviorFlags.Grow,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.StatusAttack, 1, 2, Language.C, "leak", "!", false, StatusType.MemoryLeak, 1, CombatTuning.EnemyStatusDuration)
            });

        private static readonly EnemyArchetypeDescriptor ForkBomb = new(
            "fork_bomb",
            "Fork Bomb",
            3,
            5,
            EnemyBehaviorFlags.Split | EnemyBehaviorFlags.LeavesOrphan,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.Attack, 1, 2, Language.C, "fork hit", "!")
            });

        private static readonly EnemyArchetypeDescriptor Daemon = new(
            "daemon",
            "Daemon",
            10,
            14,
            EnemyBehaviorFlags.ObfuscateIntent | EnemyBehaviorFlags.Elite,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.Attack, 6, 8, Language.C, "daemon hit", "!")
            },
            EnemyTier.Elite);

        private static readonly EnemyArchetypeDescriptor Defender = new(
            "firewalld",
            "firewalld",
            5,
            8,
            EnemyBehaviorFlags.DefendAllies,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.Defend, 3, 5, Language.C, "shield adjacent", "#"),
                new EnemyIntent(EnemyIntentKind.Defend, 3, 5, Language.C, "shield adjacent", "#"),
                new EnemyIntent(EnemyIntentKind.Attack, 0, 1, Language.C, "poke", "!")
            });

        private static readonly EnemyArchetypeDescriptor CronJob = new(
            "cron_job",
            "Cron Job",
            6,
            9,
            EnemyBehaviorFlags.Countdown,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.Attack, 10, 12, Language.C, "cron fire", "@")
            });

        private static readonly EnemyArchetypeDescriptor OrphanProcess = new(
            "orphan_process",
            "Orphan Process",
            2,
            4,
            EnemyBehaviorFlags.None,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.Attack, 1, 2, Language.C, "orphan chip", ".")
            });

        private static readonly EnemyArchetypeDescriptor Segfault = new(
            "segfault",
            "Segfault",
            2,
            4,
            EnemyBehaviorFlags.SegfaultOnDeath,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.Attack, 2, 3, Language.C, "unstable hit", "!")
            });

        private static readonly EnemyArchetypeDescriptor RaceCondition = new(
            "race_condition",
            "Race Condition",
            5,
            7,
            EnemyBehaviorFlags.RacePair,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.Attack, 2, 3, Language.C, "racy strike", "~")
            });

        private static readonly EnemyArchetypeDescriptor Rootkit = new(
            "rootkit",
            "Rootkit",
            14,
            18,
            EnemyBehaviorFlags.RootkitMasked,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.Attack, 1, 2, Language.C, "masked hit", "?")
            });

        private static readonly EnemyArchetypeDescriptor KernelPanic = new(
            "kernel_panic",
            "Kernel Panic",
            25,
            30,
            EnemyBehaviorFlags.Elite | EnemyBehaviorFlags.RamPressure,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.Attack, 5, 7, Language.C, "panic hit", "!")
            },
            EnemyTier.Elite);

        private static readonly EnemyArchetypeDescriptor TelemetryCollector = new(
            "telemetry_collector",
            "Telemetry Collector",
            20,
            25,
            EnemyBehaviorFlags.Elite | EnemyBehaviorFlags.TelemetryCollector,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.Attack, 2, 3, Language.C, "profile hit", "!")
            },
            EnemyTier.Elite);

        private static readonly EnemyArchetypeDescriptor DrmGuardian = new(
            "drm_guardian",
            "DRM Guardian",
            25,
            30,
            EnemyBehaviorFlags.Elite | EnemyBehaviorFlags.CardLocker,
            new[]
            {
                new EnemyIntent(EnemyIntentKind.Special, 4, 6, Language.C, "license check", "$")
            },
            EnemyTier.Elite);

        public static EnemyArchetypeDescriptor Get(string id)
        {
            return id switch
            {
                "memory_leak" => MemoryLeak,
                "fork_bomb" => ForkBomb,
                "daemon" => Daemon,
                "firewalld" => Defender,
                "cron_job" => CronJob,
                "orphan_process" => OrphanProcess,
                "segfault" => Segfault,
                "race_condition" => RaceCondition,
                "rootkit" => Rootkit,
                "kernel_panic" => KernelPanic,
                "telemetry_collector" => TelemetryCollector,
                "drm_guardian" => DrmGuardian,
                _ => ZombieProcess
            };
        }
    }
}
