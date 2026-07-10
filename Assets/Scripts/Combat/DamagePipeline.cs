using KernelPanic.Core;
using UnityEngine;

namespace KernelPanic.Combat
{
    public readonly struct DamageRequest
    {
        public DamageRequest(
            CombatantState source,
            CombatantState target,
            int amount,
            Language language,
            bool trueDamage,
            bool canCrit,
            bool applySourceModifiers = true)
        {
            Source = source;
            Target = target;
            Amount = amount;
            Language = language;
            TrueDamage = trueDamage;
            CanCrit = canCrit;
            ApplySourceModifiers = applySourceModifiers;
        }

        public CombatantState Source { get; }
        public CombatantState Target { get; }
        public int Amount { get; }
        public Language Language { get; }
        public bool TrueDamage { get; }
        public bool CanCrit { get; }
        public bool ApplySourceModifiers { get; }
    }

    public readonly struct DamageResult
    {
        public DamageResult(int finalAmount, bool targetDefeated, bool wasCritical = false, int absorbedAmount = 0, int shieldDamage = 0, int uptimeDamage = 0, int incomingAmount = 0)
        {
            FinalAmount = finalAmount;
            TargetDefeated = targetDefeated;
            WasCritical = wasCritical;
            AbsorbedAmount = absorbedAmount;
            ShieldDamage = shieldDamage;
            UptimeDamage = uptimeDamage;
            IncomingAmount = incomingAmount;
        }

        public int FinalAmount { get; }
        public bool TargetDefeated { get; }
        public bool WasCritical { get; }
        public int AbsorbedAmount { get; }
        public int ShieldDamage { get; }
        public int UptimeDamage { get; }
        public int IncomingAmount { get; }
    }

    /// <summary>
    /// Single damage chokepoint for shield, uptime, damage events, and defeat decisions.
    /// </summary>
    public sealed class DamagePipeline
    {
        public System.Func<DamageRequest, int, int> ResistanceResolver { get; set; }

        public DamageResult DealDamage(DamageRequest request)
        {
            if (request.Target == null || request.Target.IsDefeated)
            {
                return new DamageResult(0, false);
            }

            int amount = Mathf.Max(0, request.Amount);
            bool wasCritical = false;
            if (request.ApplySourceModifiers)
            {
                amount = ApplyFlatAdditions(amount, request);
                amount = ApplyMultipliers(amount, request);
                amount = ApplyCrit(amount, request, out wasCritical);
            }

            amount = ApplyResistancesAndWeaknesses(amount, request);

            int finalAmount = ApplyShieldAndUptime(amount, request, out int absorbedAmount, out int shieldDamage, out int uptimeDamage);
            ApplyArchRollingReleaseSave(request);
            GameEvents.RaiseDamageDealt(new DamageDealtEvent(request.Source, request.Target, finalAmount, request.Language, amount, absorbedAmount, wasCritical, shieldDamage, uptimeDamage, request.TrueDamage));

            bool defeated = false;
            if (request.Target.CurrentUptime <= 0 && !request.Target.IsDefeated)
            {
                request.Target.IsDefeated = true;
                defeated = true;
                GameEvents.RaiseCombatantDefeated(new CombatantDefeatedEvent(request.Target));
            }

            return new DamageResult(finalAmount, defeated, wasCritical, absorbedAmount, shieldDamage, uptimeDamage, amount);
        }

        private static int ApplyMultipliers(int amount, DamageRequest request)
        {
            if (amount == int.MaxValue)
            {
                return amount;
            }

            if (request.Source != null && request.Source.IgnoreDamageMultipliers)
            {
                // Mint V1+ ignores multiplicative damage modifiers. Mint V4 still allows flat
                // bonuses before this step; no flat buff status exists yet, so the flag is a hook.
                return amount;
            }

            int multiplier = request.Source == null ? 100 : Mathf.Max(0, request.Source.DamageMultiplierPercent);
            int currentCardMultiplier = request.Source == null ? 100 : Mathf.Max(0, request.Source.CurrentCardDamageMultiplierPercent);
            return Mathf.RoundToInt(amount * (multiplier / 100f) * (currentCardMultiplier / 100f));
        }

        private static int ApplyFlatAdditions(int amount, DamageRequest request)
        {
            if (amount == int.MaxValue)
            {
                return amount;
            }

            if (request.Source != null && request.Language == Language.JavaScript)
            {
                amount += Mathf.Max(0, request.Source.JavaScriptFlatDamageBonus);
            }

            if (request.Source != null && (request.Language == Language.C || request.Language == Language.Rust))
            {
                amount += Mathf.Max(0, request.Source.ArchBtwStacks) * Mathf.Max(1, request.Source.ArchBtwDamagePerStack);
            }

            return amount;
        }

        private static int ApplyCrit(int amount, DamageRequest request, out bool wasCritical)
        {
            wasCritical = false;
            if (!request.CanCrit || amount <= 0 || amount == int.MaxValue || request.Source != null && request.Source.ForceMaxRolls)
            {
                return amount;
            }

            int critChance = request.Language == Language.C ? 50 : 25;
            wasCritical = RandomRoll.RollRange(1, 100, new RollContext(request.Source)) <= critChance;
            return wasCritical ? Mathf.CeilToInt(amount * 1.5f) : amount;
        }

        private static void ApplyArchRollingReleaseSave(DamageRequest request)
        {
            CombatantState target = request.Target;
            if (target == null || target.CurrentUptime > 0 || target.ArchRollingReleaseSavesRemaining <= 0)
            {
                return;
            }

            target.ArchRollingReleaseSavesRemaining--;
            target.ArchRollingReleaseAvailableThisWave = target.ArchRollingReleaseSavesRemaining > 0;
            target.ArchBtwStacks = 0;
            target.CurrentUptime = 1;
            target.Shield += Mathf.Max(0, target.ArchRollingReleaseShieldOnSave);
            target.Cycles += Mathf.Max(0, target.ArchRollingReleaseCyclesOnSave);
            target.IsDefeated = false;
            target.ArchRollingReleaseRecoveredThisHit = true;
        }

        private int ApplyResistancesAndWeaknesses(int amount, DamageRequest request)
        {
            // TODO: Enemy resistances/reductions are not authored yet. Mint V5 should re-resolve
            // a once-per-combat reduced effect here once those reduction sources exist.
            // TODO: Apply enemy language resistances and weaknesses here.
            return ResistanceResolver == null ? amount : ResistanceResolver(request, amount);
        }

        private static int ApplyShieldAndUptime(int amount, DamageRequest request, out int absorbedAmount, out int shieldDamage, out int uptimeDamage)
        {
            CombatantState target = request.Target;
            int remaining = amount;
            shieldDamage = 0;

            if (!request.TrueDamage && target.Shield > 0)
            {
                shieldDamage = Mathf.Min(target.Shield, remaining);
                target.Shield -= shieldDamage;
                remaining -= shieldDamage;
            }

            uptimeDamage = Mathf.Min(target.CurrentUptime, remaining);
            target.CurrentUptime = Mathf.Max(0, target.CurrentUptime - uptimeDamage);
            absorbedAmount = Mathf.Max(0, amount - shieldDamage - uptimeDamage);
            return shieldDamage + uptimeDamage;
        }
    }
}
