using System.Collections.Generic;
using KernelPanic.Core;

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
        public int UsedRam
        {
            get
            {
                int used = 0;
                for (int i = 0; i < _cards.Count; i++)
                {
                    used += GetRamCost(_cards[i]);
                }

                return used;
            }
        }

        public int RemainingRam => System.Math.Max(0, RamCapacity - UsedRam);

        public bool CanAdd(CardInstance card)
        {
            return card != null && UsedRam + GetRamCost(card) <= RamCapacity;
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

        public static int GetRamCost(CardInstance card)
        {
            return card?.Definition != null && card.Definition.Language == Language.Java ? 2 : 1;
        }
    }
}
