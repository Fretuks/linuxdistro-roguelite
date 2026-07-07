using System;
using System.Collections.Generic;
using KernelPanic.Data;

namespace KernelPanic.Meta
{
    public enum CardLoadoutFailureReason
    {
        None,
        Full,
        NotOwned,
        Token,
        Duplicate,
        NotEquipped
    }

    /// <summary>
    /// Tracks equipped exclusive cards per owned distro without persistence or logging.
    /// </summary>
    public sealed class CardLoadout
    {
        public const int MaxEquippedCards = 4;

        private readonly IReadOnlyList<DistroDefinition> _ownedUnits;
        private readonly Dictionary<string, List<string>> _equippedByDistro = new(StringComparer.OrdinalIgnoreCase);

        public CardLoadout(IReadOnlyList<DistroDefinition> ownedUnits)
        {
            this._ownedUnits = ownedUnits ?? Array.Empty<DistroDefinition>();
        }

        public IReadOnlyList<string> GetEquippedCardIds(string distroId)
        {
            return _equippedByDistro.TryGetValue(distroId, out List<string> equipped)
                ? equipped
                : Array.Empty<string>();
        }

        public bool HasLoadout(string distroId)
        {
            return !string.IsNullOrWhiteSpace(distroId) && _equippedByDistro.ContainsKey(distroId);
        }

        public void EnsureLoadout(DistroDefinition distro)
        {
            if (distro == null || string.IsNullOrWhiteSpace(distro.Id) || HasLoadout(distro.Id))
            {
                return;
            }

            _equippedByDistro[distro.Id] = new List<string>();
        }

        public void ClearAll()
        {
            _equippedByDistro.Clear();
        }

        public void ClearLoadout(DistroDefinition distro)
        {
            if (distro == null || string.IsNullOrWhiteSpace(distro.Id))
            {
                return;
            }

            _equippedByDistro[distro.Id] = new List<string>();
        }

        public bool TryLoad(string distroId, IEnumerable<string> cardIds, out bool skippedInvalid)
        {
            skippedInvalid = false;
            DistroDefinition distro = FindOwnedDistro(distroId);
            if (distro == null)
            {
                skippedInvalid = true;
                return false;
            }

            List<string> equipped = new();
            foreach (string cardId in cardIds ?? Array.Empty<string>())
            {
                CardDefinition card = FindExclusiveCard(distro, cardId);
                if (card == null || card.IsToken || card.IsRunOnly || equipped.Contains(card.Id))
                {
                    skippedInvalid = true;
                    continue;
                }

                if (equipped.Count < MaxEquippedCards)
                {
                    equipped.Add(card.Id);
                }
                else
                {
                    skippedInvalid = true;
                }
            }

            _equippedByDistro[distro.Id] = equipped;
            return true;
        }

        public bool TryEquip(string distroId, string cardId, out CardLoadoutFailureReason reason)
        {
            DistroDefinition distro = FindOwnedDistro(distroId);
            if (distro == null)
            {
                reason = CardLoadoutFailureReason.NotOwned;
                return false;
            }

            CardDefinition card = FindExclusiveCard(distro, cardId);
            if (card == null)
            {
                reason = CardLoadoutFailureReason.NotOwned;
                return false;
            }

            if (card.IsToken || card.IsRunOnly)
            {
                reason = CardLoadoutFailureReason.Token;
                return false;
            }

            EnsureLoadout(distro);
            List<string> equipped = _equippedByDistro[distro.Id];
            if (equipped.Contains(card.Id))
            {
                reason = CardLoadoutFailureReason.Duplicate;
                return false;
            }

            if (equipped.Count >= MaxEquippedCards)
            {
                reason = CardLoadoutFailureReason.Full;
                return false;
            }

            equipped.Add(card.Id);
            reason = CardLoadoutFailureReason.None;
            return true;
        }

        public bool TryUnequip(string distroId, string cardId, out CardLoadoutFailureReason reason)
        {
            DistroDefinition distro = FindOwnedDistro(distroId);
            if (distro == null)
            {
                reason = CardLoadoutFailureReason.NotOwned;
                return false;
            }

            EnsureLoadout(distro);
            List<string> equipped = _equippedByDistro[distro.Id];
            if (!equipped.Remove(cardId))
            {
                reason = CardLoadoutFailureReason.NotEquipped;
                return false;
            }

            reason = CardLoadoutFailureReason.None;
            return true;
        }

        private DistroDefinition FindOwnedDistro(string distroId)
        {
            if (string.IsNullOrWhiteSpace(distroId))
            {
                return null;
            }

            for (int i = 0; i < _ownedUnits.Count; i++)
            {
                DistroDefinition distro = _ownedUnits[i];
                if (distro != null && string.Equals(distro.Id, distroId, StringComparison.OrdinalIgnoreCase))
                {
                    return distro;
                }
            }

            return null;
        }

        private static CardDefinition FindExclusiveCard(DistroDefinition distro, string cardId)
        {
            if (distro == null || string.IsNullOrWhiteSpace(cardId))
            {
                return null;
            }

            for (int i = 0; i < distro.ExclusiveCards.Count; i++)
            {
                CardDefinition card = distro.ExclusiveCards[i];
                if (card != null && !card.IsRunOnly && string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase))
                {
                    return card;
                }
            }

            return null;
        }

    }
}
