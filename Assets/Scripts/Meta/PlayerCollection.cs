using System.Collections.Generic;
using KernelPanic.Data;
using System;

namespace KernelPanic.Meta
{
    /// <summary>
    /// Tracks player-owned persistent unlocks for menu presentation and collection views.
    /// </summary>
    public sealed class PlayerCollection
    {
        private readonly List<DistroDefinition> ownedUnits = new();

        // TODO: Populate owned units from SaveService during bootstrap.
        // TODO: Add newly unlocked units from GachaService rewards when pulls exist.
        public IReadOnlyList<DistroDefinition> OwnedUnits => ownedUnits;

        public event Action Changed;

        public void Add(DistroDefinition unit)
        {
            if (unit == null || ownedUnits.Contains(unit))
            {
                return;
            }

            // Persistence is wired by the owner so this domain class stays IO-free.
            ownedUnits.Add(unit);
            Changed?.Invoke();
        }
    }
}
