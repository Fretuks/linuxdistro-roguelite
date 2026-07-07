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
        private readonly List<DistroDefinition> _ownedUnits = new();
        private int _featuredIndex;

        // TODO: Populate owned units from SaveService during bootstrap.
        public IReadOnlyList<DistroDefinition> OwnedUnits => _ownedUnits;
        public DistroDefinition FeaturedUnit => _ownedUnits.Count == 0 ? null : _ownedUnits[Math.Clamp(_featuredIndex, 0, _ownedUnits.Count - 1)];

        public event Action Changed;

        public void Add(DistroDefinition unit)
        {
            if (unit == null)
            {
                return;
            }

            // Persistence is wired by the owner so this domain class stays IO-free.
            _ownedUnits.Add(unit);
            _featuredIndex = Math.Clamp(_featuredIndex, 0, _ownedUnits.Count - 1);
            Changed?.Invoke();
        }

        public int GetOwnedCount(string unitId)
        {
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _ownedUnits.Count; i++)
            {
                DistroDefinition unit = _ownedUnits[i];
                if (unit != null && string.Equals(unit.Id, unitId, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        public void SelectNextFeatured()
        {
            if (_ownedUnits.Count < 2)
            {
                return;
            }

            _featuredIndex = (_featuredIndex + 1) % _ownedUnits.Count;
        }
    }
}
