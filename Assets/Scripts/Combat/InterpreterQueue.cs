using System.Collections.Generic;
using KernelPanic.Core;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Stores played cards in FIFO order for resolution during the Interpret phase.
    /// </summary>
    public sealed class InterpreterQueue : IResolutionTrack
    {
        private readonly Queue<CardInstance> _queuedCards = new();
        private readonly List<CardInstance> _queuedView = new();

        public int Count => _queuedCards.Count;
        public IReadOnlyList<CardInstance> Cards => _queuedView;

        public void Enqueue(CardInstance card)
        {
            if (card == null)
            {
                return;
            }

            _queuedCards.Enqueue(card);
            _queuedView.Add(card);
        }

        public void Resolve(CombatContext context)
        {
            while (_queuedCards.Count > 0)
            {
                CardInstance card = _queuedCards.Dequeue();
                _queuedView.Remove(card);
                GameEvents.RaiseCardResolved(new CardResolvedEvent(card, ResolutionTrack.InterpreterQueue));
            }
        }

        public bool TryDequeue(out CardInstance card)
        {
            if (_queuedCards.Count == 0)
            {
                card = null;
                return false;
            }

            card = _queuedCards.Dequeue();
            _queuedView.Remove(card);
            return true;
        }

        public void Clear()
        {
            _queuedCards.Clear();
            _queuedView.Clear();
        }
    }
}
