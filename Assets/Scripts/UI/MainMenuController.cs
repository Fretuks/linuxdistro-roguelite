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

        // Static fields don't reset between Play sessions when the Editor's "Reload Domain"
        // option is disabled. RuntimeInitializeOnLoadMethod runs once per player/Play-mode
        // startup regardless of that setting, so this keeps bootIntroPlayed session-scoped.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionState()
        {
            bootIntroPlayed = false;
        }

        [SerializeField] private FontAsset monospaceFont; // TODO: Assign a real monospace FontAsset when typography assets exist.
        [SerializeField] private string motdBody = "unstable userspace detected; keep a rollback shell open.";
        [SerializeField] private DistroDatabase distroDatabase;
        [SerializeField] private CardDatabase cardDatabase;
        [SerializeField] private LanguageDeckDatabase languageDeckDatabase;
        [SerializeField] private FeaturedUnitPanel featuredUnitPanel = new();
        [SerializeField] private CollectionScreenController collectionScreen = new();
        [SerializeField] private StarterSelectionController starterSelection = new();
        [SerializeField] private GachaScreenController gachaScreen = new();

        private readonly List<CommandMenuEntry> commandEntries = new();
        private readonly List<VisualElement> packageRows = new();
        private readonly List<VisualElement> loadoutRows = new();
        private readonly List<Language> selectedRunLanguages = new();
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
        private ScrollView runSetupList;
        private VisualElement runSetupDetail;
        private ScrollView runSetupPackageScroll;
        private Label appIdLabel;
        private Label entropyLabel;
        private Label pullTokensLabel;
        private Label titleCursorLabel;
        private Label promptCursorLabel;
        private Label bootIntroLogLabel;
        private Label motdBodyLabel;
        private VisualElement motdBlock;
        private Button rootCreditsToEntropyButton;
        private VisualElement rootCreditExchangeModal;
        private Label rootCreditExchangeBalanceLabel;
        private Label rootCreditExchangePreviewLabel;
        private Label rootCreditExchangeMessageLabel;
        private TextField rootCreditExchangeInput;
        private Button rootCreditExchangeMinusHundredButton;
        private Button rootCreditExchangeMinusOneButton;
        private Button rootCreditExchangeNoneButton;
        private Button rootCreditExchangePlusOneButton;
        private Button rootCreditExchangePlusHundredButton;
        private Button rootCreditExchangeMaxButton;
        private Button rootCreditExchangeConfirmButton;
        private Button rootCreditExchangeCancelButton;
        private Button collectionUnitsButton;
        private Button collectionLanguagesButton;
        private readonly ScreenFrameController runSetupFrame = new();
        private readonly ScreenFrameController collectionFrame = new();
        private readonly ScreenFrameController gachaFrame = new();
        private readonly ScreenFrameController settingsFrame = new();
        private SaveService saveService;
        private SaveData saveData;
        private EntropyWallet wallet;
        private GachaService gachaService;
        private PlayerCollection playerCollection;
        private CardLoadout cardLoadout;
        private BackgroundLogRingBuffer backgroundLog;
        private IEventBannerSource eventBannerSource;
        private int selectedCommandIndex;
        private int selectedPackageIndex;
        private string runSetupNotice;
        private bool collectionShowingLanguages;
        private float bootIntroElapsed;
        private int bootIntroCharacterCount;
        private bool cursorVisible;
        private bool suppressNextClick;
        private bool warnedUnresolvedSaveId;
        private bool rootCreditExchangeOpen;
        private string bootIntroCopy;
        private Action rootCreditExchangeMinusHundredClicked;
        private Action rootCreditExchangeMinusOneClicked;
        private Action rootCreditExchangePlusOneClicked;
        private Action rootCreditExchangePlusHundredClicked;
        private IVisualElementScheduledItem blinkSchedule;
        private IVisualElementScheduledItem bootIntroSchedule;

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
            featuredUnitPanel.Bind(root, monospaceFont);
            collectionScreen.Bind(root, monospaceFont, languageDeckDatabase, cardDatabase, playerCollection);
            BindScreenFrames();
            starterSelection.Bind(root, distroDatabase, HandleStarterConfirmed);
            gachaScreen.Bind(root, distroDatabase, monospaceFont, gachaService, playerCollection, wallet, HandleMetaStateChanged, OpenRootCreditExchange);
            LoadMetaState();
            playerCollection.Changed += HandleMetaStateChanged;
            gachaService.Changed += HandleMetaStateChanged;
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
            featuredUnitPanel.Refresh(playerCollection.OwnedUnits, playerCollection.FeaturedUnit);
            collectionScreen.RefreshUnits(playerCollection.OwnedUnits);
            gachaScreen.Refresh();
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
            starterSelection.PauseSchedules();
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
            runSetupList = root.Q<ScrollView>("RunSetupList");
            runSetupDetail = root.Q<VisualElement>("RunSetupDetail");
            backgroundLog = new BackgroundLogRingBuffer(backgroundLogLayer, BootLogCopy.Lines);

            appIdLabel = root.Q<Label>("AppIdLabel");
            entropyLabel = root.Q<Label>("EntropyLabel");
            pullTokensLabel = root.Q<Label>("PullTokensLabel");
            titleCursorLabel = root.Q<Label>("TitleCursorLabel");
            promptCursorLabel = root.Q<Label>("PromptCursorLabel");
            bootIntroLogLabel = root.Q<Label>("BootIntroLogLabel");
            motdBodyLabel = root.Q<Label>("MotdBodyLabel");

            rootCreditsToEntropyButton = root.Q<Button>("RootCreditsToEntropyButton");
            if (rootCreditsToEntropyButton != null)
            {
                rootCreditsToEntropyButton.focusable = false;
            }

            rootCreditExchangeModal = root.Q<VisualElement>("RootCreditExchangeModal");
            rootCreditExchangeBalanceLabel = root.Q<Label>("RootCreditExchangeBalanceLabel");
            rootCreditExchangePreviewLabel = root.Q<Label>("RootCreditExchangePreviewLabel");
            rootCreditExchangeMessageLabel = root.Q<Label>("RootCreditExchangeMessageLabel");
            rootCreditExchangeInput = root.Q<TextField>("RootCreditExchangeInput");
            rootCreditExchangeMinusHundredButton = root.Q<Button>("RootCreditExchangeMinusHundredButton");
            rootCreditExchangeMinusOneButton = root.Q<Button>("RootCreditExchangeMinusOneButton");
            rootCreditExchangeNoneButton = root.Q<Button>("RootCreditExchangeNoneButton");
            rootCreditExchangePlusOneButton = root.Q<Button>("RootCreditExchangePlusOneButton");
            rootCreditExchangePlusHundredButton = root.Q<Button>("RootCreditExchangePlusHundredButton");
            rootCreditExchangeMaxButton = root.Q<Button>("RootCreditExchangeMaxButton");
            rootCreditExchangeConfirmButton = root.Q<Button>("RootCreditExchangeConfirmButton");
            rootCreditExchangeCancelButton = root.Q<Button>("RootCreditExchangeCancelButton");
            collectionUnitsButton = root.Q<Button>("CollectionUnitsButton");
            collectionLanguagesButton = root.Q<Button>("CollectionLanguagesButton");
        }

        private void BindScreenFrames()
        {
            runSetupFrame.Bind(runSetupPanel, "$ ./start_run --configure", "[esc] back   [arrows] navigate   [enter] select   [b] boot run", ShowMainMenu);
            collectionFrame.Bind(collectionPanel, "$ ls ~/collection", "[esc] back   [left/right] tabs   [tab] tabs   [arrows] navigate   [enter] select", HandleCollectionBack);
            gachaFrame.Bind(gachaPanel, "$ curl gacha.sh | sh", "[esc] back   click banner   click pull", ShowMainMenu);
            settingsFrame.Bind(settingsPanel, "$ dpkg-reconfigure kernel-panic", "[esc] back", ShowMainMenu);
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
            if (rootCreditsToEntropyButton != null)
            {
                rootCreditsToEntropyButton.clicked += HandleRootCreditsToEntropyClicked;
            }

            if (rootCreditExchangeInput != null)
            {
                rootCreditExchangeInput.RegisterValueChangedCallback(HandleRootCreditExchangeInputChanged);
            }

            if (rootCreditExchangeMinusHundredButton != null)
            {
                rootCreditExchangeMinusHundredClicked ??= () => AddRootCreditExchangeAmount(-100);
                rootCreditExchangeMinusHundredButton.clicked += rootCreditExchangeMinusHundredClicked;
            }

            if (rootCreditExchangeMinusOneButton != null)
            {
                rootCreditExchangeMinusOneClicked ??= () => AddRootCreditExchangeAmount(-1);
                rootCreditExchangeMinusOneButton.clicked += rootCreditExchangeMinusOneClicked;
            }

            if (rootCreditExchangeNoneButton != null)
            {
                rootCreditExchangeNoneButton.clicked += SetRootCreditExchangeNone;
            }

            if (rootCreditExchangePlusOneButton != null)
            {
                rootCreditExchangePlusOneClicked ??= () => AddRootCreditExchangeAmount(1);
                rootCreditExchangePlusOneButton.clicked += rootCreditExchangePlusOneClicked;
            }

            if (rootCreditExchangePlusHundredButton != null)
            {
                rootCreditExchangePlusHundredClicked ??= () => AddRootCreditExchangeAmount(100);
                rootCreditExchangePlusHundredButton.clicked += rootCreditExchangePlusHundredClicked;
            }

            if (rootCreditExchangeMaxButton != null)
            {
                rootCreditExchangeMaxButton.clicked += SetRootCreditExchangeMax;
            }

            if (rootCreditExchangeConfirmButton != null)
            {
                rootCreditExchangeConfirmButton.clicked += ConfirmRootCreditExchange;
            }

            if (rootCreditExchangeCancelButton != null)
            {
                rootCreditExchangeCancelButton.clicked += CloseRootCreditExchange;
            }

            collectionScreen.ViewChanged += SyncCollectionFrame;
            collectionUnitsButton.RegisterCallback<ClickEvent>(HandleCollectionUnitsTabClicked);
            collectionLanguagesButton.RegisterCallback<ClickEvent>(HandleCollectionLanguagesTabClicked);
        }

        private void UnregisterCallbacks()
        {
            root.UnregisterCallback<KeyDownEvent>(HandleKeyDown);
            root.UnregisterCallback<PointerDownEvent>(HandlePointerDown);
            if (rootCreditsToEntropyButton != null)
            {
                rootCreditsToEntropyButton.clicked -= HandleRootCreditsToEntropyClicked;
            }

            if (rootCreditExchangeInput != null)
            {
                rootCreditExchangeInput.UnregisterValueChangedCallback(HandleRootCreditExchangeInputChanged);
            }

            if (rootCreditExchangeMinusHundredButton != null)
            {
                rootCreditExchangeMinusHundredButton.clicked -= rootCreditExchangeMinusHundredClicked;
            }

            if (rootCreditExchangeMinusOneButton != null)
            {
                rootCreditExchangeMinusOneButton.clicked -= rootCreditExchangeMinusOneClicked;
            }

            if (rootCreditExchangeNoneButton != null)
            {
                rootCreditExchangeNoneButton.clicked -= SetRootCreditExchangeNone;
            }

            if (rootCreditExchangePlusOneButton != null)
            {
                rootCreditExchangePlusOneButton.clicked -= rootCreditExchangePlusOneClicked;
            }

            if (rootCreditExchangePlusHundredButton != null)
            {
                rootCreditExchangePlusHundredButton.clicked -= rootCreditExchangePlusHundredClicked;
            }

            if (rootCreditExchangeMaxButton != null)
            {
                rootCreditExchangeMaxButton.clicked -= SetRootCreditExchangeMax;
            }

            if (rootCreditExchangeConfirmButton != null)
            {
                rootCreditExchangeConfirmButton.clicked -= ConfirmRootCreditExchange;
            }

            if (rootCreditExchangeCancelButton != null)
            {
                rootCreditExchangeCancelButton.clicked -= CloseRootCreditExchange;
            }

            collectionScreen.ViewChanged -= SyncCollectionFrame;
            collectionUnitsButton.UnregisterCallback<ClickEvent>(HandleCollectionUnitsTabClicked);
            collectionLanguagesButton.UnregisterCallback<ClickEvent>(HandleCollectionLanguagesTabClicked);
        }

        private void HandleRootCreditsToEntropyClicked()
        {
            if (gachaService == null || wallet == null)
            {
                return;
            }

            OpenRootCreditExchange();
        }

        private void OpenRootCreditExchange()
        {
            if (rootCreditExchangeModal == null || gachaService == null || wallet == null)
            {
                return;
            }

            rootCreditExchangeOpen = true;
            rootCreditExchangeModal.RemoveFromClassList(HiddenClassName);
            rootCreditExchangeMessageLabel?.AddToClassList(HiddenClassName);
            SetRootCreditExchangeAmount(Math.Min(100, gachaService.RootCredits));
            RefreshRootCreditExchange();
            rootCreditExchangeInput?.Focus();
        }

        private void CloseRootCreditExchange()
        {
            rootCreditExchangeOpen = false;
            rootCreditExchangeModal?.AddToClassList(HiddenClassName);
            root.Focus();
        }

        private void HandleRootCreditExchangeInputChanged(ChangeEvent<string> evt)
        {
            RefreshRootCreditExchange();
        }

        private void AddRootCreditExchangeAmount(int amount)
        {
            SetRootCreditExchangeAmount(GetRootCreditExchangeAmount() + amount);
        }

        private void SetRootCreditExchangeMax()
        {
            SetRootCreditExchangeAmount(gachaService == null ? 0 : gachaService.RootCredits);
        }

        private void SetRootCreditExchangeNone()
        {
            SetRootCreditExchangeAmount(0);
        }

        private void SetRootCreditExchangeAmount(int amount)
        {
            int max = gachaService == null ? 0 : gachaService.RootCredits;
            int clamped = Mathf.Clamp(amount, 0, max);
            if (rootCreditExchangeInput != null)
            {
                rootCreditExchangeInput.value = clamped.ToString();
            }

            RefreshRootCreditExchange();
        }

        private int GetRootCreditExchangeAmount()
        {
            if (rootCreditExchangeInput == null || string.IsNullOrWhiteSpace(rootCreditExchangeInput.value))
            {
                return 0;
            }

            if (!int.TryParse(rootCreditExchangeInput.value, out int amount))
            {
                return 0;
            }

            int max = gachaService == null ? 0 : gachaService.RootCredits;
            return Mathf.Clamp(amount, 0, max);
        }

        private void RefreshRootCreditExchange()
        {
            if (gachaService == null)
            {
                return;
            }

            int amount = GetRootCreditExchangeAmount();
            if (rootCreditExchangeInput != null && rootCreditExchangeInput.value != amount.ToString())
            {
                rootCreditExchangeInput.SetValueWithoutNotify(amount.ToString());
            }

            if (rootCreditExchangeBalanceLabel != null)
            {
                rootCreditExchangeBalanceLabel.text = gachaService.RootCredits.ToString();
            }

            if (rootCreditExchangePreviewLabel != null)
            {
                rootCreditExchangePreviewLabel.text = $"+{amount} entropy";
            }

            int max = gachaService.RootCredits;
            rootCreditExchangeMinusHundredButton?.SetEnabled(amount > 0);
            rootCreditExchangeMinusOneButton?.SetEnabled(amount > 0);
            rootCreditExchangeNoneButton?.SetEnabled(amount > 0);
            rootCreditExchangePlusOneButton?.SetEnabled(amount < max);
            rootCreditExchangePlusHundredButton?.SetEnabled(amount < max);
            rootCreditExchangeMaxButton?.SetEnabled(max > 0 && amount < max);
            rootCreditExchangeConfirmButton?.SetEnabled(amount > 0);
        }

        private void ConfirmRootCreditExchange()
        {
            int amount = GetRootCreditExchangeAmount();
            if (!gachaService.ConvertRootCreditsToEntropy(wallet, amount, out string failureReason))
            {
                if (rootCreditExchangeMessageLabel != null)
                {
                    rootCreditExchangeMessageLabel.text = $"exchange failed: {failureReason}";
                    rootCreditExchangeMessageLabel.RemoveFromClassList(HiddenClassName);
                }

                RefreshRootCreditExchange();
                return;
            }

            SaveCurrentState();
            RefreshCurrencyReadouts();
            gachaScreen.Refresh();
            CloseRootCreditExchange();
        }

        private void HandleCollectionUnitsTabClicked(ClickEvent evt)
        {
            ShowCollectionUnits();
            evt.StopPropagation();
        }

        private void HandleCollectionLanguagesTabClicked(ClickEvent evt)
        {
            ShowCollectionLanguages();
            evt.StopPropagation();
        }

        private void LoadMetaState()
        {
            saveData = saveService.Load();
            saveData.EnsureLists();
            wallet.SetBalance(saveData.entropyBalance);

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

            gachaService.LoadProgress(saveData);

            if (saveData.starterChosen)
            {
                AddAllFourStarDistrosToBeginnerBannerPool();
            }

            if (gachaService.BeginnerState.guaranteedDistroIds.Count == 0 && saveData.bannerPoolIds.Count > 0)
            {
                gachaService.BeginnerState.guaranteedDistroIds.AddRange(saveData.bannerPoolIds);
            }

            SaveCurrentState();
        }

        private void AddAllFourStarDistrosToBeginnerBannerPool()
        {
            if (distroDatabase == null)
            {
                return;
            }

            IReadOnlyList<DistroDefinition> distros = distroDatabase.AllDistros;
            for (int i = 0; i < distros.Count; i++)
            {
                gachaService.AddToBannerPool(distros[i]);
            }
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

        private void HandleMetaStateChanged()
        {
            SaveCurrentState();
            featuredUnitPanel.Refresh(playerCollection.OwnedUnits, playerCollection.FeaturedUnit);
            collectionScreen.RefreshUnits(playerCollection.OwnedUnits);
            gachaScreen.Refresh();
            RefreshCurrencyReadouts();
        }

        private void HandleStarterConfirmed(DistroDefinition picked, IReadOnlyList<DistroDefinition> remaining)
        {
            playerCollection.Add(picked);
            AddAllFourStarDistrosToBeginnerBannerPool();
            gachaService.SetBeginnerGuaranteedDistros(remaining);
            saveData.starterChosen = true;
            SaveCurrentState();
            featuredUnitPanel.Refresh(playerCollection.OwnedUnits, playerCollection.FeaturedUnit);
            collectionScreen.RefreshUnits(playerCollection.OwnedUnits);
            gachaScreen.Refresh();
        }

        private void SaveCurrentState()
        {
            saveData ??= SaveData.CreateDefault();
            saveData.EnsureLists();
            saveData.entropyBalance = wallet == null ? 0 : wallet.Balance;
            saveData.ownedUnitIds.Clear();
            saveData.bannerPoolIds.Clear();
            gachaService.WriteProgress(saveData);

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

            saveService.Save(saveData);
        }

        private void SaveLastRunLoadout(DistroDefinition unit, IReadOnlyList<string> cardIds)
        {
            saveData ??= SaveData.CreateDefault();
            saveData.EnsureLists();
            saveData.lastRunLoadout.distroId = unit == null ? null : unit.Id;
            saveData.lastRunLoadout.cardIds.Clear();

            for (int i = 0; i < cardIds.Count; i++)
            {
                saveData.lastRunLoadout.cardIds.Add(cardIds[i]);
            }

            SaveCurrentState();
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

        private void RefreshCurrencyReadouts()
        {
            if (entropyLabel == null || pullTokensLabel == null || gachaService == null || wallet == null)
            {
                return;
            }

            entropyLabel.text = $"entropy={wallet.Balance}";
            rootCreditsToEntropyButton?.SetEnabled(true);
            bool showPullTokens = IsGachaVisible();
            pullTokensLabel.text = showPullTokens
                ? $"stable-pull-token={gachaService.PullTokens} feature-pull-token={gachaService.LimitedPullTokens}"
                : string.Empty;
            pullTokensLabel.EnableInClassList(HiddenClassName, !showPullTokens);
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
            starterSelection.ShowIfNeeded(saveData == null || saveData.starterChosen);
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

            if (starterSelection.IsActive)
            {
                starterSelection.HandleKeyDown(evt);
                evt.StopPropagation();
                return;
            }

            if (rootCreditExchangeOpen)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    CloseRootCreditExchange();
                    evt.StopPropagation();
                    return;
                }

                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    ConfirmRootCreditExchange();
                    evt.StopPropagation();
                    return;
                }

                return;
            }

            if (evt.keyCode == KeyCode.Escape && IsCollectionVisible() && collectionScreen.BackFromSubview())
            {
                SyncCollectionFrame();
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.Escape && IsSubScreenVisible())
            {
                ShowMainMenu();
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

                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.B)
                {
                    BootSelectedPackage();
                    evt.StopPropagation();
                    return;
                }
            }

            if (IsCollectionVisible())
            {
                if (!collectionScreen.IsCardSubview &&
                    (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow || evt.keyCode == KeyCode.Tab))
                {
                    ToggleCollectionTab();
                    evt.StopPropagation();
                    return;
                }

                if (evt.keyCode == KeyCode.UpArrow)
                {
                    collectionScreen.SelectRelative(-1);
                    evt.StopPropagation();
                    return;
                }

                if (evt.keyCode == KeyCode.DownArrow)
                {
                    collectionScreen.SelectRelative(1);
                    evt.StopPropagation();
                    return;
                }

                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    collectionScreen.ActivateSelected();
                    SyncCollectionFrame();
                    evt.StopPropagation();
                    return;
                }
            }

            if (IsGachaVisible())
            {
                gachaScreen.HandleKeyDown(evt);
                return;
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
                playerCollection.SelectNextFeatured();
                featuredUnitPanel.Refresh(playerCollection.OwnedUnits, playerCollection.FeaturedUnit);
                evt.StopPropagation();
                return;
            }

            int digitIndex = CommandKeyBindings.GetDigitIndex(evt.keyCode);
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

        private bool IsCollectionVisible()
        {
            return collectionPanel != null && !collectionPanel.ClassListContains(HiddenClassName);
        }

        private bool IsGachaVisible()
        {
            return gachaPanel != null && !gachaPanel.ClassListContains(HiddenClassName);
        }

        private bool IsSubScreenVisible()
        {
            return IsRunSetupVisible() || IsCollectionVisible() ||
                   IsGachaVisible() ||
                   (settingsPanel != null && !settingsPanel.ClassListContains(HiddenClassName));
        }

        private void RefreshRunSetup()
        {
            packageRows.Clear();
            loadoutRows.Clear();
            selectedRunLanguages.Clear();
            cardLoadout.ClearAll();
            runSetupList?.Clear();
            runSetupDetail?.Clear();
            runSetupNotice = null;

            if (runSetupList == null || runSetupDetail == null)
            {
                return;
            }

            if (playerCollection.OwnedUnits.Count == 0)
            {
                runSetupDetail.Add(new Label("no units installed") { name = "RunSetupEmptyTitle" });
                runSetupDetail.Add(new Label("install a starter or summon a unit before starting a run") { name = "RunSetupEmptyHint" });
                return;
            }

            selectedPackageIndex = Mathf.Clamp(selectedPackageIndex, 0, playerCollection.OwnedUnits.Count - 1);

            for (int i = 0; i < playerCollection.OwnedUnits.Count; i++)
            {
                int index = i;
                DistroDefinition unit = playerCollection.OwnedUnits[i];
                VisualElement row = new();
                row.AddToClassList("package-row");
                row.RegisterCallback<PointerEnterEvent>(_ => SelectPackage(index));
                row.RegisterCallback<ClickEvent>(_ => SelectPackage(index));

                VisualElement summary = new();
                summary.AddToClassList("package-summary");
                summary.Add(new Label(">") { name = $"RunSetupMarker{index}" });
                summary.ElementAt(0).AddToClassList("package-marker");

                Label name = new(DistroPresentation.DisplayName(unit));
                name.AddToClassList("package-name");
                name.style.color = new StyleColor(unit.AccentColor);
                summary.Add(name);

                Label meta = new(DistroPresentation.FormatLanguages(unit));
                meta.AddToClassList("package-meta");
                summary.Add(meta);

                row.Add(summary);
                string passiveTitle = unit.Passive == null || string.IsNullOrWhiteSpace(unit.Passive.Name) ? "--" : unit.Passive.Name;
                row.Add(new Label($"passive: {passiveTitle}")
                {
                    name = $"RunSetupDescription{index}"
                });
                row.ElementAt(1).AddToClassList("package-description");

                runSetupList.Add(row);
                packageRows.Add(row);
            }

            RefreshPackageSelection();
        }

        private void SelectPackage(int index)
        {
            if (packageRows.Count == 0)
            {
                return;
            }

            selectedPackageIndex = (index + packageRows.Count) % packageRows.Count;
            cardLoadout.ClearLoadout(playerCollection.OwnedUnits[selectedPackageIndex]);
            selectedRunLanguages.Clear();
            runSetupPackageScroll = null;
            runSetupNotice = null;
            RefreshPackageSelection();
        }

        private void RefreshPackageSelection()
        {
            for (int i = 0; i < packageRows.Count; i++)
            {
                packageRows[i].EnableInClassList(SelectedClassName, i == selectedPackageIndex);
            }

            RenderSelectedPackageDetail();
        }

        private void RenderSelectedPackageDetail()
        {
            Vector2 previousScrollOffset = runSetupPackageScroll == null ? Vector2.zero : runSetupPackageScroll.scrollOffset;
            runSetupDetail?.Clear();
            loadoutRows.Clear();
            if (runSetupDetail == null || selectedPackageIndex < 0 || selectedPackageIndex >= playerCollection.OwnedUnits.Count)
            {
                return;
            }

            DistroDefinition unit = playerCollection.OwnedUnits[selectedPackageIndex];
            runSetupDetail.Add(BuildRunSetupReadout(unit));
            AddRunSetupPassive(unit, runSetupDetail);

            ScrollView packageScroll = new(ScrollViewMode.Vertical);
            packageScroll.AddToClassList("package-scroll");

            VisualElement packageList = new();
            packageList.AddToClassList("package-list");
            packageList.Add(new Label("equipped cards") { name = "RunSetupCardsHeader" });
            packageList.ElementAt(0).AddToClassList("package-header");

            IReadOnlyList<string> equippedCardIds = cardLoadout.GetEquippedCardIds(unit.Id);
            if (!string.IsNullOrWhiteSpace(runSetupNotice))
            {
                packageList.Add(new Label(runSetupNotice) { name = "RunSetupNotice" });
                packageList.ElementAt(packageList.childCount - 1).AddToClassList("package-notice");
            }

            if (equippedCardIds.Count < CardLoadout.MaxEquippedCards)
            {
                packageList.Add(new Label($"select {CardLoadout.MaxEquippedCards - equippedCardIds.Count} more card(s) before booting") { name = "RunSetupIncompleteNotice" });
                packageList.ElementAt(packageList.childCount - 1).AddToClassList("package-notice");
            }

            bool addedCards = false;
            for (int i = 0; i < unit.ExclusiveCards.Count; i++)
            {
                CardDefinition card = unit.ExclusiveCards[i];
                if (card == null || card.IsToken)
                {
                    continue;
                }

                bool equipped = ContainsCardId(equippedCardIds, card.Id);
                VisualElement row = BuildCardLoadoutRow(unit, card, equipped);
                packageList.Add(row);
                loadoutRows.Add(row);
                addedCards = true;
            }

            if (!addedCards)
            {
                packageList.Add(new Label("no compatible cards available") { name = "RunSetupCardsEmpty" });
                packageList.ElementAt(packageList.childCount - 1).AddToClassList("package-empty");
            }

            if (equippedCardIds.Count >= CardLoadout.MaxEquippedCards)
            {
                AddRunLanguageSelection(packageList);
            }
            else
            {
                selectedRunLanguages.Clear();
            }

            packageScroll.Add(packageList);
            runSetupDetail.Add(packageScroll);
            runSetupDetail.Add(BuildBootRow(unit, CanBootRun(equippedCardIds)));
            runSetupPackageScroll = packageScroll;
            RestoreRunSetupPackageScroll(previousScrollOffset);
        }

        private void RestoreRunSetupPackageScroll(Vector2 offset)
        {
            if (runSetupPackageScroll == null)
            {
                return;
            }

            runSetupPackageScroll.scrollOffset = offset;
            runSetupPackageScroll.schedule.Execute(() => runSetupPackageScroll.scrollOffset = offset).StartingIn(0);
        }

        private VisualElement BuildRunSetupReadout(DistroDefinition unit)
        {
            VisualElement readout = new();
            readout.AddToClassList("run-readout");

            Label artLabel = new();
            DistroArtPresenter.ConfigureArtLabel(artLabel, monospaceFont);
            AsciiArtFitter artFitter = new(artLabel, monospaceFont);
            VisualElement artPlaceholder = DistroArtPresenter.CreatePlaceholder();
            artFitter.SetArt(DistroArtPresenter.Render(artLabel, artPlaceholder, unit));
            readout.Add(artPlaceholder);
            readout.Add(artLabel);

            VisualElement copy = new();
            copy.AddToClassList("run-readout-copy");

            Label name = new(DistroPresentation.DisplayName(unit));
            name.AddToClassList("collection-detail-name");
            name.style.color = new StyleColor(unit.AccentColor);
            copy.Add(name);

            Label languages = new(DistroPresentation.FormatLanguages(unit));
            languages.AddToClassList("collection-detail-description");
            copy.Add(languages);

            Label stats = new($"uptime {unit.BaseUptime} · ram {unit.BaseRam} · cycles {unit.BaseCyclesPerTurn}");
            stats.AddToClassList("collection-detail-muted");
            stats.AddToClassList("run-stat-line");
            copy.Add(stats);

            readout.Add(copy);
            return readout;
        }

        private static void AddRunSetupPassive(DistroDefinition unit, VisualElement container)
        {
            if (unit.Passive == null)
            {
                return;
            }

            VisualElement passiveBlock = new();
            passiveBlock.AddToClassList("run-passive-block");

            Label rules = new($"{unit.Passive.Name}: {unit.Passive.RulesText}");
            rules.AddToClassList("run-passive-rules");
            passiveBlock.Add(rules);

            if (!string.IsNullOrWhiteSpace(unit.Passive.FlavorText))
            {
                Label flavor = new(unit.Passive.FlavorText);
                flavor.AddToClassList("run-passive-flavor");
                passiveBlock.Add(flavor);
            }

            container.Add(passiveBlock);
        }

        private static VisualElement BuildDetailLine(string key, string value)
        {
            VisualElement row = new();
            row.AddToClassList("kv-row");
            row.Add(new Label(key) { name = $"RunSetup{key}Key" });
            row.ElementAt(0).AddToClassList("kv-key");
            row.Add(new Label(value) { name = $"RunSetup{key}Value" });
            row.ElementAt(1).AddToClassList("kv-value");
            return row;
        }

        private VisualElement BuildCardLoadoutRow(DistroDefinition unit, CardDefinition card, bool equipped)
        {
            VisualElement row = new();
            row.AddToClassList("package-row");
            row.AddToClassList("loadout-card-row");
            row.EnableInClassList("equipped", equipped);
            row.RegisterCallback<ClickEvent>(_ => ToggleCardInSelectedLoadout(unit, card));

            VisualElement summary = new();
            summary.AddToClassList("package-summary");
            summary.Add(new Label(equipped ? "[x]" : "[ ]") { name = "RunSetupCardMarker" });
            summary.ElementAt(0).AddToClassList("package-marker");

            string displayName = string.IsNullOrWhiteSpace(card.DisplayName) ? card.Id : card.DisplayName;
            summary.Add(new Label(displayName) { name = "RunSetupCardName" });
            summary.ElementAt(1).AddToClassList("package-name");

            string metaText = $"{card.Language} / {card.CycleCost}c";
            summary.Add(new Label(metaText) { name = "RunSetupCardMeta" });
            summary.ElementAt(2).AddToClassList("package-meta");
            row.Add(summary);

            if (!string.IsNullOrWhiteSpace(card.Description))
            {
                row.Add(new Label(card.Description) { name = "RunSetupCardDescription" });
                row.ElementAt(1).AddToClassList("package-description");
            }

            if (!string.IsNullOrWhiteSpace(card.FlavorText))
            {
                row.Add(new Label(card.FlavorText) { name = "RunSetupCardFlavor" });
                row.ElementAt(row.childCount - 1).AddToClassList("package-flavor");
            }

            return row;
        }

        private void AddRunLanguageSelection(VisualElement packageList)
        {
            IReadOnlyList<LanguageCatalogEntry> availableLanguages = GetAvailableRunLanguages();
            packageList.Add(new Label("programming languages") { name = "RunSetupLanguagesHeader" });
            packageList.ElementAt(packageList.childCount - 1).AddToClassList("package-header");
            packageList.ElementAt(packageList.childCount - 1).AddToClassList("run-language-header");

            if (selectedRunLanguages.Count < 2)
            {
                packageList.Add(new Label($"select {2 - selectedRunLanguages.Count} more language(s) before booting") { name = "RunSetupLanguagesNotice" });
                packageList.ElementAt(packageList.childCount - 1).AddToClassList("package-notice");
            }

            if (availableLanguages.Count == 0)
            {
                packageList.Add(new Label("no programming languages unlocked") { name = "RunSetupLanguagesEmpty" });
                packageList.ElementAt(packageList.childCount - 1).AddToClassList("package-empty");
                return;
            }

            for (int i = 0; i < availableLanguages.Count; i++)
            {
                LanguageCatalogEntry language = availableLanguages[i];
                packageList.Add(BuildRunLanguageRow(language, IsRunLanguageSelected(language.Language)));
            }
        }

        private VisualElement BuildRunLanguageRow(LanguageCatalogEntry language, bool selected)
        {
            VisualElement row = new();
            row.AddToClassList("package-row");
            row.AddToClassList("run-language-row");
            row.EnableInClassList("equipped", selected);
            row.RegisterCallback<ClickEvent>(_ => ToggleRunLanguage(language.Language));

            VisualElement summary = new();
            summary.AddToClassList("package-summary");
            summary.Add(new Label(selected ? "[x]" : "[ ]") { name = "RunSetupLanguageMarker" });
            summary.ElementAt(0).AddToClassList("package-marker");

            summary.Add(new Label(language.DisplayName) { name = "RunSetupLanguageName" });
            summary.ElementAt(1).AddToClassList("package-name");

            summary.Add(new Label(language.ResolutionTrack.ToString()) { name = "RunSetupLanguageTrack" });
            summary.ElementAt(2).AddToClassList("package-meta");
            row.Add(summary);

            row.Add(new Label(language.IdentityTag) { name = "RunSetupLanguageDescription" });
            row.ElementAt(1).AddToClassList("package-description");
            return row;
        }

        private void ToggleRunLanguage(Language language)
        {
            if (IsRunLanguageSelected(language))
            {
                selectedRunLanguages.Remove(language);
                runSetupNotice = null;
                RenderSelectedPackageDetail();
                return;
            }

            if (selectedRunLanguages.Count >= 2)
            {
                runSetupNotice = "language limit reached";
                RenderSelectedPackageDetail();
                return;
            }

            selectedRunLanguages.Add(language);
            runSetupNotice = null;
            RenderSelectedPackageDetail();
        }

        private void ToggleCardInSelectedLoadout(DistroDefinition unit, CardDefinition card)
        {
            runSetupNotice = TryToggleLoadoutCard(unit, card);
            RenderSelectedPackageDetail();
        }

        private string TryToggleLoadoutCard(DistroDefinition unit, CardDefinition card)
        {
            if (unit == null || card == null)
            {
                return "loadout change failed";
            }

            IReadOnlyList<string> equippedCardIds = cardLoadout.GetEquippedCardIds(unit.Id);
            bool equipped = ContainsCardId(equippedCardIds, card.Id);
            CardLoadoutFailureReason reason;
            bool changed = equipped
                ? cardLoadout.TryUnequip(unit.Id, card.Id, out reason)
                : cardLoadout.TryEquip(unit.Id, card.Id, out reason);

            return changed ? null : FormatLoadoutFailure(reason);
        }

        private static bool ContainsCardId(IReadOnlyList<string> cardIds, string cardId)
        {
            for (int i = 0; i < cardIds.Count; i++)
            {
                if (string.Equals(cardIds[i], cardId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private IReadOnlyList<LanguageCatalogEntry> GetAvailableRunLanguages()
        {
            List<LanguageCatalogEntry> available = new();
            IReadOnlyList<LanguageCatalogEntry> languages = LanguageCatalog.All;
            for (int i = 0; i < languages.Count; i++)
            {
                if (LanguageUnlock.IsUnlocked(languages[i].Language, playerCollection))
                {
                    available.Add(languages[i]);
                }
            }

            return available;
        }

        private bool IsRunLanguageSelected(Language language)
        {
            for (int i = 0; i < selectedRunLanguages.Count; i++)
            {
                if (selectedRunLanguages[i] == language)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanBootRun(IReadOnlyList<string> equippedCardIds)
        {
            return equippedCardIds.Count >= CardLoadout.MaxEquippedCards && selectedRunLanguages.Count == 2;
        }

        private static string FormatLoadoutFailure(CardLoadoutFailureReason reason)
        {
            return reason switch
            {
                CardLoadoutFailureReason.Full => $"loadout already has {CardLoadout.MaxEquippedCards} cards",
                CardLoadoutFailureReason.Duplicate => "card is already equipped",
                CardLoadoutFailureReason.Token => "token cards cannot be equipped",
                CardLoadoutFailureReason.NotEquipped => "card is not equipped",
                CardLoadoutFailureReason.NotOwned => "card is not owned by this distro",
                _ => "loadout change failed"
            };
        }

        private VisualElement BuildBootRow(DistroDefinition unit, bool enabled)
        {
            VisualElement row = new();
            row.AddToClassList("command-row");
            row.AddToClassList("boot-command-row");
            row.EnableInClassList("disabled-row", !enabled);
            row.RegisterCallback<ClickEvent>(_ => BootSelectedPackage());

            row.Add(new Label(">") { name = "BootCommandCursor" });
            row.ElementAt(0).AddToClassList("command-cursor");
            row.Add(new Label($"./boot --distro={unit.Id}") { name = "BootCommandText" });
            row.ElementAt(1).AddToClassList("command-text");
            row.Add(new Label("start run") { name = "BootCommandDescription" });
            row.ElementAt(2).AddToClassList("command-description");
            return row;
        }

        private void BootSelectedPackage()
        {
            if (selectedPackageIndex < 0 || selectedPackageIndex >= playerCollection.OwnedUnits.Count)
            {
                return;
            }

            DistroDefinition unit = playerCollection.OwnedUnits[selectedPackageIndex];
            IReadOnlyList<string> equippedCardIds = cardLoadout.GetEquippedCardIds(unit.Id);
            if (equippedCardIds.Count < CardLoadout.MaxEquippedCards)
            {
                runSetupNotice = equippedCardIds.Count == 0
                    ? "boot: no packages staged"
                    : $"boot: stage {CardLoadout.MaxEquippedCards} packages";
                RenderSelectedPackageDetail();
                return;
            }

            if (selectedRunLanguages.Count < 2)
            {
                runSetupNotice = "boot: select 2 programming languages";
                RenderSelectedPackageDetail();
                return;
            }

            SaveLastRunLoadout(unit, equippedCardIds);
            SceneLoader.LoadGame();
        }

        private void HandlePointerDown(PointerDownEvent evt)
        {
            root.Focus();
            suppressNextClick = SkipBootIntro();
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
            cardLoadout.ClearAll();
            selectedRunLanguages.Clear();
            runSetupNotice = null;
            ShowPanel(mainMenuPanel);
            root.Focus();
        }

        private void ShowCollection()
        {
            ShowCollectionUnits();
            ShowPanel(collectionPanel);
        }

        private void HandleCollectionBack()
        {
            if (collectionScreen.BackFromSubview())
            {
                SyncCollectionFrame();
                return;
            }

            ShowMainMenu();
        }

        private void ShowCollectionUnits()
        {
            collectionShowingLanguages = false;
            collectionUnitsButton.EnableInClassList(SelectedClassName, true);
            collectionLanguagesButton.EnableInClassList(SelectedClassName, false);
            collectionScreen.RefreshUnits(playerCollection.OwnedUnits);
            SyncCollectionFrame();
        }

        private void ShowCollectionLanguages()
        {
            collectionShowingLanguages = true;
            collectionUnitsButton.EnableInClassList(SelectedClassName, false);
            collectionLanguagesButton.EnableInClassList(SelectedClassName, true);
            collectionScreen.ShowLanguages();
            SyncCollectionFrame();
        }

        private void ToggleCollectionTab()
        {
            if (collectionShowingLanguages)
            {
                ShowCollectionUnits();
                return;
            }

            ShowCollectionLanguages();
        }

        private void SyncCollectionFrame()
        {
            collectionFrame.SetTitle(collectionScreen.CurrentTitle);
            collectionFrame.SetHint(collectionScreen.CurrentHint);
        }

        private void ShowGacha()
        {
            gachaScreen.Open();
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

            bool showingMainMenu = mainMenuPanel == activePanel;
            motdBlock.EnableInClassList(HiddenClassName, !showingMainMenu || string.IsNullOrWhiteSpace(motdBody));

            if (showingMainMenu)
            {
                RefreshEventBanner();
            }
            else
            {
                eventBanner.AddToClassList(HiddenClassName);
            }

            RefreshCurrencyReadouts();
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
