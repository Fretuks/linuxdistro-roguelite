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
        [SerializeField] private FeaturedUnitPanel featuredUnitPanel = new();
        [SerializeField] private CollectionScreenController collectionScreen = new();
        [SerializeField] private StarterSelectionController starterSelection = new();

        private readonly List<CommandMenuEntry> commandEntries = new();
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
        private Label appIdLabel;
        private Label entropyLabel;
        private Label pullTokensLabel;
        private Label titleCursorLabel;
        private Label promptCursorLabel;
        private Label bootIntroLogLabel;
        private Label motdBodyLabel;
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
        private float bootIntroElapsed;
        private int bootIntroCharacterCount;
        private bool cursorVisible;
        private bool suppressNextClick;
        private bool warnedUnresolvedSaveId;
        private bool warnedInvalidLoadoutId;
        private string bootIntroCopy;
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
            collectionScreen.Bind(root, monospaceFont);
            starterSelection.Bind(root, distroDatabase, HandleStarterConfirmed);
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
            collectionScreen.Refresh(playerCollection.OwnedUnits);
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
            backgroundLog = new BackgroundLogRingBuffer(backgroundLogLayer, BootLogCopy.Lines);

            appIdLabel = root.Q<Label>("AppIdLabel");
            entropyLabel = root.Q<Label>("EntropyLabel");
            pullTokensLabel = root.Q<Label>("PullTokensLabel");
            titleCursorLabel = root.Q<Label>("TitleCursorLabel");
            promptCursorLabel = root.Q<Label>("PromptCursorLabel");
            bootIntroLogLabel = root.Q<Label>("BootIntroLogLabel");
            motdBodyLabel = root.Q<Label>("MotdBodyLabel");

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
            collectionScreen.Refresh(playerCollection.OwnedUnits);
        }

        private void HandleStarterConfirmed(DistroDefinition picked, IReadOnlyList<DistroDefinition> remaining)
        {
            playerCollection.Add(picked);
            for (int i = 0; i < remaining.Count; i++)
            {
                gachaService.AddToBannerPool(remaining[i]);
            }

            saveData.starterChosen = true;
            SaveCurrentState();
            featuredUnitPanel.Refresh(playerCollection.OwnedUnits);
            collectionScreen.Refresh(playerCollection.OwnedUnits);
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
            collectionScreen.Refresh(playerCollection.OwnedUnits);
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
