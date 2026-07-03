using System;
using KernelPanic.Core;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Applies, ticks, and expires status effects on a combatant.
    /// </summary>
    public sealed class StatusEffectController
    {
        public void Apply(CombatantState target, StatusType statusType, int stacks)
        {
            throw new NotImplementedException();
        }

        public void Tick(CombatantState target)
        {
            throw new NotImplementedException();
        }

        public void Expire(CombatantState target, StatusType statusType)
        {
            throw new NotImplementedException();
        }
    }
}
