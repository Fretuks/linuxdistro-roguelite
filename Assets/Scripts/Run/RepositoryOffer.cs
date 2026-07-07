using KernelPanic.Combat;
using KernelPanic.Data;

namespace KernelPanic.Run
{
    public enum RepositoryOfferKind
    {
        NewCard,
        CardUpgrade,
        StatUpgrade
    }

    public enum CardUpgradeKind
    {
        CostDown,
        MagnitudeUp,
        DrawRider
    }

    public enum RunStatUpgradeKind
    {
        MaxCycles,
        MaxUptime,
        Heal,
        Ram
    }

    public sealed class RepositoryOffer
    {
        private RepositoryOffer(
            RepositoryOfferKind kind,
            string commandName,
            string displayName,
            string description,
            int price)
        {
            Kind = kind;
            CommandName = commandName;
            DisplayName = displayName;
            Description = description;
            Price = price;
        }

        public RepositoryOfferKind Kind { get; }
        public string CommandName { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int Price { get; }
        public CardDefinition CardDefinition { get; private set; }
        public CardInstance TargetCard { get; private set; }
        public CardUpgradeKind CardUpgradeKind { get; private set; }
        public RunStatUpgradeKind StatUpgradeKind { get; private set; }
        public bool Sold { get; private set; }

        public static RepositoryOffer NewCard(CardDefinition card, int price)
        {
            string name = DisplayNameFor(card);
            RepositoryOffer offer = new(RepositoryOfferKind.NewCard, SafeCommandName(card?.Id, name), name, RulesTextFor(card), price)
            {
                CardDefinition = card
            };
            return offer;
        }

        public static RepositoryOffer CardUpgrade(CardInstance target, CardUpgradeKind upgradeKind, int price)
        {
            string cardName = DisplayNameFor(target?.Definition);
            string description = UpgradeDescription(target, upgradeKind);
            RepositoryOffer offer = new(RepositoryOfferKind.CardUpgrade, SafeCommandName(target?.Definition?.Id, cardName), $"upgrade {cardName}", description, price)
            {
                TargetCard = target,
                CardUpgradeKind = upgradeKind
            };
            return offer;
        }

        public static RepositoryOffer StatUpgrade(RunStatUpgradeKind upgradeKind, int price)
        {
            return new RepositoryOffer(RepositoryOfferKind.StatUpgrade, upgradeKind.ToString().ToLowerInvariant(), StatName(upgradeKind), StatDescription(upgradeKind), price)
            {
                StatUpgradeKind = upgradeKind
            };
        }

        public void MarkSold()
        {
            Sold = true;
        }

        private static string RulesTextFor(CardDefinition card)
        {
            if (card == null)
            {
                return "--";
            }

            CardInstance preview = new(card);
            return CardEffectFactory.GetRulesText(preview);
        }

        private static string UpgradeDescription(CardInstance target, CardUpgradeKind upgradeKind)
        {
            if (target?.Definition == null)
            {
                return "--";
            }

            return upgradeKind switch
            {
                CardUpgradeKind.CostDown => $"cost {CombatManager.GetCardCost(target)} -> {UnityEngine.Mathf.Max(0, CombatManager.GetCardCost(target) - 1)}",
                CardUpgradeKind.MagnitudeUp => $"effect magnitude +{CombatTuning.UpgradeMagnitudeBonus}",
                CardUpgradeKind.DrawRider => "add rider: draw 1 after resolving",
                _ => "--"
            };
        }

        private static string StatName(RunStatUpgradeKind upgradeKind)
        {
            return upgradeKind switch
            {
                RunStatUpgradeKind.MaxCycles => "+1 max cycles",
                RunStatUpgradeKind.MaxUptime => "+max uptime",
                RunStatUpgradeKind.Heal => "heal uptime",
                RunStatUpgradeKind.Ram => "+1 RAM",
                _ => upgradeKind.ToString()
            };
        }

        private static string StatDescription(RunStatUpgradeKind upgradeKind)
        {
            return upgradeKind switch
            {
                RunStatUpgradeKind.MaxCycles => $"+{CombatTuning.StatUpgradeMaxCycles} max cycles this run",
                RunStatUpgradeKind.MaxUptime => $"+{CombatTuning.StatUpgradeMaxUptime} max uptime this run",
                RunStatUpgradeKind.Heal => $"heal {CombatTuning.StatUpgradeHeal} uptime now",
                RunStatUpgradeKind.Ram => $"+{CombatTuning.StatUpgradeRam} RAM this run",
                _ => "--"
            };
        }

        private static string DisplayNameFor(CardDefinition card)
        {
            if (card == null)
            {
                return "--";
            }

            return string.IsNullOrWhiteSpace(card.DisplayName) ? card.Id : card.DisplayName;
        }

        private static string SafeCommandName(string id, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(id) ? fallback : id;
            return string.IsNullOrWhiteSpace(value) ? "package" : value.Replace(' ', '-');
        }
    }
}
