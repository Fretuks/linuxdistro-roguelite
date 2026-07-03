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
        [SerializeField] private FeaturedUnitPanel featuredUnitPanel = new();

        private readonly List<CommandMenuEntry> commandEntries = new();
        private readonly List<VisualElement> starterCards = new();
        private readonly List<Label> starterNames = new();
        private readonly List<Label> starterLanguages = new();
        private readonly List<Label> starterDescriptions = new();
        private readonly List<VisualElement> collectionRows = new();
        private UIDocument document;
        private VisualElement root;
        private VisualElement shellRoot;
        private VisualElement bootIntroPanel;
        private VisualElement mainMenuPanel;
        private VisualElement collectionPanel;
        private VisualElement gachaPanel;
        private VisualElement settingsPanel;
        private VisualElement eventBanner;
        private VisualElement backgroundLogLayer;
        private VisualElement starterModal;
        private VisualElement collectionList;
        private VisualElement collectionDetail;
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
        private Button gachaBackButton;
        private Button settingsBackButton;
        private SaveService saveService;
        private SaveData saveData;
        private EntropyWallet wallet;
        private GachaService gachaService;
        private PlayerCollection playerCollection;
        private BackgroundLogRingBuffer backgroundLog;
        private IEventBannerSource eventBannerSource;
        private int selectedCommandIndex;
        private int selectedStarterIndex;
        private int selectedCollectionIndex;
        private float bootIntroElapsed;
        private int bootIntroCharacterCount;
        private bool cursorVisible;
        private bool suppressNextClick;
        private bool starterModalActive;
        private bool starterConfirming;
        private bool warnedUnresolvedSaveId;
        private string bootIntroCopy;
        private IVisualElementScheduledItem blinkSchedule;
        private IVisualElementScheduledItem bootIntroSchedule;
        private IVisualElementScheduledItem starterCloseSchedule;

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
        }

        private void BindElements()
        {
            root = document.rootVisualElement;
            shellRoot = root.Q<VisualElement>("ShellRoot");
            bootIntroPanel = root.Q<VisualElement>("BootIntroPanel");
            mainMenuPanel = root.Q<VisualElement>("MainMenuPanel");
            collectionPanel = root.Q<VisualElement>("CollectionPanel");
            gachaPanel = root.Q<VisualElement>("GachaPanel");
            settingsPanel = root.Q<VisualElement>("SettingsPanel");
            eventBanner = root.Q<VisualElement>("EventBanner");
            motdBlock = root.Q<VisualElement>("MotdBlock");
            backgroundLogLayer = root.Q<VisualElement>("BackgroundLogLayer");
            backgroundLog = new BackgroundLogRingBuffer(backgroundLogLayer, BootLogCopy.Lines);
            starterModal = root.Q<VisualElement>("StarterModal");
            collectionList = root.Q<VisualElement>("CollectionList");
            collectionDetail = root.Q<VisualElement>("CollectionDetail");

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
            gachaBackButton.clicked += ShowMainMenu;
            settingsBackButton.clicked += ShowMainMenu;
        }

        private void UnregisterCallbacks()
        {
            root.UnregisterCallback<KeyDownEvent>(HandleKeyDown);
            root.UnregisterCallback<PointerDownEvent>(HandlePointerDown);
            collectionBackButton.clicked -= ShowMainMenu;
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
            featuredUnitPanel.Refresh(playerCollection.OwnedUnits);
            RefreshCollection();
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

            collectionRows.Clear();
            collectionList.Clear();

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

        private void RenderCollectionDetail(DistroDefinition unit)
        {
            collectionDetail.Clear();

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
            SceneLoader.LoadGame();
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
            RefreshCollection();
            ShowPanel(collectionPanel);
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
