using System;
using System.Collections.Generic;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Stores played cards in LIFO order until explicitly force-evaluated.
    /// </summary>
    public sealed class LazyStack : IResolutionTrack
    {
        private readonly Stack<CardInstance> stackedCards = new();

        public int Count => stackedCards.Count;

        public void Enqueue(CardInstance card)
        {
            throw new NotImplementedException();
        }

        public void Resolve(CombatContext context)
        {
            throw new NotImplementedException();
        }

        public void ForceEvaluate(CombatContext context)
        {
            throw new NotImplementedException();
        }
    }
}
