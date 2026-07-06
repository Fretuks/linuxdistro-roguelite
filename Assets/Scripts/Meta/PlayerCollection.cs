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
        public IReadOnlyList<DistroDefinition> OwnedUnits => ownedUnits;
        public DistroDefinition FeaturedUnit => ownedUnits.Count == 0 ? null : ownedUnits[Math.Clamp(featuredIndex, 0, ownedUnits.Count - 1)];

        public event Action Changed;

        public void Add(DistroDefinition unit)
        {
            if (unit == null)
            {
                return;
            }

            // Persistence is wired by the owner so this domain class stays IO-free.
            ownedUnits.Add(unit);
            featuredIndex = Math.Clamp(featuredIndex, 0, ownedUnits.Count - 1);
            Changed?.Invoke();
        }

        public int GetOwnedCount(string unitId)
        {
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < ownedUnits.Count; i++)
            {
                DistroDefinition unit = ownedUnits[i];
                if (unit != null && string.Equals(unit.Id, unitId, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
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
