using System;
using System.Collections.Generic;
using UnityEngine;

namespace KernelPanic.Data
{
    public enum PackageSlot
    {
        Kernel,
        Runtime,
        Daemon
    }

    public enum PackageEffectKind
    {
        None = 0,
        MaxUptime = 1,
        MaxCycles = 2,
        WaveDraw = 3,
        WaveGenerateBasicCard = 4,
        FirstCardEachWaveCostReduction = 5,
        EveryNthTurnShield = 6,
        ExhaustShield = 7,
        EveryNthCardEachWaveFree = 8,
        FirstCardsEachTurnCostReduction = 9,
        ThirdCardEachTurnGenerate = 10,
        JavaScriptFlatDamage = 11,
        WaveThresholdRestore = 12,
        FirstShieldEachTurnBonus = 13,
        DnfFedoraPassive = 14,
        WaveStartShield = 15,
        FirstTurnEachWaveShield = 16,
        FirstTurnFirstWaveDraw = 17,
        FirstTurnEachWaveCycle = 18,
        MaxRam = 19,
        FirstNativeCardEachWaveFlatDamage = 20,
        FirstInterpreterQueueCardEachWaveShield = 21,
        EveryNthTurnDraw = 22,
        StartTurnNoDebuffShield = 23,
        EveryNthCardEachWaveCycle = 24
    }

    [Serializable]
    public struct PackageEffectData
    {
        [SerializeField] private PackageEffectKind kind;
        [SerializeField] private int amount;
        [SerializeField] private int threshold;
        [SerializeField] private bool refundCycle;
        [SerializeField] private bool cleanseDebuffs;
        [SerializeField] private bool enableFedoraSecondCardPassive;

        public PackageEffectData(PackageEffectKind kind, int amount, int threshold, bool refundCycle, bool cleanseDebuffs, bool enableFedoraSecondCardPassive)
        {
            this.kind = kind;
            this.amount = amount;
            this.threshold = threshold;
            this.refundCycle = refundCycle;
            this.cleanseDebuffs = cleanseDebuffs;
            this.enableFedoraSecondCardPassive = enableFedoraSecondCardPassive;
        }

        public PackageEffectKind Kind => kind;
        public int Amount => amount;
        public int Threshold => threshold;
        public bool RefundCycle => refundCycle;
        public bool CleanseDebuffs => cleanseDebuffs;
        public bool EnableFedoraSecondCardPassive => enableFedoraSecondCardPassive;
    }

    /// <summary>
    /// Defines persistent meta packages equipped into distro Kernel/Runtime/Daemon slots.
    /// Package loadouts are meta progression and carry into every run for that distro.
    /// Card loadouts remain run setup only.
    /// </summary>
    public sealed class PackageDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private PackageSlot slot;
        [SerializeField, Range(1, 5)] private int rarity = 1;
        [SerializeField, TextArea] private string description;
        [SerializeField] private string flavourText;
        [SerializeField, TextArea] private string designNotes;
        [SerializeField] private string intendedDistroId;
        [SerializeField] private List<string> requiresSystem = new();
        [SerializeField] private PackageEffectData offDistroEffect;
        [SerializeField] private PackageEffectData onDistroEffect;

        public string Id => id;
        public string DisplayName => displayName;
        public PackageSlot Slot => slot;
        public int Rarity => Mathf.Clamp(rarity, 1, 5);
        public string Description => description;
        public string FlavourText => flavourText;
        public string FlavorText => flavourText;
        public string DesignNotes => designNotes;
        public string IntendedDistroId => intendedDistroId;
        public IReadOnlyList<string> RequiresSystem => requiresSystem;
        public PackageEffectData OffDistroEffect => offDistroEffect;
        public PackageEffectData OnDistroEffect => onDistroEffect;

        public bool IsIntendedFor(string distroId)
        {
            return !string.IsNullOrWhiteSpace(intendedDistroId)
                && string.Equals(intendedDistroId, distroId, StringComparison.OrdinalIgnoreCase);
        }

        public PackageEffectData EffectFor(string distroId)
        {
            return IsIntendedFor(distroId) ? onDistroEffect : offDistroEffect;
        }
    }

    [Serializable]
    public sealed class PackageInstance
    {
        public PackageInstance(PackageDefinition definition, int upgradeLevel)
        {
            Definition = definition;
            UpgradeLevel = Mathf.Clamp(upgradeLevel, 0, KernelPanic.Meta.PackageTuning.MaxPackageLevel);
        }

        public PackageDefinition Definition { get; }
        public int UpgradeLevel { get; }

        public PackageEffectData EffectFor(string distroId)
        {
            PackageEffectData effect = Definition == null ? default : Definition.EffectFor(distroId);
            return PackageEffectScaling.Scale(effect, UpgradeLevel);
        }
    }

    public static class PackageEffectScaling
    {
        public static PackageEffectData Scale(PackageEffectData effect, int upgradeLevel)
        {
            int level = Mathf.Clamp(upgradeLevel, 0, KernelPanic.Meta.PackageTuning.MaxPackageLevel);
            if (level <= 0 || effect.Kind == PackageEffectKind.None)
            {
                return effect;
            }

            int amount = effect.Amount + GetAmountPerLevel(effect.Kind, effect.Amount) * level;
            return new PackageEffectData(effect.Kind, amount, effect.Threshold, effect.RefundCycle, effect.CleanseDebuffs, effect.EnableFedoraSecondCardPassive);
        }

        private static int GetAmountPerLevel(PackageEffectKind kind, int baseAmount)
        {
            return kind switch
            {
                PackageEffectKind.MaxUptime => Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, baseAmount) * 0.5f)),
                PackageEffectKind.MaxCycles => 1,
                PackageEffectKind.MaxRam => 1,
                PackageEffectKind.WaveDraw => 1,
                PackageEffectKind.FirstTurnFirstWaveDraw => 1,
                PackageEffectKind.FirstTurnEachWaveCycle => 1,
                PackageEffectKind.FirstCardsEachTurnCostReduction => 1,
                PackageEffectKind.FirstCardEachWaveCostReduction => 1,
                PackageEffectKind.FirstNativeCardEachWaveFlatDamage => 1,
                PackageEffectKind.JavaScriptFlatDamage => 1,
                _ => 1
            };
        }
    }
}
