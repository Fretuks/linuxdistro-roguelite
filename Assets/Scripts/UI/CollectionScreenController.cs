using System;
using System.Collections.Generic;
using KernelPanic.Data;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// Renders the owned-units list and detail readout on the collection screen.
    /// </summary>
    [Serializable]
    public sealed class CollectionScreenController
    {
        private readonly List<VisualElement> rows = new();

        private VisualElement list;
        private VisualElement detail;
        private FontAsset monospaceFont;
        private IReadOnlyList<DistroDefinition> units = Array.Empty<DistroDefinition>();
        private IReadOnlyList<CardEntry> cards = Array.Empty<CardEntry>();
        private int selectedIndex;

        public void Bind(VisualElement root, FontAsset artFont)
        {
            monospaceFont = artFont;
            list = root.Q<VisualElement>("CollectionList");
            detail = root.Q<VisualElement>("CollectionDetail");
        }

        public void RefreshUnits(IReadOnlyList<DistroDefinition> ownedUnits)
        {
            units = ownedUnits ?? Array.Empty<DistroDefinition>();
            if (list == null || detail == null)
            {
                return;
            }

            rows.Clear();
            list.Clear();

            if (units.Count == 0)
            {
                detail.Clear();
                detail.Add(new Label("no units installed") { name = "CollectionEmptyTitle" });
                detail.Add(new Label("run: curl gacha.sh | sh") { name = "CollectionEmptyHint" });
                return;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, units.Count - 1);
            for (int i = 0; i < units.Count; i++)
            {
                int index = i;
                DistroDefinition unit = units[i];
                VisualElement row = new();
                row.AddToClassList("collection-row");
                row.RegisterCallback<PointerEnterEvent>(_ => SelectUnit(index));
                row.RegisterCallback<ClickEvent>(_ => SelectUnit(index));

                Label name = new(DistroPresentation.DisplayName(unit));
                name.AddToClassList("collection-row-name");
                name.style.color = new StyleColor(unit.AccentColor);

                Label languages = new(DistroPresentation.FormatLanguages(unit));
                languages.AddToClassList("collection-row-languages");

                row.Add(name);
                row.Add(languages);
                list.Add(row);
                rows.Add(row);
            }

            SelectUnit(selectedIndex);
        }

        public void RefreshCards(DistroDefinition featuredUnit)
        {
            cards = BuildCardEntries(featuredUnit);
            if (list == null || detail == null)
            {
                return;
            }

            rows.Clear();
            list.Clear();

            if (cards.Count == 0)
            {
                detail.Clear();
                detail.Add(new Label("no cards installed") { name = "CollectionEmptyTitle" });
                detail.Add(new Label("featured distro cards appear here") { name = "CollectionEmptyHint" });
                return;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, cards.Count - 1);
            for (int i = 0; i < cards.Count; i++)
            {
                int index = i;
                CardEntry entry = cards[i];
                VisualElement row = new();
                row.AddToClassList("collection-row");
                row.RegisterCallback<PointerEnterEvent>(_ => SelectCard(index));
                row.RegisterCallback<ClickEvent>(_ => SelectCard(index));

                Label name = new(GetCardDisplayName(entry.Card));
                name.AddToClassList("collection-row-name");

                Label meta = new($"{DistroPresentation.DisplayName(entry.Owner)} / {FormatCardMeta(entry.Card)}");
                meta.AddToClassList("collection-row-languages");

                row.Add(name);
                row.Add(meta);
                list.Add(row);
                rows.Add(row);
            }

            SelectCard(selectedIndex);
        }

        public void Refresh(IReadOnlyList<DistroDefinition> ownedUnits)
        {
            RefreshUnits(ownedUnits);
        }

        private void SelectUnit(int index)
        {
            if (units.Count == 0)
            {
                return;
            }

            selectedIndex = Mathf.Clamp(index, 0, units.Count - 1);
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].EnableInClassList("selected", i == selectedIndex);
            }

            RenderDetail(units[selectedIndex]);
        }

        private void SelectCard(int index)
        {
            if (cards.Count == 0)
            {
                return;
            }

            selectedIndex = Mathf.Clamp(index, 0, cards.Count - 1);
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].EnableInClassList("selected", i == selectedIndex);
            }

            RenderCardDetail(cards[selectedIndex]);
        }

        private void RenderDetail(DistroDefinition unit)
        {
            detail.Clear();

            Label name = new(DistroPresentation.DisplayName(unit));
            name.AddToClassList("collection-detail-name");
            name.style.color = new StyleColor(unit.AccentColor);
            detail.Add(name);

            Label description = new(string.IsNullOrWhiteSpace(unit.Description) ? "--" : unit.Description);
            description.AddToClassList("collection-detail-description");
            detail.Add(description);
            AddPassiveDetails(detail, unit);

            VisualElement readout = new();
            readout.AddToClassList("collection-detail-readout");

            Label artLabel = new();
            DistroArtPresenter.ConfigureArtLabel(artLabel, monospaceFont);
            AsciiArtFitter artFitter = new(artLabel, monospaceFont);
            VisualElement artPlaceholder = DistroArtPresenter.CreatePlaceholder();
            artFitter.SetArt(DistroArtPresenter.Render(artLabel, artPlaceholder, unit));
            readout.Add(artPlaceholder);
            readout.Add(artLabel);

            VisualElement details = new();
            details.AddToClassList("collection-detail-values");

            details.Add(BuildDetailLine("lang", DistroPresentation.FormatLanguages(unit)));
            details.Add(BuildDetailLine("uptime", unit.BaseUptime.ToString()));
            details.Add(BuildDetailLine("ram", unit.BaseRam.ToString()));
            details.Add(BuildDetailLine("cycles", unit.BaseCyclesPerTurn.ToString()));

            readout.Add(details);
            detail.Add(readout);
        }

        private void RenderCardDetail(CardEntry entry)
        {
            detail.Clear();

            CardDefinition card = entry.Card;
            Label name = new(GetCardDisplayName(card));
            name.AddToClassList("collection-detail-name");
            detail.Add(name);

            Label description = new(card == null || string.IsNullOrWhiteSpace(card.Description) ? "--" : card.Description);
            description.AddToClassList("collection-detail-description");
            detail.Add(description);
            if (card != null && !string.IsNullOrWhiteSpace(card.FlavorText))
            {
                Label flavor = new(card.FlavorText);
                flavor.AddToClassList("flavor-text");
                detail.Add(flavor);
            }

            detail.Add(BuildDetailLine("owner", DistroPresentation.DisplayName(entry.Owner)));
            detail.Add(BuildDetailLine("lang", card == null ? "--" : card.Language.ToString()));
            detail.Add(BuildDetailLine("rarity", card == null ? "--" : card.Rarity.ToString()));
            detail.Add(BuildDetailLine("cost", card == null ? "--" : card.CycleCost.ToString()));
            detail.Add(BuildDetailLine("type", card != null && card.IsToken ? "token" : "loadout"));
        }

        private static VisualElement BuildDetailLine(string key, string value)
        {
            VisualElement row = new();
            row.AddToClassList("kv-row");

            Label keyLabel = new(key);
            keyLabel.AddToClassList("kv-key");
            Label valueLabel = new(value);
            valueLabel.AddToClassList("kv-value");

            row.Add(keyLabel);
            row.Add(valueLabel);
            return row;
        }

        private static void AddPassiveDetails(VisualElement target, DistroDefinition unit)
        {
            if (unit.Passive == null)
            {
                return;
            }

            Label passiveTitle = new(unit.Passive.Name);
            passiveTitle.AddToClassList("detail-section-title");
            target.Add(passiveTitle);

            Label passiveRules = new(unit.Passive.RulesText);
            passiveRules.AddToClassList("collection-detail-description");
            target.Add(passiveRules);

            if (!string.IsNullOrWhiteSpace(unit.Passive.FlavorText))
            {
                Label passiveFlavor = new(unit.Passive.FlavorText);
                passiveFlavor.AddToClassList("flavor-text");
                target.Add(passiveFlavor);
            }
        }

        private static IReadOnlyList<CardEntry> BuildCardEntries(DistroDefinition featuredUnit)
        {
            List<CardEntry> entries = new();
            if (featuredUnit == null)
            {
                return entries;
            }

            for (int cardIndex = 0; cardIndex < featuredUnit.ExclusiveCards.Count; cardIndex++)
            {
                CardDefinition card = featuredUnit.ExclusiveCards[cardIndex];
                if (card == null)
                {
                    continue;
                }

                entries.Add(new CardEntry(featuredUnit, card));
            }

            return entries;
        }

        private static string GetCardDisplayName(CardDefinition card)
        {
            if (card == null)
            {
                return "--";
            }

            return string.IsNullOrWhiteSpace(card.DisplayName) ? card.Id : card.DisplayName;
        }

        private static string FormatCardMeta(CardDefinition card)
        {
            return card == null ? "--" : $"{card.Language} / {card.CycleCost}c";
        }

        private readonly struct CardEntry
        {
            public CardEntry(DistroDefinition owner, CardDefinition card)
            {
                Owner = owner;
                Card = card;
            }

            public DistroDefinition Owner { get; }
            public CardDefinition Card { get; }
        }
    }
}
