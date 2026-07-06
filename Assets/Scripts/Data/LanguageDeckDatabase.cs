using System.Collections.Generic;
using KernelPanic.Core;
using UnityEngine;

namespace KernelPanic.Data
{
    /// <summary>
    /// Resolves language starter decks for collection display.
    /// </summary>
    public sealed class LanguageDeckDatabase : ScriptableObject
    {
        [SerializeField] private List<LanguageDeckDefinition> allDecks = new();

        public IReadOnlyList<LanguageDeckDefinition> AllDecks => allDecks;

        public LanguageDeckDefinition FindByLanguage(Language language)
        {
            for (int i = 0; i < allDecks.Count; i++)
            {
                LanguageDeckDefinition deck = allDecks[i];
                if (deck != null && deck.Language == language)
                {
                    return deck;
                }
            }

            return null;
        }
    }
}
