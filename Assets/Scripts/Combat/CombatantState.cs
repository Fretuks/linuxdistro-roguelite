using System.Collections.Generic;
using KernelPanic.Core;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Stores mutable combat stats and statuses for a player or enemy.
    /// </summary>
    public sealed class CombatantState
    {
        private readonly List<StatusInstance> _statuses = new();

        public CombatantState(int maxUptime, int ram, int cycles)
        {
            MaxUptime = maxUptime;
            CurrentUptime = maxUptime;
            Ram = ram;
            Cycles = cycles;
            MaxCycles = cycles;
        }

        public int CurrentUptime { get; set; }
        public int MaxUptime { get; set; }
        public int Shield { get; set; }
        public int Ram { get; set; }
        public int Cycles { get; set; }
        public int MaxCycles { get; set; }
        public bool IsDefeated { get; set; }
        public bool forceMaxRolls { get; set; }
        public bool IgnoreDamageMultipliers { get; set; }
        public bool AllowFlatDamageBuffs { get; set; }
        public int FlatEffectBonus { get; set; }
        public int JavaScriptFlatDamageBonus { get; set; }
        public int DamageMultiplierPercent { get; set; } = 100;
        public int CurrentCardDamageMultiplierPercent { get; set; } = 100;
        public int IncomingAttackHalfCharges { get; set; }
        public int ArchBtwStacks { get; set; }
        public int ArchBtwDamagePerStack { get; set; } = 1;
        public int ArchMakepkgBtwMultiplier { get; set; } = 2;
        public int ArchRollingReleaseSavesRemaining { get; set; }
        public int ArchRollingReleaseShieldOnSave { get; set; }
        public int ArchRollingReleaseCyclesOnSave { get; set; }
        public bool ArchRollingReleaseAvailableThisWave { get; set; }
        public bool ArchRollingReleaseRecoveredThisHit { get; set; }
        public bool HasLastPlayedCardLanguage { get; set; }
        public Language LastPlayedCardLanguage { get; set; }
        public bool HasPreviousPlayedCardLanguage { get; set; }
        public Language PreviousPlayedCardLanguage { get; set; }
        public IReadOnlyList<StatusInstance> Statuses => _statuses;
        public IReadOnlyDictionary<StatusType, int> ActiveStatuses => BuildStatusSnapshot();

        internal List<StatusInstance> MutableStatuses => _statuses;

        public bool HasStatus(StatusType type)
        {
            return _statuses.Exists(status => status.Type == type);
        }

        public int StacksOf(StatusType type)
        {
            StatusInstance status = _statuses.Find(instance => instance.Type == type);
            return status == null ? 0 : status.Stacks;
        }

        private IReadOnlyDictionary<StatusType, int> BuildStatusSnapshot()
        {
            Dictionary<StatusType, int> snapshot = new();
            for (int i = 0; i < _statuses.Count; i++)
            {
                snapshot[_statuses[i].Type] = _statuses[i].Stacks;
            }

            return snapshot;
        }
    }
}
