using System;
using System.Collections.Generic;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Stores played cards in FIFO order for resolution during the Interpret phase.
    /// </summary>
    public sealed class InterpreterQueue : IResolutionTrack
    {
        private readonly Queue<CardInstance> queuedCards = new();

        public int Count => queuedCards.Count;

        public void Enqueue(CardInstance card)
        {
            throw new NotImplementedException();
        }

        public void Resolve(CombatContext context)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }
    }
}
