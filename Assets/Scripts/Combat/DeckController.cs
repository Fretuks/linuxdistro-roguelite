using System;
using System.Collections.Generic;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Manages draw, discard, and exhaust piles for a combat deck.
    /// </summary>
    public sealed class DeckController
    {
        private readonly List<CardInstance> drawPile = new();
        private readonly List<CardInstance> discardPile = new();
        private readonly List<CardInstance> exhaustPile = new();

        public IReadOnlyList<CardInstance> DrawPile => drawPile;
        public IReadOnlyList<CardInstance> DiscardPile => discardPile;
        public IReadOnlyList<CardInstance> ExhaustPile => exhaustPile;

        public void Initialize(IEnumerable<CardInstance> cards)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<CardInstance> Draw(int count)
        {
            throw new NotImplementedException();
        }

        public void Discard(CardInstance card)
        {
            throw new NotImplementedException();
        }

        public void Exhaust(CardInstance card)
        {
            throw new NotImplementedException();
        }

        public void ShuffleDrawPile()
        {
            throw new NotImplementedException();
        }

        public void ReshuffleDiscardIntoDraw()
        {
            throw new NotImplementedException();
        }
    }
}
