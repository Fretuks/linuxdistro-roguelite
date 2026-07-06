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

        /// <summary>
        /// Checks that every entry is assigned and has a unique, non-blank id. Used at both
        /// edit time (OnValidate) and runtime, so misconfiguration surfaces before it can
        /// silently disable a feature that depends on this database.
        /// </summary>
        public bool TryValidate(out string error)
        {
            for (int i = 0; i < allDistros.Count; i++)
            {
                if (allDistros[i] == null)
                {
                    error = $"entry {i} is empty";
                    return false;
                }
            }

            HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < allDistros.Count; i++)
            {
                string id = allDistros[i].Id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    error = $"'{allDistros[i].name}' has a blank id";
                    return false;
                }

                if (!seenIds.Add(id))
                {
                    error = $"duplicate distro id '{id}'";
                    return false;
                }
            }

            error = null;
            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!TryValidate(out string error))
            {
                Debug.LogError($"DistroDatabase '{name}': {error}.", this);
            }
        }
#endif
    }
}
