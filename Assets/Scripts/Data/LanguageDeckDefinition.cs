using System.Collections.Generic;
using KernelPanic.Core;
using UnityEngine;

namespace KernelPanic.Data
{
    /// <summary>
    /// Defines the read-only starter deck shown for a language in the collection codex.
    /// </summary>
    public sealed class LanguageDeckDefinition : ScriptableObject
    {
        [System.Serializable]
        public struct LanguageDeckEntry
        {
            [SerializeField] private CardDefinition card;
            [SerializeField] private int count;

            public CardDefinition Card => card;
            public int Count => count;
        }

        [SerializeField] private Language language;
        [SerializeField] private List<LanguageDeckEntry> entries = new();

        public Language Language => language;
        public IReadOnlyList<LanguageDeckEntry> Entries => entries;
    }
}
