using System;
using System.Collections.Generic;
using UnityEngine;

namespace KernelPanic.Data
{
    /// <summary>
    /// Resolves persisted distro ids to playable distro definitions.
    /// </summary>
    public sealed class DistroDatabase : ScriptableObject
    {
        [SerializeField] private List<DistroDefinition> allDistros = new();

        public IReadOnlyList<DistroDefinition> AllDistros => allDistros;

        public DistroDefinition FindById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            for (int i = 0; i < allDistros.Count; i++)
            {
                DistroDefinition distro = allDistros[i];
                if (distro != null && string.Equals(distro.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return distro;
                }
            }

            return null;
        }
    }
}
