using System;
using System.Collections.Generic;
using KernelPanic.Core;
using KernelPanic.Data;
using KernelPanic.Meta;
using KernelPanic.Run;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KernelPanic.UI
{
    internal static class TerminalFontResolver
    {
        private static readonly string[] PreferredMonospaceFontFamilies =
        {
            "Consolas",
            "Cascadia Mono",
            "Menlo",
            "Monaco",
            "DejaVu Sans Mono",
            "Liberation Mono",
            "Ubuntu Mono",
            "Courier New"
        };

        private static FontAsset runtimeMonospaceFont;
        private static bool warnedMissingFont;

        public static FontAsset Resolve(FontAsset configuredFont)
        {
            if (configuredFont != null)
            {
                return configuredFont;
            }

            if (runtimeMonospaceFont != null)
            {
                return runtimeMonospaceFont;
            }

            Font dynamicFont = Font.CreateDynamicFontFromOSFont(PreferredMonospaceFontFamilies, 16);
            if (dynamicFont == null)
            {
                if (!warnedMissingFont)
                {
                    Debug.LogWarning("No monospace OS font found; terminal UI will use Unity's default font.");
                    warnedMissingFont = true;
                }

                return null;
            }

            runtimeMonospaceFont = FontAsset.CreateFontAsset(dynamicFont);
            return runtimeMonospaceFont;
        }
    }

    /// <summary>
    /// Binds the main menu terminal UI document and routes command activation between panels.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        private const string HiddenClassName = "hidden";
        private const string SelectedClassName = "selected";
        private const string CursorOnClassName = "cursor-on";
        private const string SharedScrollbarStyleResourcePath = "TerminalScrollbars";
        private const float BootIntroSeconds = 1.5f;
        private static bool _bootIntroPlayed;

        // Static fields don't reset between Play sessions when the Editor's "Reload Domain"
        // option is disabled. RuntimeInitializeOnLoadMethod runs once per player/Play-mode
        // startup regardless of that setting, so this keeps bootIntroPlayed session-scoped.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionState()
        {
            _bootIntroPlayed = false;
        }

        [SerializeField] private FontAsset monospaceFont;
        [SerializeField] private string motdBody = "unstable userspace detected; keep a rollback shell open.";
        [SerializeField] private DistroDatabase distroDatabase;
        [SerializeField] private CardDatabase cardDatabase;
        [SerializeField] private LanguageDeckDatabase languageDeckDatabase;
        [SerializeField] private FeaturedUnitPanel featuredUnitPanel = new();
        [SerializeField] private CollectionScreenController collectionScreen = new();
        [SerializeField] private StarterSelectionController starterSelection = new();
        [SerializeField] private GachaScreenController gachaScreen = new();

        private readonly List<CommandMenuEntry> _commandEntries = new();
        private readonly List<VisualElement> _packageRows = new();
        private readonly List<VisualElement> _loadoutRows = new();
        private readonly List<Language> _selectedRunLanguages = new();
        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _shellRoot;
        private VisualElement _bootIntroPanel;
        private VisualElement _mainMenuPanel;
        private VisualElement _collectionPanel;
        private VisualElement _runSetupPanel;
        private VisualElement _gachaPanel;
        private VisualElement _settingsPanel;
        private VisualElement _eventBanner;
        private VisualElement _backgroundLogLayer;
        private ScrollView _runSetupList;
        private VisualElement _runSetupDetail;
        private ScrollView _runSetupPackageScroll;
        private Label _appIdLabel;
        private Label _entropyLabel;
        private Label _pullTokensLabel;
        private Label _titleCursorLabel;
        private Label _promptCursorLabel;
        private Label _bootIntroLogLabel;
        private Label _motdBodyLabel;
        private VisualElement _motdBlock;
        private Button _rootCreditsToEntropyButton;
        private VisualElement _rootCreditExchangeModal;
        private Label _rootCreditExchangeBalanceLabel;
        private Label _rootCreditExchangePreviewLabel;
        private Label _rootCreditExchangeMessageLabel;
        private TextField _rootCreditExchangeInput;
        private Button _rootCreditExchangeMinusHundredButton;
        private Button _rootCreditExchangeMinusOneButton;
        private Button _rootCreditExchangeNoneButton;
        private Button _rootCreditExchangePlusOneButton;
        private Button _rootCreditExchangePlusHundredButton;
        private Button _rootCreditExchangeMaxButton;
        private Button _rootCreditExchangeConfirmButton;
        private Button _rootCreditExchangeCancelButton;
        private Button _rootCreditExchangeCloseButton;
        private Button _collectionUnitsButton;
        private Button _collectionLanguagesButton;
        private readonly ScreenFrameController _runSetupFrame = new();
        private readonly ScreenFrameController _collectionFrame = new();
        private readonly ScreenFrameController _gachaFrame = new();
        private readonly ScreenFrameController _settingsFrame = new();
        private SaveService _saveService;
        private SaveData _saveData;
        private EntropyWallet _wallet;
        private GachaService _gachaService;
        private PlayerCollection _playerCollection;
        private CardLoadout _cardLoadout;
        private BackgroundLogRingBuffer _backgroundLog;
        private IEventBannerSource _eventBannerSource;
        private int _selectedCommandIndex;
        private int _selectedPackageIndex;
        private string _runSetupNotice;
        private bool _collectionShowingLanguages;
        private float _bootIntroElapsed;
        private int _bootIntroCharacterCount;
        private bool _cursorVisible;
        private bool _suppressNextClick;
        private bool _warnedUnresolvedSaveId;
        private bool _rootCreditExchangeOpen;
        private string _bootIntroCopy;
        private Action _rootCreditExchangeMinusHundredClicked;
        private Action _rootCreditExchangeMinusOneClicked;
        private Action _rootCreditExchangePlusOneClicked;
        private Action _rootCreditExchangePlusHundredClicked;
        private IVisualElementScheduledItem _blinkSchedule;
        private IVisualElementScheduledItem _bootIntroSchedule;

        public void Initialize(EntropyWallet initializedWallet)
        {
            _wallet = initializedWallet ?? new EntropyWallet();
            RefreshCurrencyReadouts();
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _saveService = new SaveService();
            _gachaService = new GachaService();
            _playerCollection = new PlayerCollection(); // TODO: Replace with persistent player-collection service composition.
            _cardLoadout = new CardLoadout(_playerCollection.OwnedUnits);
            Initialize(new EntropyWallet()); // TODO: Replace with persistent wallet service composition.
            BindElements();
            LoadSharedStyles();
            BindCommandEntries();
            RegisterCommandEntryCallbacks();
            BindScreenFrames();
            starterSelection.Bind(_root, distroDatabase, HandleStarterConfirmed);
            FontAsset resolvedFont = ResolveMonospaceFont();
            featuredUnitPanel.Bind(_root, resolvedFont);
            collectionScreen.Bind(_root, resolvedFont, languageDeckDatabase, cardDatabase, _playerCollection, GetMergesBalance, UpgradeCollectionUnit);
            gachaScreen.Bind(_root, distroDatabase, resolvedFont, _gachaService, _playerCollection, _wallet, ResolvePulledDistros, HandleMetaStateChanged, OpenRootCreditExchange);
            LoadMetaState();
            _playerCollection.Changed += HandleMetaStateChanged;
            _gachaService.Changed += HandleMetaStateChanged;
            ApplyOptionalFont(resolvedFont);
        }

        private void OnEnable()
        {
            RegisterCallbacks();
            _root.Focus();
            _shellRoot.EnableInClassList("reduced-motion", UIPreferences.ReducedMotion);
            RefreshStaticText();
            RefreshCurrencyReadouts();
            RefreshEventBanner();
            featuredUnitPanel.Refresh(_playerCollection.OwnedUnits, _playerCollection.FeaturedUnit);
            collectionScreen.RefreshUnits(_playerCollection.OwnedUnits);
            gachaScreen.Refresh();
            SelectCommand(0);
            ShowMainMenu();
            StartAmbientSchedules();
            PlayBootIntroIfNeeded();
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
            _blinkSchedule?.Pause();
            _backgroundLog?.Stop();
            _bootIntroSchedule?.Pause();
            starterSelection.PauseSchedules();
        }

        private void BindElements()
        {
            _root = _document.rootVisualElement;
            _shellRoot = _root.Q<VisualElement>("ShellRoot");
            _bootIntroPanel = _root.Q<VisualElement>("BootIntroPanel");
            _mainMenuPanel = _root.Q<VisualElement>("MainMenuPanel");
            _collectionPanel = _root.Q<VisualElement>("CollectionPanel");
            _runSetupPanel = _root.Q<VisualElement>("RunSetupPanel");
            _gachaPanel = _root.Q<VisualElement>("GachaPanel");
            _settingsPanel = _root.Q<VisualElement>("SettingsPanel");
            _eventBanner = _root.Q<VisualElement>("EventBanner");
            _motdBlock = _root.Q<VisualElement>("MotdBlock");
            _backgroundLogLayer = _root.Q<VisualElement>("BackgroundLogLayer");
            _runSetupList = _root.Q<ScrollView>("RunSetupList");
            _runSetupDetail = _root.Q<VisualElement>("RunSetupDetail");
            _backgroundLog = new BackgroundLogRingBuffer(_backgroundLogLayer, BootLogCopy.Lines);

            _appIdLabel = _root.Q<Label>("AppIdLabel");
            _entropyLabel = _root.Q<Label>("EntropyLabel");
            _pullTokensLabel = _root.Q<Label>("PullTokensLabel");
            _titleCursorLabel = _root.Q<Label>("TitleCursorLabel");
            _promptCursorLabel = _root.Q<Label>("PromptCursorLabel");
            _bootIntroLogLabel = _root.Q<Label>("BootIntroLogLabel");
            _motdBodyLabel = _root.Q<Label>("MotdBodyLabel");

            _rootCreditsToEntropyButton = _root.Q<Button>("RootCreditsToEntropyButton");
            if (_rootCreditsToEntropyButton != null)
            {
                _rootCreditsToEntropyButton.focusable = false;
            }

            _rootCreditExchangeModal = _root.Q<VisualElement>("RootCreditExchangeModal");
            _rootCreditExchangeBalanceLabel = _root.Q<Label>("RootCreditExchangeBalanceLabel");
            _rootCreditExchangePreviewLabel = _root.Q<Label>("RootCreditExchangePreviewLabel");
            _rootCreditExchangeMessageLabel = _root.Q<Label>("RootCreditExchangeMessageLabel");
            _rootCreditExchangeInput = _root.Q<TextField>("RootCreditExchangeInput");
            _rootCreditExchangeMinusHundredButton = _root.Q<Button>("RootCreditExchangeMinusHundredButton");
            _rootCreditExchangeMinusOneButton = _root.Q<Button>("RootCreditExchangeMinusOneButton");
            _rootCreditExchangeNoneButton = _root.Q<Button>("RootCreditExchangeNoneButton");
            _rootCreditExchangePlusOneButton = _root.Q<Button>("RootCreditExchangePlusOneButton");
            _rootCreditExchangePlusHundredButton = _root.Q<Button>("RootCreditExchangePlusHundredButton");
            _rootCreditExchangeMaxButton = _root.Q<Button>("RootCreditExchangeMaxButton");
            _rootCreditExchangeConfirmButton = _root.Q<Button>("RootCreditExchangeConfirmButton");
            _rootCreditExchangeCancelButton = _root.Q<Button>("RootCreditExchangeCancelButton");
            _rootCreditExchangeCloseButton = _root.Q<Button>("RootCreditExchangeCloseButton");
            _collectionUnitsButton = _root.Q<Button>("CollectionUnitsButton");
            _collectionLanguagesButton = _root.Q<Button>("CollectionLanguagesButton");
        }

        private void LoadSharedStyles()
        {
            StyleSheet scrollbarStyleSheet = Resources.Load<StyleSheet>(SharedScrollbarStyleResourcePath);
            if (scrollbarStyleSheet != null)
            {
                _root.styleSheets.Add(scrollbarStyleSheet);
            }
        }

        private void BindScreenFrames()
        {
            _runSetupFrame.Bind(_runSetupPanel, "$ ./start_run --configure", "[esc] back   [arrows] navigate   [enter] select   [b] boot run", ShowMainMenu);
            _collectionFrame.Bind(_collectionPanel, "$ ls ~/collection", "[esc] back   [left/right] tabs   [tab] tabs   [arrows] navigate   [enter] select", HandleCollectionBack);
            _gachaFrame.Bind(_gachaPanel, "$ curl gacha.sh | sh", "[esc] back   click banner   click pull", ShowMainMenu);
            _settingsFrame.Bind(_settingsPanel, "$ dpkg-reconfigure kernel-panic", "[esc] back", ShowMainMenu);
        }

        private void BindCommandEntries()
        {
            _commandEntries.Clear();
            _commandEntries.Add(new CommandMenuEntry(_root.Q<VisualElement>("CommandStartRun"), HandleStartRunClicked));
            _commandEntries.Add(new CommandMenuEntry(_root.Q<VisualElement>("CommandCollection"), ShowCollection));
            _commandEntries.Add(new CommandMenuEntry(_root.Q<VisualElement>("CommandGacha"), ShowGacha));
            _commandEntries.Add(new CommandMenuEntry(_root.Q<VisualElement>("CommandSettings"), ShowSettings));
            _commandEntries.Add(new CommandMenuEntry(_root.Q<VisualElement>("CommandQuit"), HandleQuitClicked));
        }

        private FontAsset ResolveMonospaceFont()
        {
            monospaceFont = TerminalFontResolver.Resolve(monospaceFont);
            return monospaceFont;
        }

        private void ApplyOptionalFont(FontAsset resolvedFont)
        {
            if (resolvedFont == null)
            {
                return;
            }

            _root.style.unityFontDefinition = new StyleFontDefinition(resolvedFont);
        }

        private void RegisterCallbacks()
        {
            _root.RegisterCallback<KeyDownEvent>(HandleKeyDown);
            _root.RegisterCallback<PointerDownEvent>(HandlePointerDown);
            if (_rootCreditsToEntropyButton != null)
            {
                _rootCreditsToEntropyButton.clicked += HandleRootCreditsToEntropyClicked;
            }

            if (_rootCreditExchangeInput != null)
            {
                _rootCreditExchangeInput.RegisterValueChangedCallback(HandleRootCreditExchangeInputChanged);
            }

            if (_rootCreditExchangeMinusHundredButton != null)
            {
                _rootCreditExchangeMinusHundredClicked ??= () => AddRootCreditExchangeAmount(-100);
                _rootCreditExchangeMinusHundredButton.clicked += _rootCreditExchangeMinusHundredClicked;
            }

            if (_rootCreditExchangeMinusOneButton != null)
            {
                _rootCreditExchangeMinusOneClicked ??= () => AddRootCreditExchangeAmount(-1);
                _rootCreditExchangeMinusOneButton.clicked += _rootCreditExchangeMinusOneClicked;
            }

            if (_rootCreditExchangeNoneButton != null)
            {
                _rootCreditExchangeNoneButton.clicked += SetRootCreditExchangeNone;
            }

            if (_rootCreditExchangePlusOneButton != null)
            {
                _rootCreditExchangePlusOneClicked ??= () => AddRootCreditExchangeAmount(1);
                _rootCreditExchangePlusOneButton.clicked += _rootCreditExchangePlusOneClicked;
            }

            if (_rootCreditExchangePlusHundredButton != null)
            {
                _rootCreditExchangePlusHundredClicked ??= () => AddRootCreditExchangeAmount(100);
                _rootCreditExchangePlusHundredButton.clicked += _rootCreditExchangePlusHundredClicked;
            }

            if (_rootCreditExchangeMaxButton != null)
            {
                _rootCreditExchangeMaxButton.clicked += SetRootCreditExchangeMax;
            }

            if (_rootCreditExchangeConfirmButton != null)
            {
                _rootCreditExchangeConfirmButton.clicked += ConfirmRootCreditExchange;
            }

            if (_rootCreditExchangeCancelButton != null)
            {
                _rootCreditExchangeCancelButton.clicked += CloseRootCreditExchange;
            }

            if (_rootCreditExchangeCloseButton != null)
            {
                _rootCreditExchangeCloseButton.clicked += CloseRootCreditExchange;
            }

            if (_rootCreditExchangeModal != null)
            {
                _rootCreditExchangeModal.RegisterCallback<KeyDownEvent>(HandleRootCreditExchangeKeyDown, TrickleDown.TrickleDown);
            }

            collectionScreen.ViewChanged += SyncCollectionFrame;
            _collectionUnitsButton.RegisterCallback<ClickEvent>(HandleCollectionUnitsTabClicked);
            _collectionLanguagesButton.RegisterCallback<ClickEvent>(HandleCollectionLanguagesTabClicked);
        }

        private void UnregisterCallbacks()
        {
            _root.UnregisterCallback<KeyDownEvent>(HandleKeyDown);
            _root.UnregisterCallback<PointerDownEvent>(HandlePointerDown);
            if (_rootCreditsToEntropyButton != null)
            {
                _rootCreditsToEntropyButton.clicked -= HandleRootCreditsToEntropyClicked;
            }

            if (_rootCreditExchangeInput != null)
            {
                _rootCreditExchangeInput.UnregisterValueChangedCallback(HandleRootCreditExchangeInputChanged);
            }

            if (_rootCreditExchangeMinusHundredButton != null)
            {
                _rootCreditExchangeMinusHundredButton.clicked -= _rootCreditExchangeMinusHundredClicked;
            }

            if (_rootCreditExchangeMinusOneButton != null)
            {
                _rootCreditExchangeMinusOneButton.clicked -= _rootCreditExchangeMinusOneClicked;
            }

            if (_rootCreditExchangeNoneButton != null)
            {
                _rootCreditExchangeNoneButton.clicked -= SetRootCreditExchangeNone;
            }

            if (_rootCreditExchangePlusOneButton != null)
            {
                _rootCreditExchangePlusOneButton.clicked -= _rootCreditExchangePlusOneClicked;
            }

            if (_rootCreditExchangePlusHundredButton != null)
            {
                _rootCreditExchangePlusHundredButton.clicked -= _rootCreditExchangePlusHundredClicked;
            }

            if (_rootCreditExchangeMaxButton != null)
            {
                _rootCreditExchangeMaxButton.clicked -= SetRootCreditExchangeMax;
            }

            if (_rootCreditExchangeConfirmButton != null)
            {
                _rootCreditExchangeConfirmButton.clicked -= ConfirmRootCreditExchange;
            }

            if (_rootCreditExchangeCancelButton != null)
            {
                _rootCreditExchangeCancelButton.clicked -= CloseRootCreditExchange;
            }

            if (_rootCreditExchangeCloseButton != null)
            {
                _rootCreditExchangeCloseButton.clicked -= CloseRootCreditExchange;
            }

            if (_rootCreditExchangeModal != null)
            {
                _rootCreditExchangeModal.UnregisterCallback<KeyDownEvent>(HandleRootCreditExchangeKeyDown, TrickleDown.TrickleDown);
            }

            collectionScreen.ViewChanged -= SyncCollectionFrame;
            _collectionUnitsButton.UnregisterCallback<ClickEvent>(HandleCollectionUnitsTabClicked);
            _collectionLanguagesButton.UnregisterCallback<ClickEvent>(HandleCollectionLanguagesTabClicked);
        }

        private void HandleRootCreditsToEntropyClicked()
        {
            if (_gachaService == null || _wallet == null)
            {
                return;
            }

            OpenRootCreditExchange();
        }

        private void OpenRootCreditExchange()
        {
            if (_rootCreditExchangeModal == null || _gachaService == null || _wallet == null)
            {
                return;
            }

            _rootCreditExchangeOpen = true;
            _rootCreditExchangeModal.RemoveFromClassList(HiddenClassName);
            _rootCreditExchangeMessageLabel?.AddToClassList(HiddenClassName);
            SetRootCreditExchangeAmount(Math.Min(100, _gachaService.RootCredits));
            RefreshRootCreditExchange();
            _rootCreditExchangeInput?.Focus();
        }

        private void CloseRootCreditExchange()
        {
            _rootCreditExchangeOpen = false;
            _rootCreditExchangeModal?.AddToClassList(HiddenClassName);
            _root.Focus();
        }

        private void HandleRootCreditExchangeKeyDown(KeyDownEvent evt)
        {
            if (!_rootCreditExchangeOpen || evt.keyCode != KeyCode.Escape)
            {
                return;
            }

            CloseRootCreditExchange();
            evt.StopImmediatePropagation();
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
            SetRootCreditExchangeAmount(_gachaService == null ? 0 : _gachaService.RootCredits);
        }

        private void SetRootCreditExchangeNone()
        {
            SetRootCreditExchangeAmount(0);
        }

        private void SetRootCreditExchangeAmount(int amount)
        {
            int max = _gachaService == null ? 0 : _gachaService.RootCredits;
            int clamped = Mathf.Clamp(amount, 0, max);
            if (_rootCreditExchangeInput != null)
            {
                _rootCreditExchangeInput.value = clamped.ToString();
            }

            RefreshRootCreditExchange();
        }

        private int GetRootCreditExchangeAmount()
        {
            if (_rootCreditExchangeInput == null || string.IsNullOrWhiteSpace(_rootCreditExchangeInput.value))
            {
                return 0;
            }

            if (!int.TryParse(_rootCreditExchangeInput.value, out int amount))
            {
                return 0;
            }

            int max = _gachaService == null ? 0 : _gachaService.RootCredits;
            return Mathf.Clamp(amount, 0, max);
        }

        private void RefreshRootCreditExchange()
        {
            if (_gachaService == null)
            {
                return;
            }

            int amount = GetRootCreditExchangeAmount();
            if (_rootCreditExchangeInput != null && _rootCreditExchangeInput.value != amount.ToString())
            {
                _rootCreditExchangeInput.SetValueWithoutNotify(amount.ToString());
            }

            if (_rootCreditExchangeBalanceLabel != null)
            {
                _rootCreditExchangeBalanceLabel.text = _gachaService.RootCredits.ToString();
            }

            if (_rootCreditExchangePreviewLabel != null)
            {
                _rootCreditExchangePreviewLabel.text = $"+{amount} entropy";
            }

            int max = _gachaService.RootCredits;
            _rootCreditExchangeMinusHundredButton?.SetEnabled(amount > 0);
            _rootCreditExchangeMinusOneButton?.SetEnabled(amount > 0);
            _rootCreditExchangeNoneButton?.SetEnabled(amount > 0);
            _rootCreditExchangePlusOneButton?.SetEnabled(amount < max);
            _rootCreditExchangePlusHundredButton?.SetEnabled(amount < max);
            _rootCreditExchangeMaxButton?.SetEnabled(max > 0 && amount < max);
            _rootCreditExchangeConfirmButton?.SetEnabled(amount > 0);
        }

        private void ConfirmRootCreditExchange()
        {
            int amount = GetRootCreditExchangeAmount();
            if (!_gachaService.ConvertRootCreditsToEntropy(_wallet, amount, out string failureReason))
            {
                if (_rootCreditExchangeMessageLabel != null)
                {
                    _rootCreditExchangeMessageLabel.text = $"exchange failed: {failureReason}";
                    _rootCreditExchangeMessageLabel.RemoveFromClassList(HiddenClassName);
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
            _saveData = _saveService.Load();
            _saveData.EnsureLists();
            _wallet.SetBalance(_saveData.entropyBalance);

            for (int i = 0; i < _saveData.ownedUnits.Count; i++)
            {
                OwnedUnitSaveEntry entry = _saveData.ownedUnits[i];
                DistroDefinition unit = ResolveSavedDistro(entry?.id);
                if (unit != null)
                {
                    _playerCollection.Add(unit, entry.version);
                }
            }

            for (int i = 0; i < _saveData.bannerPoolIds.Count; i++)
            {
                DistroDefinition unit = ResolveSavedDistro(_saveData.bannerPoolIds[i]);
                if (unit != null)
                {
                    _gachaService.AddToBannerPool(unit);
                }
            }

            _gachaService.LoadProgress(_saveData);

            if (_saveData.starterChosen)
            {
                AddAllFourStarDistrosToBeginnerBannerPool();
            }

            if (_gachaService.BeginnerState.guaranteedDistroIds.Count == 0 && _saveData.bannerPoolIds.Count > 0)
            {
                _gachaService.BeginnerState.guaranteedDistroIds.AddRange(_saveData.bannerPoolIds);
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
                _gachaService.AddToBannerPool(distros[i]);
            }
        }

        private DistroDefinition ResolveSavedDistro(string id)
        {
            DistroDefinition unit = distroDatabase == null ? null : distroDatabase.FindById(id);
            if (unit == null && !_warnedUnresolvedSaveId)
            {
                _warnedUnresolvedSaveId = true;
                Debug.LogWarning($"Save references a distro id that is not in DistroDatabase: {id}");
            }

            return unit;
        }

        private void HandleMetaStateChanged()
        {
            SaveCurrentState();
            featuredUnitPanel.Refresh(_playerCollection.OwnedUnits, _playerCollection.FeaturedUnit);
            collectionScreen.RefreshUnits(_playerCollection.OwnedUnits);
            gachaScreen.Refresh();
            RefreshCurrencyReadouts();
        }

        private void HandleStarterConfirmed(DistroDefinition picked, IReadOnlyList<DistroDefinition> remaining)
        {
            _playerCollection.Add(picked, 1);
            AddAllFourStarDistrosToBeginnerBannerPool();
            _gachaService.SetBeginnerGuaranteedDistros(remaining);
            _saveData.starterChosen = true;
            SaveCurrentState();
            featuredUnitPanel.Refresh(_playerCollection.OwnedUnits, _playerCollection.FeaturedUnit);
            collectionScreen.RefreshUnits(_playerCollection.OwnedUnits);
            gachaScreen.Refresh();
        }

        private void SaveCurrentState()
        {
            _saveData ??= SaveData.CreateDefault();
            _saveData.EnsureLists();
            Dictionary<string, int> mergeBalances = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _saveData.ownedUnits.Count; i++)
            {
                OwnedUnitSaveEntry entry = _saveData.ownedUnits[i];
                if (entry != null && !string.IsNullOrWhiteSpace(entry.id))
                {
                    mergeBalances[entry.id] = Mathf.Max(0, entry.merges);
                }
            }

            _saveData.entropyBalance = _wallet == null ? 0 : _wallet.Balance;
            _saveData.ownedUnits.Clear();
            _saveData.ownedUnitIds.Clear();
            _saveData.bannerPoolIds.Clear();
            _gachaService.WriteProgress(_saveData);

            for (int i = 0; i < _playerCollection.OwnedUnits.Count; i++)
            {
                DistroDefinition unit = _playerCollection.OwnedUnits[i];
                if (unit != null && !string.IsNullOrWhiteSpace(unit.Id))
                {
                    _saveData.ownedUnits.Add(new OwnedUnitSaveEntry
                    {
                        id = unit.Id,
                        version = Mathf.Clamp(_playerCollection.GetVersion(unit.Id), 1, GachaTuning.MaxVersion),
                        merges = mergeBalances.TryGetValue(unit.Id, out int merges) ? merges : 0
                    });
                }
            }

            for (int i = 0; i < _gachaService.BannerPool.Count; i++)
            {
                DistroDefinition unit = _gachaService.BannerPool[i];
                if (unit != null && !string.IsNullOrWhiteSpace(unit.Id))
                {
                    _saveData.bannerPoolIds.Add(unit.Id);
                }
            }

            _saveService.Save(_saveData);
        }

        private PullResolutionResult ResolvePulledDistros(IReadOnlyList<DistroDefinition> pulledDistros)
        {
            PullResolutionContext context = new(_saveData, _playerCollection, distroDatabase, _playerCollection.FeaturedUnit?.Id);
            return PullResolver.Resolve(pulledDistros, context);
        }

        private int GetMergesBalance(DistroDefinition unit)
        {
            _saveData ??= SaveData.CreateDefault();
            _saveData.EnsureLists();
            OwnedUnitSaveEntry entry = unit == null ? null : _saveData.FindOwnedUnit(unit.Id);
            return Math.Max(0, entry?.merges ?? 0);
        }

        private VersionUpgradeResult UpgradeCollectionUnit(DistroDefinition unit)
        {
            if (unit == null)
            {
                return VersionUpgradeResult.Failed(null, VersionUpgradeFailureReason.NotOwned);
            }

            VersionUpgradeResult result = VersionUpgrader.TryUpgrade(unit.Id, _saveData, _playerCollection);
            if (result.Success)
            {
                SaveCurrentState();
                featuredUnitPanel.Refresh(_playerCollection.OwnedUnits, _playerCollection.FeaturedUnit);
                collectionScreen.RefreshUnits(_playerCollection.OwnedUnits);
                RefreshCurrencyReadouts();
            }

            return result;
        }

        private void SaveLastRunLoadout(DistroDefinition unit, IReadOnlyList<string> cardIds)
        {
            _saveData ??= SaveData.CreateDefault();
            _saveData.EnsureLists();
            _saveData.lastRunLoadout.distroId = unit == null ? null : unit.Id;
            _saveData.lastRunLoadout.cardIds.Clear();

            for (int i = 0; i < cardIds.Count; i++)
            {
                _saveData.lastRunLoadout.cardIds.Add(cardIds[i]);
            }

            SaveCurrentState();
        }

        private void RefreshStaticText()
        {
            _appIdLabel.text = $"kernel-panic v{Application.version} - tty1";
            _motdBodyLabel.text = motdBody ?? string.Empty;
            _motdBlock.EnableInClassList(HiddenClassName, string.IsNullOrWhiteSpace(motdBody));
            _bootIntroCopy = string.Join("\n", BootLogCopy.Lines);
        }

        private void RegisterCommandEntryCallbacks()
        {
            for (int i = 0; i < _commandEntries.Count; i++)
            {
                int index = i;
                _commandEntries[i].Row.RegisterCallback<PointerEnterEvent>(_ => SelectCommand(index));
                _commandEntries[i].Row.RegisterCallback<ClickEvent>(_ => HandleCommandClicked(index));
            }
        }

        private void RefreshCurrencyReadouts()
        {
            if (_entropyLabel == null || _pullTokensLabel == null || _gachaService == null || _wallet == null)
            {
                return;
            }

            _entropyLabel.text = $"entropy={_wallet.Balance}";
            _rootCreditsToEntropyButton?.SetEnabled(true);
            bool showPullTokens = IsGachaVisible();
            _pullTokensLabel.text = showPullTokens
                ? $"commits={_gachaService.PullTokens} root-credits={_gachaService.LimitedPullTokens} distro-merges={GetTotalDistroMerges()}"
                : string.Empty;
            _pullTokensLabel.EnableInClassList(HiddenClassName, !showPullTokens);
        }

        private int GetTotalDistroMerges()
        {
            _saveData ??= SaveData.CreateDefault();
            _saveData.EnsureLists();
            int total = 0;
            for (int i = 0; i < _saveData.ownedUnits.Count; i++)
            {
                total += Mathf.Max(0, _saveData.ownedUnits[i]?.merges ?? 0);
            }

            return total;
        }

        private void RefreshEventBanner()
        {
            EventBannerContent banner = _eventBannerSource?.GetBanner();
            if (banner == null || string.IsNullOrWhiteSpace(banner.Text))
            {
                _eventBanner.AddToClassList(HiddenClassName);
                return;
            }

            _eventBanner.RemoveFromClassList(HiddenClassName);
            _root.Q<Label>("EventBannerLabel").text = banner.RemainingTime.HasValue
                ? $"{banner.Text}  {banner.RemainingTime.Value:g}"
                : banner.Text;
        }

        private void StartAmbientSchedules()
        {
            _blinkSchedule?.Pause();
            _backgroundLog?.Stop();
            _cursorVisible = true;
            _titleCursorLabel.EnableInClassList(CursorOnClassName, true);
            _promptCursorLabel.EnableInClassList(CursorOnClassName, true);

            if (UIPreferences.ReducedMotion)
            {
                return;
            }

            _blinkSchedule = _root.schedule.Execute(() =>
            {
                _cursorVisible = !_cursorVisible;
                _titleCursorLabel.EnableInClassList(CursorOnClassName, _cursorVisible);
                _promptCursorLabel.EnableInClassList(CursorOnClassName, _cursorVisible);
            }).Every(500);
        }

        private void PlayBootIntroIfNeeded()
        {
            if (_bootIntroPlayed || UIPreferences.ReducedMotion)
            {
                CompleteBootIntro();
                return;
            }

            _bootIntroPlayed = true;
            _bootIntroElapsed = 0f;
            _bootIntroCharacterCount = 0;
            _bootIntroLogLabel.text = string.Empty;
            _bootIntroPanel.RemoveFromClassList(HiddenClassName);
            _shellRoot.AddToClassList("boot-hidden");

            _bootIntroSchedule = _root.schedule.Execute(UpdateBootIntro).Every(16);
        }

        private void UpdateBootIntro()
        {
            _bootIntroElapsed += 0.016f;
            int targetCount = Mathf.Clamp(Mathf.CeilToInt(_bootIntroCopy.Length * (_bootIntroElapsed / BootIntroSeconds)), 0, _bootIntroCopy.Length);
            if (targetCount != _bootIntroCharacterCount)
            {
                _bootIntroCharacterCount = targetCount;
                _bootIntroLogLabel.text = _bootIntroCopy.Substring(0, _bootIntroCharacterCount);
            }

            if (_bootIntroElapsed >= BootIntroSeconds)
            {
                CompleteBootIntro();
            }
        }

        private void CompleteBootIntro()
        {
            _bootIntroSchedule?.Pause();
            _bootIntroPanel.AddToClassList(HiddenClassName);
            _shellRoot.RemoveFromClassList("boot-hidden");
            _shellRoot.AddToClassList("boot-visible");
            _backgroundLog?.Start(UIPreferences.ReducedMotion);
            starterSelection.ShowIfNeeded(_saveData == null || _saveData.starterChosen);
            _root.Focus();
        }

        private bool SkipBootIntro()
        {
            if (_bootIntroPanel.ClassListContains(HiddenClassName))
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

            if (_rootCreditExchangeOpen)
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
                    SelectPackage(_selectedPackageIndex - 1);
                    evt.StopPropagation();
                    return;
                }

                if (evt.keyCode == KeyCode.DownArrow)
                {
                    SelectPackage(_selectedPackageIndex + 1);
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
                SelectCommand(_selectedCommandIndex - 1);
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.DownArrow)
            {
                SelectCommand(_selectedCommandIndex + 1);
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Space)
            {
                ActivateCommand(_selectedCommandIndex);
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.Tab)
            {
                _playerCollection.SelectNextFeatured();
                featuredUnitPanel.Refresh(_playerCollection.OwnedUnits, _playerCollection.FeaturedUnit);
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
            return _runSetupPanel != null && !_runSetupPanel.ClassListContains(HiddenClassName);
        }

        private bool IsCollectionVisible()
        {
            return _collectionPanel != null && !_collectionPanel.ClassListContains(HiddenClassName);
        }

        private bool IsGachaVisible()
        {
            return _gachaPanel != null && !_gachaPanel.ClassListContains(HiddenClassName);
        }

        private bool IsSubScreenVisible()
        {
            return IsRunSetupVisible() || IsCollectionVisible() ||
                   IsGachaVisible() ||
                   (_settingsPanel != null && !_settingsPanel.ClassListContains(HiddenClassName));
        }

        private void RefreshRunSetup()
        {
            _packageRows.Clear();
            _loadoutRows.Clear();
            _selectedRunLanguages.Clear();
            _cardLoadout.ClearAll();
            _runSetupList?.Clear();
            _runSetupDetail?.Clear();
            _runSetupNotice = null;

            if (_runSetupList == null || _runSetupDetail == null)
            {
                return;
            }

            if (_playerCollection.OwnedUnits.Count == 0)
            {
                _runSetupDetail.Add(new Label("no units installed") { name = "RunSetupEmptyTitle" });
                _runSetupDetail.Add(new Label("install a starter or summon a unit before starting a run") { name = "RunSetupEmptyHint" });
                return;
            }

            _selectedPackageIndex = Mathf.Clamp(_selectedPackageIndex, 0, _playerCollection.OwnedUnits.Count - 1);

            for (int i = 0; i < _playerCollection.OwnedUnits.Count; i++)
            {
                int index = i;
                DistroDefinition unit = _playerCollection.OwnedUnits[i];
                VisualElement row = new();
                row.AddToClassList("package-row");
                row.RegisterCallback<PointerEnterEvent>(_ => SelectPackage(index));
                row.RegisterCallback<ClickEvent>(_ => SelectPackage(index));

                VisualElement summary = new();
                summary.AddToClassList("package-summary");
                summary.Add(new Label(">") { name = $"RunSetupMarker{index}" });
                summary.ElementAt(0).AddToClassList("package-marker");

                Label label = new(DistroPresentation.DisplayName(unit));
                label.AddToClassList("package-name");
                label.style.color = new StyleColor(unit.AccentColor);
                summary.Add(label);

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

                _runSetupList.Add(row);
                _packageRows.Add(row);
            }

            RefreshPackageSelection();
        }

        private void SelectPackage(int index)
        {
            if (_packageRows.Count == 0)
            {
                return;
            }

            _selectedPackageIndex = (index + _packageRows.Count) % _packageRows.Count;
            _cardLoadout.ClearLoadout(_playerCollection.OwnedUnits[_selectedPackageIndex]);
            _selectedRunLanguages.Clear();
            _runSetupPackageScroll = null;
            _runSetupNotice = null;
            RefreshPackageSelection();
        }

        private void RefreshPackageSelection()
        {
            for (int i = 0; i < _packageRows.Count; i++)
            {
                _packageRows[i].EnableInClassList(SelectedClassName, i == _selectedPackageIndex);
            }

            RenderSelectedPackageDetail();
        }

        private void RenderSelectedPackageDetail()
        {
            Vector2 previousScrollOffset = _runSetupPackageScroll == null ? Vector2.zero : _runSetupPackageScroll.scrollOffset;
            _runSetupDetail?.Clear();
            _loadoutRows.Clear();
            if (_runSetupDetail == null || _selectedPackageIndex < 0 || _selectedPackageIndex >= _playerCollection.OwnedUnits.Count)
            {
                return;
            }

            DistroDefinition unit = _playerCollection.OwnedUnits[_selectedPackageIndex];
            _runSetupDetail.Add(BuildRunSetupReadout(unit));
            AddRunSetupPassive(unit, _runSetupDetail);

            ScrollView packageScroll = new(ScrollViewMode.Vertical);
            packageScroll.AddToClassList("package-scroll");

            VisualElement packageList = new();
            packageList.AddToClassList("package-list");
            packageList.Add(new Label("equipped cards") { name = "RunSetupCardsHeader" });
            packageList.ElementAt(0).AddToClassList("package-header");

            IReadOnlyList<string> equippedCardIds = _cardLoadout.GetEquippedCardIds(unit.Id);
            if (!string.IsNullOrWhiteSpace(_runSetupNotice))
            {
                packageList.Add(new Label(_runSetupNotice) { name = "RunSetupNotice" });
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
                if (card == null || card.IsToken || card.IsRunOnly)
                {
                    continue;
                }

                bool equipped = ContainsCardId(equippedCardIds, card.Id);
                VisualElement row = BuildCardLoadoutRow(unit, card, equipped);
                packageList.Add(row);
                _loadoutRows.Add(row);
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
                _selectedRunLanguages.Clear();
            }

            packageScroll.Add(packageList);
            _runSetupDetail.Add(packageScroll);
            _runSetupDetail.Add(BuildBootRow(unit, CanBootRun(equippedCardIds)));
            _runSetupPackageScroll = packageScroll;
            RestoreRunSetupPackageScroll(previousScrollOffset);
        }

        private void RestoreRunSetupPackageScroll(Vector2 offset)
        {
            if (_runSetupPackageScroll == null)
            {
                return;
            }

            _runSetupPackageScroll.scrollOffset = offset;
            _runSetupPackageScroll.schedule.Execute(() => _runSetupPackageScroll.scrollOffset = offset).StartingIn(0);
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

            Label label = new(DistroPresentation.DisplayName(unit));
            label.AddToClassList("collection-detail-name");
            label.style.color = new StyleColor(unit.AccentColor);
            copy.Add(label);

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

            if (_selectedRunLanguages.Count < 2)
            {
                packageList.Add(new Label($"select {2 - _selectedRunLanguages.Count} more language(s) before booting") { name = "RunSetupLanguagesNotice" });
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
                _selectedRunLanguages.Remove(language);
                _runSetupNotice = null;
                RenderSelectedPackageDetail();
                return;
            }

            if (_selectedRunLanguages.Count >= 2)
            {
                _runSetupNotice = "language limit reached";
                RenderSelectedPackageDetail();
                return;
            }

            _selectedRunLanguages.Add(language);
            _runSetupNotice = null;
            RenderSelectedPackageDetail();
        }

        private void ToggleCardInSelectedLoadout(DistroDefinition unit, CardDefinition card)
        {
            _runSetupNotice = TryToggleLoadoutCard(unit, card);
            RenderSelectedPackageDetail();
        }

        private string TryToggleLoadoutCard(DistroDefinition unit, CardDefinition card)
        {
            if (unit == null || card == null)
            {
                return "loadout change failed";
            }

            IReadOnlyList<string> equippedCardIds = _cardLoadout.GetEquippedCardIds(unit.Id);
            bool equipped = ContainsCardId(equippedCardIds, card.Id);
            CardLoadoutFailureReason reason;
            bool changed = equipped
                ? _cardLoadout.TryUnequip(unit.Id, card.Id, out reason)
                : _cardLoadout.TryEquip(unit.Id, card.Id, out reason);

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
                if (LanguageUnlock.IsUnlocked(languages[i].Language, _playerCollection))
                {
                    available.Add(languages[i]);
                }
            }

            return available;
        }

        private bool IsRunLanguageSelected(Language language)
        {
            for (int i = 0; i < _selectedRunLanguages.Count; i++)
            {
                if (_selectedRunLanguages[i] == language)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanBootRun(IReadOnlyList<string> equippedCardIds)
        {
            return equippedCardIds.Count >= CardLoadout.MaxEquippedCards && _selectedRunLanguages.Count == 2;
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
            if (_selectedPackageIndex < 0 || _selectedPackageIndex >= _playerCollection.OwnedUnits.Count)
            {
                return;
            }

            DistroDefinition unit = _playerCollection.OwnedUnits[_selectedPackageIndex];
            IReadOnlyList<string> equippedCardIds = _cardLoadout.GetEquippedCardIds(unit.Id);
            if (equippedCardIds.Count < CardLoadout.MaxEquippedCards)
            {
                _runSetupNotice = equippedCardIds.Count == 0
                    ? "boot: no packages staged"
                    : $"boot: stage {CardLoadout.MaxEquippedCards} packages";
                RenderSelectedPackageDetail();
                return;
            }

            if (_selectedRunLanguages.Count < 2)
            {
                _runSetupNotice = "boot: select 2 programming languages";
                RenderSelectedPackageDetail();
                return;
            }

            SaveLastRunLoadout(unit, equippedCardIds);
            RunContext.Set(unit, BuildEquippedCardDefinitions(unit, equippedCardIds), _selectedRunLanguages[0], _selectedRunLanguages[1], _playerCollection.GetVersion(unit.Id));
            SceneLoader.LoadGame();
        }

        private IReadOnlyList<CardDefinition> BuildEquippedCardDefinitions(DistroDefinition unit, IReadOnlyList<string> equippedCardIds)
        {
            List<CardDefinition> cards = new();
            for (int i = 0; i < equippedCardIds.Count; i++)
            {
                CardDefinition card = cardDatabase == null ? null : cardDatabase.FindById(equippedCardIds[i]);
                card ??= FindExclusiveCard(unit, equippedCardIds[i]);
                if (card != null)
                {
                    cards.Add(card);
                }
            }

            return cards;
        }

        private static CardDefinition FindExclusiveCard(DistroDefinition unit, string cardId)
        {
            if (unit == null || string.IsNullOrWhiteSpace(cardId))
            {
                return null;
            }

            for (int i = 0; i < unit.ExclusiveCards.Count; i++)
            {
                CardDefinition card = unit.ExclusiveCards[i];
                if (card != null && !card.IsRunOnly && string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase))
                {
                    return card;
                }
            }

            return null;
        }

        private void HandlePointerDown(PointerDownEvent evt)
        {
            _root.Focus();
            _suppressNextClick = SkipBootIntro();
        }

        private void SelectCommand(int index)
        {
            _selectedCommandIndex = (index + _commandEntries.Count) % _commandEntries.Count;
            for (int i = 0; i < _commandEntries.Count; i++)
            {
                _commandEntries[i].SetSelected(i == _selectedCommandIndex);
            }
        }

        private void ActivateCommand(int index)
        {
            _commandEntries[index].Activate();
        }

        private void HandleCommandClicked(int index)
        {
            if (_suppressNextClick)
            {
                _suppressNextClick = false;
                return;
            }

            ActivateCommand(index);
        }

        private void HandleStartRunClicked()
        {
            RefreshRunSetup();
            ShowPanel(_runSetupPanel);
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
            _cardLoadout.ClearAll();
            _selectedRunLanguages.Clear();
            _runSetupNotice = null;
            ShowPanel(_mainMenuPanel);
            _root.Focus();
        }

        private void ShowCollection()
        {
            ShowCollectionUnits();
            ShowPanel(_collectionPanel);
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
            _collectionShowingLanguages = false;
            _collectionUnitsButton.EnableInClassList(SelectedClassName, true);
            _collectionLanguagesButton.EnableInClassList(SelectedClassName, false);
            collectionScreen.RefreshUnits(_playerCollection.OwnedUnits);
            SyncCollectionFrame();
        }

        private void ShowCollectionLanguages()
        {
            _collectionShowingLanguages = true;
            _collectionUnitsButton.EnableInClassList(SelectedClassName, false);
            _collectionLanguagesButton.EnableInClassList(SelectedClassName, true);
            collectionScreen.ShowLanguages();
            SyncCollectionFrame();
        }

        private void ToggleCollectionTab()
        {
            if (_collectionShowingLanguages)
            {
                ShowCollectionUnits();
                return;
            }

            ShowCollectionLanguages();
        }

        private void SyncCollectionFrame()
        {
            _collectionFrame.SetTitle(collectionScreen.CurrentTitle);
            _collectionFrame.SetHint(collectionScreen.CurrentHint);
        }

        private void ShowGacha()
        {
            gachaScreen.Open();
            ShowPanel(_gachaPanel);
        }

        private void ShowSettings()
        {
            ShowPanel(_settingsPanel);
        }

        // TODO: Replace this direct toggle with a screen-stack/router when menu flows need history or transitions.
        private void ShowPanel(VisualElement activePanel)
        {
            _mainMenuPanel.EnableInClassList(HiddenClassName, _mainMenuPanel != activePanel);
            _collectionPanel.EnableInClassList(HiddenClassName, _collectionPanel != activePanel);
            _runSetupPanel.EnableInClassList(HiddenClassName, _runSetupPanel != activePanel);
            _gachaPanel.EnableInClassList(HiddenClassName, _gachaPanel != activePanel);
            _settingsPanel.EnableInClassList(HiddenClassName, _settingsPanel != activePanel);

            bool showingMainMenu = _mainMenuPanel == activePanel;
            _motdBlock.EnableInClassList(HiddenClassName, !showingMainMenu || string.IsNullOrWhiteSpace(motdBody));

            if (showingMainMenu)
            {
                RefreshEventBanner();
            }
            else
            {
                _eventBanner.AddToClassList(HiddenClassName);
            }

            RefreshCurrencyReadouts();
        }

        private sealed class CommandMenuEntry
        {
            private readonly Label _cursor;
            private readonly Action _action;

            public CommandMenuEntry(VisualElement row, Action action)
            {
                Row = row;
                this._action = action;
                _cursor = row.Q<Label>(className: "command-cursor");
            }

            public VisualElement Row { get; }

            public void SetSelected(bool selected)
            {
                Row.EnableInClassList(SelectedClassName, selected);
                _cursor.visible = selected;
            }

            public void Activate()
            {
                _action.Invoke();
            }
        }
    }
}
