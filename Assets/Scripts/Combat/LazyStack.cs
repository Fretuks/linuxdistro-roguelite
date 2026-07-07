using System.Collections.Generic;
using KernelPanic.Core;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Stores played cards in LIFO order until explicitly force-evaluated.
    /// </summary>
    public sealed class LazyStack : IResolutionTrack
    {
        private readonly Stack<CardInstance> _stackedCards = new();
        private readonly List<CardInstance> _stackedView = new();

        public int Count => _stackedCards.Count;
        public IReadOnlyList<CardInstance> Cards => _stackedView;

        public void Enqueue(CardInstance card)
        {
            if (card == null)
            {
                return;
            }

            _stackedCards.Push(card);
            _stackedView.Insert(0, card);
        }

        public void Resolve(CombatContext context)
        {
            ForceEvaluate(context);
        }

        public void ForceEvaluate(CombatContext context)
        {
            while (TryPop(out CardInstance card))
            {
                GameEvents.RaiseCardResolved(new CardResolvedEvent(card, ResolutionTrack.LazyStack));
            }
        }

        public bool TryPop(out CardInstance card)
        {
            if (_stackedCards.Count == 0)
            {
                card = null;
                return false;
            }

            card = _stackedCards.Pop();
            _stackedView.Remove(card);
            return true;
        }
    }
}
