using System;
using System.Collections.Generic;
using KernelPanic.Data;

namespace KernelPanic.Meta
{
    public enum PackageLoadoutFailureReason
    {
        None,
        WrongSlot,
        NotOwned,
        SlotOccupied,
        NotEquipped
    }

    /// <summary>
    /// Pure meta model for persistent package slots. Unlike CardLoadout, this is not
    /// run-only: equipped package ids are saved per distro and reused in every run.
    /// </summary>
    public sealed class PackageLoadout
    {
        private readonly PlayerCollection _collection;
        private readonly PackageDatabase _database;
        private readonly Dictionary<string, Dictionary<PackageSlot, string>> _equippedByDistro = new(StringComparer.OrdinalIgnoreCase);

        public PackageLoadout(PlayerCollection collection, PackageDatabase database)
        {
            _collection = collection;
            _database = database;
        }

        public IReadOnlyDictionary<PackageSlot, string> GetEquippedPackageIds(string distroId)
        {
            return !string.IsNullOrWhiteSpace(distroId) && _equippedByDistro.TryGetValue(distroId, out Dictionary<PackageSlot, string> equipped)
                ? equipped
                : Empty;
        }

        public PackageDefinition GetEquippedPackage(string distroId, PackageSlot slot)
        {
            if (string.IsNullOrWhiteSpace(distroId)
                || !_equippedByDistro.TryGetValue(distroId, out Dictionary<PackageSlot, string> equipped)
                || !equipped.TryGetValue(slot, out string packageId))
            {
                return null;
            }

            return _database == null ? null : _database.FindById(packageId);
        }

        public IReadOnlyList<PackageDefinition> GetEquippedPackages(string distroId)
        {
            List<PackageDefinition> packages = new();
            AddIfEquipped(distroId, PackageSlot.Kernel, packages);
            AddIfEquipped(distroId, PackageSlot.Runtime, packages);
            AddIfEquipped(distroId, PackageSlot.Daemon, packages);
            return packages;
        }

        public IReadOnlyList<PackageInstance> GetEquippedPackageInstances(string distroId)
        {
            List<PackageInstance> packages = new();
            AddInstanceIfEquipped(distroId, PackageSlot.Kernel, packages);
            AddInstanceIfEquipped(distroId, PackageSlot.Runtime, packages);
            AddInstanceIfEquipped(distroId, PackageSlot.Daemon, packages);
            return packages;
        }

        public bool IsEquipped(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            foreach (KeyValuePair<string, Dictionary<PackageSlot, string>> distro in _equippedByDistro)
            {
                foreach (KeyValuePair<PackageSlot, string> slot in distro.Value)
                {
                    if (string.Equals(slot.Value, packageId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool TryEquip(string distroId, PackageSlot slot, string packageId, out PackageLoadoutFailureReason reason)
        {
            PackageDefinition package = _database == null ? null : _database.FindById(packageId);
            if (package == null || _collection == null || !_collection.IsPackageOwned(package.Id))
            {
                reason = PackageLoadoutFailureReason.NotOwned;
                return false;
            }

            if (package.Slot != slot)
            {
                reason = PackageLoadoutFailureReason.WrongSlot;
                return false;
            }

            Dictionary<PackageSlot, string> equipped = EnsureDistro(distroId);
            if (equipped.TryGetValue(slot, out string current) && !string.Equals(current, package.Id, StringComparison.OrdinalIgnoreCase))
            {
                reason = PackageLoadoutFailureReason.SlotOccupied;
                return false;
            }

            equipped[slot] = package.Id;
            reason = PackageLoadoutFailureReason.None;
            return true;
        }

        public bool TryUnequip(string distroId, PackageSlot slot, out PackageLoadoutFailureReason reason)
        {
            if (string.IsNullOrWhiteSpace(distroId)
                || !_equippedByDistro.TryGetValue(distroId, out Dictionary<PackageSlot, string> equipped)
                || !equipped.Remove(slot))
            {
                reason = PackageLoadoutFailureReason.NotEquipped;
                return false;
            }

            reason = PackageLoadoutFailureReason.None;
            return true;
        }

        public void Load(string distroId, IEnumerable<PackageLoadoutSlotSaveEntry> slots)
        {
            if (string.IsNullOrWhiteSpace(distroId))
            {
                return;
            }

            Dictionary<PackageSlot, string> equipped = EnsureDistro(distroId);
            equipped.Clear();
            foreach (PackageLoadoutSlotSaveEntry slotEntry in slots ?? Array.Empty<PackageLoadoutSlotSaveEntry>())
            {
                PackageDefinition package = _database == null ? null : _database.FindById(slotEntry?.packageId);
                if (package == null || !_collection.IsPackageOwned(package.Id) || package.Slot != slotEntry.slot)
                {
                    continue;
                }

                equipped[package.Slot] = package.Id;
            }
        }

        public void Clear()
        {
            _equippedByDistro.Clear();
        }

        private void AddIfEquipped(string distroId, PackageSlot slot, List<PackageDefinition> packages)
        {
            PackageDefinition package = GetEquippedPackage(distroId, slot);
            if (package != null)
            {
                packages.Add(package);
            }
        }

        private void AddInstanceIfEquipped(string distroId, PackageSlot slot, List<PackageInstance> packages)
        {
            PackageDefinition definition = GetEquippedPackage(distroId, slot);
            OwnedPackageInstance owned = definition == null ? null : _collection.GetOwnedPackage(definition.Id);
            if (owned != null)
            {
                packages.Add(owned.ToRuntimeInstance());
            }
        }

        private Dictionary<PackageSlot, string> EnsureDistro(string distroId)
        {
            string key = string.IsNullOrWhiteSpace(distroId) ? string.Empty : distroId;
            if (!_equippedByDistro.TryGetValue(key, out Dictionary<PackageSlot, string> equipped))
            {
                equipped = new Dictionary<PackageSlot, string>();
                _equippedByDistro[key] = equipped;
            }

            return equipped;
        }

        private static readonly IReadOnlyDictionary<PackageSlot, string> Empty = new Dictionary<PackageSlot, string>();
    }
}
