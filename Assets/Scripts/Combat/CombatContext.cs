using System.Collections.Generic;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Carries runtime combat references needed by card effects during resolution.
    /// </summary>
    public sealed class CombatContext
    {
        public CombatContext(CombatantState source, IReadOnlyList<CombatantState> targets, CombatManager combatManager)
        {
            Source = source;
            Targets = targets;
            CombatManager = combatManager;
        }

        public CombatantState Source { get; }
        public IReadOnlyList<CombatantState> Targets { get; }
        public CombatManager CombatManager { get; }
    }
}
