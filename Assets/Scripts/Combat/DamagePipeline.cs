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
            bool canCrit)
        {
            Source = source;
            Target = target;
            Amount = amount;
            Language = language;
            TrueDamage = trueDamage;
            CanCrit = canCrit;
        }

        public CombatantState Source { get; }
        public CombatantState Target { get; }
        public int Amount { get; }
        public Language Language { get; }
        public bool TrueDamage { get; }
        public bool CanCrit { get; }
    }

    public readonly struct DamageResult
    {
        public DamageResult(int finalAmount, bool targetDefeated)
        {
            FinalAmount = finalAmount;
            TargetDefeated = targetDefeated;
        }

        public int FinalAmount { get; }
        public bool TargetDefeated { get; }
    }

    /// <summary>
    /// Single damage chokepoint for shield, uptime, damage events, and defeat decisions.
    /// </summary>
    public sealed class DamagePipeline
    {
        public DamageResult DealDamage(DamageRequest request)
        {
            if (request.Target == null || request.Target.IsDefeated)
            {
                return new DamageResult(0, false);
            }

            int amount = Mathf.Max(0, request.Amount);
            amount = ApplyMultipliers(amount, request);
            amount = ApplyCrit(amount, request);
            amount = ApplyResistancesAndWeaknesses(amount, request);

            int finalAmount = ApplyShieldAndUptime(amount, request);
            GameEvents.RaiseDamageDealt(new DamageDealtEvent(request.Source, request.Target, finalAmount, request.Language));

            bool defeated = false;
            if (request.Target.CurrentUptime <= 0 && !request.Target.IsDefeated)
            {
                request.Target.IsDefeated = true;
                defeated = true;
                GameEvents.RaiseCombatantDefeated(new CombatantDefeatedEvent(request.Target));
            }

            return new DamageResult(finalAmount, defeated);
        }

        private static int ApplyMultipliers(int amount, DamageRequest request)
        {
            if (request.Source != null && request.Source.IgnoreDamageMultipliers)
            {
                // Mint V1+ ignores multiplicative damage modifiers. Mint V4 still allows flat
                // bonuses before this step; no flat buff status exists yet, so the flag is a hook.
                return amount;
            }

            int multiplier = request.Source == null ? 100 : Mathf.Max(0, request.Source.DamageMultiplierPercent);
            return Mathf.RoundToInt(amount * (multiplier / 100f));
        }

        private static int ApplyCrit(int amount, DamageRequest request)
        {
            if (request.Source != null && request.Source.forceMaxRolls)
            {
                return amount;
            }

            // TODO: Add crit calculation and crit modifiers.
            return amount;
        }

        private static int ApplyResistancesAndWeaknesses(int amount, DamageRequest request)
        {
            // TODO: Enemy resistances/reductions are not authored yet. Mint V5 should re-resolve
            // a once-per-combat reduced effect here once those reduction sources exist.
            // TODO: Apply enemy language resistances and weaknesses here.
            return amount;
        }

        private static int ApplyShieldAndUptime(int amount, DamageRequest request)
        {
            CombatantState target = request.Target;
            int remaining = amount;
            int shieldDamage = 0;

            if (!request.TrueDamage && target.Shield > 0)
            {
                shieldDamage = Mathf.Min(target.Shield, remaining);
                target.Shield -= shieldDamage;
                remaining -= shieldDamage;
            }

            int uptimeDamage = Mathf.Min(target.CurrentUptime, remaining);
            target.CurrentUptime = Mathf.Max(0, target.CurrentUptime - uptimeDamage);
            return shieldDamage + uptimeDamage;
        }
    }
}
