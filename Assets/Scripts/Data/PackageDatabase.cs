using System.Collections.Generic;
using UnityEngine;

namespace KernelPanic.Data
{
    public sealed class PackageDatabase : ScriptableObject
    {
        [SerializeField] private List<PackageDefinition> allPackages = new();

        public IReadOnlyList<PackageDefinition> AllPackages => allPackages;

        public PackageDefinition FindById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            for (int i = 0; i < allPackages.Count; i++)
            {
                PackageDefinition package = allPackages[i];
                if (package != null && string.Equals(package.Id, id, System.StringComparison.OrdinalIgnoreCase))
                {
                    return package;
                }
            }

            return null;
        }

        public PackageDefinition FindByRarity(int rarity, int index)
        {
            int safeRarity = Mathf.Clamp(rarity, 1, 5);
            int matchIndex = 0;
            for (int i = 0; i < allPackages.Count; i++)
            {
                PackageDefinition package = allPackages[i];
                if (package == null || package.Rarity != safeRarity)
                {
                    continue;
                }

                if (matchIndex == index)
                {
                    return package;
                }

                matchIndex++;
            }

            return null;
        }

        public int CountByRarity(int rarity)
        {
            int safeRarity = Mathf.Clamp(rarity, 1, 5);
            int count = 0;
            for (int i = 0; i < allPackages.Count; i++)
            {
                if (allPackages[i] != null && allPackages[i].Rarity == safeRarity)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
