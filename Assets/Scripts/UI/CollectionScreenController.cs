using System;
using System.Collections.Generic;
using KernelPanic.Core;
using KernelPanic.Data;
using KernelPanic.Meta;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// Renders Collection tabs and the pushed read-only card subview.
    /// </summary>
    [Serializable]
    public sealed class CollectionScreenController
    {
        private readonly List<VisualElement> rows = new();

        private VisualElement list;
        private VisualElement detail;
        private FontAsset monospaceFont;
        private LanguageDeckDatabase languageDeckDatabase;
        private CardDatabase cardDatabase;
        private PlayerCollection playerCollection;
        private IReadOnlyList<DistroDefinition> units = Array.Empty<DistroDefinition>();
        private IReadOnlyList<LanguageCatalogEntry> languages = LanguageCatalog.All;
        private IReadOnlyList<CardEntry> cards = Array.Empty<CardEntry>();
        private CollectionMode mode;
        private CollectionMode returnMode;
        private int selectedUnitIndex;
        private int selectedLanguageIndex;
        private int selectedCardIndex;
        private string emptyCardMessage = "no cards installed";

        public event Action ViewChanged;

        public void Bind(VisualElement root, FontAsset artFont, LanguageDeckDatabase deckDatabase, CardDatabase cardsDatabase, PlayerCollection collection)
        {
            monospaceFont = artFont;
            languageDeckDatabase = deckDatabase;
            cardDatabase = cardsDatabase;
            playerCollection = collection;
            list = root.Q<VisualElement>("CollectionList");
            detail = root.Q<VisualElement>("CollectionDetail");
        }

        public bool IsCardSubview => mode == CollectionMode.CardSubview;

        public string CurrentTitle { get; private set; } = "$ ls ~/collection";
        public string CurrentHint { get; private set; } = "[esc] back   [left/right] tabs   [tab] tabs   [arrows] navigate   [enter] select";

        public void RefreshUnits(IReadOnlyList<DistroDefinition> ownedUnits)
        {
            units = ownedUnits ?? Array.Empty<DistroDefinition>();
            ShowUnits();
        }

        public void ShowUnits()
        {
            mode = CollectionMode.Units;
            CurrentTitle = "$ ls ~/collection";
            CurrentHint = "[esc] back   [left/right] tabs   [tab] tabs   [arrows] navigate   [enter] select";
            RenderUnits();
            ViewChanged?.Invoke();
        }

        public void ShowLanguages()
        {
            mode = CollectionMode.Languages;
            CurrentTitle = "$ ls ~/collection/languages";
            CurrentHint = "[esc] back   [left/right] tabs   [tab] tabs   [arrows] navigate   [enter] select";
            RenderLanguages();
            ViewChanged?.Invoke();
        }

        public bool BackFromSubview()
        {
            if (!IsCardSubview)
            {
                return false;
            }

            if (returnMode == CollectionMode.Languages)
            {
                ShowLanguages();
            }
            else
            {
                ShowUnits();
            }

            return true;
        }

        public void SelectRelative(int delta)
        {
            if (mode == CollectionMode.Units)
            {
                SelectUnit(selectedUnitIndex + delta);
                return;
            }

            if (mode == CollectionMode.Languages)
            {
                SelectLanguage(selectedLanguageIndex + delta);
                return;
            }

            SelectCard(selectedCardIndex + delta);
        }

        public void ActivateSelected()
        {
            if (mode == CollectionMode.Units && units.Count > 0)
            {
                OpenUnitCards(units[selectedUnitIndex]);
                return;
            }

            if (mode == CollectionMode.Languages && languages.Count > 0)
            {
                LanguageCatalogEntry language = languages[selectedLanguageIndex];
                if (LanguageUnlock.IsUnlocked(language.Language, playerCollection))
                {
                    OpenLanguageDeck(language);
                }
            }
        }

        private void RenderUnits()
        {
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

            selectedUnitIndex = Mathf.Clamp(selectedUnitIndex, 0, units.Count - 1);
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

                Label unitLanguages = new(DistroPresentation.FormatLanguages(unit));
                unitLanguages.AddToClassList("collection-row-languages");

                row.Add(name);
                row.Add(unitLanguages);
                list.Add(row);
                rows.Add(row);
            }

            SelectUnit(selectedUnitIndex);
        }

        private void RenderLanguages()
        {
            if (list == null || detail == null)
            {
                return;
            }

            rows.Clear();
            list.Clear();
            selectedLanguageIndex = Mathf.Clamp(selectedLanguageIndex, 0, languages.Count - 1);

            for (int i = 0; i < languages.Count; i++)
            {
                int index = i;
                LanguageCatalogEntry language = languages[i];
                bool unlocked = LanguageUnlock.IsUnlocked(language.Language, playerCollection);
                VisualElement row = new();
                row.AddToClassList("collection-row");
                row.EnableInClassList("locked", !unlocked);
                row.RegisterCallback<PointerEnterEvent>(_ => SelectLanguage(index));
                row.RegisterCallback<ClickEvent>(_ => SelectLanguage(index));

                Label name = new(unlocked ? language.DisplayName : $"{language.DisplayName} [locked]");
                name.AddToClassList("collection-row-name");

                Label tag = new(language.IdentityTag);
                tag.AddToClassList("collection-row-languages");

                row.Add(name);
                row.Add(tag);
                list.Add(row);
                rows.Add(row);
            }

            SelectLanguage(selectedLanguageIndex);
        }

        private void SelectUnit(int index)
        {
            if (units.Count == 0)
            {
                return;
            }

            selectedUnitIndex = Mathf.Clamp(index, 0, units.Count - 1);
            ApplySelection(selectedUnitIndex);
            RenderUnitDetail(units[selectedUnitIndex]);
        }

        private void SelectLanguage(int index)
        {
            selectedLanguageIndex = Mathf.Clamp(index, 0, languages.Count - 1);
            ApplySelection(selectedLanguageIndex);
            RenderLanguageDetail(languages[selectedLanguageIndex]);
        }

        private void SelectCard(int index)
        {
            if (cards.Count == 0)
            {
                return;
            }

            selectedCardIndex = Mathf.Clamp(index, 0, cards.Count - 1);
            ApplySelection(selectedCardIndex);
            RenderCardDetail(cards[selectedCardIndex]);
        }

        private void ApplySelection(int selectedIndex)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].EnableInClassList("selected", i == selectedIndex);
            }
        }

        private void RenderUnitDetail(DistroDefinition unit)
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
            detail.Add(BuildSubviewCommand($"> cat ~/units/{unit.Id}/cards", "exclusive packages", HasListableCards(unit), () => OpenUnitCards(unit), "no packages installed"));
        }

        private void RenderLanguageDetail(LanguageCatalogEntry language)
        {
            detail.Clear();
            bool unlocked = LanguageUnlock.IsUnlocked(language.Language, playerCollection);

            Label name = new(language.DisplayName);
            name.AddToClassList("collection-detail-name");
            detail.Add(name);
            detail.Add(BuildDetailLine("track", language.ResolutionTrack.ToString()));

            Label how = new(language.HowItWorks);
            how.AddToClassList("collection-detail-description");
            detail.Add(how);

            if (!unlocked)
            {
                string hintText = string.IsNullOrWhiteSpace(language.UnlockHint)
                    ? $"unlock: own a distro that speaks {language.DisplayName} ({FormatSupportingDistros(language)})"
                    : language.UnlockHint;
                Label hint = new(hintText);
                hint.AddToClassList("package-notice");
                detail.Add(hint);
                return;
            }

            detail.Add(BuildSubviewCommand($"> cat ~/lang/{GetLanguageId(language.Language)}/deck", "starter deck", true, () => OpenLanguageDeck(language), null));
        }

        private void OpenUnitCards(DistroDefinition unit)
        {
            if (!HasListableCards(unit))
            {
                return;
            }

            returnMode = CollectionMode.Units;
            cards = BuildUnitCardEntries(unit);
            emptyCardMessage = "no packages installed";
            CurrentTitle = $"$ cat ~/units/{unit.Id}/cards";
            CurrentHint = "[esc] back   [arrows] navigate";
            RenderCardSubview();
            ViewChanged?.Invoke();
        }

        private void OpenLanguageDeck(LanguageCatalogEntry language)
        {
            returnMode = CollectionMode.Languages;
            cards = BuildStarterDeckEntries(language);
            emptyCardMessage = "starter deck not yet available";
            CurrentTitle = $"$ cat ~/lang/{GetLanguageId(language.Language)}/deck";
            CurrentHint = "[esc] back   [arrows] navigate";
            RenderCardSubview();
            ViewChanged?.Invoke();
        }

        private void RenderCardSubview()
        {
            mode = CollectionMode.CardSubview;
            rows.Clear();
            list.Clear();
            detail.Clear();
            selectedCardIndex = Mathf.Clamp(selectedCardIndex, 0, Math.Max(0, cards.Count - 1));

            if (cards.Count == 0)
            {
                detail.Add(new Label(emptyCardMessage) { name = "CollectionEmptyTitle" });
                detail.Add(new Label("check back after deck data is installed") { name = "CollectionEmptyHint" });
                return;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                int index = i;
                CardEntry entry = cards[i];
                VisualElement row = BuildCardRow(entry);
                row.RegisterCallback<PointerEnterEvent>(_ => SelectCard(index));
                row.RegisterCallback<ClickEvent>(_ => SelectCard(index));
                list.Add(row);
                rows.Add(row);
            }

            SelectCard(selectedCardIndex);
        }

        private VisualElement BuildCardRow(CardEntry entry)
        {
            VisualElement row = new();
            row.AddToClassList("package-row");
            row.AddToClassList("collection-card-row");

            VisualElement summary = new();
            summary.AddToClassList("package-summary");

            Label count = new(entry.Count > 1 ? $"x{entry.Count}" : string.Empty);
            count.AddToClassList("package-marker");
            Label name = new(GetCardDisplayName(entry.Card));
            name.AddToClassList("package-name");
            Label meta = new(FormatCardMeta(entry.Card));
            meta.AddToClassList("package-meta");

            summary.Add(count);
            summary.Add(name);
            summary.Add(meta);
            row.Add(summary);

            Label description = new(entry.Card == null || string.IsNullOrWhiteSpace(entry.Card.Description) ? "--" : entry.Card.Description);
            description.AddToClassList("package-description");
            row.Add(description);
            return row;
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

            detail.Add(BuildDetailLine("lang", card == null ? "--" : card.Language.ToString()));
            detail.Add(BuildDetailLine("rarity", card == null ? "--" : card.Rarity.ToString()));
            detail.Add(BuildDetailLine("cost", card == null ? "--" : card.CycleCost.ToString()));
            detail.Add(BuildDetailLine("track", card == null ? "--" : card.ResolutionTrack.ToString()));
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

        private VisualElement BuildSubviewCommand(string command, string description, bool enabled, Action action, string disabledDescription)
        {
            VisualElement row = new();
            row.AddToClassList("command-row");
            row.AddToClassList("collection-command-row");
            row.EnableInClassList("disabled-row", !enabled);

            Label cursor = new(">");
            cursor.AddToClassList("command-cursor");
            Label commandLabel = new(command);
            commandLabel.AddToClassList("command-text");
            Label descriptionLabel = new(enabled ? description : disabledDescription);
            descriptionLabel.AddToClassList("command-description");

            row.Add(cursor);
            row.Add(commandLabel);
            row.Add(descriptionLabel);

            if (enabled)
            {
                row.RegisterCallback<ClickEvent>(_ => action?.Invoke());
            }

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

        private static IReadOnlyList<CardEntry> BuildUnitCardEntries(DistroDefinition unit)
        {
            List<CardEntry> entries = new();
            if (unit == null)
            {
                return entries;
            }

            for (int cardIndex = 0; cardIndex < unit.ExclusiveCards.Count; cardIndex++)
            {
                CardDefinition card = unit.ExclusiveCards[cardIndex];
                if (card == null || card.IsToken)
                {
                    continue;
                }

                entries.Add(new CardEntry(unit, card, 1));
            }

            return entries;
        }

        private IReadOnlyList<CardEntry> BuildStarterDeckEntries(LanguageCatalogEntry language)
        {
            LanguageDeckDefinition deck = languageDeckDatabase == null ? null : languageDeckDatabase.FindByLanguage(language.Language);
            if (deck == null)
            {
                return BuildRegisteredStarterDeckEntries(language.Language);
            }

            List<CardEntry> entries = new();
            for (int i = 0; i < deck.Entries.Count; i++)
            {
                LanguageDeckDefinition.LanguageDeckEntry deckEntry = deck.Entries[i];
                if (deckEntry.Card == null || deckEntry.Count <= 0)
                {
                    continue;
                }

                entries.Add(new CardEntry(null, deckEntry.Card, deckEntry.Count));
            }

            return entries;
        }

        private IReadOnlyList<CardEntry> BuildRegisteredStarterDeckEntries(Language language)
        {
            if (cardDatabase == null)
            {
                return Array.Empty<CardEntry>();
            }

            return language switch
            {
                Language.Python => BuildRegisteredStarterDeckEntries(
                    ("lang_py_print", 2),
                    ("lang_py_import_antigravity", 1),
                    ("lang_py_for_loop", 1)),
                Language.JavaScript => BuildRegisteredStarterDeckEntries(
                    ("lang_js_console_log", 2),
                    ("lang_js_fetch", 1),
                    ("lang_js_typeof", 1)),
                _ => Array.Empty<CardEntry>()
            };
        }

        private IReadOnlyList<CardEntry> BuildRegisteredStarterDeckEntries(params (string Id, int Count)[] cardRefs)
        {
            List<CardEntry> entries = new();
            for (int i = 0; i < cardRefs.Length; i++)
            {
                CardDefinition card = cardDatabase.FindById(cardRefs[i].Id);
                if (card == null)
                {
                    continue;
                }

                entries.Add(new CardEntry(null, card, cardRefs[i].Count));
            }

            return entries;
        }

        private static bool HasListableCards(DistroDefinition unit)
        {
            return BuildUnitCardEntries(unit).Count > 0;
        }

        private string FormatSupportingDistros(LanguageCatalogEntry language)
        {
            List<string> names = new();
            for (int i = 0; i < language.SupportingDistros.Count; i++)
            {
                names.Add(language.SupportingDistros[i].DisplayName);
            }

            return string.Join(", ", names);
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

        private static string GetLanguageId(Language language)
        {
            return language switch
            {
                Language.CPlusPlus => "cpp",
                Language.JavaScript => "javascript",
                Language.TypeScript => "typescript",
                _ => language.ToString().ToLowerInvariant()
            };
        }

        private enum CollectionMode
        {
            Units,
            Languages,
            CardSubview
        }

        private readonly struct CardEntry
        {
            public CardEntry(DistroDefinition owner, CardDefinition card, int count)
            {
                Owner = owner;
                Card = card;
                Count = count;
            }

            public DistroDefinition Owner { get; }
            public CardDefinition Card { get; }
            public int Count { get; }
        }
    }
}
