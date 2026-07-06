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
        private int featuredIndex;

        // TODO: Populate owned units from SaveService during bootstrap.
        // TODO: Add newly unlocked units from GachaService rewards when pulls exist.
        public IReadOnlyList<DistroDefinition> OwnedUnits => ownedUnits;
        public DistroDefinition FeaturedUnit => ownedUnits.Count == 0 ? null : ownedUnits[Math.Clamp(featuredIndex, 0, ownedUnits.Count - 1)];

        public event Action Changed;

        public void Add(DistroDefinition unit)
        {
            if (unit == null || ownedUnits.Contains(unit))
            {
                return;
            }

            // Persistence is wired by the owner so this domain class stays IO-free.
            ownedUnits.Add(unit);
            featuredIndex = Math.Clamp(featuredIndex, 0, ownedUnits.Count - 1);
            Changed?.Invoke();
        }

        public void SelectNextFeatured()
        {
            if (ownedUnits.Count < 2)
            {
                return;
            }

            featuredIndex = (featuredIndex + 1) % ownedUnits.Count;
        }
    }
}
