using System.Collections.Generic;
using KernelPanic.Core;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Stores mutable combat stats and statuses for a player or enemy.
    /// </summary>
    public sealed class CombatantState
    {
        private readonly Dictionary<StatusType, int> activeStatuses = new();

        public CombatantState(int maxUptime, int ram, int cycles)
        {
            MaxUptime = maxUptime;
            CurrentUptime = maxUptime;
            Ram = ram;
            Cycles = cycles;
        }

        public int CurrentUptime { get; set; }
        public int MaxUptime { get; set; }
        public int Shield { get; set; }
        public int Ram { get; set; }
        public int Cycles { get; set; }
        public IReadOnlyDictionary<StatusType, int> ActiveStatuses => activeStatuses;
    }
}
