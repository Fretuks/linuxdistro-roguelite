using System;
using System.Collections.Generic;
using KernelPanic.Core;
using UnityEngine;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Applies, ticks, cleanses, and expires status effects on a combatant.
    /// </summary>
    public sealed class StatusEffectController
    {
        private static readonly Dictionary<StatusType, StatusDescriptor> Descriptors = new()
        {
            [StatusType.MemoryLeak] = new(StatusType.MemoryLeak, "Memory Leak", "leak", false, StatusStackingRule.Intensity, StatusTickTiming.StartOfTurn, "Takes damage equal to stacks at start of turn."),
            [StatusType.Segfault] = new(StatusType.Segfault, "Segfault", "segv", false, StatusStackingRule.Unique, StatusTickTiming.StartOfTurn, "Loses one cycle this turn. TODO: lock a random card slot."),
            [StatusType.RaceCondition] = new(StatusType.RaceCondition, "Race Condition", "race", false, StatusStackingRule.Refresh, StatusTickTiming.None, "Randomizes multi-target resolution order where supported."),
            [StatusType.Deprecated] = new(StatusType.Deprecated, "Deprecated", "dep", false, StatusStackingRule.Duration, StatusTickTiming.StartOfTurn, "A temporary positive value decays. Currently reduces shield by 1."),
            [StatusType.DependencyError] = new(StatusType.DependencyError, "Dependency Error", "deps", false, StatusStackingRule.Unique, StatusTickTiming.None, "Registered for off-support card cost rules. TODO: connect draw-time cost path."),
            [StatusType.Deadlock] = new(StatusType.Deadlock, "Deadlock", "lock", false, StatusStackingRule.Duration, StatusTickTiming.None, "Registered goroutine freeze. TODO: connect goroutine token ticking."),
            [StatusType.UnattendedUpgrades] = new(StatusType.UnattendedUpgrades, "Unattended Upgrades", "apt", true, StatusStackingRule.Duration, StatusTickTiming.EndOfTurn, "Grants shield at end of turn, then expires."),
        };

        public static StatusDescriptor GetDescriptor(StatusType statusType)
        {
            return Descriptors[statusType];
        }

        public static bool Has(CombatantState target, StatusType statusType)
        {
            return target != null && target.HasStatus(statusType);
        }

        public static int StacksOf(CombatantState target, StatusType statusType)
        {
            return target == null ? 0 : target.StacksOf(statusType);
        }

        public void Apply(CombatantState target, StatusType statusType, int stacks, int duration, CombatantState source = null, bool skipNextTick = false)
        {
            if (target == null || target.IsDefeated)
            {
                return;
            }

            StatusDescriptor descriptor = GetDescriptor(statusType);
            StatusInstance existing = target.MutableStatuses.Find(status => status.Type == statusType);
            int safeStacks = Mathf.Max(1, stacks);
            int safeDuration = duration == -1 ? -1 : Mathf.Max(1, duration);

            if (existing == null)
            {
                target.MutableStatuses.Add(new StatusInstance(statusType, safeStacks, safeDuration, source, skipNextTick));
            }
            else
            {
                existing.Source = source ?? existing.Source;
                Merge(existing, descriptor.StackingRule, safeStacks, safeDuration, skipNextTick);
            }

            GameEvents.RaiseStatusApplied(new StatusAppliedEvent(source, target, statusType, safeStacks, safeDuration));
        }

        public void Tick(CombatantState target, StatusTickTiming timing, CombatantState source, DamagePipeline damagePipeline)
        {
            if (target == null || target.IsDefeated)
            {
                return;
            }

            List<StatusInstance> snapshot = new(target.MutableStatuses);
            for (int i = 0; i < snapshot.Count; i++)
            {
                StatusInstance status = snapshot[i];
                if (!target.MutableStatuses.Contains(status))
                {
                    continue;
                }

                StatusDescriptor descriptor = GetDescriptor(status.Type);
                if (descriptor.TickTiming != timing)
                {
                    continue;
                }

                if (status.SkipNextTick)
                {
                    status.SkipNextTick = false;
                    continue;
                }

                ResolveTick(status, target, status.Source ?? source, damagePipeline);
                DecayDuration(target, status);
            }
        }

        public int Cleanse(CombatantState target, Predicate<StatusInstance> predicate)
        {
            if (target == null || predicate == null)
            {
                return 0;
            }

            int removed = 0;
            for (int i = target.MutableStatuses.Count - 1; i >= 0; i--)
            {
                StatusInstance status = target.MutableStatuses[i];
                if (predicate(status))
                {
                    target.MutableStatuses.RemoveAt(i);
                    removed++;
                    GameEvents.RaiseStatusCleansed(new StatusCleansedEvent(target, status.Type));
                }
            }

            return removed;
        }

        public int CleanseHarmful(CombatantState target)
        {
            return Cleanse(target, status => !GetDescriptor(status.Type).IsBeneficial);
        }

        public void Expire(CombatantState target, StatusType statusType)
        {
            if (target == null)
            {
                return;
            }

            for (int i = target.MutableStatuses.Count - 1; i >= 0; i--)
            {
                if (target.MutableStatuses[i].Type == statusType)
                {
                    target.MutableStatuses.RemoveAt(i);
                    GameEvents.RaiseStatusExpired(new StatusExpiredEvent(target, statusType));
                }
            }
        }

        private static void Merge(StatusInstance existing, StatusStackingRule stackingRule, int stacks, int duration, bool skipNextTick)
        {
            switch (stackingRule)
            {
                case StatusStackingRule.Intensity:
                    existing.Stacks += stacks;
                    existing.Duration = MergeDuration(existing.Duration, duration);
                    break;
                case StatusStackingRule.Duration:
                    existing.Stacks = Mathf.Max(existing.Stacks, stacks);
                    existing.Duration = existing.Duration == -1 || duration == -1 ? -1 : existing.Duration + duration;
                    break;
                case StatusStackingRule.Refresh:
                    existing.Stacks = Mathf.Max(existing.Stacks, stacks);
                    existing.Duration = duration;
                    break;
                case StatusStackingRule.Unique:
                    break;
            }

            existing.SkipNextTick |= skipNextTick;
        }

        private static int MergeDuration(int current, int incoming)
        {
            if (current == -1 || incoming == -1)
            {
                return -1;
            }

            return Mathf.Max(current, incoming);
        }

        private static void ResolveTick(StatusInstance status, CombatantState target, CombatantState source, DamagePipeline damagePipeline)
        {
            switch (status.Type)
            {
                case StatusType.MemoryLeak:
                    damagePipeline.DealDamage(new DamageRequest(source, target, status.Stacks, Language.C, false, false));
                    break;
                case StatusType.Segfault:
                    target.Cycles = Mathf.Max(0, target.Cycles - 1);
                    break;
                case StatusType.Deprecated:
                    target.Shield = Mathf.Max(0, target.Shield - 1);
                    break;
                case StatusType.UnattendedUpgrades:
                    target.Shield += status.Stacks;
                    break;
                case StatusType.DependencyError:
                case StatusType.Deadlock:
                case StatusType.RaceCondition:
                    break;
            }
        }

        private void DecayDuration(CombatantState target, StatusInstance status)
        {
            if (status.Duration == -1)
            {
                return;
            }

            status.Duration--;
            if (status.Duration <= 0)
            {
                Expire(target, status.Type);
            }
        }
    }
}
