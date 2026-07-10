using System;
using System.Collections.Generic;
using KernelPanic.Combat;
using KernelPanic.Core;
using KernelPanic.Data;
using KernelPanic.Meta;
using KernelPanic.Run;
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
        private VisualElement _root;
        private VisualElement _packageEquipOverlay;
        private VisualElement _packageEquipDialog;
        private FontAsset _monospaceFont;
        private LanguageDeckDatabase _languageDeckDatabase;
        private CardDatabase _cardDatabase;
        private PlayerCollection _playerCollection;
        private PackageDatabase _packageDatabase;
        private PackageLoadout _packageLoadout;
        private Action _packageLoadoutChanged;
        private Func<SaveData> _getSaveData;
        private Func<OwnedPackageInstance, PackageUpgradeResult> _tryUpgradePackage;
        private Func<OwnedPackageInstance, PackageScrapResult> _tryScrapPackage;
        private Func<DistroDefinition, int> _getMergesBalance;
        private Func<DistroDefinition, VersionUpgradeResult> _tryUpgradeUnit;
        private IReadOnlyList<DistroDefinition> _units = Array.Empty<DistroDefinition>();
        private IReadOnlyList<LanguageCatalogEntry> _languages = LanguageCatalog.All;
        private IReadOnlyList<CardEntry> _cards = Array.Empty<CardEntry>();
        private CollectionMode _mode;
        private CollectionMode _returnMode;
        private UnitDetailTab _unitDetailTab = UnitDetailTab.Overview;
        private PackageSlot? _packagePickerSlot;
        private bool _unitDetailReturnsToOverview = true;
        private DistroDefinition _equipWindowUnit;
        private PackageSlot _equipWindowSlot;
        private int _selectedUnitIndex;
        private int _selectedLanguageIndex;
        private int _selectedCardIndex;
        private string _emptyCardMessage = "no cards installed";
        private string _pendingScrapPackageId;
        private string _packageSlotNotice;
        private IReadOnlyList<OwnedPackageInstance> _packageInventory = Array.Empty<OwnedPackageInstance>();
        private int _selectedPackageInventoryIndex;
        private PackageSlot? _packageInventoryFilter;

        public event Action ViewChanged;

        public void Bind(VisualElement root, FontAsset artFont, LanguageDeckDatabase deckDatabase, CardDatabase cardsDatabase, PlayerCollection collection, Func<DistroDefinition, int> getMergesBalance = null, Func<DistroDefinition, VersionUpgradeResult> tryUpgradeUnit = null, PackageDatabase packageDatabase = null, PackageLoadout packageLoadout = null, Action packageLoadoutChanged = null, Func<SaveData> getSaveData = null, Func<OwnedPackageInstance, PackageUpgradeResult> tryUpgradePackage = null, Func<OwnedPackageInstance, PackageScrapResult> tryScrapPackage = null)
        {
            _monospaceFont = artFont;
            _root = root;
            _languageDeckDatabase = deckDatabase;
            _cardDatabase = cardsDatabase;
            _playerCollection = collection;
            _getMergesBalance = getMergesBalance;
            _tryUpgradeUnit = tryUpgradeUnit;
            _packageDatabase = packageDatabase;
            _packageLoadout = packageLoadout;
            _packageLoadoutChanged = packageLoadoutChanged;
            _getSaveData = getSaveData;
            _tryUpgradePackage = tryUpgradePackage;
            _tryScrapPackage = tryScrapPackage;
            _list = root.Q<VisualElement>("CollectionList");
            _detailScroll = root.Q<ScrollView>("CollectionDetail");
            _detail = _detailScroll?.contentContainer;
            EnsurePackageEquipOverlay();
        }

        public bool IsCardSubview => _mode == CollectionMode.CardSubview;
        public bool IsUnitDetail => _mode == CollectionMode.UnitDetail;
        public bool IsPushedSubview => _mode == CollectionMode.UnitDetail || _mode == CollectionMode.CardSubview;

        public string CurrentTitle { get; private set; } = "$ ls ~/collection";
        public string CurrentHint { get; private set; } = "[esc] back   [left/right] tabs   [tab] tabs   [arrows] navigate   [enter] select";

        public void RefreshUnits(IReadOnlyList<DistroDefinition> ownedUnits)
        {
            _units = ownedUnits ?? Array.Empty<DistroDefinition>();
            ShowUnits();
        }

        public void ShowUnits()
        {
            ClosePackageEquipWindow();
            _mode = CollectionMode.Units;
            CurrentTitle = "$ ls ~/collection";
            CurrentHint = "[esc] back   [left/right] tabs   [tab] tabs   [arrows] navigate   [enter] select";
            RenderUnits();
            ViewChanged?.Invoke();
        }

        public void ShowLanguages()
        {
            ClosePackageEquipWindow();
            _mode = CollectionMode.Languages;
            CurrentTitle = "$ ls ~/collection/languages";
            CurrentHint = "[esc] back   [left/right] tabs   [tab] tabs   [arrows] navigate   [enter] select";
            RenderLanguages();
            ViewChanged?.Invoke();
        }

        public void ShowPackages()
        {
            ClosePackageEquipWindow();
            _mode = CollectionMode.Packages;
            _pendingScrapPackageId = null;
            CurrentTitle = "$ ls ~/packages";
            CurrentHint = "[esc] back   [left/right] tabs   [tab] tabs   [arrows] navigate   [click] upgrade/scrap";
            RenderPackages();
            ViewChanged?.Invoke();
        }

        public bool BackFromSubview()
        {
            if (_packageEquipOverlay != null && !_packageEquipOverlay.ClassListContains("hidden"))
            {
                ClosePackageEquipWindow();
                return true;
            }

            if (_mode == CollectionMode.UnitDetail)
            {
                _packagePickerSlot = null;
                _packageSlotNotice = null;
                if (_unitDetailReturnsToOverview)
                {
                    ShowUnits();
                    return true;
                }

                return false;
            }

            if (_mode != CollectionMode.CardSubview)
            {
                return false;
            }

            if (_returnMode == CollectionMode.Languages)
            {
                ShowLanguages();
            }
            else if (_returnMode == CollectionMode.UnitDetail)
            {
                OpenUnitDetail(_units[_selectedUnitIndex], false, _unitDetailReturnsToOverview);
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

            if (_mode == CollectionMode.Packages)
            {
                SelectPackageInventory(_selectedPackageInventoryIndex + delta);
                return;
            }

            if (_mode == CollectionMode.UnitDetail)
            {
                return;
            }

            SelectCard(_selectedCardIndex + delta);
        }

        public void ActivateSelected()
        {
            if (_mode == CollectionMode.Units && _units.Count > 0)
            {
                OpenUnitDetail(_units[_selectedUnitIndex], true);
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

            if (_mode == CollectionMode.UnitDetail && _units.Count > 0)
            {
                DistroDefinition unit = _units[_selectedUnitIndex];
                switch (_unitDetailTab)
                {
                    case UnitDetailTab.Upgrade:
                        UpgradeUnit(unit);
                        break;
                    case UnitDetailTab.Packages:
                        _packagePickerSlot ??= PackageSlot.Kernel;
                        OpenPackageEquipWindow(unit, _packagePickerSlot.Value);
                        break;
                    case UnitDetailTab.Cards:
                        OpenUnitCards(unit);
                        break;
                }
            }
        }

        public void SwitchUnitDetailTabRelative(int delta)
        {
            if (_mode != CollectionMode.UnitDetail || _units.Count == 0)
            {
                return;
            }

            int count = Enum.GetValues(typeof(UnitDetailTab)).Length;
            _unitDetailTab = (UnitDetailTab)(((int)_unitDetailTab + delta + count) % count);
            _packagePickerSlot = null;
            _packageSlotNotice = null;
            RenderUnitDetail(_units[_selectedUnitIndex]);
            ViewChanged?.Invoke();
        }

        public void ShowUnitDetail(DistroDefinition unit, bool returnToOverview)
        {
            SelectUnitById(unit?.Id);
            OpenUnitDetail(unit, true, returnToOverview);
        }

        private void RenderUnits()
        {
            if (_list == null || _detail == null)
            {
                return;
            }

            _rows.Clear();
            _list.Clear();
            _list.AddToClassList("hidden");
            _detailScroll?.AddToClassList("collection-detail-full");
            if (_detailScroll != null)
            {
                _detailScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            }

            _detail.Clear();

            if (_units.Count == 0)
            {
                _detail.Add(new Label("no units installed") { name = "CollectionEmptyTitle" });
                _detail.Add(new Label("run: curl gacha.sh | sh") { name = "CollectionEmptyHint" });
                return;
            }

            Label title = new("installed units");
            title.AddToClassList("detail-section-title");
            _detail.Add(title);

            VisualElement roster = new();
            roster.AddToClassList("unit-overview-roster");
            _detail.Add(roster);

            _selectedUnitIndex = Mathf.Clamp(_selectedUnitIndex, 0, _units.Count - 1);
            for (int i = 0; i < _units.Count; i++)
            {
                int index = i;
                DistroDefinition unit = _units[i];
                VisualElement row = BuildUnitOverviewRow(unit);
                row.RegisterCallback<PointerEnterEvent>(_ => SelectUnit(index));
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    SelectUnit(index);
                    OpenUnitDetail(unit, true, true);
                });
                roster.Add(row);
                _rows.Add(row);
            }

            ApplySelection(_selectedUnitIndex);
        }

        private void RenderLanguages()
        {
            if (_list == null || _detail == null)
            {
                return;
            }

            _rows.Clear();
            _list.Clear();
            _detail.Clear();
            _list.RemoveFromClassList("hidden");
            _detailScroll?.RemoveFromClassList("collection-detail-full");
            if (_detailScroll != null)
            {
                _detailScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            }

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

        private void RenderPackages()
        {
            if (_list == null || _detail == null)
            {
                return;
            }

            _rows.Clear();
            _list.Clear();
            _detail.Clear();
            _list.RemoveFromClassList("hidden");
            _detailScroll?.RemoveFromClassList("collection-detail-full");
            if (_detailScroll != null)
            {
                _detailScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            }

            SaveData saveData = _getSaveData?.Invoke();
            int cache = Math.Max(0, saveData?.cacheBalance ?? 0);
            int bandwidth = Math.Max(0, saveData?.bandwidthBalance ?? 0);

            VisualElement filterBar = new();
            filterBar.AddToClassList("collection-actions");
            AddPackageFilterButton(filterBar, null, "all");
            AddPackageFilterButton(filterBar, PackageSlot.Kernel, "kernel");
            AddPackageFilterButton(filterBar, PackageSlot.Runtime, "runtime");
            AddPackageFilterButton(filterBar, PackageSlot.Daemon, "daemon");
            _list.Add(filterBar);
            _list.Add(BuildDetailLine("wallet", $"Cache={cache} Bandwidth={bandwidth}"));

            List<OwnedPackageInstance> packages = BuildSortedOwnedPackages(_packageInventoryFilter);
            _packageInventory = packages;

            if (packages.Count == 0)
            {
                _detail.Add(new Label("no packages owned") { name = "CollectionEmptyTitle" });
                _detail.Add(new Label("pull packages via gacha, or check a different slot filter") { name = "CollectionEmptyHint" });
                return;
            }

            _selectedPackageInventoryIndex = Mathf.Clamp(_selectedPackageInventoryIndex, 0, packages.Count - 1);
            PackageSlot? lastSlot = null;
            for (int i = 0; i < packages.Count; i++)
            {
                int index = i;
                OwnedPackageInstance owned = packages[i];
                if (!_packageInventoryFilter.HasValue && owned.Definition.Slot != lastSlot)
                {
                    lastSlot = owned.Definition.Slot;
                    Label header = new(lastSlot.Value.ToString().ToUpperInvariant());
                    header.AddToClassList("detail-section-title");
                    _list.Add(header);
                }

                VisualElement row = BuildPackageInventoryRow(owned, cache, bandwidth);
                row.RegisterCallback<PointerEnterEvent>(_ => SelectPackageInventory(index));
                row.RegisterCallback<ClickEvent>(_ => SelectPackageInventory(index));
                _list.Add(row);
                _rows.Add(row);
            }

            SelectPackageInventory(_selectedPackageInventoryIndex);
        }

        private void AddPackageFilterButton(VisualElement target, PackageSlot? slot, string label)
        {
            Button button = new(() =>
            {
                _packageInventoryFilter = slot;
                _selectedPackageInventoryIndex = 0;
                RenderPackages();
                ViewChanged?.Invoke();
            })
            {
                text = label,
                focusable = false
            };
            button.AddToClassList("collection-tab");
            button.EnableInClassList("selected", _packageInventoryFilter == slot);
            target.Add(button);
        }

        private List<OwnedPackageInstance> BuildSortedOwnedPackages(PackageSlot? filter)
        {
            List<OwnedPackageInstance> packages = new();
            if (_playerCollection == null)
            {
                return packages;
            }

            for (int i = 0; i < _playerCollection.OwnedPackageInstances.Count; i++)
            {
                OwnedPackageInstance owned = _playerCollection.OwnedPackageInstances[i];
                if (owned?.Definition == null || (filter.HasValue && owned.Definition.Slot != filter.Value))
                {
                    continue;
                }

                packages.Add(owned);
            }

            packages.Sort((a, b) =>
            {
                int slotCompare = ((int)a.Definition.Slot).CompareTo((int)b.Definition.Slot);
                if (slotCompare != 0)
                {
                    return slotCompare;
                }

                int rarityCompare = b.Definition.Rarity.CompareTo(a.Definition.Rarity);
                return rarityCompare != 0 ? rarityCompare : string.Compare(PackageName(a.Definition), PackageName(b.Definition), StringComparison.OrdinalIgnoreCase);
            });

            return packages;
        }

        private VisualElement BuildPackageInventoryRow(OwnedPackageInstance owned, int cache, int bandwidth)
        {
            PackageDefinition package = owned.Definition;
            VisualElement row = new();
            row.AddToClassList("collection-row");
            row.AddToClassList("package-slot-row");

            Label name = new(PackageName(package));
            name.AddToClassList("package-slot-name");
            name.style.color = new StyleColor(RarityPresentation.ForStars(package.Rarity).Color);
            row.Add(name);

            Label slot = new(package.Slot.ToString().ToUpperInvariant());
            slot.AddToClassList("package-slot-label");
            row.Add(slot);

            RarityStyle rarity = RarityPresentation.ForStars(package.Rarity);
            Label rarityLabel = new(rarity.Badge);
            rarityLabel.AddToClassList("package-slot-rarity");
            rarityLabel.AddToClassList(rarity.ClassName);
            row.Add(rarityLabel);

            Label level = new($"Lv {owned.UpgradeLevel}/{PackageTuning.MaxPackageLevel}");
            level.AddToClassList("package-slot-level");
            row.Add(level);

            Label effect = new(OneLineEffect(package, owned.UpgradeLevel, null));
            effect.AddToClassList("package-slot-effect");
            row.Add(effect);

            string equippedOn = FindEquippedDistroName(package.Id);
            if (equippedOn != null)
            {
                Label tag = new($"@ {equippedOn}");
                tag.AddToClassList("package-notice");
                row.Add(tag);
            }

            if (CanAffordNextUpgrade(owned, cache, bandwidth))
            {
                Label hint = new("upgrade");
                hint.AddToClassList("unit-overview-upgrade");
                row.Add(hint);
            }

            return row;
        }

        private static bool CanAffordNextUpgrade(OwnedPackageInstance owned, int cache, int bandwidth)
        {
            if (owned.UpgradeLevel >= PackageTuning.MaxPackageLevel)
            {
                return false;
            }

            int nextLevel = owned.UpgradeLevel + 1;
            int rarity = owned.Definition.Rarity;
            return cache >= PackageTuning.GetUpgradeCacheCost(nextLevel, rarity) && bandwidth >= PackageTuning.GetUpgradeBandwidthCost(nextLevel, rarity);
        }

        private string FindEquippedDistroName(string packageId)
        {
            if (_packageLoadout == null || string.IsNullOrWhiteSpace(packageId))
            {
                return null;
            }

            for (int i = 0; i < _units.Count; i++)
            {
                DistroDefinition unit = _units[i];
                if (unit == null)
                {
                    continue;
                }

                foreach (KeyValuePair<PackageSlot, string> equipped in _packageLoadout.GetEquippedPackageIds(unit.Id))
                {
                    if (string.Equals(equipped.Value, packageId, StringComparison.OrdinalIgnoreCase))
                    {
                        return DistroPresentation.DisplayName(unit);
                    }
                }
            }

            return null;
        }

        private void SelectPackageInventory(int index)
        {
            if (_packageInventory.Count == 0)
            {
                return;
            }

            _selectedPackageInventoryIndex = Mathf.Clamp(index, 0, _packageInventory.Count - 1);
            ApplySelection(_selectedPackageInventoryIndex);
            RenderPackageInventoryDetail(_packageInventory[_selectedPackageInventoryIndex]);
        }

        private void RenderPackageInventoryDetail(OwnedPackageInstance owned)
        {
            _detail.Clear();
            if (owned?.Definition == null)
            {
                _detail.Add(new Label("no packages owned") { name = "CollectionEmptyTitle" });
                return;
            }

            PackageDefinition package = owned.Definition;
            SaveData saveData = _getSaveData?.Invoke();
            int cache = Math.Max(0, saveData?.cacheBalance ?? 0);
            int bandwidth = Math.Max(0, saveData?.bandwidthBalance ?? 0);

            Label name = new(PackageName(package));
            name.AddToClassList("collection-detail-name");
            name.style.color = new StyleColor(RarityPresentation.ForStars(package.Rarity).Color);
            _detail.Add(name);

            _detail.Add(BuildDetailLine("wallet", $"Cache={cache} Bandwidth={bandwidth}"));
            _detail.Add(BuildDetailLine("slot", package.Slot.ToString()));
            _detail.Add(BuildDetailLine("rarity", RarityPresentation.ForStars(package.Rarity).Stars));
            _detail.Add(BuildDetailLine("level", $"Lv {owned.UpgradeLevel}/{PackageTuning.MaxPackageLevel}"));
            _detail.Add(BuildDetailLine("equipped", FindEquippedDistroName(package.Id) ?? "not equipped"));
            _detail.Add(BuildDetailLine("current", FormatPackageEffect(package, owned.UpgradeLevel, null)));

            int nextLevel = owned.UpgradeLevel + 1;
            bool maxLevel = owned.UpgradeLevel >= PackageTuning.MaxPackageLevel;
            int upgradeCacheCost = maxLevel ? 0 : PackageTuning.GetUpgradeCacheCost(nextLevel, package.Rarity);
            int upgradeBandwidthCost = maxLevel ? 0 : PackageTuning.GetUpgradeBandwidthCost(nextLevel, package.Rarity);
            bool canUpgrade = !maxLevel && cache >= upgradeCacheCost && bandwidth >= upgradeBandwidthCost && _tryUpgradePackage != null;
            string upgradeState = maxLevel ? "max level" : cache < upgradeCacheCost ? "insufficient cache" : bandwidth < upgradeBandwidthCost ? "insufficient bandwidth" : "ready";
            string nextPreview = maxLevel
                ? "max level reached"
                : $"{FormatPackageEffect(package, owned.UpgradeLevel, null)} -> {FormatPackageEffect(package, nextLevel, null)}";
            _detail.Add(BuildDetailLine("next", nextPreview));
            _detail.Add(BuildCompactUpgradeCommand($"> make upgrade  (Cache {upgradeCacheCost} / Bandwidth {upgradeBandwidthCost})", upgradeState, canUpgrade, () => UpgradePackage(owned)));

            bool equipped = _packageLoadout != null && _packageLoadout.IsEquipped(package.Id);
            int scrapCache = PackageTuning.GetCacheForRarity(package.Rarity) + PackageTuning.GetRefundedInvestedCache(owned.UpgradeLevel, package.Rarity);
            int scrapBandwidth = PackageTuning.GetRefundedInvestedBandwidth(owned.UpgradeLevel, package.Rarity);
            bool confirming = string.Equals(_pendingScrapPackageId, package.Id, StringComparison.OrdinalIgnoreCase);
            string scrapCommand = confirming ? $"> rm --purge --yes  (+{scrapCache} cache/+{scrapBandwidth} bandwidth)" : $"> rm --purge  (+{scrapCache} cache)";
            string scrapState = equipped ? "equipped: unequip first" : confirming ? "confirm permanent scrap" : "requires confirm";
            _detail.Add(BuildCompactUpgradeCommand(scrapCommand, scrapState, !equipped && _tryScrapPackage != null, () => ScrapPackage(owned)));
        }

        private VisualElement BuildUnitOverviewRow(DistroDefinition unit)
        {
            VisualElement row = new();
            row.AddToClassList("collection-row");
            row.AddToClassList("unit-overview-row");

            VisualElement top = new();
            top.AddToClassList("unit-overview-top");

            Label name = new(DistroPresentation.DisplayName(unit));
            name.AddToClassList("collection-row-name");
            name.style.color = new StyleColor(unit.AccentColor);
            top.Add(name);

            RarityStyle rarity = RarityPresentation.ForStars(GetDistroStars(unit));
            Label rarityLabel = new(rarity.Badge);
            rarityLabel.AddToClassList("unit-overview-rarity");
            rarityLabel.AddToClassList(rarity.ClassName);
            top.Add(rarityLabel);

            if (CanUpgradeUnit(unit))
            {
                Label upgrade = new("upgrade");
                upgrade.AddToClassList("unit-overview-upgrade");
                top.Add(upgrade);
            }

            row.Add(top);

            VisualElement meta = new();
            meta.AddToClassList("unit-overview-meta");
            AddLanguageTag(meta, unit.PrimaryLanguage);
            AddLanguageTag(meta, unit.SecondaryLanguage);
            row.Add(meta);

            int version = GetUnitVersion(unit);
            Label release = new($"{DistroPresentation.DisplayName(unit)} {DistroVersionCatalog.GetReleaseLabel(unit.Id, version)} · v{version}/{GachaTuning.MaxVersion}");
            release.AddToClassList("unit-overview-release");
            row.Add(release);

            VisualElement slots = new();
            slots.AddToClassList("unit-overview-slots");
            AddPackageSlotDot(slots, unit, PackageSlot.Kernel);
            AddPackageSlotDot(slots, unit, PackageSlot.Runtime);
            AddPackageSlotDot(slots, unit, PackageSlot.Daemon);
            row.Add(slots);

            return row;
        }

        private void AddLanguageTag(VisualElement target, Language language)
        {
            Label tag = new(language.ToString());
            tag.AddToClassList("unit-language-tag");
            tag.style.color = new StyleColor(GetLanguageColor(language));
            target.Add(tag);
        }

        private void AddPackageSlotDot(VisualElement target, DistroDefinition unit, PackageSlot slot)
        {
            PackageDefinition package = _packageLoadout == null || unit == null ? null : _packageLoadout.GetEquippedPackage(unit.Id, slot);
            VisualElement dot = new();
            dot.AddToClassList("unit-package-dot");
            dot.EnableInClassList("empty", package == null);
            if (package != null)
            {
                RarityStyle rarity = RarityPresentation.ForStars(package.Rarity);
                dot.AddToClassList(rarity.ClassName);
                dot.style.backgroundColor = new StyleColor(rarity.Color);
            }

            target.Add(dot);
        }

        private bool CanUpgradeUnit(DistroDefinition unit)
        {
            if (unit == null)
            {
                return false;
            }

            int version = GetUnitVersion(unit);
            if (version >= GachaTuning.MaxVersion)
            {
                return false;
            }

            int merges = _getMergesBalance?.Invoke(unit) ?? 0;
            return merges >= GachaTuning.GetVersionUpgradeCost(version + 1);
        }

        private int GetUnitVersion(DistroDefinition unit)
        {
            return unit == null || _playerCollection == null ? 1 : Mathf.Clamp(_playerCollection.GetVersion(unit.Id), 1, GachaTuning.MaxVersion);
        }

        private static int GetDistroStars(DistroDefinition unit)
        {
            return unit != null && string.Equals(unit.Id, "arch", StringComparison.OrdinalIgnoreCase) ? 5 : 4;
        }

        private static Color GetLanguageColor(Language language)
        {
            return language switch
            {
                Language.C => new Color(0.55f, 0.85f, 1f),
                Language.CPlusPlus => new Color(0.48f, 0.72f, 1f),
                Language.Rust => new Color(1f, 0.55f, 0.35f),
                Language.Python => new Color(1f, 0.82f, 0.35f),
                Language.JavaScript => new Color(1f, 0.9f, 0.28f),
                Language.TypeScript => new Color(0.4f, 0.72f, 1f),
                Language.Haskell => new Color(0.75f, 0.55f, 1f),
                Language.Assembly => new Color(0.85f, 0.85f, 0.85f),
                Language.Java => new Color(1f, 0.58f, 0.42f),
                Language.Go => new Color(0.35f, 0.95f, 1f),
                Language.Ruby => new Color(1f, 0.35f, 0.45f),
                Language.Php => new Color(0.68f, 0.68f, 1f),
                _ => new Color(0.5f, 0.62f, 0.54f)
            };
        }

        private void SelectUnit(int index)
        {
            if (_units.Count == 0)
            {
                return;
            }

            _selectedUnitIndex = Mathf.Clamp(index, 0, _units.Count - 1);
            ApplySelection(_selectedUnitIndex);
        }

        private void SelectUnitById(string unitId)
        {
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return;
            }

            for (int i = 0; i < _units.Count; i++)
            {
                DistroDefinition unit = _units[i];
                if (unit != null && string.Equals(unit.Id, unitId, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedUnitIndex = i;
                    return;
                }
            }
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

        private void OpenUnitDetail(DistroDefinition unit, bool notify, bool returnToOverview = true)
        {
            if (unit == null)
            {
                return;
            }

            if (_mode != CollectionMode.UnitDetail)
            {
                _unitDetailTab = UnitDetailTab.Overview;
                _packagePickerSlot = null;
                _packageSlotNotice = null;
            }

            _unitDetailReturnsToOverview = returnToOverview;
            _mode = CollectionMode.UnitDetail;
            CurrentTitle = $"$ cat ~/units/{unit.Id}";
            CurrentHint = GetUnitDetailHint();
            RenderUnitDetail(unit);
            if (notify)
            {
                ViewChanged?.Invoke();
            }
        }

        private void RenderUnitDetail(DistroDefinition unit)
        {
            _list.AddToClassList("hidden");
            _detailScroll?.AddToClassList("collection-detail-full");
            if (_detailScroll != null)
            {
                _detailScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            }

            _detail.Clear();
            CurrentHint = GetUnitDetailHint();

            _detail.Add(BuildUnitDetailHeader(unit));
            _detail.Add(BuildUnitDetailTabs(unit));

            ScrollView bodyScroll = new();
            bodyScroll.AddToClassList("unit-detail-tab-body");
            VisualElement body = bodyScroll.contentContainer;
            switch (_unitDetailTab)
            {
                case UnitDetailTab.Upgrade:
                    AddUnitUpgradeTab(body, unit);
                    break;
                case UnitDetailTab.Packages:
                    AddUnitPackagesTab(body, unit);
                    break;
                case UnitDetailTab.Cards:
                    AddUnitCardsTab(body, unit);
                    break;
                default:
                    AddUnitOverviewTab(body, unit);
                    break;
            }

            _detail.Add(bodyScroll);
        }

        private VisualElement BuildUnitDetailHeader(DistroDefinition unit)
        {
            VisualElement header = new();
            header.AddToClassList("unit-detail-header");
            VisualElement nameBlock = new();
            nameBlock.AddToClassList("unit-detail-header-copy");
            int version = GetUnitVersion(unit);
            Label name = new($"{DistroPresentation.DisplayName(unit)} {DistroVersionCatalog.GetReleaseLabel(unit.Id, version)} · v{version}/{GachaTuning.MaxVersion}");
            name.AddToClassList("collection-detail-name");
            name.style.color = new StyleColor(unit.AccentColor);
            nameBlock.Add(name);

            VisualElement languages = new();
            languages.AddToClassList("unit-overview-meta");
            AddLanguageTag(languages, unit.PrimaryLanguage);
            AddLanguageTag(languages, unit.SecondaryLanguage);
            nameBlock.Add(languages);
            header.Add(nameBlock);

            RarityStyle rarity = RarityPresentation.ForStars(GetDistroStars(unit));
            Label rarityLabel = new(rarity.Stars);
            rarityLabel.AddToClassList("unit-overview-rarity");
            rarityLabel.AddToClassList(rarity.ClassName);
            header.Add(rarityLabel);
            return header;
        }

        private VisualElement BuildUnitDetailTabs(DistroDefinition unit)
        {
            VisualElement tabs = new();
            tabs.AddToClassList("unit-detail-tabs");
            AddUnitDetailTabButton(tabs, UnitDetailTab.Overview, "overview", unit);
            AddUnitDetailTabButton(tabs, UnitDetailTab.Upgrade, "upgrade", unit);
            AddUnitDetailTabButton(tabs, UnitDetailTab.Packages, "packages", unit);
            AddUnitDetailTabButton(tabs, UnitDetailTab.Cards, "cards", unit);
            return tabs;
        }

        private void AddUnitDetailTabButton(VisualElement tabs, UnitDetailTab tab, string label, DistroDefinition unit)
        {
            Button button = new(() =>
            {
                _unitDetailTab = tab;
                _packagePickerSlot = null;
                _packageSlotNotice = null;
                RenderUnitDetail(unit);
                ViewChanged?.Invoke();
            })
            {
                text = label,
                focusable = false
            };
            button.AddToClassList("collection-tab");
            button.EnableInClassList("selected", _unitDetailTab == tab);
            tabs.Add(button);
        }

        private void AddUnitOverviewTab(VisualElement target, DistroDefinition unit)
        {
            Label identityTitle = new("identity");
            identityTitle.AddToClassList("detail-section-title");
            target.Add(identityTitle);
            target.Add(BuildDetailLine("lang", DistroPresentation.FormatLanguages(unit)));

            Label description = new(string.IsNullOrWhiteSpace(unit.Description) ? "--" : unit.Description);
            description.AddToClassList("collection-detail-description");
            target.Add(description);
            AddPassiveDetails(target, unit);

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
            target.Add(readout);
        }

        private void AddUnitUpgradeTab(VisualElement target, DistroDefinition unit)
        {
            Label title = new("version upgrade");
            title.AddToClassList("detail-section-title");
            target.Add(title);
            AddVersionUpgradePanel(target, unit);
        }

        private void AddUnitCardsTab(VisualElement target, DistroDefinition unit)
        {
            Label title = new("core package cards");
            title.AddToClassList("detail-section-title");
            target.Add(title);
            target.Add(BuildSubviewCommand($"cat ~/units/{unit.Id}/cards", "exclusive packages", HasListableCards(unit), () => OpenUnitCards(unit), "no packages installed"));
        }

        private void AddUnitPackagesTab(VisualElement target, DistroDefinition unit)
        {
            if (unit == null || _packageLoadout == null || _playerCollection == null)
            {
                return;
            }

            Label title = new("packages");
            title.AddToClassList("detail-section-title");
            target.Add(title);
            _packagePickerSlot ??= PackageSlot.Kernel;

            VisualElement layout = new();
            layout.AddToClassList("packages-summary-layout");

            VisualElement slots = new();
            slots.AddToClassList("packages-slot-list");
            AddPackageSlotSummaryRow(slots, unit, PackageSlot.Kernel);
            AddPackageSlotSummaryRow(slots, unit, PackageSlot.Runtime);
            AddPackageSlotSummaryRow(slots, unit, PackageSlot.Daemon);
            layout.Add(slots);

            VisualElement impact = new();
            impact.AddToClassList("packages-impact-panel");
            AddPackageStatImpactPanel(impact, unit);
            layout.Add(impact);
            target.Add(layout);
        }

        private void AddPackageSlotSummaryRow(VisualElement target, DistroDefinition unit, PackageSlot slot)
        {
            PackageDefinition equipped = _packageLoadout.GetEquippedPackage(unit.Id, slot);
            OwnedPackageInstance owned = equipped == null ? null : _playerCollection.GetOwnedPackage(equipped.Id);
            VisualElement row = new();
            row.AddToClassList("package-slot-row");
            row.EnableInClassList("selected", _packagePickerSlot == slot);
            row.RegisterCallback<ClickEvent>(_ =>
            {
                _packagePickerSlot = slot;
                _packageSlotNotice = null;
                OpenPackageEquipWindow(unit, slot);
            });
            row.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    _packagePickerSlot = slot;
                    _packageSlotNotice = null;
                    OpenPackageEquipWindow(unit, slot);
                    evt.StopPropagation();
                }
            });
            row.focusable = true;

            Label slotLabel = new(slot.ToString().ToUpperInvariant());
            slotLabel.AddToClassList("package-slot-label");
            row.Add(slotLabel);

            Label name = new(equipped == null ? "[ empty ]" : $"[{PackageName(equipped)}]");
            name.AddToClassList("package-slot-name");
            if (equipped != null)
            {
                name.style.color = new StyleColor(RarityPresentation.ForStars(equipped.Rarity).Color);
            }

            row.Add(name);

            Label rarity = new(equipped == null ? string.Empty : RarityPresentation.ForStars(equipped.Rarity).Badge);
            rarity.AddToClassList("package-slot-rarity");
            if (equipped != null)
            {
                rarity.AddToClassList(RarityPresentation.ForStars(equipped.Rarity).ClassName);
            }

            row.Add(rarity);

            Label level = new(owned == null ? string.Empty : $"Lv {owned.UpgradeLevel}/{PackageTuning.MaxPackageLevel}");
            level.AddToClassList("package-slot-level");
            row.Add(level);

            Label effect = new(equipped == null ? string.Empty : OneLineEffect(equipped, owned?.UpgradeLevel ?? 0, unit.Id));
            effect.AddToClassList("package-slot-effect");
            row.Add(effect);

            Label action = new("[enter] equip");
            action.AddToClassList("package-slot-action");
            row.Add(action);
            target.Add(row);
        }

        private void AddPackageStatImpactPanel(VisualElement target, DistroDefinition unit)
        {
            Label title = new("stat impact");
            title.AddToClassList("detail-section-title");
            target.Add(title);

            IReadOnlyList<PackageInstance> packages = _packageLoadout.GetEquippedPackageInstances(unit.Id);
            PackageStatImpact impact = RunManager.CalculatePackageStatImpact(unit, packages);
            target.Add(BuildStatImpactLine("Uptime", impact.BaseUptime, impact.FinalUptime, impact.UptimeBonus, impact.UptimeSources));
            target.Add(BuildStatImpactLine("RAM", impact.BaseRam, impact.FinalRam, impact.RamBonus, impact.RamSources));
            target.Add(BuildStatImpactLine("Cycles", impact.BaseCycles, impact.FinalCycles, impact.CyclesBonus, impact.CyclesSources));

            VisualElement active = new();
            active.AddToClassList("packages-active-effects");
            Label activeTitle = new("active effects");
            activeTitle.AddToClassList("detail-section-title");
            active.Add(activeTitle);

            int count = 0;
            AddActivePackageEffect(active, unit, PackageSlot.Kernel, ref count);
            AddActivePackageEffect(active, unit, PackageSlot.Runtime, ref count);
            AddActivePackageEffect(active, unit, PackageSlot.Daemon, ref count);
            if (count == 0)
            {
                Label empty = new("none");
                empty.AddToClassList("collection-detail-muted");
                active.Add(empty);
            }

            target.Add(active);
        }

        private VisualElement BuildStatImpactLine(string label, int before, int after, int delta, IReadOnlyList<string> sources)
        {
            VisualElement row = new();
            row.AddToClassList("package-stat-line");
            Label key = new(label);
            key.AddToClassList("package-stat-key");
            Label values = new($"{before} -> {after}");
            values.AddToClassList("package-stat-value");
            Label bonus = new(delta > 0 ? $"(+{delta}{FormatSources(sources)})" : string.Empty);
            bonus.AddToClassList("package-stat-bonus");
            row.Add(key);
            row.Add(values);
            row.Add(bonus);
            return row;
        }

        private void AddActivePackageEffect(VisualElement target, DistroDefinition unit, PackageSlot slot, ref int count)
        {
            PackageDefinition package = _packageLoadout.GetEquippedPackage(unit.Id, slot);
            OwnedPackageInstance owned = package == null ? null : _playerCollection.GetOwnedPackage(package.Id);
            if (package == null || owned == null)
            {
                return;
            }

            PackageEffectData effect = package.EffectFor(unit.Id);
            if (package.Slot == PackageSlot.Kernel && IsStatPackageEffect(effect.Kind))
            {
                return;
            }

            string summary = OneLineEffect(package, owned.UpgradeLevel, unit.Id);
            if (string.IsNullOrWhiteSpace(summary))
            {
                return;
            }

            Label line = new($"{PackageName(package)}: {summary}");
            line.AddToClassList("package-active-effect");
            target.Add(line);
            count++;
        }

        private static bool IsStatPackageEffect(PackageEffectKind kind)
        {
            return kind == PackageEffectKind.MaxUptime
                || kind == PackageEffectKind.MaxRam
                || kind == PackageEffectKind.MaxCycles;
        }

        private static string FormatSources(IReadOnlyList<string> sources)
        {
            return sources == null || sources.Count == 0 ? string.Empty : $", {string.Join(", ", sources)}";
        }

        private void AddSelectedPackageSlotActions(VisualElement target, DistroDefinition unit, PackageSlot slot, int cache, int bandwidth)
        {
            PackageDefinition equipped = _packageLoadout.GetEquippedPackage(unit.Id, slot);
            OwnedPackageInstance owned = equipped == null ? null : _playerCollection.GetOwnedPackage(equipped.Id);
            Label title = new($"{slot.ToString().ToLowerInvariant()} actions");
            title.AddToClassList("detail-section-title");
            target.Add(title);
            if (!string.IsNullOrWhiteSpace(_packageSlotNotice))
            {
                Label notice = new(_packageSlotNotice);
                notice.AddToClassList("package-notice");
                notice.AddToClassList("package-slot-notice");
                target.Add(notice);
            }

            if (owned == null || equipped == null)
            {
                target.Add(BuildDetailLine("status", "empty slot"));
                target.Add(BuildDetailLine("equip", "select an owned package below"));
                return;
            }

            target.Add(BuildDetailLine("level", $"Lv {owned.UpgradeLevel}/{PackageTuning.MaxPackageLevel}"));

            int nextLevel = owned.UpgradeLevel + 1;
            bool maxLevel = owned.UpgradeLevel >= PackageTuning.MaxPackageLevel;
            int cacheCost = maxLevel ? 0 : PackageTuning.GetUpgradeCacheCost(nextLevel, equipped.Rarity);
            int bandwidthCost = maxLevel ? 0 : PackageTuning.GetUpgradeBandwidthCost(nextLevel, equipped.Rarity);
            bool canUpgrade = !maxLevel && cache >= cacheCost && bandwidth >= bandwidthCost && _tryUpgradePackage != null;
            string upgradeState = maxLevel ? "max level" : cache < cacheCost ? "insufficient cache" : bandwidth < bandwidthCost ? "insufficient bandwidth" : "ready";
            string currentEffect = FormatPackageEffect(equipped, owned.UpgradeLevel, unit.Id);
            string nextPreview = maxLevel ? "max level reached" : $"{currentEffect} -> {FormatPackageEffect(equipped, nextLevel, unit.Id)}";
            target.Add(BuildPackagePreviewLine("current", currentEffect));
            target.Add(BuildPackagePreviewLine("next", nextPreview));
            target.Add(BuildCompactUpgradeCommand($"> make upgrade  (Cache {cacheCost} / Bandwidth {bandwidthCost})", upgradeState, canUpgrade, () => UpgradePackage(owned)));
        }

        private void AddPackagePicker(VisualElement target, DistroDefinition unit, PackageSlot slot)
        {
            Label title = new($"select {slot.ToString().ToLowerInvariant()} package");
            title.AddToClassList("detail-section-title");
            target.Add(title);

            PackageDefinition equipped = _packageLoadout.GetEquippedPackage(unit.Id, slot);
            for (int i = 0; i < _playerCollection.OwnedPackages.Count; i++)
            {
                PackageDefinition package = _playerCollection.OwnedPackages[i];
                if (package == null || package.Slot != slot)
                {
                    continue;
                }

                bool isEquipped = equipped != null && string.Equals(equipped.Id, package.Id, StringComparison.OrdinalIgnoreCase);
                int packageLevel = _playerCollection.GetOwnedPackage(package.Id)?.UpgradeLevel ?? 0;
                VisualElement row = new();
                row.AddToClassList("package-row");
                row.AddToClassList("package-picker-row");
                row.EnableInClassList("equipped", isEquipped);
                row.EnableInClassList($"rarity-{package.Rarity}", true);
                row.RegisterCallback<ClickEvent>(_ => TogglePackage(unit, slot, package, isEquipped));

                VisualElement summary = new();
                summary.AddToClassList("package-summary");
                Label marker = new(isEquipped ? "[x]" : "[ ]");
                marker.AddToClassList("package-marker");
                Label name = new(PackageName(package));
                name.AddToClassList("package-name");
                Label effect = new(OneLineEffect(package, packageLevel, unit.Id));
                effect.AddToClassList("package-picker-effect");
                Label meta = new(package.IsIntendedFor(unit.Id) ? $"{package.Rarity}* on" : $"{package.Rarity}*");
                meta.AddToClassList("package-meta");
                summary.Add(marker);
                summary.Add(name);
                summary.Add(effect);
                summary.Add(meta);
                row.Add(summary);

                target.Add(row);
            }
        }

        private bool TogglePackage(DistroDefinition unit, PackageSlot slot, PackageDefinition package, bool isEquipped)
        {
            if (unit == null || package == null || _packageLoadout == null)
            {
                return false;
            }

            PackageLoadoutFailureReason reason;
            bool changed = isEquipped
                ? _packageLoadout.TryUnequip(unit.Id, slot, out reason)
                : _packageLoadout.TryEquip(unit.Id, slot, package.Id, out reason);

            if (changed)
            {
                _packageSlotNotice = null;
                _packageLoadoutChanged?.Invoke();
            }

            if (!changed)
            {
                _packageSlotNotice = FormatPackageFailure(reason);
            }

            RenderUnitDetail(unit);
            return changed;
        }

        private void EnsurePackageEquipOverlay()
        {
            if (_root == null || _packageEquipOverlay != null)
            {
                return;
            }

            _packageEquipOverlay = new VisualElement();
            _packageEquipOverlay.AddToClassList("modal-overlay");
            _packageEquipOverlay.AddToClassList("package-equip-overlay");
            _packageEquipOverlay.AddToClassList("hidden");
            _packageEquipDialog = new VisualElement();
            _packageEquipDialog.AddToClassList("package-equip-dialog");
            _packageEquipOverlay.Add(_packageEquipDialog);
            _root.Add(_packageEquipOverlay);
        }

        private void OpenPackageEquipWindow(DistroDefinition unit, PackageSlot slot)
        {
            if (unit == null)
            {
                return;
            }

            EnsurePackageEquipOverlay();
            _equipWindowUnit = unit;
            _equipWindowSlot = slot;
            _packageSlotNotice = null;
            RenderPackageEquipWindow();
            _packageEquipOverlay.RemoveFromClassList("hidden");
            _packageEquipOverlay.AddToClassList("modal-open");
        }

        private void ClosePackageEquipWindow()
        {
            _packageEquipOverlay?.RemoveFromClassList("modal-open");
            _packageEquipOverlay?.AddToClassList("hidden");
            _equipWindowUnit = null;
            _packageSlotNotice = null;
        }

        private void RenderPackageEquipWindow()
        {
            if (_packageEquipDialog == null || _equipWindowUnit == null)
            {
                return;
            }

            _packageEquipDialog.Clear();
            PackageDefinition equipped = _packageLoadout.GetEquippedPackage(_equipWindowUnit.Id, _equipWindowSlot);

            VisualElement header = new();
            header.AddToClassList("package-equip-header");
            Label title = new($"equip: {_equipWindowSlot.ToString().ToLowerInvariant()} - {DistroPresentation.DisplayName(_equipWindowUnit)}");
            title.AddToClassList("collection-detail-name");
            title.style.color = new StyleColor(_equipWindowUnit.AccentColor);
            header.Add(title);
            Button close = new(ClosePackageEquipWindow) { text = "x", focusable = false };
            close.AddToClassList("modal-close-button");
            header.Add(close);
            _packageEquipDialog.Add(header);

            if (!string.IsNullOrWhiteSpace(_packageSlotNotice))
            {
                Label notice = new(_packageSlotNotice);
                notice.AddToClassList("package-notice");
                _packageEquipDialog.Add(notice);
            }

            string equippedName = equipped == null ? "empty" : PackageName(equipped);
            _packageEquipDialog.Add(BuildDetailLine("current", equippedName));
            if (equipped != null)
            {
                _packageEquipDialog.Add(BuildCompactUpgradeCommand("> unlink current", "unequip", true, () => UnequipPackageFromWindow(_equipWindowUnit, _equipWindowSlot)));
            }

            ScrollView list = new(ScrollViewMode.Vertical);
            list.AddToClassList("package-equip-list");
            int count = 0;
            for (int i = 0; i < _playerCollection.OwnedPackageInstances.Count; i++)
            {
                OwnedPackageInstance owned = _playerCollection.OwnedPackageInstances[i];
                PackageDefinition package = owned?.Definition;
                if (package == null || package.Slot != _equipWindowSlot)
                {
                    continue;
                }

                list.Add(BuildEquipWindowPackageRow(owned, equipped));
                count++;
            }

            if (count == 0)
            {
                Label empty = new($"no owned {_equipWindowSlot.ToString().ToLowerInvariant()} packages");
                empty.AddToClassList("package-empty");
                list.Add(empty);
            }

            _packageEquipDialog.Add(list);
            _packageEquipDialog.Add(BuildCompactUpgradeCommand("> cancel", "back", true, ClosePackageEquipWindow));
        }

        private VisualElement BuildEquipWindowPackageRow(OwnedPackageInstance owned, PackageDefinition equipped)
        {
            PackageDefinition package = owned.Definition;
            bool isEquippedHere = equipped != null && string.Equals(equipped.Id, package.Id, StringComparison.OrdinalIgnoreCase);
            VisualElement row = new();
            row.AddToClassList("package-row");
            row.AddToClassList("package-equip-row");
            row.EnableInClassList("equipped", isEquippedHere);
            row.EnableInClassList($"rarity-{package.Rarity}", true);
            row.EnableInClassList("on-distro", package.IsIntendedFor(_equipWindowUnit.Id));
            row.RegisterCallback<ClickEvent>(_ => EquipPackageFromWindow(_equipWindowUnit, _equipWindowSlot, package, isEquippedHere));

            VisualElement summary = new();
            summary.AddToClassList("package-summary");
            Label marker = new(isEquippedHere ? "[x]" : "[ ]");
            marker.AddToClassList("package-marker");
            Label name = new(PackageName(package));
            name.AddToClassList("package-name");
            Label meta = new($"{RarityPresentation.ForStars(package.Rarity).Badge}  Lv {owned.UpgradeLevel}/{PackageTuning.MaxPackageLevel}");
            meta.AddToClassList("package-meta");
            summary.Add(marker);
            summary.Add(name);
            summary.Add(meta);
            row.Add(summary);

            Label effect = new(FormatPackageEffect(package, owned.UpgradeLevel, _equipWindowUnit.Id));
            effect.AddToClassList("package-description");
            row.Add(effect);

            VisualElement tags = new();
            tags.AddToClassList("package-equip-tags");
            string equippedOn = FindEquippedDistroName(package.Id);
            if (!string.IsNullOrWhiteSpace(equippedOn))
            {
                Label equippedTag = new($"equipped on {equippedOn}");
                equippedTag.AddToClassList("package-notice");
                tags.Add(equippedTag);
            }

            if (package.IsIntendedFor(_equipWindowUnit.Id))
            {
                Label intended = new("on-distro");
                intended.AddToClassList("package-notice");
                tags.Add(intended);
            }

            if (tags.childCount > 0)
            {
                row.Add(tags);
            }

            return row;
        }

        private void EquipPackageFromWindow(DistroDefinition unit, PackageSlot slot, PackageDefinition package, bool isEquipped)
        {
            if (isEquipped)
            {
                UnequipPackageFromWindow(unit, slot);
                return;
            }

            if (EquipOrSwapPackage(unit, slot, package))
            {
                ClosePackageEquipWindow();
                RenderUnitDetail(unit);
                ViewChanged?.Invoke();
                return;
            }

            RenderPackageEquipWindow();
        }

        private bool EquipOrSwapPackage(DistroDefinition unit, PackageSlot slot, PackageDefinition package)
        {
            if (unit == null || package == null || _packageLoadout == null)
            {
                return false;
            }

            PackageDefinition currentlyEquipped = _packageLoadout.GetEquippedPackage(unit.Id, slot);
            if (currentlyEquipped != null && !string.Equals(currentlyEquipped.Id, package.Id, StringComparison.OrdinalIgnoreCase))
            {
                if (!_packageLoadout.TryUnequip(unit.Id, slot, out PackageLoadoutFailureReason unequipReason))
                {
                    _packageSlotNotice = FormatPackageFailure(unequipReason);
                    return false;
                }
            }

            if (!_packageLoadout.TryEquip(unit.Id, slot, package.Id, out PackageLoadoutFailureReason equipReason))
            {
                _packageSlotNotice = FormatPackageFailure(equipReason);
                return false;
            }

            _packageSlotNotice = null;
            _packageLoadoutChanged?.Invoke();
            return true;
        }

        private void UnequipPackageFromWindow(DistroDefinition unit, PackageSlot slot)
        {
            PackageDefinition equipped = _packageLoadout.GetEquippedPackage(unit.Id, slot);
            if (equipped == null)
            {
                return;
            }

            if (TogglePackage(unit, slot, equipped, true))
            {
                ClosePackageEquipWindow();
                RenderUnitDetail(unit);
                ViewChanged?.Invoke();
                return;
            }

            RenderPackageEquipWindow();
        }

        private void AddVersionUpgradePanel(VisualElement target, DistroDefinition unit)
        {
            int version = _playerCollection == null ? 1 : Mathf.Clamp(_playerCollection.GetVersion(unit.Id), 1, GachaTuning.MaxVersion);
            string release = DistroVersionCatalog.GetReleaseLabel(unit.Id, version);
            int merges = _getMergesBalance?.Invoke(unit) ?? 0;

            VisualElement panel = new();
            panel.AddToClassList("version-upgrade-panel");

            VisualElement summary = new();
            summary.AddToClassList("version-upgrade-summary");

            Label current = new($"{DistroPresentation.DisplayName(unit)} {release}  v{version}/{GachaTuning.MaxVersion}");
            current.AddToClassList("version-upgrade-current");
            summary.Add(current);

            Label wallet = new(version < GachaTuning.MaxVersion ? $"{unit.DisplayName} merges {merges} / {GachaTuning.GetVersionUpgradeCost(version + 1)}" : $"{unit.DisplayName} merges {merges}");
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
            path.AddToClassList("version-roadmap");
            for (int i = 1; i <= GachaTuning.MaxVersion; i++)
            {
                bool owned = i <= version;
                bool next = i == version + 1;
                int cost = i <= 1 ? 0 : GachaTuning.GetVersionUpgradeCost(i);
                string className = owned ? "version-path-owned" : next ? "version-path-next" : "version-path-locked";
                string costText = owned || i <= 1 ? string.Empty : $"  ({cost} merges)";
                Label step = new($"v{i} {DistroVersionCatalog.GetReleaseLabel(unit.Id, i)} - {DistroVersionCatalog.GetEffectSummary(unit.Id, i)}{costText}");
                step.AddToClassList("version-roadmap-step");
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

        private void UpgradePackage(OwnedPackageInstance package)
        {
            bool inventoryMode = _mode == CollectionMode.Packages;
            PackageUpgradeResult result = _tryUpgradePackage == null ? new PackageUpgradeResult(false, PackageUpgradeFailureReason.NotOwned, 0, 0, 0) : _tryUpgradePackage(package);
            RefreshAfterPackageAction(inventoryMode);
            if (!result.Success)
            {
                _detail.Add(BuildDetailLine("package upgrade", FormatPackageUpgradeFailure(result.FailureReason)));
            }
        }

        private void ScrapPackage(OwnedPackageInstance package)
        {
            if (package == null)
            {
                return;
            }

            bool inventoryMode = _mode == CollectionMode.Packages;
            if (!string.Equals(_pendingScrapPackageId, package.PackageId, StringComparison.OrdinalIgnoreCase))
            {
                _pendingScrapPackageId = package.PackageId;
                RefreshAfterPackageAction(inventoryMode);
                return;
            }

            PackageScrapResult result = _tryScrapPackage == null ? new PackageScrapResult(false, PackageScrapFailureReason.NotOwned, 0, 0) : _tryScrapPackage(package);
            _pendingScrapPackageId = null;
            RefreshAfterPackageAction(inventoryMode);
            if (!result.Success)
            {
                _detail.Add(BuildDetailLine("package scrap", FormatPackageScrapFailure(result.FailureReason)));
            }
        }

        // Wired upgrade/scrap callbacks (MainMenuController) may call RefreshUnits() internally,
        // which forces CollectionMode.Units and re-renders. Restore the caller's actual view so
        // the Packages inventory screen (and its keyboard navigation) don't get bumped to Units.
        private void RefreshAfterPackageAction(bool inventoryMode)
        {
            if (inventoryMode)
            {
                _mode = CollectionMode.Packages;
                RenderPackages();
                return;
            }

            if (_units.Count > 0)
            {
                RenderUnitDetail(_units[_selectedUnitIndex]);
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

            _returnMode = _mode == CollectionMode.UnitDetail ? CollectionMode.UnitDetail : CollectionMode.Units;
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
            _list.RemoveFromClassList("hidden");
            _detailScroll?.RemoveFromClassList("collection-detail-full");
            if (_detailScroll != null)
            {
                _detailScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            }

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

            Label description = new(GetCardCollectionDescription(entry.Card));
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

            Label description = new(GetCardCollectionDescription(card));
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

        private static string GetCardCollectionDescription(CardDefinition card)
        {
            if (card == null)
            {
                return "--";
            }

            if (!string.IsNullOrWhiteSpace(card.Description))
            {
                return card.Description;
            }

            string rulesText = CardEffectFactory.GetRulesText(new CardInstance(card));
            return string.IsNullOrWhiteSpace(rulesText) || rulesText == "effect not authored." ? "--" : rulesText;
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

        private static VisualElement BuildPackagePreviewLine(string key, string value)
        {
            VisualElement row = new();
            row.AddToClassList("package-preview-line");

            Label keyLabel = new(key);
            keyLabel.AddToClassList("package-preview-key");
            Label valueLabel = new(value);
            valueLabel.AddToClassList("package-preview-value");

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

        private static string PackageName(PackageDefinition package)
        {
            return package == null ? "--" : string.IsNullOrWhiteSpace(package.DisplayName) ? package.Id : package.DisplayName;
        }

        private static string FormatPackageEffect(PackageDefinition package, int upgradeLevel, string distroId)
        {
            if (package == null)
            {
                return "--";
            }

            string summary = FormatEffectSummary(package.EffectFor(distroId), upgradeLevel);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                return summary;
            }

            return string.IsNullOrWhiteSpace(package.Description) ? "--" : package.Description;
        }

        private static string OneLineEffect(PackageDefinition package, int upgradeLevel, string distroId)
        {
            if (package == null)
            {
                return string.Empty;
            }

            string text = FormatEffectSummary(package.EffectFor(distroId), upgradeLevel);
            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(package.Description))
            {
                text = package.Description.Trim();
            }

            int stop = text.IndexOf('.');
            if (stop >= 0)
            {
                text = text.Substring(0, stop + 1);
            }

            return text.Length > 54 ? $"{text.Substring(0, 51)}..." : text;
        }

        private static string FormatEffectSummary(PackageEffectData baseEffect, int upgradeLevel)
        {
            PackageEffectData effect = PackageEffectScaling.Scale(baseEffect, upgradeLevel);
            int amount = Math.Max(0, effect.Amount);
            return effect.Kind switch
            {
                PackageEffectKind.None => string.Empty,
                PackageEffectKind.MaxUptime => $"+{amount} max Uptime",
                PackageEffectKind.MaxCycles => $"+{amount} max Cycles",
                PackageEffectKind.MaxRam => $"+{amount} max RAM",
                PackageEffectKind.WaveStartShield => $"+{amount} Shield at wave start",
                PackageEffectKind.FirstTurnEachWaveShield => $"+{amount} Shield on first turn",
                PackageEffectKind.FirstTurnFirstWaveDraw => $"draw {amount} on first turn",
                PackageEffectKind.WaveDraw => $"draw {amount} at wave start",
                PackageEffectKind.FirstTurnEachWaveCycle => $"+{amount} Cycle on first turn",
                PackageEffectKind.WaveGenerateBasicCard => $"generate {amount} basic card(s) at wave start",
                PackageEffectKind.FirstCardEachWaveCostReduction => $"first card each wave costs {amount} less",
                PackageEffectKind.FirstCardsEachTurnCostReduction => $"first card each turn costs {amount} less",
                PackageEffectKind.ThirdCardEachTurnGenerate => $"third card each turn generates {amount} card(s)",
                PackageEffectKind.FirstNativeCardEachWaveFlatDamage => $"first Native card +{amount} damage",
                PackageEffectKind.ExhaustShield => $"+{amount} Shield when exhausting",
                PackageEffectKind.EveryNthTurnShield => $"+{amount} Shield every {effect.Threshold} turns",
                PackageEffectKind.EveryNthTurnDraw => $"draw {amount} every {effect.Threshold} turns",
                PackageEffectKind.FirstInterpreterQueueCardEachWaveShield => $"+{amount} Shield on queued card",
                PackageEffectKind.FirstShieldEachTurnBonus => $"+{amount} extra Shield once/turn",
                PackageEffectKind.EveryNthCardEachWaveFree => $"every {effect.Threshold}th card costs 0",
                PackageEffectKind.EveryNthCardEachWaveCycle => $"+{amount} Cycle every {effect.Threshold} cards",
                PackageEffectKind.StartTurnNoDebuffShield => $"+{amount} Shield if no debuffs",
                PackageEffectKind.JavaScriptFlatDamage => $"+{amount} JavaScript damage",
                PackageEffectKind.WaveThresholdRestore => $"restore {amount} Uptime after wave {effect.Threshold}",
                PackageEffectKind.DnfFedoraPassive => "Fedora passive can trigger twice",
                PackageEffectKind.FirstCThisTurnDamageMultiplier => $"first C card each turn deals {amount}% damage",
                PackageEffectKind.TurnStartGenerateLanguageCard => amount > 0 ? "turn start: generate C/Rust card at -1c" : "turn start: generate common C/Rust card",
                _ => amount > 0 ? $"+{amount} package effect" : "package effect"
            };
        }

        private string GetUnitDetailHint()
        {
            string action = _unitDetailTab switch
            {
                UnitDetailTab.Upgrade => "[enter/click] upgrade",
                UnitDetailTab.Packages => "[enter/click] select/equip",
                UnitDetailTab.Cards => "[enter] open cards",
                _ => "[read-only]"
            };

            return $"[esc] back   [left/right/tab] tabs   {action}";
        }

        private static string FormatPackageFailure(PackageLoadoutFailureReason reason)
        {
            return reason switch
            {
                PackageLoadoutFailureReason.WrongSlot => "wrong slot",
                PackageLoadoutFailureReason.NotOwned => "package not owned",
                PackageLoadoutFailureReason.SlotOccupied => "slot occupied: unequip current package first",
                PackageLoadoutFailureReason.NotEquipped => "package not equipped",
                _ => "package change failed"
            };
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

        private static string FormatPackageUpgradeFailure(PackageUpgradeFailureReason reason)
        {
            return reason switch
            {
                PackageUpgradeFailureReason.MaxLevel => "max level",
                PackageUpgradeFailureReason.InsufficientCache => "insufficient cache",
                PackageUpgradeFailureReason.InsufficientBandwidth => "insufficient bandwidth",
                PackageUpgradeFailureReason.NotOwned => "not owned",
                _ => "upgrade failed"
            };
        }

        private static string FormatPackageScrapFailure(PackageScrapFailureReason reason)
        {
            return reason switch
            {
                PackageScrapFailureReason.Equipped => "equipped: unequip first",
                PackageScrapFailureReason.NotOwned => "not owned",
                _ => "scrap failed"
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
            Packages,
            UnitDetail,
            CardSubview
        }

        private enum UnitDetailTab
        {
            Overview,
            Upgrade,
            Packages,
            Cards
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
