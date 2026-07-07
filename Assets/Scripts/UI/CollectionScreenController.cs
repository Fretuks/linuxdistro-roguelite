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
        private readonly List<VisualElement> _rows = new();

        private VisualElement _list;
        private ScrollView _detailScroll;
        private VisualElement _detail;
        private FontAsset _monospaceFont;
        private LanguageDeckDatabase _languageDeckDatabase;
        private CardDatabase _cardDatabase;
        private PlayerCollection _playerCollection;
        private Func<int> _getMergesBalance;
        private Func<DistroDefinition, VersionUpgradeResult> _tryUpgradeUnit;
        private IReadOnlyList<DistroDefinition> _units = Array.Empty<DistroDefinition>();
        private IReadOnlyList<LanguageCatalogEntry> _languages = LanguageCatalog.All;
        private IReadOnlyList<CardEntry> _cards = Array.Empty<CardEntry>();
        private CollectionMode _mode;
        private CollectionMode _returnMode;
        private int _selectedUnitIndex;
        private int _selectedLanguageIndex;
        private int _selectedCardIndex;
        private string _emptyCardMessage = "no cards installed";

        public event Action ViewChanged;

        public void Bind(VisualElement root, FontAsset artFont, LanguageDeckDatabase deckDatabase, CardDatabase cardsDatabase, PlayerCollection collection, Func<int> getMergesBalance = null, Func<DistroDefinition, VersionUpgradeResult> tryUpgradeUnit = null)
        {
            _monospaceFont = artFont;
            _languageDeckDatabase = deckDatabase;
            _cardDatabase = cardsDatabase;
            _playerCollection = collection;
            _getMergesBalance = getMergesBalance;
            _tryUpgradeUnit = tryUpgradeUnit;
            _list = root.Q<VisualElement>("CollectionList");
            _detailScroll = root.Q<ScrollView>("CollectionDetail");
            _detail = _detailScroll?.contentContainer;
        }

        public bool IsCardSubview => _mode == CollectionMode.CardSubview;

        public string CurrentTitle { get; private set; } = "$ ls ~/collection";
        public string CurrentHint { get; private set; } = "[esc] back   [left/right] tabs   [tab] tabs   [arrows] navigate   [enter] select";

        public void RefreshUnits(IReadOnlyList<DistroDefinition> ownedUnits)
        {
            _units = ownedUnits ?? Array.Empty<DistroDefinition>();
            ShowUnits();
        }

        public void ShowUnits()
        {
            _mode = CollectionMode.Units;
            CurrentTitle = "$ ls ~/collection";
            CurrentHint = "[esc] back   [left/right] tabs   [tab] tabs   [arrows] navigate   [enter] select";
            RenderUnits();
            ViewChanged?.Invoke();
        }

        public void ShowLanguages()
        {
            _mode = CollectionMode.Languages;
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

            if (_returnMode == CollectionMode.Languages)
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
            if (_mode == CollectionMode.Units)
            {
                SelectUnit(_selectedUnitIndex + delta);
                return;
            }

            if (_mode == CollectionMode.Languages)
            {
                SelectLanguage(_selectedLanguageIndex + delta);
                return;
            }

            SelectCard(_selectedCardIndex + delta);
        }

        public void ActivateSelected()
        {
            if (_mode == CollectionMode.Units && _units.Count > 0)
            {
                OpenUnitCards(_units[_selectedUnitIndex]);
                return;
            }

            if (_mode == CollectionMode.Languages && _languages.Count > 0)
            {
                LanguageCatalogEntry language = _languages[_selectedLanguageIndex];
                if (LanguageUnlock.IsUnlocked(language.Language, _playerCollection))
                {
                    OpenLanguageDeck(language);
                }
            }
        }

        private void RenderUnits()
        {
            if (_list == null || _detail == null)
            {
                return;
            }

            _rows.Clear();
            _list.Clear();

            if (_units.Count == 0)
            {
                _detail.Clear();
                _detail.Add(new Label("no units installed") { name = "CollectionEmptyTitle" });
                _detail.Add(new Label("run: curl gacha.sh | sh") { name = "CollectionEmptyHint" });
                return;
            }

            _selectedUnitIndex = Mathf.Clamp(_selectedUnitIndex, 0, _units.Count - 1);
            for (int i = 0; i < _units.Count; i++)
            {
                int index = i;
                DistroDefinition unit = _units[i];
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
                _list.Add(row);
                _rows.Add(row);
            }

            SelectUnit(_selectedUnitIndex);
        }

        private void RenderLanguages()
        {
            if (_list == null || _detail == null)
            {
                return;
            }

            _rows.Clear();
            _list.Clear();
            _selectedLanguageIndex = Mathf.Clamp(_selectedLanguageIndex, 0, _languages.Count - 1);

            for (int i = 0; i < _languages.Count; i++)
            {
                int index = i;
                LanguageCatalogEntry language = _languages[i];
                bool unlocked = LanguageUnlock.IsUnlocked(language.Language, _playerCollection);
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
                _list.Add(row);
                _rows.Add(row);
            }

            SelectLanguage(_selectedLanguageIndex);
        }

        private void SelectUnit(int index)
        {
            if (_units.Count == 0)
            {
                return;
            }

            _selectedUnitIndex = Mathf.Clamp(index, 0, _units.Count - 1);
            ApplySelection(_selectedUnitIndex);
            RenderUnitDetail(_units[_selectedUnitIndex]);
        }

        private void SelectLanguage(int index)
        {
            _selectedLanguageIndex = Mathf.Clamp(index, 0, _languages.Count - 1);
            ApplySelection(_selectedLanguageIndex);
            RenderLanguageDetail(_languages[_selectedLanguageIndex]);
        }

        private void SelectCard(int index)
        {
            if (_cards.Count == 0)
            {
                return;
            }

            _selectedCardIndex = Mathf.Clamp(index, 0, _cards.Count - 1);
            ApplySelection(_selectedCardIndex);
            RenderCardDetail(_cards[_selectedCardIndex]);
        }

        private void ApplySelection(int selectedIndex)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].EnableInClassList("selected", i == selectedIndex);
            }
        }

        private void RenderUnitDetail(DistroDefinition unit)
        {
            _detail.Clear();

            Label name = new(DistroPresentation.DisplayName(unit));
            name.AddToClassList("collection-detail-name");
            name.style.color = new StyleColor(unit.AccentColor);
            _detail.Add(name);

            Label description = new(string.IsNullOrWhiteSpace(unit.Description) ? "--" : unit.Description);
            description.AddToClassList("collection-detail-description");
            _detail.Add(description);
            AddPassiveDetails(_detail, unit);

            VisualElement readout = new();
            readout.AddToClassList("collection-detail-readout");

            Label artLabel = new();
            DistroArtPresenter.ConfigureArtLabel(artLabel, _monospaceFont);
            AsciiArtFitter artFitter = new(artLabel, _monospaceFont);
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
            _detail.Add(readout);
            AddVersionUpgradePanel(_detail, unit);
            _detail.Add(BuildSubviewCommand($"cat ~/units/{unit.Id}/cards", "exclusive packages", HasListableCards(unit), () => OpenUnitCards(unit), "no packages installed"));
        }

        private void AddVersionUpgradePanel(VisualElement target, DistroDefinition unit)
        {
            int version = _playerCollection == null ? 1 : Mathf.Clamp(_playerCollection.GetVersion(unit.Id), 1, GachaTuning.MaxVersion);
            string release = DistroVersionCatalog.GetReleaseLabel(unit.Id, version);
            int merges = _getMergesBalance?.Invoke() ?? 0;

            VisualElement panel = new();
            panel.AddToClassList("version-upgrade-panel");

            VisualElement summary = new();
            summary.AddToClassList("version-upgrade-summary");

            Label current = new($"{DistroPresentation.DisplayName(unit)} {release}  v{version}/{GachaTuning.MaxVersion}");
            current.AddToClassList("version-upgrade-current");
            summary.Add(current);

            Label wallet = new(version < GachaTuning.MaxVersion ? $"merges {merges} / {GachaTuning.GetVersionUpgradeCost(version + 1)}" : $"merges {merges}");
            wallet.AddToClassList("version-upgrade-wallet");
            summary.Add(wallet);
            panel.Add(summary);

            if (version < GachaTuning.MaxVersion)
            {
                int targetVersion = version + 1;
                int cost = GachaTuning.GetVersionUpgradeCost(targetVersion);
                Label next = new($"next {DistroVersionCatalog.GetReleaseLabel(unit.Id, targetVersion)} - {DistroVersionCatalog.GetEffectSummary(unit.Id, targetVersion)}");
                next.AddToClassList("version-upgrade-next");
                panel.Add(next);

                bool affordable = merges >= cost;
                wallet.EnableInClassList(affordable ? "version-upgrade-wallet-affordable" : "version-upgrade-wallet-warning", true);
                panel.Add(BuildCompactUpgradeCommand($"> apt full-upgrade  ({cost} merges)", affordable ? "ready" : "insufficient merges", affordable && _tryUpgradeUnit != null, () => UpgradeUnit(unit)));
            }
            else
            {
                Label next = new("latest release installed");
                next.AddToClassList("version-upgrade-next");
                panel.Add(next);
                panel.Add(BuildCompactUpgradeCommand("> apt full-upgrade", "latest release", false, null));
            }

            VisualElement path = new();
            path.AddToClassList("version-path");
            for (int i = 1; i <= GachaTuning.MaxVersion; i++)
            {
                string className = i <= version ? "version-path-owned" : i == version + 1 ? "version-path-next" : "version-path-locked";
                Label step = new($"v{i} {DistroVersionCatalog.GetReleaseLabel(unit.Id, i)}");
                step.AddToClassList("version-path-step");
                step.AddToClassList(className);
                path.Add(step);
            }

            panel.Add(path);
            target.Add(panel);
        }

        private void UpgradeUnit(DistroDefinition unit)
        {
            if (unit == null || _tryUpgradeUnit == null)
            {
                return;
            }

            VersionUpgradeResult result = _tryUpgradeUnit(unit);
            RenderUnitDetail(unit);
            if (!result.Success)
            {
                _detail.Add(BuildDetailLine("upgrade", FormatUpgradeFailure(result.FailureReason)));
            }
        }

        private void RenderLanguageDetail(LanguageCatalogEntry language)
        {
            _detail.Clear();
            bool unlocked = LanguageUnlock.IsUnlocked(language.Language, _playerCollection);

            Label name = new(language.DisplayName);
            name.AddToClassList("collection-detail-name");
            _detail.Add(name);
            _detail.Add(BuildDetailLine("track", language.ResolutionTrack.ToString()));

            Label how = new(language.HowItWorks);
            how.AddToClassList("collection-detail-description");
            _detail.Add(how);

            if (!unlocked)
            {
                string hintText = string.IsNullOrWhiteSpace(language.UnlockHint)
                    ? $"unlock: own a distro that speaks {language.DisplayName} ({FormatSupportingDistros(language)})"
                    : language.UnlockHint;
                Label hint = new(hintText);
                hint.AddToClassList("package-notice");
                _detail.Add(hint);
                return;
            }

            _detail.Add(BuildSubviewCommand($"cat ~/lang/{GetLanguageId(language.Language)}/deck", "starter deck", true, () => OpenLanguageDeck(language), null));
        }

        private void OpenUnitCards(DistroDefinition unit)
        {
            if (!HasListableCards(unit))
            {
                return;
            }

            _returnMode = CollectionMode.Units;
            _cards = BuildUnitCardEntries(unit);
            _emptyCardMessage = "no packages installed";
            CurrentTitle = $"$ cat ~/units/{unit.Id}/cards";
            CurrentHint = "[esc] back   [arrows] navigate";
            RenderCardSubview();
            ViewChanged?.Invoke();
        }

        private void OpenLanguageDeck(LanguageCatalogEntry language)
        {
            _returnMode = CollectionMode.Languages;
            _cards = BuildStarterDeckEntries(language);
            _emptyCardMessage = "starter deck not yet available";
            CurrentTitle = $"$ cat ~/lang/{GetLanguageId(language.Language)}/deck";
            CurrentHint = "[esc] back   [arrows] navigate";
            RenderCardSubview();
            ViewChanged?.Invoke();
        }

        private void RenderCardSubview()
        {
            _mode = CollectionMode.CardSubview;
            _rows.Clear();
            _list.Clear();
            _detail.Clear();
            _selectedCardIndex = Mathf.Clamp(_selectedCardIndex, 0, Math.Max(0, _cards.Count - 1));

            if (_cards.Count == 0)
            {
                _detail.Add(new Label(_emptyCardMessage) { name = "CollectionEmptyTitle" });
                _detail.Add(new Label("check back after deck data is installed") { name = "CollectionEmptyHint" });
                return;
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                int index = i;
                CardEntry entry = _cards[i];
                VisualElement row = BuildCardRow(entry);
                row.RegisterCallback<PointerEnterEvent>(_ => SelectCard(index));
                row.RegisterCallback<ClickEvent>(_ => SelectCard(index));
                _list.Add(row);
                _rows.Add(row);
            }

            SelectCard(_selectedCardIndex);
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
            _detail.Clear();

            CardDefinition card = entry.Card;
            Label name = new(GetCardDisplayName(card));
            name.AddToClassList("collection-detail-name");
            _detail.Add(name);

            Label description = new(card == null || string.IsNullOrWhiteSpace(card.Description) ? "--" : card.Description);
            description.AddToClassList("collection-detail-description");
            _detail.Add(description);

            if (card != null && !string.IsNullOrWhiteSpace(card.FlavorText))
            {
                Label flavor = new(card.FlavorText);
                flavor.AddToClassList("flavor-text");
                _detail.Add(flavor);
            }

            _detail.Add(BuildDetailLine("lang", card == null ? "--" : card.Language.ToString()));
            _detail.Add(BuildDetailLine("rarity", card == null ? "--" : card.Rarity.ToString()));
            _detail.Add(BuildDetailLine("cost", card == null ? "--" : card.CycleCost.ToString()));
            _detail.Add(BuildDetailLine("track", card == null ? "--" : card.ResolutionTrack.ToString()));
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

        private static VisualElement BuildCompactUpgradeCommand(string command, string state, bool enabled, Action action)
        {
            VisualElement row = new();
            row.AddToClassList("version-upgrade-command");
            row.EnableInClassList("disabled-row", !enabled);

            Label commandLabel = new(command);
            commandLabel.AddToClassList("version-upgrade-command-text");
            Label stateLabel = new(state);
            stateLabel.AddToClassList("version-upgrade-command-state");

            row.Add(commandLabel);
            row.Add(stateLabel);

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
                if (card == null || card.IsToken || card.IsRunOnly)
                {
                    continue;
                }

                entries.Add(new CardEntry(unit, card, 1));
            }

            return entries;
        }

        private IReadOnlyList<CardEntry> BuildStarterDeckEntries(LanguageCatalogEntry language)
        {
            LanguageDeckDefinition deck = _languageDeckDatabase == null ? null : _languageDeckDatabase.FindByLanguage(language.Language);
            if (deck == null)
            {
                return BuildRegisteredStarterDeckEntries(language.Language);
            }

            List<CardEntry> entries = new();
            for (int i = 0; i < deck.Entries.Count; i++)
            {
                LanguageDeckDefinition.LanguageDeckEntry deckEntry = deck.Entries[i];
                if (deckEntry.Card == null || deckEntry.Card.IsRunOnly || deckEntry.Count <= 0)
                {
                    continue;
                }

                entries.Add(new CardEntry(null, deckEntry.Card, deckEntry.Count));
            }

            return entries;
        }

        private IReadOnlyList<CardEntry> BuildRegisteredStarterDeckEntries(Language language)
        {
            if (_cardDatabase == null)
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
                CardDefinition card = _cardDatabase.FindById(cardRefs[i].Id);
                if (card == null || card.IsRunOnly)
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

        private static string FormatUpgradeFailure(VersionUpgradeFailureReason reason)
        {
            return reason switch
            {
                VersionUpgradeFailureReason.NotOwned => "not owned",
                VersionUpgradeFailureReason.MaxVersion => "latest release",
                VersionUpgradeFailureReason.InsufficientMerges => "insufficient merges",
                _ => "upgrade failed"
            };
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
