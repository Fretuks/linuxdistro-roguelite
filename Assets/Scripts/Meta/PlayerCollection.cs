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
        private readonly Dictionary<string, int> _versions = new(StringComparer.OrdinalIgnoreCase);
        private int _featuredIndex;

        // TODO: Populate owned units from SaveService during bootstrap.
        public IReadOnlyList<DistroDefinition> OwnedUnits => _ownedUnits;
        public DistroDefinition FeaturedUnit => _ownedUnits.Count == 0 ? null : _ownedUnits[Math.Clamp(_featuredIndex, 0, _ownedUnits.Count - 1)];

        public event Action Changed;

        public void Add(DistroDefinition unit)
        {
            Add(unit, 1);
        }

        public void Add(DistroDefinition unit, int version)
        {
            Add(unit, version, true);
        }

        public void AddSilently(DistroDefinition unit, int version)
        {
            Add(unit, version, false);
        }

        private void Add(DistroDefinition unit, int version, bool notify)
        {
            if (unit == null)
            {
                return;
            }

            if (IsOwned(unit.Id))
            {
                SetVersion(unit.Id, version, notify);
                return;
            }

            // Persistence is wired by the owner so this domain class stays IO-free.
            _ownedUnits.Add(unit);
            _versions[unit.Id] = Math.Max(1, version);
            _featuredIndex = Math.Clamp(_featuredIndex, 0, _ownedUnits.Count - 1);
            if (notify)
            {
                Changed?.Invoke();
            }
        }

        public bool IsOwned(string unitId)
        {
            return GetOwnedCount(unitId) > 0;
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

        public int GetVersion(string unitId)
        {
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return 0;
            }

            return _versions.TryGetValue(unitId, out int version) ? Math.Max(1, version) : IsOwned(unitId) ? 1 : 0;
        }

        public void SetVersion(string unitId, int version)
        {
            SetVersion(unitId, version, true);
        }

        public void SetVersionSilently(string unitId, int version)
        {
            SetVersion(unitId, version, false);
        }

        private void SetVersion(string unitId, int version, bool notify)
        {
            if (string.IsNullOrWhiteSpace(unitId) || !IsOwned(unitId))
            {
                return;
            }

            int safeVersion = Math.Max(1, version);
            if (_versions.TryGetValue(unitId, out int oldVersion) && oldVersion == safeVersion)
            {
                return;
            }

            _versions[unitId] = safeVersion;
            if (notify)
            {
                Changed?.Invoke();
            }
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
