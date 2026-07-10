using System.Collections.Generic;
using System.Linq;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Describes one card inspected by a top-of-deck peek (e.g. Ubuntu's apt update), for UI display.
    /// </summary>
    public readonly struct PeekedCardInfo
    {
        public PeekedCardInfo(CardInstance card, int cost)
        {
            Card = card;
            Cost = cost;
        }

        public CardInstance Card { get; }
        public int Cost { get; }
    }

    /// <summary>
    /// Manages draw, discard, and exhaust piles for a combat deck.
    /// </summary>
    public sealed class DeckController
    {
        private readonly List<CardInstance> _drawPile = new();
        private readonly List<CardInstance> _discardPile = new();
        private readonly List<CardInstance> _exhaustPile = new();

        public IReadOnlyList<CardInstance> DrawPile => _drawPile;
        public IReadOnlyList<CardInstance> DiscardPile => _discardPile;
        public IReadOnlyList<CardInstance> ExhaustPile => _exhaustPile;
        public int AvailableToDrawCount => _drawPile.Count + _discardPile.Count;

        public void Initialize(IEnumerable<CardInstance> cards)
        {
            _drawPile.Clear();
            _discardPile.Clear();
            _exhaustPile.Clear();

            if (cards != null)
            {
                _drawPile.AddRange(cards.Where(card => card != null));
            }

            ShuffleDrawPile();
        }

        public IReadOnlyList<CardInstance> Draw(int count)
        {
            List<CardInstance> drawn = new();
            for (int i = 0; i < count; i++)
            {
                if (_drawPile.Count == 0)
                {
                    ReshuffleDiscardIntoDraw();
                }

                if (_drawPile.Count == 0)
                {
                    break;
                }

                int topIndex = _drawPile.Count - 1;
                CardInstance card = _drawPile[topIndex];
                _drawPile.RemoveAt(topIndex);
                drawn.Add(card);
            }

            return drawn;
        }

        public bool TryDrawCheapestFromTop(int lookCount, out CardInstance card, out IReadOnlyList<PeekedCardInfo> peeked, out bool wasTie)
        {
            card = null;
            peeked = System.Array.Empty<PeekedCardInfo>();
            wasTie = false;
            if (lookCount <= 0)
            {
                return false;
            }

            if (_drawPile.Count == 0)
            {
                ReshuffleDiscardIntoDraw();
            }

            if (_drawPile.Count == 0)
            {
                return false;
            }

            int inspectCount = System.Math.Min(lookCount, _drawPile.Count);
            int selectedIndex = _drawPile.Count - 1;
            int selectedCost = CombatManager.GetCardCost(_drawPile[selectedIndex]);
            List<PeekedCardInfo> inspected = new(inspectCount) { new PeekedCardInfo(_drawPile[selectedIndex], selectedCost) };
            int tieCount = 1;
            for (int offset = 1; offset < inspectCount; offset++)
            {
                int index = _drawPile.Count - 1 - offset;
                int cost = CombatManager.GetCardCost(_drawPile[index]);
                inspected.Add(new PeekedCardInfo(_drawPile[index], cost));
                if (cost < selectedCost)
                {
                    selectedCost = cost;
                    selectedIndex = index;
                    tieCount = 1;
                }
                else if (cost == selectedCost)
                {
                    tieCount++;
                }
            }

            peeked = inspected;
            wasTie = tieCount > 1;
            card = _drawPile[selectedIndex];
            _drawPile.RemoveAt(selectedIndex);
            return card != null;
        }

        public void Discard(CardInstance card)
        {
            if (card != null)
            {
                _discardPile.Add(card);
            }
        }

        public void AddToDrawPile(CardInstance card, bool shuffle)
        {
            if (card == null)
            {
                return;
            }

            _drawPile.Add(card);
            if (shuffle)
            {
                ShuffleDrawPile();
            }
        }

        public void Exhaust(CardInstance card)
        {
            if (card != null)
            {
                _exhaustPile.Add(card);
            }
        }

        public void ShuffleDrawPile()
        {
            for (int i = _drawPile.Count - 1; i > 0; i--)
            {
                int swapIndex = RandomRoll.RollRange(0, i, RollContext.None);
                (_drawPile[i], _drawPile[swapIndex]) = (_drawPile[swapIndex], _drawPile[i]);
            }
        }

        public void ReshuffleDiscardIntoDraw()
        {
            if (_discardPile.Count == 0)
            {
                return;
            }

            _drawPile.AddRange(_discardPile);
            _discardPile.Clear();
            ShuffleDrawPile();
        }
    }
}
