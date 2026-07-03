using System;
using System.Collections.Generic;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Manages cards currently held in hand with a RAM-based capacity.
    /// </summary>
    public sealed class HandController
    {
        private readonly List<CardInstance> cards = new();

        public HandController(int ramCapacity)
        {
            RamCapacity = ramCapacity;
        }

        public int RamCapacity { get; private set; }
        public IReadOnlyList<CardInstance> Cards => cards;

        public bool CanAdd(CardInstance card)
        {
            throw new NotImplementedException();
        }

        public void Add(CardInstance card)
        {
            throw new NotImplementedException();
        }

        public bool Remove(CardInstance card)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }
    }
}
