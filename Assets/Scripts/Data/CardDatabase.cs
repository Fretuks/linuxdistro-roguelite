using System;
using System.Collections.Generic;
using UnityEngine;

namespace KernelPanic.Data
{
    /// <summary>
    /// Resolves persisted card ids to immutable card definitions.
    /// </summary>
    public sealed class CardDatabase : ScriptableObject
    {
        [SerializeField] private List<CardDefinition> allCards = new();

        public IReadOnlyList<CardDefinition> AllCards => allCards;

        public CardDefinition FindById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            for (int i = 0; i < allCards.Count; i++)
            {
                CardDefinition card = allCards[i];
                if (card != null && string.Equals(card.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return card;
                }
            }

            return null;
        }
    }
}
