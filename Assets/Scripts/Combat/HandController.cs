using System.Collections.Generic;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Manages cards currently held in hand with a RAM-based capacity.
    /// </summary>
    public sealed class HandController
    {
        private readonly List<CardInstance> _cards = new();

        public HandController(int ramCapacity)
        {
            RamCapacity = ramCapacity;
        }

        public int RamCapacity { get; private set; }
        public IReadOnlyList<CardInstance> Cards => _cards;

        public bool CanAdd(CardInstance card)
        {
            return card != null && _cards.Count < RamCapacity;
        }

        public bool Add(CardInstance card)
        {
            if (!CanAdd(card))
            {
                return false;
            }

            _cards.Add(card);
            return true;
        }

        public bool Remove(CardInstance card)
        {
            return card != null && _cards.Remove(card);
        }

        public void Clear()
        {
            _cards.Clear();
        }
    }
}
