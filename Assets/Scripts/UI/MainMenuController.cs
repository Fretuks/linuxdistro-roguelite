using System;
using System.Collections.Generic;
using KernelPanic.Core;
using KernelPanic.Data;
using KernelPanic.Meta;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KernelPanic.UI
{
    /// <summary>
    /// Binds the main menu terminal UI document and routes command activation between panels.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        private const string HiddenClassName = "hidden";
        private const string SelectedClassName = "selected";
        private const string CursorOnClassName = "cursor-on";
        private const float BootIntroSeconds = 1.5f;
        private static bool bootIntroPlayed;

        [SerializeField] private FontAsset monospaceFont; // TODO: Assign a real monospace FontAsset when typography assets exist.
        [SerializeField] private string motdBody = "unstable userspace detected; keep a rollback shell open.";
        [SerializeField] private DistroDatabase distroDatabase;
        [SerializeField] private CardDatabase cardDatabase;
        [SerializeField] private FeaturedUnitPanel featuredUnitPanel = new();

        private readonly List<CommandMenuEntry> commandEntries = new();
        private readonly List<VisualElement> starterCards = new();
        private readonly List<Label> starterNames = new();
        private readonly List<Label> starterLanguages = new();
        private readonly List<Label> starterDescriptions = new();
        private readonly List<VisualElement> collectionRows = new();
        private readonly List<VisualElement> cardRows = new();
        private readonly List<VisualElement> runSetupRows = new();
        private readonly List<VisualElement> packageRows = new();
        private UIDocument document;
        private VisualElement root;
        private VisualElement shellRoot;
        private VisualElement bootIntroPanel;
        private VisualElement mainMenuPanel;
        private VisualElement collectionPanel;
        private VisualElement runSetupPanel;
        private VisualElement gachaPanel;
        private VisualElement settingsPanel;
        private VisualElement eventBanner;
        private VisualElement backgroundLogLayer;
        private VisualElement starterModal;
        private VisualElement collectionList;
        private VisualElement collectionDetail;
        private VisualElement runSetupList;
        private VisualElement runSetupDetail;
        private Label appIdLabel;
        private Label entropyLabel;
        private Label pullTokensLabel;
        private Label titleCursorLabel;
        private Label promptCursorLabel;
        private Label bootIntroLogLabel;
        private Label motdBodyLabel;
        private Label starterConfirmLabel;
        private VisualElement motdBlock;
        private Button collectionBackButton;
        private Button collectionUnitsButton;
        private Button collectionCardsButton;
        private Button runSetupBackButton;
        private Button gachaBackButton;
        private Button settingsBackButton;
        private SaveService saveService;
        private SaveData saveData;
        private EntropyWallet wallet;
        private GachaService gachaService;
        private PlayerCollection playerCollection;
        private CardLoadout cardLoadout;
        private BackgroundLogRingBuffer backgroundLog;
        private IEventBannerSource eventBannerSource;
        private int selectedCommandIndex;
        private int selectedStarterIndex;
        private int selectedCollectionIndex;
        private int selectedCardIndex;
        private int selectedRunSetupIndex;
        private int selectedPackageIndex;
        private float bootIntroElapsed;
        private int bootIntroCharacterCount;
        private bool cursorVisible;
        private bool suppressNextClick;
        private bool starterModalActive;
        private bool starterConfirming;
        private bool collectionShowingCards;
        private bool warnedUnresolvedSaveId;
        private bool warnedInvalidLoadoutId;
        private string bootIntroCopy;
        private IVisualElementScheduledItem blinkSchedule;
        private IVisualElementScheduledItem bootIntroSchedule;
        private IVisualElementScheduledItem starterCloseSchedule;
        private IVisualElementScheduledItem packageNoticeSchedule;

        public void Initialize(EntropyWallet initializedWallet)
        {
            wallet = initializedWallet ?? new EntropyWallet();
            RefreshCurrencyReadouts();
        }

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            saveService = new SaveService();
            gachaService = new GachaService();
            playerCollection = new PlayerCollection(); // TODO: Replace with persistent player-collection service composition.
            cardLoadout = new CardLoadout(playerCollection.OwnedUnits);
            Initialize(new EntropyWallet()); // TODO: Replace with persistent wallet service composition.
            BindElements();
            BindCommandEntries();
            RegisterCommandEntryCallbacks();
            RegisterStarterCallbacks();
            featuredUnitPanel.Bind(root, monospaceFont);
            LoadMetaState();
            playerCollection.Changed += HandleMetaStateChanged;
            gachaService.BannerPoolChanged += HandleMetaStateChanged;
            ApplyOptionalFont();
        }

        private void OnEnable()
        {
            RegisterCallbacks();
            root.Focus();
            shellRoot.EnableInClassList("reduced-motion", UIPreferences.ReducedMotion);
            RefreshStaticText();
            RefreshCurrencyReadouts();
            RefreshEventBanner();
            featuredUnitPanel.Refresh(playerCollection.OwnedUnits);
            RefreshCollection();
            SelectCommand(0);
            ShowMainMenu();
            StartAmbientSchedules();
            PlayBootIntroIfNeeded();
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
            blinkSchedule?.Pause();
            backgroundLog?.Stop();
            bootIntroSchedule?.Pause();
            starterCloseSchedule?.Pause();
            packageNoticeSchedule?.Pause();
        }

        private void BindElements()
        {
            root = document.rootVisualElement;
            shellRoot = root.Q<VisualElement>("ShellRoot");
            bootIntroPanel = root.Q<VisualElement>("BootIntroPanel");
            mainMenuPanel = root.Q<VisualElement>("MainMenuPanel");
            collectionPanel = root.Q<VisualElement>("CollectionPanel");
            runSetupPanel = root.Q<VisualElement>("RunSetupPanel");
            gachaPanel = root.Q<VisualElement>("GachaPanel");
            settingsPanel = root.Q<VisualElement>("SettingsPanel");
            eventBanner = root.Q<VisualElement>("EventBanner");
            motdBlock = root.Q<VisualElement>("MotdBlock");
            backgroundLogLayer = root.Q<VisualElement>("BackgroundLogLayer");
            backgroundLog = new BackgroundLogRingBuffer(backgroundLogLayer, BootLogCopy.Lines);
            starterModal = root.Q<VisualElement>("StarterModal");
            collectionList = root.Q<VisualElement>("CollectionList");
            collectionDetail = root.Q<VisualElement>("CollectionDetail");
            runSetupList = root.Q<VisualElement>("RunSetupList");
            runSetupDetail = root.Q<VisualElement>("RunSetupDetail");

            appIdLabel = root.Q<Label>("AppIdLabel");
            entropyLabel = root.Q<Label>("EntropyLabel");
            pullTokensLabel = root.Q<Label>("PullTokensLabel");
            titleCursorLabel = root.Q<Label>("TitleCursorLabel");
            promptCursorLabel = root.Q<Label>("PromptCursorLabel");
            bootIntroLogLabel = root.Q<Label>("BootIntroLogLabel");
            motdBodyLabel = root.Q<Label>("MotdBodyLabel");
            starterConfirmLabel = root.Q<Label>("StarterConfirmLabel");

            for (int i = 0; i < 3; i++)
            {
                starterCards.Add(root.Q<VisualElement>($"StarterOption{i}"));
                starterNames.Add(root.Q<Label>($"StarterName{i}"));
                starterLanguages.Add(root.Q<Label>($"StarterLanguages{i}"));
                starterDescriptions.Add(root.Q<Label>($"StarterDescription{i}"));
            }

            collectionBackButton = root.Q<Button>("CollectionBackButton");
            collectionUnitsButton = root.Q<Button>("CollectionUnitsButton");
            collectionCardsButton = root.Q<Button>("CollectionCardsButton");
            runSetupBackButton = root.Q<Button>("RunSetupBackButton");
            gachaBackButton = root.Q<Button>("GachaBackButton");
            settingsBackButton = root.Q<Button>("SettingsBackButton");
        }

        private void BindCommandEntries()
        {
            commandEntries.Clear();
            commandEntries.Add(new CommandMenuEntry(root.Q<VisualElement>("CommandStartRun"), HandleStartRunClicked));
            commandEntries.Add(new CommandMenuEntry(root.Q<VisualElement>("CommandCollection"), ShowCollection));
            commandEntries.Add(new CommandMenuEntry(root.Q<VisualElement>("CommandGacha"), ShowGacha));
            commandEntries.Add(new CommandMenuEntry(root.Q<VisualElement>("CommandSettings"), ShowSettings));
            commandEntries.Add(new CommandMenuEntry(root.Q<VisualElement>("CommandQuit"), HandleQuitClicked));
        }

        private void ApplyOptionalFont()
        {
            if (monospaceFont == null)
            {
                return;
            }

            root.style.unityFontDefinition = new StyleFontDefinition(monospaceFont);
        }

        private void RegisterCallbacks()
        {
            root.RegisterCallback<KeyDownEvent>(HandleKeyDown);
            root.RegisterCallback<PointerDownEvent>(HandlePointerDown);
            collectionBackButton.clicked += ShowMainMenu;
            collectionUnitsButton.clicked += ShowCollectionUnits;
            collectionCardsButton.clicked += ShowCollectionCards;
            runSetupBackButton.clicked += ShowMainMenu;
            gachaBackButton.clicked += ShowMainMenu;
            settingsBackButton.clicked += ShowMainMenu;
        }

        private void UnregisterCallbacks()
        {
            root.UnregisterCallback<KeyDownEvent>(HandleKeyDown);
            root.UnregisterCallback<PointerDownEvent>(HandlePointerDown);
            collectionBackButton.clicked -= ShowMainMenu;
            collectionUnitsButton.clicked -= ShowCollectionUnits;
            collectionCardsButton.clicked -= ShowCollectionCards;
            runSetupBackButton.clicked -= ShowMainMenu;
            gachaBackButton.clicked -= ShowMainMenu;
            settingsBackButton.clicked -= ShowMainMenu;
        }

        private void LoadMetaState()
        {
            saveData = saveService.Load();
            saveData.EnsureLists();

            for (int i = 0; i < saveData.ownedUnitIds.Count; i++)
            {
                DistroDefinition unit = ResolveSavedDistro(saveData.ownedUnitIds[i]);
                if (unit != null)
                {
                    playerCollection.Add(unit);
                }
            }

            for (int i = 0; i < saveData.bannerPoolIds.Count; i++)
            {
                DistroDefinition unit = ResolveSavedDistro(saveData.bannerPoolIds[i]);
                if (unit != null)
                {
                    gachaService.AddToBannerPool(unit);
                }
            }

            for (int i = 0; i < saveData.cardLoadouts.Count; i++)
            {
                CardLoadoutSaveEntry entry = saveData.cardLoadouts[i];
                entry.EnsureLists();
                List<string> resolvedIds = new();
                for (int cardIndex = 0; cardIndex < entry.equippedCardIds.Count; cardIndex++)
                {
                    string resolvedId = ResolveSavedCardId(entry.equippedCardIds[cardIndex]);
                    if (!string.IsNullOrWhiteSpace(resolvedId))
                    {
                        resolvedIds.Add(resolvedId);
                    }
                }

                if (!cardLoadout.TryLoad(entry.distroId, resolvedIds, out bool skippedInvalid) || skippedInvalid)
                {
                    WarnInvalidLoadoutId();
                }
            }

            EnsureLoadoutsForOwnedUnits();
            SaveCurrentState();
        }

        private DistroDefinition ResolveSavedDistro(string id)
        {
            DistroDefinition unit = distroDatabase == null ? null : distroDatabase.FindById(id);
            if (unit == null && !warnedUnresolvedSaveId)
            {
                warnedUnresolvedSaveId = true;
                Debug.LogWarning($"Save references a distro id that is not in DistroDatabase: {id}");
            }

            return unit;
        }

        private string ResolveSavedCardId(string id)
        {
            if (cardDatabase == null)
            {
                return id;
            }

            CardDefinition card = cardDatabase.FindById(id);
            if (card == null)
            {
                WarnInvalidLoadoutId();
                return null;
            }

            return card.Id;
        }

        private void WarnInvalidLoadoutId()
        {
            if (warnedInvalidLoadoutId)
            {
                return;
            }

            warnedInvalidLoadoutId = true;
            Debug.LogWarning("Save references an invalid card loadout id; restoring a valid default loadout.");
        }

        private void HandleMetaStateChanged()
        {
            EnsureLoadoutsForOwnedUnits();
            SaveCurrentState();
            featuredUnitPanel.Refresh(playerCollection.OwnedUnits);
            RefreshCollection();
            RefreshRunSetup();
        }

        private void EnsureLoadoutsForOwnedUnits()
        {
            for (int i = 0; i < playerCollection.OwnedUnits.Count; i++)
            {
                cardLoadout.EnsureDefaultLoadout(playerCollection.OwnedUnits[i]);
            }
        }

        private void SaveCurrentState()
        {
            saveData ??= SaveData.CreateDefault();
            saveData.EnsureLists();
            saveData.ownedUnitIds.Clear();
            saveData.bannerPoolIds.Clear();

            for (int i = 0; i < playerCollection.OwnedUnits.Count; i++)
            {
                DistroDefinition unit = playerCollection.OwnedUnits[i];
                if (unit != null && !string.IsNullOrWhiteSpace(unit.Id))
                {
                    saveData.ownedUnitIds.Add(unit.Id);
                }
            }

            for (int i = 0; i < gachaService.BannerPool.Count; i++)
            {
                DistroDefinition unit = gachaService.BannerPool[i];
                if (unit != null && !string.IsNullOrWhiteSpace(unit.Id))
                {
                    saveData.bannerPoolIds.Add(unit.Id);
                }
            }

            cardLoadout.WriteTo(saveData.cardLoadouts);

            saveService.Save(saveData);
        }

        private void RefreshStaticText()
        {
            appIdLabel.text = $"kernel-panic v{Application.version} - tty1";
            motdBodyLabel.text = motdBody ?? string.Empty;
            motdBlock.EnableInClassList(HiddenClassName, string.IsNullOrWhiteSpace(motdBody));
            bootIntroCopy = string.Join("\n", BootLogCopy.Lines);
        }

        private void RegisterCommandEntryCallbacks()
        {
            for (int i = 0; i < commandEntries.Count; i++)
            {
                int index = i;
                commandEntries[i].Row.RegisterCallback<PointerEnterEvent>(_ => SelectCommand(index));
                commandEntries[i].Row.RegisterCallback<ClickEvent>(_ => HandleCommandClicked(index));
            }
        }

        private void RegisterStarterCallbacks()
        {
            for (int i = 0; i < starterCards.Count; i++)
            {
                int index = i;
                starterCards[i].RegisterCallback<PointerEnterEvent>(_ => SelectStarter(index));
                starterCards[i].RegisterCallback<ClickEvent>(_ =>
                {
                    SelectStarter(index);
                    ConfirmStarterSelectionIntent();
                });
            }
        }

        private void RefreshCurrencyReadouts()
        {
            if (entropyLabel == null || pullTokensLabel == null || gachaService == null || wallet == null)
            {
                return;
            }

            entropyLabel.text = $"entropy={wallet.Balance}";
            pullTokensLabel.text = $"pulls={gachaService.PullTokens}";
        }

        private void RefreshEventBanner()
        {
            EventBannerContent banner = eventBannerSource?.GetBanner();
            if (banner == null || string.IsNullOrWhiteSpace(banner.Text))
            {
                eventBanner.AddToClassList(HiddenClassName);
                return;
            }

            eventBanner.RemoveFromClassList(HiddenClassName);
            root.Q<Label>("EventBannerLabel").text = banner.RemainingTime.HasValue
                ? $"{banner.Text}  {banner.RemainingTime.Value:g}"
                : banner.Text;
        }

        private void RefreshCollection()
        {
            if (collectionList == null || collectionDetail == null)
            {
                return;
            }

            if (collectionShowingCards)
            {
                RefreshCollectionCards();
                return;
            }

            RefreshCollectionUnits();
        }

        private void RefreshCollectionUnits()
        {
            collectionRows.Clear();
            collectionList.Clear();
            collectionDetail.Clear();
            collectionUnitsButton?.AddToClassList(SelectedClassName);
            collectionCardsButton?.RemoveFromClassList(SelectedClassName);

            IReadOnlyList<DistroDefinition> units = playerCollection.OwnedUnits;
            if (units.Count == 0)
            {
                collectionDetail.Clear();
                collectionDetail.Add(new Label("no units installed") { name = "CollectionEmptyTitle" });
                collectionDetail.Add(new Label("run: curl gacha.sh | sh") { name = "CollectionEmptyHint" });
                return;
            }

            selectedCollectionIndex = Mathf.Clamp(selectedCollectionIndex, 0, units.Count - 1);
            for (int i = 0; i < units.Count; i++)
            {
                int index = i;
                DistroDefinition unit = units[i];
                VisualElement row = new();
                row.AddToClassList("collection-row");
                row.RegisterCallback<PointerEnterEvent>(_ => SelectCollectionUnit(index));
                row.RegisterCallback<ClickEvent>(_ => SelectCollectionUnit(index));

                Label name = new(DisplayName(unit));
                name.AddToClassList("collection-row-name");
                name.style.color = new StyleColor(unit.AccentColor);

                Label languages = new(FormatLanguages(unit));
                languages.AddToClassList("collection-row-languages");

                row.Add(name);
                row.Add(languages);
                collectionList.Add(row);
                collectionRows.Add(row);
            }

            SelectCollectionUnit(selectedCollectionIndex);
        }

        private void RefreshCollectionCards()
        {
            cardRows.Clear();
            collectionList.Clear();
            collectionDetail.Clear();
            collectionUnitsButton?.RemoveFromClassList(SelectedClassName);
            collectionCardsButton?.AddToClassList(SelectedClassName);

            IReadOnlyList<CardDefinition> cards = cardDatabase == null ? Array.Empty<CardDefinition>() : cardDatabase.AllCards;
            if (cards.Count == 0)
            {
                collectionDetail.Add(new Label("no cards indexed") { name = "CollectionEmptyTitle" });
                collectionDetail.Add(new Label("assign a CardDatabase to browse card rules here") { name = "CollectionEmptyHint" });
                return;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                CardDefinition card = cards[i];
                if (card == null)
                {
                    continue;
                }

                int index = cardRows.Count;
                VisualElement row = new();
                row.AddToClassList("collection-row");
                row.RegisterCallback<PointerEnterEvent>(_ => SelectCollectionCard(index));
                row.RegisterCallback<ClickEvent>(_ => SelectCollectionCard(index));

                Label name = new(DisplayName(card));
                name.AddToClassList("collection-row-name");

                Label meta = new($"{card.Language}  {card.CycleCost}c");
                meta.AddToClassList("collection-row-languages");

                row.Add(name);
                row.Add(meta);
                collectionList.Add(row);
                cardRows.Add(row);
            }

            if (cardRows.Count == 0)
            {
                collectionDetail.Add(new Label("no cards indexed") { name = "CollectionEmptyTitle" });
                return;
            }

            selectedCardIndex = Mathf.Clamp(selectedCardIndex, 0, cardRows.Count - 1);
            SelectCollectionCard(selectedCardIndex);
        }

        private void SelectCollectionUnit(int index)
        {
            IReadOnlyList<DistroDefinition> units = playerCollection.OwnedUnits;
            if (units.Count == 0)
            {
                return;
            }

            selectedCollectionIndex = Mathf.Clamp(index, 0, units.Count - 1);
            for (int i = 0; i < collectionRows.Count; i++)
            {
                collectionRows[i].EnableInClassList(SelectedClassName, i == selectedCollectionIndex);
            }

            RenderCollectionDetail(units[selectedCollectionIndex]);
        }

        private void SelectCollectionCard(int index)
        {
            if (cardRows.Count == 0)
            {
                return;
            }

            selectedCardIndex = Mathf.Clamp(index, 0, cardRows.Count - 1);
            for (int i = 0; i < cardRows.Count; i++)
            {
                cardRows[i].EnableInClassList(SelectedClassName, i == selectedCardIndex);
            }

            RenderCardDetail(GetVisibleCard(selectedCardIndex));
        }

        private void RenderCollectionDetail(DistroDefinition unit)
        {
            collectionDetail.Clear();
            packageRows.Clear();
            selectedPackageIndex = 0;

            // TODO: Unify this readout with FeaturedUnitPanel when unit presentation grows beyond labels.
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

            Label name = new(DisplayName(unit));
            name.AddToClassList("collection-detail-name");
            name.style.color = new StyleColor(unit.AccentColor);
            details.Add(name);

            details.Add(BuildDetailLine("lang", FormatLanguages(unit)));
            details.Add(BuildDetailLine("passive", string.IsNullOrWhiteSpace(unit.PassiveName) ? "--" : unit.PassiveName));
            details.Add(BuildDetailLine("uptime", unit.BaseUptime.ToString()));
            details.Add(BuildDetailLine("ram", unit.BaseRam.ToString()));
            details.Add(BuildDetailLine("cycles", unit.BaseCyclesPerTurn.ToString()));

            readout.Add(details);
            collectionDetail.Add(readout);

            Label description = new(string.IsNullOrWhiteSpace(unit.Description) ? "--" : unit.Description);
            description.AddToClassList("collection-detail-description");
            collectionDetail.Add(description);
        }

        private void RenderCardDetail(CardDefinition card)
        {
            collectionDetail.Clear();
            if (card == null)
            {
                return;
            }

            Label name = new(DisplayName(card));
            name.AddToClassList("collection-detail-name");
            collectionDetail.Add(name);

            collectionDetail.Add(BuildDetailLine("language", card.Language.ToString()));
            collectionDetail.Add(BuildDetailLine("rarity", card.Rarity.ToString()));
            collectionDetail.Add(BuildDetailLine("cost", $"{card.CycleCost} cycles"));
            collectionDetail.Add(BuildDetailLine("track", card.ResolutionTrack.ToString()));
            collectionDetail.Add(BuildDetailLine("type", card.DistroExclusive ? "distro exclusive" : "standard"));

            Label description = new(string.IsNullOrWhiteSpace(card.Description) ? "--" : card.Description);
            description.AddToClassList("collection-detail-description");
            collectionDetail.Add(description);
        }

        private void RefreshRunSetup()
        {
            if (runSetupList == null || runSetupDetail == null)
            {
                return;
            }

            runSetupRows.Clear();
            runSetupList.Clear();
            runSetupDetail.Clear();

            IReadOnlyList<DistroDefinition> units = playerCollection.OwnedUnits;
            if (units.Count == 0)
            {
                runSetupDetail.Add(new Label("no units installed") { name = "RunSetupEmptyTitle" });
                runSetupDetail.Add(new Label("run: curl gacha.sh | sh") { name = "RunSetupEmptyHint" });
                return;
            }

            selectedRunSetupIndex = Mathf.Clamp(selectedRunSetupIndex, 0, units.Count - 1);
            for (int i = 0; i < units.Count; i++)
            {
                int index = i;
                DistroDefinition unit = units[i];
                VisualElement row = new();
                row.AddToClassList("collection-row");
                row.RegisterCallback<PointerEnterEvent>(_ => SelectRunSetupUnit(index));
                row.RegisterCallback<ClickEvent>(_ => SelectRunSetupUnit(index));

                Label name = new(DisplayName(unit));
                name.AddToClassList("collection-row-name");
                name.style.color = new StyleColor(unit.AccentColor);

                Label languages = new(FormatLanguages(unit));
                languages.AddToClassList("collection-row-languages");

                row.Add(name);
                row.Add(languages);
                runSetupList.Add(row);
                runSetupRows.Add(row);
            }

            SelectRunSetupUnit(selectedRunSetupIndex);
        }

        private void SelectRunSetupUnit(int index)
        {
            IReadOnlyList<DistroDefinition> units = playerCollection.OwnedUnits;
            if (units.Count == 0)
            {
                return;
            }

            selectedRunSetupIndex = Mathf.Clamp(index, 0, units.Count - 1);
            for (int i = 0; i < runSetupRows.Count; i++)
            {
                runSetupRows[i].EnableInClassList(SelectedClassName, i == selectedRunSetupIndex);
            }

            RenderRunSetupDetail(units[selectedRunSetupIndex]);
        }

        private void RenderRunSetupDetail(DistroDefinition unit)
        {
            runSetupDetail.Clear();
            packageRows.Clear();
            selectedPackageIndex = 0;

            Label name = new(DisplayName(unit));
            name.AddToClassList("collection-detail-name");
            name.style.color = new StyleColor(unit.AccentColor);
            runSetupDetail.Add(name);
            runSetupDetail.Add(BuildDetailLine("lang", FormatLanguages(unit)));
            runSetupDetail.Add(BuildDetailLine("loadout", $"pick {CardLoadout.MaxEquippedCards} cards"));
            BuildPackageList(unit, runSetupDetail);
        }

        private void BuildPackageList(DistroDefinition unit, VisualElement parent)
        {
            VisualElement packageSection = new();
            packageSection.AddToClassList("package-list");

            Label header = new("dpkg -l (pick 4)");
            header.AddToClassList("package-header");
            packageSection.Add(header);

            Label notice = new();
            notice.name = "PackageNotice";
            notice.AddToClassList("package-notice");
            notice.AddToClassList(HiddenClassName);
            packageSection.Add(notice);

            if (unit.ExclusiveCards.Count == 0)
            {
                Label empty = new("no distro packages installed");
                empty.AddToClassList("package-empty");
                packageSection.Add(empty);
                parent.Add(packageSection);
                return;
            }

            IReadOnlyList<string> equippedIds = cardLoadout.GetEquippedCardIds(unit.Id);
            for (int i = 0; i < unit.ExclusiveCards.Count; i++)
            {
                CardDefinition card = unit.ExclusiveCards[i];
                if (card == null || card.IsToken)
                {
                    continue;
                }

                int index = packageRows.Count;
                VisualElement row = BuildPackageRow(unit, card, equippedIds, index);
                packageRows.Add(row);
                packageSection.Add(row);
            }

            parent.Add(packageSection);
            RefreshPackageSelection();
        }

        private VisualElement BuildPackageRow(DistroDefinition unit, CardDefinition card, IReadOnlyList<string> equippedIds, int index)
        {
            bool equipped = ContainsId(equippedIds, card.Id);
            bool loadoutFull = equippedIds.Count >= CardLoadout.MaxEquippedCards;

            VisualElement row = new();
            row.AddToClassList("package-row");
            row.EnableInClassList("equipped", equipped);
            row.EnableInClassList("dimmed", !equipped && loadoutFull);
            row.RegisterCallback<PointerEnterEvent>(_ =>
            {
                selectedPackageIndex = index;
                RefreshPackageSelection();
            });
            row.RegisterCallback<ClickEvent>(_ => TogglePackage(unit, card));

            VisualElement summary = new();
            summary.AddToClassList("package-summary");

            Label marker = new(equipped ? "[x]" : "[ ]");
            marker.AddToClassList("package-marker");
            marker.style.color = new StyleColor(equipped ? unit.AccentColor : Color.gray);

            Label name = new(DisplayName(card));
            name.AddToClassList("package-name");

            Label meta = new($"{card.Language}  {card.CycleCost}c");
            meta.AddToClassList("package-meta");

            summary.Add(marker);
            summary.Add(name);
            summary.Add(meta);

            Label cardDescription = new(string.IsNullOrWhiteSpace(card.Description) ? "--" : card.Description);
            cardDescription.AddToClassList("package-description");

            row.Add(summary);
            row.Add(cardDescription);
            return row;
        }

        private void RefreshPackageSelection()
        {
            for (int i = 0; i < packageRows.Count; i++)
            {
                packageRows[i].EnableInClassList(SelectedClassName, i == selectedPackageIndex);
            }
        }

        private void ToggleSelectedPackage()
        {
            IReadOnlyList<DistroDefinition> units = playerCollection.OwnedUnits;
            if (units.Count == 0 || selectedRunSetupIndex >= units.Count)
            {
                return;
            }

            DistroDefinition unit = units[selectedRunSetupIndex];
            CardDefinition card = GetVisibleExclusiveCard(unit, selectedPackageIndex);
            if (card != null)
            {
                TogglePackage(unit, card);
            }
        }

        private void TogglePackage(DistroDefinition unit, CardDefinition card)
        {
            IReadOnlyList<string> equippedIds = cardLoadout.GetEquippedCardIds(unit.Id);
            bool changed = ContainsId(equippedIds, card.Id)
                ? cardLoadout.TryUnequip(unit.Id, card.Id, out CardLoadoutFailureReason reason)
                : cardLoadout.TryEquip(unit.Id, card.Id, out reason);

            if (!changed)
            {
                ShowPackageNotice(reason);
                return;
            }

            SaveCurrentState();
            RenderRunSetupDetail(unit);
        }

        private void ShowPackageNotice(CardLoadoutFailureReason reason)
        {
            Label notice = runSetupDetail.Q<Label>("PackageNotice");
            if (notice == null)
            {
                return;
            }

            notice.text = reason == CardLoadoutFailureReason.Full
                ? "dpkg: dependency limit reached (unequip a package first)"
                : $"dpkg: package toggle failed ({reason})";
            notice.RemoveFromClassList(HiddenClassName);

            packageNoticeSchedule?.Pause();
            if (UIPreferences.ReducedMotion)
            {
                return;
            }

            packageNoticeSchedule = root.schedule.Execute(() => notice.AddToClassList(HiddenClassName)).StartingIn(2000);
        }

        private static CardDefinition GetVisibleExclusiveCard(DistroDefinition unit, int visibleIndex)
        {
            int currentIndex = 0;
            for (int i = 0; i < unit.ExclusiveCards.Count; i++)
            {
                CardDefinition card = unit.ExclusiveCards[i];
                if (card == null || card.IsToken)
                {
                    continue;
                }

                if (currentIndex == visibleIndex)
                {
                    return card;
                }

                currentIndex++;
            }

            return null;
        }

        private CardDefinition GetVisibleCard(int visibleIndex)
        {
            IReadOnlyList<CardDefinition> cards = cardDatabase == null ? Array.Empty<CardDefinition>() : cardDatabase.AllCards;
            int currentIndex = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                CardDefinition card = cards[i];
                if (card == null)
                {
                    continue;
                }

                if (currentIndex == visibleIndex)
                {
                    return card;
                }

                currentIndex++;
            }

            return null;
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

        private static string DisplayName(DistroDefinition unit)
        {
            return string.IsNullOrWhiteSpace(unit.DisplayName) ? unit.name : unit.DisplayName;
        }

        private static string DisplayName(CardDefinition card)
        {
            return string.IsNullOrWhiteSpace(card.DisplayName) ? card.name : card.DisplayName;
        }

        private static bool ContainsId(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatLanguages(DistroDefinition unit)
        {
            return $"{unit.PrimaryLanguage} / {unit.SecondaryLanguage}";
        }

        private void StartAmbientSchedules()
        {
            blinkSchedule?.Pause();
            backgroundLog?.Stop();
            cursorVisible = true;
            titleCursorLabel.EnableInClassList(CursorOnClassName, true);
            promptCursorLabel.EnableInClassList(CursorOnClassName, true);

            if (UIPreferences.ReducedMotion)
            {
                return;
            }

            blinkSchedule = root.schedule.Execute(() =>
            {
                cursorVisible = !cursorVisible;
                titleCursorLabel.EnableInClassList(CursorOnClassName, cursorVisible);
                promptCursorLabel.EnableInClassList(CursorOnClassName, cursorVisible);
            }).Every(500);
        }

        private void PlayBootIntroIfNeeded()
        {
            if (bootIntroPlayed || UIPreferences.ReducedMotion)
            {
                CompleteBootIntro();
                return;
            }

            bootIntroPlayed = true;
            bootIntroElapsed = 0f;
            bootIntroCharacterCount = 0;
            bootIntroLogLabel.text = string.Empty;
            bootIntroPanel.RemoveFromClassList(HiddenClassName);
            shellRoot.AddToClassList("boot-hidden");

            bootIntroSchedule = root.schedule.Execute(UpdateBootIntro).Every(16);
        }

        private void UpdateBootIntro()
        {
            bootIntroElapsed += 0.016f;
            int targetCount = Mathf.Clamp(Mathf.CeilToInt(bootIntroCopy.Length * (bootIntroElapsed / BootIntroSeconds)), 0, bootIntroCopy.Length);
            if (targetCount != bootIntroCharacterCount)
            {
                bootIntroCharacterCount = targetCount;
                bootIntroLogLabel.text = bootIntroCopy.Substring(0, bootIntroCharacterCount);
            }

            if (bootIntroElapsed >= BootIntroSeconds)
            {
                CompleteBootIntro();
            }
        }

        private void CompleteBootIntro()
        {
            bootIntroSchedule?.Pause();
            bootIntroPanel.AddToClassList(HiddenClassName);
            shellRoot.RemoveFromClassList("boot-hidden");
            shellRoot.AddToClassList("boot-visible");
            backgroundLog?.Start(UIPreferences.ReducedMotion);
            ShowStarterModalIfNeeded();
            root.Focus();
        }

        private bool SkipBootIntro()
        {
            if (bootIntroPanel.ClassListContains(HiddenClassName))
            {
                return false;
            }

            CompleteBootIntro();
            return true;
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            if (SkipBootIntro())
            {
                evt.StopPropagation();
                return;
            }

            if (starterModalActive)
            {
                HandleStarterKeyDown(evt);
                evt.StopPropagation();
                return;
            }

            if (IsRunSetupVisible())
            {
                if (evt.keyCode == KeyCode.UpArrow)
                {
                    SelectPackage(selectedPackageIndex - 1);
                    evt.StopPropagation();
                    return;
                }

                if (evt.keyCode == KeyCode.DownArrow)
                {
                    SelectPackage(selectedPackageIndex + 1);
                    evt.StopPropagation();
                    return;
                }

                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    ToggleSelectedPackage();
                    evt.StopPropagation();
                    return;
                }
            }

            if (evt.keyCode == KeyCode.UpArrow)
            {
                SelectCommand(selectedCommandIndex - 1);
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.DownArrow)
            {
                SelectCommand(selectedCommandIndex + 1);
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Space)
            {
                ActivateCommand(selectedCommandIndex);
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.Tab)
            {
                featuredUnitPanel.SelectNext();
                evt.StopPropagation();
                return;
            }

            int digitIndex = GetDigitCommandIndex(evt.keyCode);
            if (digitIndex >= 0)
            {
                SelectCommand(digitIndex);
                ActivateCommand(digitIndex);
                evt.StopPropagation();
            }
        }

        private bool IsRunSetupVisible()
        {
            return runSetupPanel != null && !runSetupPanel.ClassListContains(HiddenClassName);
        }

        private void SelectPackage(int index)
        {
            if (packageRows.Count == 0)
            {
                return;
            }

            selectedPackageIndex = (index + packageRows.Count) % packageRows.Count;
            RefreshPackageSelection();
        }

        private void HandlePointerDown(PointerDownEvent evt)
        {
            root.Focus();
            suppressNextClick = SkipBootIntro();
        }

        private void ShowStarterModalIfNeeded()
        {
            if (saveData == null || saveData.starterChosen)
            {
                return;
            }

            IReadOnlyList<DistroDefinition> starters = GetStarterDistros();
            if (starters.Count < 3)
            {
                Debug.LogWarning("Starter distro selection needs three distros in DistroDatabase.");
                return;
            }

            starterModalActive = true;
            starterConfirming = false;
            starterConfirmLabel.AddToClassList(HiddenClassName);
            starterModal.RemoveFromClassList(HiddenClassName);

            for (int i = 0; i < starterCards.Count; i++)
            {
                DistroDefinition unit = starters[i];
                starterNames[i].text = DisplayName(unit);
                starterNames[i].style.color = new StyleColor(unit.AccentColor);
                starterLanguages[i].text = FormatLanguages(unit);
                starterDescriptions[i].text = unit.Description;
            }

            SelectStarter(0);
            root.Focus();
        }

        private IReadOnlyList<DistroDefinition> GetStarterDistros()
        {
            return distroDatabase == null ? Array.Empty<DistroDefinition>() : distroDatabase.AllDistros;
        }

        private void SelectStarter(int index)
        {
            IReadOnlyList<DistroDefinition> starters = GetStarterDistros();
            if (starters.Count == 0)
            {
                return;
            }

            selectedStarterIndex = (index + Mathf.Min(starters.Count, starterCards.Count)) % Mathf.Min(starters.Count, starterCards.Count);
            starterConfirming = false;
            starterConfirmLabel.AddToClassList(HiddenClassName);

            for (int i = 0; i < starterCards.Count; i++)
            {
                bool selected = i == selectedStarterIndex;
                starterCards[i].EnableInClassList(SelectedClassName, selected);
                if (i < starters.Count && selected)
                {
                    StyleColor accent = new(starters[i].AccentColor);
                    starterCards[i].style.borderTopColor = accent;
                    starterCards[i].style.borderRightColor = accent;
                    starterCards[i].style.borderBottomColor = accent;
                    starterCards[i].style.borderLeftColor = accent;
                }
                else
                {
                    starterCards[i].style.borderTopColor = StyleKeyword.Null;
                    starterCards[i].style.borderRightColor = StyleKeyword.Null;
                    starterCards[i].style.borderBottomColor = StyleKeyword.Null;
                    starterCards[i].style.borderLeftColor = StyleKeyword.Null;
                }
            }
        }

        private void HandleStarterKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.LeftArrow)
            {
                SelectStarter(selectedStarterIndex - 1);
                return;
            }

            if (evt.keyCode == KeyCode.RightArrow)
            {
                SelectStarter(selectedStarterIndex + 1);
                return;
            }

            int digitIndex = GetDigitCommandIndex(evt.keyCode);
            if (digitIndex >= 0 && digitIndex < 3)
            {
                SelectStarter(digitIndex);
                return;
            }

            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                if (starterConfirming)
                {
                    ConfirmStarterSelection();
                    return;
                }

                ConfirmStarterSelectionIntent();
                return;
            }

            if (evt.keyCode == KeyCode.Y && starterConfirming)
            {
                ConfirmStarterSelection();
                return;
            }

            if (evt.keyCode == KeyCode.Escape || evt.keyCode == KeyCode.N)
            {
                starterConfirming = false;
                starterConfirmLabel.AddToClassList(HiddenClassName);
            }
        }

        private void ConfirmStarterSelectionIntent()
        {
            IReadOnlyList<DistroDefinition> starters = GetStarterDistros();
            if (selectedStarterIndex >= starters.Count)
            {
                return;
            }

            starterConfirming = true;
            starterConfirmLabel.text = $"install {DisplayName(starters[selectedStarterIndex])}? this cannot be undone [Y/n]";
            starterConfirmLabel.RemoveFromClassList(HiddenClassName);
        }

        private void ConfirmStarterSelection()
        {
            IReadOnlyList<DistroDefinition> starters = GetStarterDistros();
            if (selectedStarterIndex >= starters.Count)
            {
                return;
            }

            DistroDefinition picked = starters[selectedStarterIndex];
            playerCollection.Add(picked);
            for (int i = 0; i < starters.Count; i++)
            {
                if (i != selectedStarterIndex)
                {
                    gachaService.AddToBannerPool(starters[i]);
                }
            }

            saveData.starterChosen = true;
            SaveCurrentState();
            featuredUnitPanel.Refresh(playerCollection.OwnedUnits);
            RefreshCollection();

            starterConfirmLabel.text = $"installing {picked.Id} (1/1)... done";
            starterConfirmLabel.RemoveFromClassList(HiddenClassName);
            if (UIPreferences.ReducedMotion)
            {
                CloseStarterModal();
                return;
            }

            starterCloseSchedule?.Pause();
            starterCloseSchedule = root.schedule.Execute(CloseStarterModal).StartingIn(600);
        }

        private void CloseStarterModal()
        {
            starterCloseSchedule?.Pause();
            starterCloseSchedule = null;
            starterModalActive = false;
            starterConfirming = false;
            starterModal.AddToClassList(HiddenClassName);
            root.Focus();
        }

        private static int GetDigitCommandIndex(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.Alpha1 or KeyCode.Keypad1 => 0,
                KeyCode.Alpha2 or KeyCode.Keypad2 => 1,
                KeyCode.Alpha3 or KeyCode.Keypad3 => 2,
                KeyCode.Alpha4 or KeyCode.Keypad4 => 3,
                KeyCode.Alpha5 or KeyCode.Keypad5 => 4,
                _ => -1
            };
        }

        private void SelectCommand(int index)
        {
            selectedCommandIndex = (index + commandEntries.Count) % commandEntries.Count;
            for (int i = 0; i < commandEntries.Count; i++)
            {
                commandEntries[i].SetSelected(i == selectedCommandIndex);
            }
        }

        private void ActivateCommand(int index)
        {
            commandEntries[index].Activate();
        }

        private void HandleCommandClicked(int index)
        {
            if (suppressNextClick)
            {
                suppressNextClick = false;
                return;
            }

            ActivateCommand(index);
        }

        private void HandleStartRunClicked()
        {
            RefreshRunSetup();
            ShowPanel(runSetupPanel);
        }

        private void HandleQuitClicked()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void ShowMainMenu()
        {
            ShowPanel(mainMenuPanel);
            root.Focus();
        }

        private void ShowCollection()
        {
            collectionShowingCards = false;
            RefreshCollection();
            ShowPanel(collectionPanel);
        }

        private void ShowCollectionUnits()
        {
            collectionShowingCards = false;
            RefreshCollection();
        }

        private void ShowCollectionCards()
        {
            collectionShowingCards = true;
            RefreshCollection();
        }

        private void ShowGacha()
        {
            ShowPanel(gachaPanel);
        }

        private void ShowSettings()
        {
            ShowPanel(settingsPanel);
        }

        // TODO: Replace this direct toggle with a screen-stack/router when menu flows need history or transitions.
        private void ShowPanel(VisualElement activePanel)
        {
            mainMenuPanel.EnableInClassList(HiddenClassName, mainMenuPanel != activePanel);
            collectionPanel.EnableInClassList(HiddenClassName, collectionPanel != activePanel);
            runSetupPanel.EnableInClassList(HiddenClassName, runSetupPanel != activePanel);
            gachaPanel.EnableInClassList(HiddenClassName, gachaPanel != activePanel);
            settingsPanel.EnableInClassList(HiddenClassName, settingsPanel != activePanel);
        }

        private sealed class CommandMenuEntry
        {
            private readonly Label cursor;
            private readonly Action action;

            public CommandMenuEntry(VisualElement row, Action action)
            {
                Row = row;
                this.action = action;
                cursor = row.Q<Label>(className: "command-cursor");
            }

            public VisualElement Row { get; }

            public void SetSelected(bool selected)
            {
                Row.EnableInClassList(SelectedClassName, selected);
                cursor.visible = selected;
            }

            public void Activate()
            {
                action.Invoke();
            }
        }
    }
}
