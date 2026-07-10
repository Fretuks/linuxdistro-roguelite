using System;
using System.Collections.Generic;
using KernelPanic.Combat;
using KernelPanic.Core;
using KernelPanic.Data;
using KernelPanic.Meta;
using KernelPanic.Run;
using UnityEngine;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// Runtime-built GameScene combat UI. Intent icons use a compact terminal vocabulary:
    /// ! attack/status attack/segfault, # defend, + buff, * special, ? hidden/rootkit,
    /// ~ reviving/race link, : split, @ countdown, . orphan.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(RunManager))]
    [RequireComponent(typeof(CombatManager))]
    public sealed class CombatSceneController : MonoBehaviour
    {
        private const string StyleResourcePath = "CombatScene";
        private const string SharedScrollbarStyleResourcePath = "TerminalScrollbars";
        private const string SharedRarityStyleResourcePath = "RarityPresentation";
        private const int MaxFeedbackElements = 42;
        private const string FilledCycle = "●";
        private const string EmptyCycle = "○";

        [SerializeField] private DistroDatabase distroDatabase;
        [SerializeField] private CardDatabase cardDatabase;
        [SerializeField] private LanguageDeckDatabase languageDeckDatabase;

        private UIDocument _document;
        private RunManager _runManager;
        private CombatManager _combatManager;
        private SaveService _saveService;
        private VisualElement _root;
        private VisualElement _statusBar;
        private Label _waveLabel;
        private Label _phaseLabel;
        private Label _entropyLabel;
        private Label _bitsLabel;
        private Label _bandwidthLabel;
        private Label _rootCreditsLabel;
        private Label _seedLabel;
        private VisualElement _playerPanel;
        private VisualElement _interpreterStrip;
        private VisualElement _lazyStackPile;
        private VisualElement _tokenArea;
        private VisualElement _enemyRow;
        private VisualElement _handRow;
        private VisualElement _turnResourceGrid;
        private Label _logLabel;
        private Button _surrenderButton;
        private Label _surrenderConfirmLabel;
        private VisualElement _feedbackLayer;
        private VisualElement _damageVignette;
        private VisualElement _overlay;
        private Color _distroAccent;
        private readonly Dictionary<CombatantState, VisualElement> _combatantElements = new();
        private readonly Dictionary<CardInstance, VisualElement> _handCardElements = new();
        private readonly Dictionary<CardInstance, VisualElement> _queueChipElements = new();
        private readonly Dictionary<CardInstance, VisualElement> _stackChipElements = new();
        private readonly Dictionary<CombatantState, int> _previousUptime = new();
        private readonly Dictionary<CombatantState, int> _previousShield = new();
        private readonly Dictionary<VisualElement, int> _feedbackBeatVersions = new();
        private readonly Dictionary<CombatantState, Rect> _lastCombatantFeedbackRects = new();
        private readonly Dictionary<CombatantState, int> _heldDeathUptime = new();
        private readonly HashSet<CombatantState> _deathBeatStartedCombatants = new();
        private readonly HashSet<CombatantState> _visuallyRemovedCombatants = new();
        private readonly HashSet<CardInstance> _knownHandCards = new();
        private int _queueCascadeIndex;
        private int _feedbackCascadeIndex;
        private int _feedbackBeatVersion;
        private int _combatBeatCursorMs;
        private int _combatBeatCursorVersion;
        private int _rootShakeVersion;
        private int _hitStopVersion;
        private int _lastHitStopFrame = -1;
        private Label _archBtwCounterLabel;
        private VisualElement _fedoraRiskFillElement;
        private Label _fedoraRiskValueLabel;
        private bool _pendingFedoraPayoff;
        private int _rollingReleaseHitStopVersion;
        private bool _surrenderConfirming;
        private bool _runEndedBySurrender;
        private bool _runEndOverlayPresented;
        private bool _playerFeedbackAnchorTracked;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _runManager = GetComponent<RunManager>();
            _combatManager = GetComponent<CombatManager>();
            _saveService = new SaveService();
            _root = _document.rootVisualElement;
            LoadStyles();
            ApplyTerminalFont();
            BuildLayout();

            _root.AddToClassList("scene-fade-hidden");
            _root.schedule.Execute(() => _root.RemoveFromClassList("scene-fade-hidden")).StartingIn(0);
        }

        private void ApplyTerminalFont()
        {
            var font = TerminalFontResolver.Resolve(null);
            if (font != null)
            {
                _root.style.unityFontDefinition = new StyleFontDefinition(font);
            }
        }

        private void OnEnable()
        {
            _root?.EnableInClassList("reduced-motion", UIPreferences.ReducedMotion);
            _combatManager.StateChanged += Refresh;
            _combatManager.CombatLog += HandleCombatLog;
            _runManager.RepositoryChanged += Refresh;
            GameEvents.CardPlayed += HandleCardPlayed;
            GameEvents.CardResolved += HandleCardResolved;
            GameEvents.PhaseChanged += HandlePhaseChanged;
            GameEvents.DamageDealt += HandleDamageDealt;
            GameEvents.OverflowDamageTravel += HandleOverflowDamageTravel;
            GameEvents.CombatantDefeated += HandleCombatantDefeated;
            GameEvents.DeathSpawnedEnemy += HandleDeathSpawnedEnemy;
            GameEvents.EnemyWouldAct += HandleEnemyWouldAct;
            GameEvents.PlayerDamaged += HandlePlayerDamaged;
            GameEvents.StatusApplied += HandleStatusApplied;
            GameEvents.StatusExpired += HandleStatusEnded;
            GameEvents.StatusCleansed += HandleStatusEnded;
            GameEvents.UbuntuAptUpdatePeeked += HandleUbuntuAptUpdatePeeked;
            GameEvents.FedoraBleedingEdgeTriggered += HandleFedoraBleedingEdgeTriggered;
            GameEvents.ArchBtwTurnEnded += HandleArchBtwTurnEnded;
            _root?.RegisterCallback<KeyDownEvent>(HandleKeyDown);
        }

        private void Start()
        {
            if (!TryBuildRunConfig(out RunConfig config))
            {
                HandleMissingRunConfig();
                return;
            }

            _distroAccent = config.Distro == null ? Color.white : config.Distro.AccentColor;
            _combatManager.SetGeneratedCardPool(BuildGeneratedCardPool());
            _runManager.StartRun(config);
            Refresh();
        }

        private IReadOnlyList<CardDefinition> BuildGeneratedCardPool()
        {
            List<CardDefinition> cards = new();
            if (cardDatabase != null)
            {
                cards.AddRange(cardDatabase.AllCards);
                return cards;
            }

            if (languageDeckDatabase == null)
            {
                return cards;
            }

            AddLanguageDeck(Language.Python, cards);
            return cards;
        }

        private void OnDisable()
        {
            Time.timeScale = 1f;
            SettleRunRewardsIfNeeded();
            _combatManager.StateChanged -= Refresh;
            _combatManager.CombatLog -= HandleCombatLog;
            _runManager.RepositoryChanged -= Refresh;
            GameEvents.CardPlayed -= HandleCardPlayed;
            GameEvents.CardResolved -= HandleCardResolved;
            GameEvents.PhaseChanged -= HandlePhaseChanged;
            GameEvents.DamageDealt -= HandleDamageDealt;
            GameEvents.OverflowDamageTravel -= HandleOverflowDamageTravel;
            GameEvents.CombatantDefeated -= HandleCombatantDefeated;
            GameEvents.DeathSpawnedEnemy -= HandleDeathSpawnedEnemy;
            GameEvents.EnemyWouldAct -= HandleEnemyWouldAct;
            GameEvents.PlayerDamaged -= HandlePlayerDamaged;
            GameEvents.StatusApplied -= HandleStatusApplied;
            GameEvents.StatusExpired -= HandleStatusEnded;
            GameEvents.StatusCleansed -= HandleStatusEnded;
            GameEvents.UbuntuAptUpdatePeeked -= HandleUbuntuAptUpdatePeeked;
            GameEvents.FedoraBleedingEdgeTriggered -= HandleFedoraBleedingEdgeTriggered;
            GameEvents.ArchBtwTurnEnded -= HandleArchBtwTurnEnded;
            _root?.UnregisterCallback<KeyDownEvent>(HandleKeyDown);
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            if (!_surrenderConfirming)
            {
                return;
            }

            if (evt.keyCode == KeyCode.Y || evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                ConfirmSurrender();
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.N || evt.keyCode == KeyCode.Escape)
            {
                CancelSurrender();
                evt.StopPropagation();
            }
        }

        private bool TryBuildRunConfig(out RunConfig config)
        {
            if (RunContext.TryCreateRunConfig(languageDeckDatabase, out config))
            {
                return true;
            }

            if (Application.isEditor && TryBuildEditorFallback(out config))
            {
                Debug.Log("GameScene started without RunContext; using Ubuntu Python/JavaScript dev fallback.");
                return true;
            }

            config = null;
            return false;
        }

        private bool TryBuildEditorFallback(out RunConfig config)
        {
            config = null;
            DistroDefinition distro = distroDatabase == null ? null : distroDatabase.FindById("ubuntu");
            if (distro == null)
            {
                return false;
            }

            List<CardDefinition> startingDeck = new();
            for (int i = 0; i < distro.ExclusiveCards.Count && startingDeck.Count < CardLoadout.MaxEquippedCards; i++)
            {
                if (distro.ExclusiveCards[i] != null && !distro.ExclusiveCards[i].IsToken && !distro.ExclusiveCards[i].IsRunOnly)
                {
                    startingDeck.Add(distro.ExclusiveCards[i]);
                }
            }

            AddLanguageDeck(Language.Python, startingDeck);
            AddLanguageDeck(Language.JavaScript, startingDeck);
            config = new RunConfig(distro, Language.Python, Language.JavaScript, startingDeck, Environment.TickCount, 1);
            return true;
        }

        private void AddLanguageDeck(Language language, List<CardDefinition> target)
        {
            LanguageDeckDefinition deck = languageDeckDatabase == null ? null : languageDeckDatabase.FindByLanguage(language);
            if (deck == null)
            {
                return;
            }

            for (int entryIndex = 0; entryIndex < deck.Entries.Count; entryIndex++)
            {
                LanguageDeckDefinition.LanguageDeckEntry entry = deck.Entries[entryIndex];
                for (int copy = 0; copy < entry.Count; copy++)
                {
                    if (entry.Card != null && !entry.Card.IsRunOnly)
                    {
                        target.Add(entry.Card);
                    }
                }
            }
        }

        private void HandleMissingRunConfig()
        {
            _root.Clear();
            _root.AddToClassList("combat-root");
            Label error = new("combat bootstrap failed: no RunContext and no editor fallback data");
            error.AddToClassList("overlay-line");
            _root.Add(error);
            Debug.LogError("GameScene could not start: no RunContext and no Ubuntu editor fallback data.");
        }

        private void LoadStyles()
        {
            StyleSheet rarityStyleSheet = Resources.Load<StyleSheet>(SharedRarityStyleResourcePath);
            if (rarityStyleSheet != null)
            {
                _root.styleSheets.Add(rarityStyleSheet);
            }

            StyleSheet styleSheet = Resources.Load<StyleSheet>(StyleResourcePath);
            if (styleSheet != null)
            {
                _root.styleSheets.Add(styleSheet);
            }

            StyleSheet scrollbarStyleSheet = Resources.Load<StyleSheet>(SharedScrollbarStyleResourcePath);
            if (scrollbarStyleSheet != null)
            {
                _root.styleSheets.Add(scrollbarStyleSheet);
            }
        }

        private void BuildLayout()
        {
            _root.Clear();
            _lastCombatantFeedbackRects.Clear();
            _heldDeathUptime.Clear();
            _deathBeatStartedCombatants.Clear();
            _visuallyRemovedCombatants.Clear();
            _playerFeedbackAnchorTracked = false;
            _root.AddToClassList("combat-root");
            _root.style.flexGrow = 1;

            _statusBar = BuildStatusBar();
            _root.Add(_statusBar);

            VisualElement main = new();
            main.AddToClassList("main-layout");
            _root.Add(main);

            _playerPanel = CreatePanel("player", "htop process monitor", 242);
            main.Add(_playerPanel);

            VisualElement fieldPanel = CreatePanel("field", "resolution tracks", 0);
            fieldPanel.AddToClassList("field-panel");
            main.Add(fieldPanel);
            _interpreterStrip = AddTrackZone(fieldPanel, "interpreter queue", "FIFO queued scripts", "track-zone-queue");
            _lazyStackPile = AddTrackZone(fieldPanel, "lazy stack", "LIFO delayed work", "track-zone-stack");
            _tokenArea = AddTrackZone(fieldPanel, "goroutines", "token lane", "track-zone-token");

            VisualElement enemiesPanel = CreatePanel("enemy processes", "intent telemetry", 438);
            main.Add(enemiesPanel);
            ScrollView enemyScroll = new(ScrollViewMode.Vertical);
            enemyScroll.AddToClassList("enemy-scroll");
            enemiesPanel.Add(enemyScroll);
            _enemyRow = new();
            _enemyRow.AddToClassList("enemy-row");
            enemyScroll.Add(_enemyRow);

            VisualElement bottom = new();
            bottom.AddToClassList("bottom-layout");
            _root.Add(bottom);

            VisualElement handPanel = CreatePanel("hand", "executable cards", 0);
            handPanel.AddToClassList("hand-panel");
            bottom.Add(handPanel);
            _handRow = new();
            _handRow.AddToClassList("hand-row");
            handPanel.Add(_handRow);

            VisualElement commandPanel = CreatePanel("turn", "command", 240);
            commandPanel.AddToClassList("turn-panel");
            bottom.Add(commandPanel);
            _turnResourceGrid = new();
            _turnResourceGrid.AddToClassList("turn-resource-grid");
            commandPanel.Add(_turnResourceGrid);

            Button endTurn = new(() => _combatManager.EndPlayerTurn()) { text = "> end-turn" };
            endTurn.AddToClassList("primary-action");
            commandPanel.Add(endTurn);

            _surrenderButton = new(HandleSurrenderClicked) { text = "> shutdown -r run" };
            _surrenderButton.AddToClassList("secondary-action");
            _surrenderButton.AddToClassList("surrender-action");
            commandPanel.Add(_surrenderButton);

            _surrenderConfirmLabel = new("end this run now? progress since the last wave clear is lost. [Y/n]");
            _surrenderConfirmLabel.AddToClassList("surrender-confirm");
            _surrenderConfirmLabel.AddToClassList("hidden");
            commandPanel.Add(_surrenderConfirmLabel);

            _logLabel = new();
            _logLabel.AddToClassList("log-line");
            commandPanel.Add(_logLabel);

            _feedbackLayer = new();
            _feedbackLayer.AddToClassList("feedback-layer");
            _feedbackLayer.pickingMode = PickingMode.Ignore;
            _root.Add(_feedbackLayer);

            _damageVignette = new();
            _damageVignette.AddToClassList("damage-vignette");
            _damageVignette.pickingMode = PickingMode.Ignore;
            _root.Add(_damageVignette);

            _overlay = new();
            _overlay.AddToClassList("overlay");
            _overlay.style.display = DisplayStyle.None;
            _root.Add(_overlay);
        }

        private VisualElement BuildStatusBar()
        {
            VisualElement bar = new();
            bar.AddToClassList("combat-status-bar");

            _waveLabel = AddStatusReadout(bar, "wave", "--", false);
            _phaseLabel = AddStatusReadout(bar, "phase", "--", true);
            _bitsLabel = AddStatusReadout(bar, "Bits", "0", false);
            _entropyLabel = AddStatusReadout(bar, "Entropy", "0", false);
            _bandwidthLabel = AddStatusReadout(bar, "Bandwidth", "0", false);
            _rootCreditsLabel = AddStatusReadout(bar, "Root Credits", "0", false);

            VisualElement spacer = new();
            spacer.AddToClassList("status-spacer");
            bar.Add(spacer);

            _seedLabel = new("--");
            _seedLabel.AddToClassList("seed-value");
            bar.Add(_seedLabel);
            return bar;
        }

        private static Label AddStatusReadout(VisualElement parent, string labelText, string valueText, bool phase)
        {
            VisualElement cluster = new();
            cluster.AddToClassList("status-cluster");
            Label label = new(labelText);
            label.AddToClassList("status-label");
            Label value = new(valueText);
            value.AddToClassList(phase ? "phase-pill" : "status-value");
            cluster.Add(label);
            cluster.Add(value);
            parent.Add(cluster);
            return value;
        }

        private VisualElement AddTrackZone(VisualElement parent, string title, string subtitle, string className)
        {
            VisualElement zone = new();
            zone.AddToClassList("track-zone");
            zone.AddToClassList(className);

            VisualElement header = new();
            header.AddToClassList("track-header");
            Label titleLabel = new(title);
            titleLabel.AddToClassList("track-title");
            Label subtitleLabel = new(subtitle);
            subtitleLabel.AddToClassList("track-subtitle");
            header.Add(titleLabel);
            header.Add(subtitleLabel);
            zone.Add(header);

            VisualElement contents = new();
            contents.AddToClassList("track-contents");
            zone.Add(contents);
            parent.Add(zone);
            return contents;
        }

        private void Refresh()
        {
            if (_combatManager.PlayerState == null)
            {
                return;
            }

            RefreshStatus();
            RefreshPlayer();
            RefreshTracks();
            RefreshEnemies();
            RefreshHand();
            RefreshTurnPanel();
            RefreshOverlay();
        }

        private void RefreshStatus()
        {
            SaveData data = _saveService.Load();
            RunConfig config = _runManager.CurrentConfig;
            _waveLabel.text = _runManager.CurrentWaveNumber.ToString();
            _phaseLabel.text = PhaseText(_combatManager.CurrentPhase);
            _bitsLabel.text = _runManager.Bits.ToString();
            _entropyLabel.text = FormatWalletWithAccrual(data.entropyBalance, _runManager.AccruedEntropy);
            _bandwidthLabel.text = FormatWalletWithAccrual(data.bandwidthBalance, _runManager.AccruedBandwidth);
            _rootCreditsLabel.text = data.rootCredits.ToString();
            _seedLabel.text = $"seed {config?.RunSeed ?? 0}";
        }

        private void RefreshPlayer()
        {
            _playerPanel.Clear();
            _combatantElements.Remove(_combatManager.PlayerState);
            _combatantElements[_combatManager.PlayerState] = _playerPanel;
            _playerPanel.Add(PanelHeader(DisplayName(_runManager.CurrentConfig.Distro), "htop"));
            ApplyAccent(_playerPanel);

            CombatantState state = _combatManager.PlayerState;
            TrackCombatantFeedbackAnchor(state, _playerPanel);
            DetectBeneficialResourceFeedback(state, _playerPanel);
            ApplyStatusStateClasses(_playerPanel, state);
            _playerPanel.Add(MeterBlock("uptime", state.CurrentUptime, state.MaxUptime, MeterTone.Uptime));
            _playerPanel.Add(MeterBlock("shield", state.Shield, Mathf.Max(1, state.Shield), state.Shield > 0 ? MeterTone.Beneficial : MeterTone.Muted));
            _playerPanel.Add(CycleBlock(state.Cycles, state.MaxCycles));

            Label ram = new($"ram hand cap {_combatManager.HandController.UsedRam}/{_combatManager.HandController.RamCapacity}");
            ram.AddToClassList("ram-note");
            _playerPanel.Add(ram);
            if (_combatManager.ActiveKernelRamPenalty > 0)
            {
                Label ramPressure = new($"! kernel panic: RAM -{_combatManager.ActiveKernelRamPenalty}");
                ramPressure.AddToClassList("status-label");
                ramPressure.AddToClassList("intent-attack");
                _playerPanel.Add(ramPressure);
            }

            _playerPanel.Add(StatusBlock(state, true));

            if (IsDistro("fedora"))
            {
                _playerPanel.Add(FedoraRiskMeter(_combatManager.FedoraCrashChance));
            }

            if (IsDistro("arch"))
            {
                _playerPanel.Add(ArchBtwCounter(state.ArchBtwStacks));
            }
        }

        private bool IsDistro(string id)
        {
            return string.Equals(_runManager?.CurrentConfig?.Distro?.Id, id, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshTracks()
        {
            _queueChipElements.Clear();
            _stackChipElements.Clear();
            FillCardStrip(_interpreterStrip, _combatManager.InterpreterQueue.Cards, "queue empty");
            FillCardStrip(_lazyStackPile, _combatManager.LazyStack.Cards, "stack empty");
            _tokenArea.Clear();
            _tokenArea.Add(EmptyState("no goroutines or tokens"));
        }

        private void RefreshEnemies()
        {
            _enemyRow.Clear();
            _combatantElements.Clear();
            PruneVisuallyRemovedCombatants();
            int enemyCount = _combatManager.Enemies.Count;
            bool compactEnemies = enemyCount >= 5;
            _enemyRow.EnableInClassList("enemy-row-compact", compactEnemies);
            if (_combatManager.PlayerState != null)
            {
                _combatantElements[_combatManager.PlayerState] = _playerPanel;
            }

            for (int i = 0; i < _combatManager.Enemies.Count; i++)
            {
                int index = i;
                EnemyInstance enemy = _combatManager.Enemies[i];
                if (_visuallyRemovedCombatants.Contains(enemy.State))
                {
                    continue;
                }

                Button card = new(() => _combatManager.SelectEnemy(index));
                card.text = string.Empty;
                card.AddToClassList("enemy-card");
                card.EnableInClassList("enemy-card-compact", compactEnemies);
                card.EnableInClassList("enemy-card-elite", enemy.HasEliteSignal);
                bool deathPending = enemy.DefeatedPendingRemoval || (enemy.State.IsDefeated && !enemy.PendingRevive);
                bool deathBeatStarted = _deathBeatStartedCombatants.Contains(enemy.State);
                bool holdDeathDisplay = deathPending && !deathBeatStarted;
                int displayedUptime = holdDeathDisplay
                    ? HeldDeathUptime(enemy.State, enemy.MaxUptime)
                    : enemy.CurrentUptime;
                card.EnableInClassList("enemy-card-defeated-hold", deathPending && deathBeatStarted);
                card.SetEnabled(!enemy.State.IsDefeated || holdDeathDisplay);
                bool highlighted = _combatManager.PendingTargetCard != null || _combatManager.SelectedEnemyIndex == index;
                if (highlighted)
                {
                    card.AddToClassList("enemy-card-targeted");
                }

                Label name = new(enemy.Name);
                name.AddToClassList("enemy-name");
                card.Add(name);

                if (enemy.HasPendingMarker)
                {
                    Label marker = new(enemy.PendingRevive ? "~ reviving" : "~ revived once");
                    marker.AddToClassList("status-label");
                    card.Add(marker);
                }

                if (enemy.HasSpecialSignal)
                {
                    card.Add(EnemySignalPanel(enemy, _combatManager.Enemies));
                }

                _combatantElements[enemy.State] = card;
                if (!deathPending)
                {
                    DetectBeneficialResourceFeedback(enemy.State, card);
                }

                ApplyStatusStateClasses(card, enemy.State);
                card.Add(MeterBlock("uptime", displayedUptime, enemy.MaxUptime, MeterTone.Uptime));
                if (enemy.State.Shield > 0)
                {
                    Label shield = new($"# shield {enemy.State.Shield}");
                    shield.AddToClassList("enemy-shield");
                    card.Add(shield);
                }

                card.Add(StatusPipRow(enemy.State));
                if (!deathPending && (!enemy.SpawnedFromDeath || enemy.DeathSpawnIntentRevealed))
                {
                    card.Add(IntentPanel(enemy.DisplayIntent));
                }
                _enemyRow.Add(card);
                TrackCombatantFeedbackAnchor(enemy.State, card);
            }
        }

        private int HeldDeathUptime(CombatantState state, int maxUptime)
        {
            if (state != null && _heldDeathUptime.TryGetValue(state, out int uptime))
            {
                return Mathf.Clamp(uptime, 0, Mathf.Max(1, maxUptime));
            }

            return Mathf.Clamp(maxUptime, 1, Mathf.Max(1, maxUptime));
        }

        private void PruneVisuallyRemovedCombatants()
        {
            bool IsRemoved(CombatantState state)
            {
                IReadOnlyList<EnemyInstance> enemies = _combatManager.Enemies;
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i].State == state)
                    {
                        return false;
                    }
                }

                return true;
            }

            _visuallyRemovedCombatants.RemoveWhere(IsRemoved);
            _deathBeatStartedCombatants.RemoveWhere(IsRemoved);
            List<CombatantState> staleHeldStates = null;
            foreach (CombatantState state in _heldDeathUptime.Keys)
            {
                if (!IsRemoved(state))
                {
                    continue;
                }

                staleHeldStates ??= new List<CombatantState>();
                staleHeldStates.Add(state);
            }

            if (staleHeldStates == null)
            {
                return;
            }

            for (int i = 0; i < staleHeldStates.Count; i++)
            {
                _heldDeathUptime.Remove(staleHeldStates[i]);
            }
        }

        private void RefreshHand()
        {
            _handRow.Clear();
            _handCardElements.Clear();
            HashSet<CardInstance> currentHandCards = new();
            IReadOnlyList<CardInstance> hand = _combatManager.HandController.Cards;
            for (int i = 0; i < hand.Count; i++)
            {
                CardInstance card = hand[i];
                currentHandCards.Add(card);
                VisualElement face = CardFace(card);
                _handCardElements[card] = face;
                if (!_knownHandCards.Contains(card))
                {
                    PlayElementBeat(face, "hand-card-drawn", 260);
                }

                _handRow.Add(face);
            }

            _knownHandCards.Clear();
            foreach (CardInstance card in currentHandCards)
            {
                _knownHandCards.Add(card);
            }
        }

        private void RefreshTurnPanel()
        {
            _turnResourceGrid.Clear();
            CombatantState state = _combatManager.PlayerState;
            IReadOnlyList<CardInstance> hand = _combatManager.HandController.Cards;
            _turnResourceGrid.Add(TurnStat("cycles", $"{state.Cycles}/{state.MaxCycles}"));
            _turnResourceGrid.Add(TurnStat("hand", $"{_combatManager.HandController.UsedRam}/{_combatManager.HandController.RamCapacity}"));
            _turnResourceGrid.Add(TurnStat("draw", _combatManager.DeckController.DrawPile.Count.ToString()));
            _turnResourceGrid.Add(TurnStat("discard", _combatManager.DeckController.DiscardPile.Count.ToString()));
            _turnResourceGrid.Add(TurnStat("exhaust", _combatManager.DeckController.ExhaustPile.Count.ToString()));
            RefreshSurrenderCommand();
        }

        private void RefreshSurrenderCommand()
        {
            bool canSurrender = _combatManager.CanSurrenderRun;
            if (!canSurrender)
            {
                _surrenderConfirming = false;
            }

            if (_surrenderButton != null)
            {
                _surrenderButton.text = _surrenderConfirming ? "> confirm shutdown" : "> shutdown -r run";
                _surrenderButton.SetEnabled(canSurrender);
                _surrenderButton.EnableInClassList("surrender-action-confirming", _surrenderConfirming);
            }

            if (_surrenderConfirmLabel != null)
            {
                _surrenderConfirmLabel.EnableInClassList("hidden", !_surrenderConfirming);
            }
        }

        private void HandleSurrenderClicked()
        {
            if (!_combatManager.CanSurrenderRun)
            {
                _surrenderConfirming = false;
                RefreshSurrenderCommand();
                return;
            }

            if (!_surrenderConfirming)
            {
                _surrenderConfirming = true;
                RefreshSurrenderCommand();
                _root?.Focus();
                return;
            }

            ConfirmSurrender();
        }

        private void ConfirmSurrender()
        {
            if (!_combatManager.CanSurrenderRun)
            {
                _surrenderConfirming = false;
                RefreshSurrenderCommand();
                return;
            }

            _surrenderConfirming = false;
            _runEndedBySurrender = true;
            _combatManager.SurrenderRun();
        }

        private void CancelSurrender()
        {
            if (!_surrenderConfirming)
            {
                return;
            }

            _surrenderConfirming = false;
            RefreshSurrenderCommand();
        }

        private void RefreshOverlay()
        {
            if (_overlay == null)
            {
                return;
            }

            _overlay.Clear();
            if (_combatManager.RunLost)
            {
                SettleRunRewardsIfNeeded();
                _overlay.style.display = DisplayStyle.Flex;
                _overlay.Add(BuildRunEndOverlay());
                return;
            }

            _runEndOverlayPresented = false;
            _runEndedBySurrender = false;
            if (_combatManager.AwaitingWaveContinue)
            {
                if (!_runManager.RepositoryVisitActive)
                {
                    _runManager.GenerateRepositoryOffers(cardDatabase, languageDeckDatabase);
                    return;
                }

                _overlay.style.display = DisplayStyle.Flex;
                _overlay.Add(BuildRepositoryView());
                return;
            }

            _overlay.style.display = DisplayStyle.None;
        }

        private VisualElement BuildRunEndOverlay()
        {
            VisualElement screen = new();
            screen.AddToClassList("death-overlay");

            if (!_runEndOverlayPresented)
            {
                _runEndOverlayPresented = true;
                if (!UIPreferences.ReducedMotion)
                {
                    VisualElement beat = new();
                    beat.AddToClassList("death-crash-beat");
                    screen.Add(beat);
                    screen.schedule.Execute(() => beat.AddToClassList("death-crash-beat-settled")).StartingIn(25);
                    screen.schedule.Execute(() => beat.RemoveFromHierarchy()).StartingIn(260);
                }
            }

            VisualElement content = new();
            content.AddToClassList("death-content");

            content.Add(OverlayTitle("KERNEL PANIC"));

            string cause = _runEndedBySurrender ? "fatal: shutdown signal accepted" : "fatal: player uptime reached 0";
            Label causeLabel = OverlayLine(cause);
            causeLabel.AddToClassList("death-cause");
            content.Add(causeLabel);

            VisualElement summary = new();
            summary.AddToClassList("death-section");
            summary.AddToClassList("death-summary");
            summary.Add(DeathStat("wave reached", _runManager.CurrentWaveNumber.ToString()));
            summary.Add(DeathStat("waves cleared", _runManager.WavesCleared.ToString()));
            summary.Add(DeathStat("seed", (_runManager.CurrentConfig?.RunSeed ?? 0).ToString()));
            content.Add(summary);

            VisualElement rewards = new();
            rewards.AddToClassList("death-section");
            rewards.AddToClassList("death-rewards");
            rewards.Add(RewardReadout("#", _runManager.AccruedBandwidth, "bandwidth", false));
            rewards.Add(RewardReadout("*", _runManager.AccruedEntropy, "entropy", _runManager.AccruedEntropy <= 0));
            content.Add(rewards);

            Button returnButton = new(() => SceneLoader.LoadMainMenu(_root)) { text = "> return to menu" };
            returnButton.AddToClassList("primary-action");
            returnButton.AddToClassList("death-return-action");
            content.Add(returnButton);

            screen.Add(content);
            return screen;
        }

        private void SettleRunRewardsIfNeeded()
        {
            if (_runManager == null || _saveService == null)
            {
                return;
            }

            if (!_runManager.TrySettleRunRewards(out int bandwidth, out int entropy))
            {
                return;
            }

            SaveData data = _saveService.Load();
            data.bandwidthBalance = Math.Max(0, data.bandwidthBalance) + bandwidth;
            data.entropyBalance = Math.Max(0, data.entropyBalance) + entropy;
            data.RecordRunStats(_runManager.CurrentConfig?.Distro?.Id, _runManager.CurrentWaveNumber);
            _saveService.Save(data);
        }

        private VisualElement BuildRepositoryView()
        {
            VisualElement repo = new();
            repo.AddToClassList("repository");
            repo.AddToClassList("screen-frame");
            repo.Add(BuildRepositoryHeader());

            ScrollView body = new(ScrollViewMode.Vertical);
            body.AddToClassList("repository-body");
            body.AddToClassList("screen-frame-content");

            VisualElement offers = new();
            offers.AddToClassList("repository-offers");
            if (_runManager.RepositoryOffers.Count == 0)
            {
                offers.Add(EmptyState("repository clean: no offers left this visit"));
            }
            else
            {
                for (int i = 0; i < _runManager.RepositoryOffers.Count; i++)
                {
                    offers.Add(RepositoryOfferTile(_runManager.RepositoryOffers[i]));
                }
            }

            body.Add(offers);
            body.Add(RemoveCardBlock());
            repo.Add(body);

            VisualElement footer = new();
            footer.AddToClassList("repository-footer");
            footer.AddToClassList("screen-frame-footer");
            Button reroll = new(() => _runManager.RerollRepositoryOffers(cardDatabase, languageDeckDatabase))
            {
                text = $"apt update   {_runManager.RerollCost} bits"
            };
            reroll.AddToClassList("primary-action");
            reroll.AddToClassList("repository-footer-action");
            reroll.AddToClassList("repository-reroll-action");
            reroll.EnableInClassList("repo-unaffordable", _runManager.Bits < _runManager.RerollCost);
            reroll.SetEnabled(_runManager.Bits >= _runManager.RerollCost);
            footer.Add(reroll);

            Button leave = new(() => _combatManager.ContinueToNextWave()) { text = "> boot next-wave" };
            leave.AddToClassList("primary-action");
            leave.AddToClassList("repository-leave-action");
            VisualElement leaveBlock = new();
            leaveBlock.AddToClassList("repository-leave-block");
            leaveBlock.Add(leave);
            Label banked = new($"{_runManager.Bits} bits remaining");
            banked.AddToClassList("repository-banked-hint");
            banked.AddToClassList("screen-frame-hint");
            leaveBlock.Add(banked);
            footer.Add(leaveBlock);
            repo.Add(footer);
            return repo;
        }

        private VisualElement BuildRepositoryHeader()
        {
            VisualElement header = new();
            header.AddToClassList("repository-header");
            header.AddToClassList("screen-frame-header");

            VisualElement copy = new();
            copy.AddToClassList("repository-header-copy");
            Label title = new("repository");
            title.AddToClassList("repository-title");
            title.AddToClassList("screen-frame-title");
            Label subtitle = new($"wave {_runManager.CurrentWaveNumber - 1} cleared  /  {_runManager.RepositoryOffers.Count} packages indexed");
            subtitle.AddToClassList("repository-subtitle");
            copy.Add(title);
            copy.Add(subtitle);

            VisualElement wallet = new();
            wallet.AddToClassList("repository-wallet");
            Label walletLabel = new("available bits");
            walletLabel.AddToClassList("status-label");
            Label walletValue = new($"◆ {_runManager.Bits}");
            walletValue.AddToClassList("repository-wallet-value");
            wallet.Add(walletLabel);
            wallet.Add(walletValue);

            header.Add(copy);
            header.Add(wallet);
            return header;
        }

        private VisualElement RepositoryOfferTile(RepositoryOffer offer)
        {
            VisualElement tile = new();
            tile.AddToClassList("repository-tile");
            tile.AddToClassList(OfferTileClass(offer.Kind));
            bool unavailable = offer.Sold || _runManager.Bits < offer.Price;
            tile.EnableInClassList("repo-unaffordable", unavailable);
            tile.EnableInClassList("repo-sold", offer.Sold);

            VisualElement top = new();
            top.AddToClassList("repository-tile-top");
            Label kind = new($"{OfferKindIcon(offer.Kind)} {OfferKindText(offer.Kind)}");
            kind.AddToClassList("repository-kind");
            top.Add(kind);
            top.Add(PriceBadge(offer.Price, unavailable));
            tile.Add(top);

            VisualElement content = offer.Kind switch
            {
                RepositoryOfferKind.NewCard => CardOfferContent(offer),
                RepositoryOfferKind.CardUpgrade => UpgradeOfferContent(offer),
                RepositoryOfferKind.StatUpgrade => StatOfferContent(offer),
                _ => GenericOfferContent(offer)
            };
            tile.Add(content);

            Button buy = new(() => _runManager.BuyOffer(offer, _combatManager.PlayerState))
            {
                text = offer.Sold ? "installed" : OfferActionText(offer.Kind)
            };
            buy.tooltip = OfferCommandText(offer);
            buy.AddToClassList("repository-action");
            buy.SetEnabled(!offer.Sold && _runManager.Bits >= offer.Price);
            tile.Add(buy);
            return tile;
        }

        private VisualElement CardOfferContent(RepositoryOffer offer)
        {
            VisualElement content = new();
            content.AddToClassList("repository-card-content");
            CardInstance preview = new(offer.CardDefinition);
            VisualElement card = CardFaceView(preview);
            card.AddToClassList("repository-card-preview");
            content.Add(card);

            if (offer.CardDefinition != null && offer.CardDefinition.IsRunOnly)
            {
                content.Add(Tag("run-only", "repository-run-only-tag"));
            }

            return content;
        }

        private VisualElement UpgradeOfferContent(RepositoryOffer offer)
        {
            VisualElement content = new();
            content.AddToClassList("repository-upgrade-content");
            if (offer.TargetCard != null)
            {
                VisualElement target = CardFaceView(offer.TargetCard);
                target.AddToClassList("repository-upgrade-card");
                content.Add(target);
            }

            VisualElement delta = new();
            delta.AddToClassList("repository-delta");
            Label arrow = new("▲");
            arrow.AddToClassList("repository-delta-icon");
            Label text = new(offer.Description);
            text.AddToClassList("repository-delta-text");
            delta.Add(arrow);
            delta.Add(text);
            content.Add(delta);
            return content;
        }

        private VisualElement StatOfferContent(RepositoryOffer offer)
        {
            VisualElement content = new();
            content.AddToClassList("repository-stat-content");
            Label icon = new(StatOfferIcon(offer.StatUpgradeKind));
            icon.AddToClassList("repository-stat-icon");
            Label name = new(offer.DisplayName);
            name.AddToClassList("repository-name");
            Label description = new(offer.Description);
            description.AddToClassList("repository-description");
            content.Add(icon);
            content.Add(name);
            content.Add(description);
            return content;
        }

        private static VisualElement GenericOfferContent(RepositoryOffer offer)
        {
            VisualElement content = new();
            Label name = new(offer.DisplayName);
            name.AddToClassList("repository-name");
            Label description = new(offer.Description);
            description.AddToClassList("repository-description");
            content.Add(name);
            content.Add(description);
            return content;
        }

        private static VisualElement PriceBadge(int price, bool warning)
        {
            Label badge = new($"◆ {price}");
            badge.AddToClassList("repository-price");
            badge.EnableInClassList("repository-price-warning", warning);
            return badge;
        }

        private VisualElement RemoveCardBlock()
        {
            VisualElement block = new();
            block.AddToClassList("repository-remove");
            Label title = new($"installed deck maintenance  /  remove for {CombatTuning.RemoveCardCost} bit");
            title.AddToClassList("repository-name");
            block.Add(title);

            VisualElement rows = new();
            rows.AddToClassList("repository-remove-list");
            for (int i = 0; i < _runManager.RunDeck.Count; i++)
            {
                CardInstance card = _runManager.RunDeck[i];
                Button remove = new(() => _runManager.RemoveCard(card))
                {
                    text = $"apt remove {DisplayName(card.Definition)}"
                };
                remove.AddToClassList("repo-remove-button");
                remove.EnableInClassList("repo-unaffordable", _runManager.Bits < CombatTuning.RemoveCardCost || _runManager.RunDeck.Count <= 1);
                remove.SetEnabled(_runManager.Bits >= CombatTuning.RemoveCardCost && _runManager.RunDeck.Count > 1);
                rows.Add(remove);
            }

            block.Add(rows);
            return block;
        }

        private VisualElement CardFace(CardInstance card)
        {
            Button button = new(() =>
            {
                if (card.IsLocked)
                {
                    UnlockCardWithFeedback(card);
                    return;
                }

                PlayCardWithFeedback(card);
            });
            button.text = string.Empty;
            button.AddToClassList("hand-card");
            if (_combatManager.PendingTargetCard == card)
            {
                button.AddToClassList("hand-card-selected");
            }

            if (GetDisplayCardCost(card) > _combatManager.PlayerState.Cycles)
            {
                button.AddToClassList("hand-card-unaffordable");
            }

            if (card.IsBroken || card.IsLocked)
            {
                button.AddToClassList("hand-card-unaffordable");
                button.SetEnabled(!card.IsBroken);
            }

            PopulateCardFace(button, card);
            return button;
        }

        private VisualElement CardFaceView(CardInstance card)
        {
            VisualElement face = new();
            face.AddToClassList("hand-card");
            PopulateCardFace(face, card);
            return face;
        }

        private void PopulateCardFace(VisualElement target, CardInstance card)
        {
            string cardRarityClass = CardRarityClass(card.Definition.Rarity);
            if (!string.IsNullOrEmpty(cardRarityClass))
            {
                target.AddToClassList(cardRarityClass);
            }

            VisualElement top = new();
            top.AddToClassList("card-top-row");
            Label cost = new(GetDisplayCardCost(card).ToString());
            cost.AddToClassList("card-cost");
            Label name = new($"{DisplayName(card.Definition)}{(card.IsUpgraded ? " +" : string.Empty)}");
            name.AddToClassList("card-name");
            top.Add(cost);
            top.Add(name);
            target.Add(top);

            if (card.IsBroken || card.IsLocked)
            {
                Label state = new(card.IsBroken ? "segv corrupt" : $"locked: pay {EnemyArchetypeCatalog.DrmUnlockCycleCost}c");
                state.AddToClassList("status-label");
                target.Add(state);
            }

            VisualElement tags = new();
            tags.AddToClassList("tag-row");
            tags.Add(Tag(card.Definition.Language.ToString(), LanguageClass(card.Definition.Language)));
            tags.Add(Tag(TrackText(card.Definition.ResolutionTrack), "track-tag"));
            tags.Add(Tag(card.Definition.Rarity.ToString(), RarityTagClass(card.Definition.Rarity)));
            target.Add(tags);

            Label effect = new(CardEffectFactory.GetRulesText(card));
            effect.AddToClassList("card-effect");
            target.Add(effect);
        }

        private static VisualElement IntentPanel(EnemyIntent intent)
        {
            VisualElement panel = new();
            panel.AddToClassList("intent-panel");
            string className = IntentClass(intent.Kind);
            panel.AddToClassList(className);

            Label label = new("intent");
            label.AddToClassList("intent-label");
            Label value = new($"{IntentIcon(intent)} {intent.ValueText}");
            value.AddToClassList("intent-value");
            value.AddToClassList(className);
            Label detail = new(IntentText(intent));
            detail.AddToClassList("status-label");
            panel.Add(label);
            panel.Add(value);
            panel.Add(detail);
            return panel;
        }

        private static VisualElement EnemySignalPanel(EnemyInstance enemy, IReadOnlyList<EnemyInstance> enemies)
        {
            VisualElement row = new();
            row.AddToClassList("status-row");

            if (enemy.SpawnedFromDeath)
            {
                row.Add(Tag(". orphaned", "track-tag"));
            }

            if (enemy.HasEliteSignal)
            {
                row.Add(Tag("! elite", "intent-attack"));
            }

            if (enemy.HasRamPressureSignal)
            {
                row.Add(Tag($"# RAM -{EnemyArchetypeCatalog.KernelPanicRamPenalty}", "intent-attack"));
            }

            if (enemy.HasTelemetrySignal)
            {
                row.Add(Tag($"+ profile {enemy.TelemetryStacks}", "intent-special"));
            }

            if (enemy.HasCardLockerSignal)
            {
                row.Add(Tag($"$ lock {EnemyArchetypeCatalog.DrmUnlockCycleCost}c", "intent-special"));
            }

            if (enemy.HasSplitSignal)
            {
                string text = CountLivingArchetype(enemies, enemy.ArchetypeId) >= EnemyArchetypeCatalog.ForkBombTotalCap
                    ? ": split capped"
                    : ": split next";
                row.Add(Tag(text, "track-tag"));
            }

            if (enemy.HasSegfaultSignal)
            {
                row.Add(Tag($"! corrupt in {enemy.CountdownRemaining}", "track-tag"));
            }

            if (enemy.HasRaceSignal)
            {
                string text = HasLivingPair(enemies, enemy) ? "~ linked random" : "~ unlinked";
                row.Add(Tag(text, "track-tag"));
            }

            if (enemy.HasRootkitSignal)
            {
                string text = HasOtherLivingEnemy(enemies, enemy) ? "? masked reduced" : "? exposed";
                row.Add(Tag(text, "track-tag"));
            }

            return row;
        }

        private static int CountLivingArchetype(IReadOnlyList<EnemyInstance> enemies, string archetypeId)
        {
            int count = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyInstance enemy = enemies[i];
                if (enemy.ArchetypeId == archetypeId && !enemy.State.IsDefeated)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasLivingPair(IReadOnlyList<EnemyInstance> enemies, EnemyInstance target)
        {
            if (target == null || target.PairId < 0)
            {
                return false;
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyInstance enemy = enemies[i];
                if (enemy != target && enemy.PairId == target.PairId && !enemy.State.IsDefeated)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasOtherLivingEnemy(IReadOnlyList<EnemyInstance> enemies, EnemyInstance target)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyInstance enemy = enemies[i];
                if (enemy != target && !enemy.State.IsDefeated)
                {
                    return true;
                }
            }

            return false;
        }

        private static VisualElement MeterBlock(string name, int current, int max, MeterTone tone)
        {
            float ratio = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);
            VisualElement block = new();
            block.AddToClassList("meter-block");

            VisualElement head = new();
            head.AddToClassList("meter-head");
            Label label = new(name);
            label.AddToClassList("metric-label");
            Label value = new(max <= 0 ? current.ToString() : $"{current}/{max}");
            value.AddToClassList(tone == MeterTone.Muted ? "metric-value-muted" : "metric-value");
            head.Add(label);
            head.Add(value);
            block.Add(head);

            VisualElement track = new();
            track.AddToClassList("meter-track");
            VisualElement fill = new();
            fill.AddToClassList("meter-fill");
            fill.AddToClassList(MeterFillClass(tone, ratio));
            fill.style.width = Length.Percent(ratio * 100f);
            track.Add(fill);
            block.Add(track);
            return block;
        }

        private static VisualElement CycleBlock(int current, int max)
        {
            VisualElement block = new();
            block.AddToClassList("cycle-row");
            VisualElement head = new();
            head.AddToClassList("meter-head");
            Label label = new("cycles");
            label.AddToClassList("metric-label");
            Label value = new($"{current}/{max}");
            value.AddToClassList("metric-value");
            head.Add(label);
            head.Add(value);
            block.Add(head);

            Label pips = new(BuildPips(current, max));
            pips.AddToClassList("cycle-pips");
            block.Add(pips);
            return block;
        }

        private VisualElement FedoraRiskMeter(float chancePercent)
        {
            float clamped = Mathf.Clamp(chancePercent, 0f, 100f);
            VisualElement block = new();
            block.AddToClassList("meter-block");
            block.AddToClassList("fedora-risk-meter");

            VisualElement head = new();
            head.AddToClassList("meter-head");
            Label label = new("crash risk");
            label.AddToClassList("metric-label");
            Label value = new($"{clamped:0.#}%");
            value.AddToClassList("metric-value");
            head.Add(label);
            head.Add(value);
            block.Add(head);

            VisualElement track = new();
            track.AddToClassList("meter-track");
            VisualElement fill = new();
            fill.AddToClassList("meter-fill");
            fill.AddToClassList(FedoraRiskFillClass(clamped));
            fill.style.width = Length.Percent(clamped);
            track.Add(fill);
            block.Add(track);

            _fedoraRiskFillElement = fill;
            _fedoraRiskValueLabel = value;
            return block;
        }

        private static string FedoraRiskFillClass(float chancePercent)
        {
            if (chancePercent >= 50f)
            {
                return "meter-fill-danger";
            }

            return chancePercent >= 25f ? "meter-fill-warning" : "meter-fill";
        }

        private VisualElement ArchBtwCounter(int stacks)
        {
            VisualElement block = new();
            block.AddToClassList("arch-btw-counter");
            Label label = new($"btw ×{stacks}");
            label.AddToClassList("arch-btw-value");
            label.style.color = _distroAccent;
            block.Add(label);
            _archBtwCounterLabel = label;
            return block;
        }

        private static VisualElement StatusBlock(CombatantState state, bool includeTooltips)
        {
            VisualElement block = new();
            Label title = new("statuses");
            title.AddToClassList("status-section-title");
            block.Add(title);
            if (!includeTooltips || state.Statuses.Count == 0)
            {
                block.Add(StatusPipRow(state));
                return block;
            }

            VisualElement details = new();
            details.AddToClassList("status-detail-list");
            for (int i = 0; i < state.Statuses.Count; i++)
            {
                details.Add(StatusDetail(state.Statuses[i]));
            }

            block.Add(details);
            return block;
        }

        private static VisualElement StatusDetail(StatusInstance status)
        {
            StatusDescriptor descriptor = StatusEffectController.GetDescriptor(status.Type);
            VisualElement item = new();
            item.AddToClassList("status-detail");
            item.Add(StatusPip(status));

            Label tooltip = new(descriptor.Tooltip);
            tooltip.AddToClassList("status-tooltip");
            item.Add(tooltip);
            return item;
        }

        private static VisualElement StatusPipRow(CombatantState state)
        {
            VisualElement row = new();
            row.AddToClassList("status-row");
            if (state.Statuses.Count == 0)
            {
                row.Add(EmptyStatusPip());
                return row;
            }

            for (int i = 0; i < state.Statuses.Count; i++)
            {
                row.Add(StatusPip(state.Statuses[i]));
            }

            return row;
        }

        private static VisualElement StatusPip(StatusInstance status)
        {
            StatusDescriptor descriptor = StatusEffectController.GetDescriptor(status.Type);
            Label pip = new($"{descriptor.IconKey} x{status.Stacks} {DurationText(status.Duration)}");
            pip.AddToClassList("status-pip");
            pip.AddToClassList(descriptor.IsBeneficial ? "status-pip-beneficial" : "status-pip-harmful");
            pip.tooltip = $"{descriptor.DisplayName}: {descriptor.Tooltip}";
            return pip;
        }

        private static VisualElement EmptyStatusPip()
        {
            Label pip = new("none");
            pip.AddToClassList("status-pip");
            pip.AddToClassList("status-pip-empty");
            return pip;
        }

        private void FillCardStrip(VisualElement target, IReadOnlyList<CardInstance> cards, string emptyText)
        {
            target.Clear();
            if (cards.Count == 0)
            {
                target.Add(EmptyState(emptyText));
                return;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                VisualElement chip = CardChip(cards[i]);
                if (target == _interpreterStrip)
                {
                    _queueChipElements[cards[i]] = chip;
                }
                else if (target == _lazyStackPile)
                {
                    _stackChipElements[cards[i]] = chip;
                }

                target.Add(chip);
            }
        }

        private bool PlayCardWithFeedback(CardInstance card)
        {
            int cyclesBefore = _combatManager.PlayerState == null ? 0 : _combatManager.PlayerState.Cycles;
            bool played = _combatManager.PlayCard(card);
            if (played)
            {
                int spent = Mathf.Max(0, cyclesBefore - (_combatManager.PlayerState == null ? cyclesBefore : _combatManager.PlayerState.Cycles));
                if (spent > 0)
                {
                    PlayElementBeat(_turnResourceGrid, "cycles-spent-beat", 220);
                }

                return true;
            }

            if (_combatManager.PendingTargetCard != card && _handCardElements.TryGetValue(card, out VisualElement face))
            {
                PlayElementBeat(face, "feedback-denied", 220);
            }

            return false;
        }

        private bool UnlockCardWithFeedback(CardInstance card)
        {
            int cyclesBefore = _combatManager.PlayerState == null ? 0 : _combatManager.PlayerState.Cycles;
            bool unlocked = _combatManager.TryUnlockCard(card);
            if (unlocked)
            {
                int spent = Mathf.Max(0, cyclesBefore - (_combatManager.PlayerState == null ? cyclesBefore : _combatManager.PlayerState.Cycles));
                if (spent > 0)
                {
                    PlayElementBeat(_turnResourceGrid, "cycles-spent-beat", 220);
                }

                return true;
            }

            if (_handCardElements.TryGetValue(card, out VisualElement face))
            {
                PlayElementBeat(face, "feedback-denied", 220);
            }

            return false;
        }

        private void HandleCardPlayed(CardPlayedEvent payload)
        {
            if (payload.Card == null)
            {
                return;
            }

            VisualElement source = _handCardElements.TryGetValue(payload.Card, out VisualElement cardElement) ? cardElement : null;
            VisualElement destination = payload.Track switch
            {
                ResolutionTrack.InterpreterQueue => _interpreterStrip,
                ResolutionTrack.LazyStack => _lazyStackPile,
                _ => FirstTargetElement(payload.Card)
            };

            FlyCardGhost(payload.Card, source, destination, payload.Track == ResolutionTrack.Native ? "feedback-card-strike" : "feedback-card-route");

            if (IsDistro("arch") && _archBtwCounterLabel != null && _combatManager.PlayerState != null)
            {
                _archBtwCounterLabel.text = $"btw ×{_combatManager.PlayerState.ArchBtwStacks}";
                PlayElementBeat(_archBtwCounterLabel, "arch-btw-pop", 200);
            }
        }

        private void HandleCardResolved(CardResolvedEvent payload)
        {
            if (payload.Card == null)
            {
                return;
            }

            if (payload.Track == ResolutionTrack.InterpreterQueue)
            {
                int delay = UIPreferences.ReducedMotion ? 0 : Mathf.Min(_queueCascadeIndex * 120, 600);
                _queueCascadeIndex++;
                ScheduleFeedback(() =>
                {
                    VisualElement chip = _queueChipElements.TryGetValue(payload.Card, out VisualElement queuedChip) ? queuedChip : _interpreterStrip;
                    PlayElementBeat(chip, "queue-chip-resolve", 260);
                    FlyCardGhost(payload.Card, chip, _turnResourceGrid, "feedback-card-discard");
                }, delay);
                return;
            }

            if (payload.Track == ResolutionTrack.LazyStack)
            {
                VisualElement chip = _stackChipElements.TryGetValue(payload.Card, out VisualElement stackChip) ? stackChip : _lazyStackPile;
                PlayElementBeat(chip, "stack-chip-resolve", 320);
                FlyCardGhost(payload.Card, chip, _turnResourceGrid, "feedback-card-discard");
            }
        }

        private void HandlePhaseChanged(PhaseChangedEvent payload)
        {
            if (payload.NextPhase == TurnPhase.Interpret)
            {
                _queueCascadeIndex = 0;
            }

            _feedbackCascadeIndex = 0;
            _combatBeatCursorMs = 0;
            _combatBeatCursorVersion++;
            ScheduleFeedback(() => PlayElementBeat(_phaseLabel, "phase-pulse", 260), 0);
        }

        private void HandleDamageDealt(DamageDealtEvent payload)
        {
            bool isPlayer = payload.Target == _combatManager.PlayerState;
            int magnitudeAmount = payload.UptimeDamage > 0 ? payload.UptimeDamage : payload.ShieldDamage > 0 ? payload.ShieldDamage : payload.Amount;
            bool defeated = payload.Target != null && payload.Target.CurrentUptime <= 0;
            HitMagnitudeTier tier = HitMagnitude.Classify(magnitudeAmount, payload.Target?.MaxUptime ?? 1, defeated);
            bool shieldOnly = payload.WasFullyBlocked && payload.ShieldDamage > 0;

            bool fedoraPayoff = _pendingFedoraPayoff && payload.Source == _combatManager.PlayerState && payload.UptimeDamage > 0;
            _pendingFedoraPayoff = false;
            if (fedoraPayoff && tier < HitMagnitudeTier.Major)
            {
                tier = HitMagnitudeTier.Major;
            }

            if (payload.ArchRollingReleaseSaveTriggered)
            {
                TriggerRollingReleaseSave();
            }

            ScheduleCombatBeat(beatIndex =>
            {
                string bypass = payload.TrueDamage ? "! " : string.Empty;
                string text = shieldOnly
                    ? $"{bypass}block {payload.ShieldDamage}"
                    : payload.WasFullyBlocked
                        ? $"{bypass}blocked"
                        : $"{bypass}-{Mathf.RoundToInt(payload.UptimeDamage)}";
                string tone = BuildDamageFloatClasses(tier, payload, shieldOnly);
                string beatClass = fedoraPayoff ? "feedback-fedora-payoff" : BuildDamageBeatClass(tier, payload, shieldOnly);
                if (fedoraPayoff)
                {
                    tone += " float-fedora-payoff";
                }

                int beatDuration = fedoraPayoff ? Mathf.Max(360, HitBeatDuration(tier, payload.WasMitigated)) : HitBeatDuration(tier, payload.WasMitigated);

                TriggerHitStop(tier);
                TriggerRootShake(tier, isPlayer);
                SpawnFloatingText(payload.Target, text, tone, beatIndex);
                SpawnImpactMarker(payload.Target, payload.WasCritical, payload.WasMitigated, beatIndex, tier, shieldOnly);
                if (TryGetCombatantElement(payload.Target, out VisualElement target))
                {
                    PlayElementBeat(target, beatClass, beatDuration);
                }
                if (payload.ArchBtwBonusAmount > 0)
                {
                    SpawnFloatingText(payload.Target, $"+{payload.ArchBtwBonusAmount} btw", "float-btw-bonus", beatIndex + 1);
                }

                if (isPlayer && payload.UptimeDamage > 0)
                {
                    FlashDamageVignette(tier);
                }
            }, HitBeatDuration(tier, payload.WasMitigated));
        }

        private void HandleOverflowDamageTravel(OverflowDamageTravelEvent payload)
        {
            ScheduleCombatBeat(() => SpawnOverflowTravel(payload.From, payload.To, payload.Amount), CombatTuning.OverflowTravelMs);
        }

        private void TriggerRollingReleaseSave()
        {
            SpawnFloatingText(_playerPanel, "rolling release: recovered", "float-heal float-large");
            PlayElementBeat(_damageVignette, "damage-vignette-rolling-release", 420);
            if (UIPreferences.ReducedMotion)
            {
                return;
            }

            int version = ++_rollingReleaseHitStopVersion;
            Time.timeScale = 0.04f;
            ScheduleFeedback(() =>
            {
                if (_rollingReleaseHitStopVersion == version)
                {
                    Time.timeScale = 1f;
                }
            }, 220);
        }

        private void HandleCombatantDefeated(CombatantDefeatedEvent payload)
        {
            HoldDeathUptime(payload.Combatant);
            int completionMs = ScheduleCombatBeat(() =>
            {
                if (payload.Combatant != null && payload.Combatant != _combatManager.PlayerState)
                {
                    _deathBeatStartedCombatants.Add(payload.Combatant);
                    RefreshEnemies();
                }

                SpawnFloatingText(payload.Combatant, "killed", "float-kill float-large", 0);
                if (TryGetCombatantFeedbackRect(payload.Combatant, out Rect targetBounds))
                {
                    SpawnDeathGhost(targetBounds);
                }

                if (TryGetCombatantElement(payload.Combatant, out VisualElement target))
                {
                    if (target != _playerPanel)
                    {
                        target.AddToClassList("enemy-card-defeated-hold");
                        target.SetEnabled(false);
                    }

                    PlayElementBeat(target, "feedback-killed", 420);
                }
            }, CombatTuning.DeathBeatMs + CombatTuning.PostDeathPauseMs);
            ScheduleFeedback(() => CompleteCombatantDeathVisual(payload.Combatant), completionMs);
        }

        private void HoldDeathUptime(CombatantState combatant)
        {
            if (combatant == null || combatant == _combatManager.PlayerState || _heldDeathUptime.ContainsKey(combatant))
            {
                return;
            }

            int held = _previousUptime.TryGetValue(combatant, out int previous)
                ? previous
                : combatant.MaxUptime;
            _heldDeathUptime[combatant] = Mathf.Clamp(held, 1, Mathf.Max(1, combatant.MaxUptime));
        }

        private void CompleteCombatantDeathVisual(CombatantState combatant)
        {
            if (ShouldHideCombatantAfterDeathVisual(combatant))
            {
                _visuallyRemovedCombatants.Add(combatant);
                _heldDeathUptime.Remove(combatant);
                _deathBeatStartedCombatants.Remove(combatant);
                RefreshEnemies();
            }

            GameEvents.RaiseCombatantDeathVisualCompleted(new CombatantDeathVisualCompletedEvent(combatant));
        }

        private bool ShouldHideCombatantAfterDeathVisual(CombatantState combatant)
        {
            if (combatant == null || combatant == _combatManager.PlayerState)
            {
                return false;
            }

            IReadOnlyList<EnemyInstance> enemies = _combatManager.Enemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].State == combatant)
                {
                    return !enemies[i].PendingRevive;
                }
            }

            return false;
        }

        private void HandleDeathSpawnedEnemy(DeathSpawnedEnemyEvent payload)
        {
            ScheduleFeedback(() =>
            {
                SpawnFloatingText(payload.Enemy?.State, "spawned", "float-status");
                if (TryGetCombatantElement(payload.Enemy?.State, out VisualElement target))
                {
                    PlayElementBeat(target, "feedback-spawned", CombatTuning.SpawnMaterializeMs);
                }
            }, 0);
        }

        private void HandleEnemyWouldAct(EnemyWouldActEvent payload)
        {
            ScheduleCombatBeat(() =>
            {
                if (TryGetCombatantElement(payload.Enemy?.State, out VisualElement enemy))
                {
                    PlayElementBeat(enemy, "enemy-anticipating", 340);
                }
            });
        }

        private void HandlePlayerDamaged(PlayerDamagedEvent payload)
        {
            // DamageDealt carries richer mitigation data and owns the visual hit beat.
        }

        private void HandleStatusApplied(StatusAppliedEvent payload)
        {
            ScheduleCombatBeat(() =>
            {
                StatusDescriptor descriptor = StatusEffectController.GetDescriptor(payload.StatusType);
                if (TryGetCombatantElement(payload.Target, out VisualElement target))
                {
                    ApplyStatusStateClass(target, payload.StatusType, true);
                    PlayElementBeat(target, descriptor.IsBeneficial ? "feedback-boost" : "feedback-status", 220);
                }

                SpawnFloatingText(payload.Target, $"{descriptor.IconKey} x{payload.Stacks}", descriptor.IsBeneficial ? "float-heal" : "float-status");
            });
        }

        private void HandleStatusEnded(StatusExpiredEvent payload)
        {
            ApplyStatusStateClass(CombatantElement(payload.Target), payload.StatusType, false);
        }

        private void HandleStatusEnded(StatusCleansedEvent payload)
        {
            ApplyStatusStateClass(CombatantElement(payload.Target), payload.StatusType, false);
        }

        private void HandleUbuntuAptUpdatePeeked(UbuntuAptUpdatePeekedEvent payload)
        {
            if (payload.Peeked == null || payload.Peeked.Count == 0 || payload.Chosen == null)
            {
                return;
            }

            int minCost = int.MaxValue;
            for (int i = 0; i < payload.Peeked.Count; i++)
            {
                minCost = Mathf.Min(minCost, payload.Peeked[i].Cost);
            }

            if (UIPreferences.ReducedMotion)
            {
                string summary = string.Empty;
                for (int i = 0; i < payload.Peeked.Count; i++)
                {
                    PeekedCardInfo info = payload.Peeked[i];
                    summary += (i > 0 ? ", " : string.Empty) + $"{DisplayName(info.Card.Definition)} {info.Cost}c";
                }

                string prefix = payload.WasTie ? "apt update tie" : "apt update peeked";
                SpawnStaticInfoLabel(_turnResourceGrid, $"{prefix}: {summary} -> added {DisplayName(payload.Chosen.Definition)}", 900);
                return;
            }

            if (!TryGetElementWorldRect(_turnResourceGrid, out Rect turnResourceRect)
                || !TryGetFeedbackLocalRect(turnResourceRect, out Rect anchorRect))
            {
                return;
            }

            List<VisualElement> stageElements = new(payload.Peeked.Count);
            for (int i = 0; i < payload.Peeked.Count; i++)
            {
                PruneFeedbackLayer();
                VisualElement stageCard = CardFaceView(payload.Peeked[i].Card);
                stageCard.AddToClassList("peek-card");
                stageCard.style.position = Position.Absolute;
                stageCard.style.left = anchorRect.x + (i * 96f);
                stageCard.style.top = anchorRect.y - 128f;
                stageCard.style.width = 88f;
                stageCard.style.height = 108f;
                stageCard.style.opacity = 0f;
                _feedbackLayer.Add(stageCard);
                stageElements.Add(stageCard);
            }

            ScheduleFeedback(() =>
            {
                for (int i = 0; i < stageElements.Count; i++)
                {
                    stageElements[i].style.opacity = 1f;
                }
            }, 20);

            ScheduleFeedback(() =>
            {
                for (int i = 0; i < payload.Peeked.Count; i++)
                {
                    if (payload.Peeked[i].Cost != minCost)
                    {
                        continue;
                    }

                    stageElements[i].AddToClassList("peek-card-marked");
                    stageElements[i].EnableInClassList("peek-card-tie", payload.WasTie);
                }
            }, 180);

            ScheduleFeedback(() =>
            {
                for (int i = 0; i < payload.Peeked.Count; i++)
                {
                    VisualElement stage = stageElements[i];
                    if (payload.Peeked[i].Card == payload.Chosen)
                    {
                        FlyCardGhost(payload.Chosen, stage, _handRow, "feedback-card-route");
                        stage.style.opacity = 0f;
                        ScheduleFeedback(() => stage.RemoveFromHierarchy(), 40);
                    }
                    else
                    {
                        stage.style.opacity = 0f;
                        ScheduleFeedback(() => stage.RemoveFromHierarchy(), 220);
                    }
                }
            }, 340);
        }

        private void HandleFedoraBleedingEdgeTriggered(FedoraBleedingEdgeEvent payload)
        {
            if (payload.Card == null)
            {
                return;
            }

            VisualElement anchor = _handCardElements.TryGetValue(payload.Card, out VisualElement cardElement)
                ? cardElement
                : CombatantElement(_combatManager.PlayerState);

            if (payload.Crashed)
            {
                SnapFedoraRiskMeter(payload.CrashChanceAfter);
            }

            if (UIPreferences.ReducedMotion)
            {
                string outcome = payload.Crashed ? "crashed - no effect" : $"hit +{payload.DamageMultiplierPercent - 100}%";
                SpawnStaticInfoLabel(anchor, $"bleeding edge: -1c {outcome}", 700);
                if (!payload.Crashed)
                {
                    _pendingFedoraPayoff = true;
                }

                return;
            }

            VisualElement windup = CardFaceView(payload.Card);
            windup.AddToClassList("feedback-fedora-windup");
            windup.style.position = Position.Absolute;
            if (!TryGetElementWorldRect(anchor, out Rect anchorWorldRect) || !TryGetFeedbackLocalRect(anchorWorldRect, out Rect anchorRect))
            {
                return;
            }

            windup.style.left = anchorRect.x;
            windup.style.top = anchorRect.y;
            windup.style.width = Mathf.Max(110f, anchorRect.width);
            windup.style.height = Mathf.Max(94f, anchorRect.height);
            PruneFeedbackLayer();
            _feedbackLayer.Add(windup);

            VisualElement badgeRow = new();
            badgeRow.AddToClassList("tag-row");
            badgeRow.Add(Tag("-1c", "fedora-badge"));
            badgeRow.Add(Tag($"+{payload.DamageMultiplierPercent - 100}%", "fedora-badge"));
            windup.Add(badgeRow);

            ScheduleFeedback(() =>
            {
                if (payload.Crashed)
                {
                    windup.AddToClassList("feedback-fedora-crash");
                    Label stamp = new("CRASHED");
                    stamp.AddToClassList("fedora-crash-stamp");
                    windup.Add(stamp);
                    SpawnFloatingText(anchor, "no effect", "float-crash");
                    ScheduleFeedback(() => windup.style.opacity = 0.15f, 60);
                    ScheduleFeedback(() => windup.style.opacity = 0.85f, 120);
                    ScheduleFeedback(() => windup.style.opacity = 0f, 220);
                    ScheduleFeedback(() => windup.RemoveFromHierarchy(), 320);
                    return;
                }

                _pendingFedoraPayoff = true;
                windup.style.opacity = 0f;
                ScheduleFeedback(() => windup.RemoveFromHierarchy(), 220);
            }, 200);
        }

        private void SnapFedoraRiskMeter(float chanceAfter)
        {
            float clamped = Mathf.Clamp(chanceAfter, 0f, 100f);
            if (_fedoraRiskValueLabel != null)
            {
                _fedoraRiskValueLabel.text = $"{clamped:0.#}%";
            }

            if (_fedoraRiskFillElement != null)
            {
                _fedoraRiskFillElement.style.width = Length.Percent(clamped);
                _fedoraRiskFillElement.RemoveFromClassList("meter-fill-warning");
                _fedoraRiskFillElement.RemoveFromClassList("meter-fill-danger");
                _fedoraRiskFillElement.AddToClassList(FedoraRiskFillClass(clamped));
                PlayElementBeat(_fedoraRiskFillElement, "fedora-risk-snap", 260);
            }
        }

        private void HandleArchBtwTurnEnded(ArchBtwTurnEndedEvent payload)
        {
            if (_archBtwCounterLabel == null)
            {
                return;
            }

            if (payload.Persisted)
            {
                PlayElementBeat(_archBtwCounterLabel, "arch-btw-persist", 320);
                return;
            }

            _archBtwCounterLabel.text = "btw ×0";
            PlayElementBeat(_archBtwCounterLabel, "arch-btw-reset", 260);
        }

        private void SpawnStaticInfoLabel(VisualElement anchor, string text, int holdMs)
        {
            if (_feedbackLayer == null || !TryGetElementWorldRect(anchor, out Rect anchorRect) || !TryGetFeedbackLocalRect(anchorRect, out Rect localRect))
            {
                return;
            }

            PruneFeedbackLayer();
            Label label = new(text);
            label.AddToClassList("float-number");
            label.AddToClassList("float-status");
            label.AddToClassList("float-static");
            label.style.left = localRect.center.x - 80f;
            label.style.top = localRect.y - 26f;
            _feedbackLayer.Add(label);
            ScheduleFeedback(() => label.RemoveFromHierarchy(), holdMs);
        }

        private void DetectBeneficialResourceFeedback(CombatantState state, VisualElement element)
        {
            if (state == null || element == null)
            {
                return;
            }

            if (_previousUptime.TryGetValue(state, out int oldUptime) && state.CurrentUptime > oldUptime)
            {
                SpawnFloatingText(element, $"+{state.CurrentUptime - oldUptime}", "float-heal");
                PlayElementBeat(element, "feedback-boost", 220);
            }

            if (_previousShield.TryGetValue(state, out int oldShield) && state.Shield > oldShield)
            {
                SpawnFloatingText(element, $"+{state.Shield - oldShield} shield", "float-heal");
                PlayElementBeat(element, "feedback-block", 220);
            }

            _previousUptime[state] = state.CurrentUptime;
            _previousShield[state] = state.Shield;
        }

        private static string BuildDamageFloatClasses(HitMagnitudeTier tier, DamageDealtEvent payload, bool shieldOnly)
        {
            string classes = shieldOnly || payload.WasFullyBlocked ? "float-shield" : "float-damage";
            classes += " " + TierFloatClass(tier);
            if (payload.TrueDamage)
            {
                classes += " float-true";
            }

            return classes;
        }

        private static string BuildDamageBeatClass(HitMagnitudeTier tier, DamageDealtEvent payload, bool shieldOnly)
        {
            if (shieldOnly || payload.WasFullyBlocked)
            {
                return tier >= HitMagnitudeTier.Major ? "feedback-clang-major" : "feedback-clang";
            }

            if (payload.WasCritical || tier == HitMagnitudeTier.Massive)
            {
                return "feedback-crit";
            }

            return tier switch
            {
                HitMagnitudeTier.Minor => "feedback-hit-minor",
                HitMagnitudeTier.Moderate => "feedback-hit",
                HitMagnitudeTier.Major => "feedback-hit-major",
                _ => "feedback-crit"
            };
        }

        private static string TierFloatClass(HitMagnitudeTier tier)
        {
            return tier switch
            {
                HitMagnitudeTier.Minor => "float-tier-minor",
                HitMagnitudeTier.Moderate => "float-tier-moderate",
                HitMagnitudeTier.Major => "float-tier-major",
                HitMagnitudeTier.Massive => "float-tier-massive",
                _ => "float-tier-moderate"
            };
        }

        private static string TierImpactClass(HitMagnitudeTier tier)
        {
            return tier switch
            {
                HitMagnitudeTier.Minor => "impact-tier-minor",
                HitMagnitudeTier.Moderate => "impact-tier-moderate",
                HitMagnitudeTier.Major => "impact-tier-major",
                HitMagnitudeTier.Massive => "impact-tier-massive",
                _ => "impact-tier-moderate"
            };
        }

        private static int HitBeatDuration(HitMagnitudeTier tier, bool mitigated)
        {
            if (mitigated && tier < HitMagnitudeTier.Major)
            {
                return 220;
            }

            return tier switch
            {
                HitMagnitudeTier.Minor => 120,
                HitMagnitudeTier.Moderate => 220,
                HitMagnitudeTier.Major => 340,
                HitMagnitudeTier.Massive => 460,
                _ => 220
            };
        }

        private void TrackCombatantFeedbackAnchor(CombatantState state, VisualElement element)
        {
            if (state == null || element == null)
            {
                return;
            }

            TryCacheCombatantFeedbackRect(state, element);
            if (element == _playerPanel)
            {
                if (_playerFeedbackAnchorTracked)
                {
                    return;
                }

                _playerFeedbackAnchorTracked = true;
            }

            element.RegisterCallback<GeometryChangedEvent>(_ => TryCacheCombatantFeedbackRect(state, element));
        }

        private bool TryCacheCombatantFeedbackRect(CombatantState state, VisualElement element)
        {
            if (state == null || !TryGetElementWorldRect(element, out Rect rect))
            {
                return false;
            }

            _lastCombatantFeedbackRects[state] = rect;
            return true;
        }

        private bool TryGetCombatantFeedbackRect(CombatantState state, out Rect rect)
        {
            if (state != null && _combatantElements.TryGetValue(state, out VisualElement element) && TryGetElementWorldRect(element, out rect))
            {
                _lastCombatantFeedbackRects[state] = rect;
                return true;
            }

            if (state != null && _lastCombatantFeedbackRects.TryGetValue(state, out rect) && IsValidRect(rect))
            {
                return true;
            }

            rect = default;
            return false;
        }

        private bool TryGetCombatantElement(CombatantState state, out VisualElement element)
        {
            if (state != null && _combatantElements.TryGetValue(state, out element))
            {
                return element != null;
            }

            element = state == _combatManager.PlayerState ? _playerPanel : null;
            return element != null;
        }

        private bool TryGetElementWorldRect(VisualElement element, out Rect rect)
        {
            rect = default;
            if (element == null || element.panel == null)
            {
                return false;
            }

            rect = element.worldBound;
            return IsValidRect(rect);
        }

        private bool TryGetFeedbackLocalRect(Rect worldRect, out Rect localRect)
        {
            localRect = default;
            if (_feedbackLayer == null || !IsValidRect(worldRect) || !TryGetElementWorldRect(_feedbackLayer, out Rect layerRect))
            {
                return false;
            }

            localRect = new Rect(worldRect.x - layerRect.x, worldRect.y - layerRect.y, worldRect.width, worldRect.height);
            return IsValidRect(localRect);
        }

        private static bool IsValidRect(Rect rect)
        {
            return rect.width > 0f
                && rect.height > 0f
                && IsFinite(rect.x)
                && IsFinite(rect.y)
                && IsFinite(rect.width)
                && IsFinite(rect.height)
                && IsFinite(rect.center.x)
                && IsFinite(rect.center.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private VisualElement CombatantElement(CombatantState state)
        {
            if (state != null && _combatantElements.TryGetValue(state, out VisualElement element))
            {
                return element;
            }

            return state == _combatManager.PlayerState ? _playerPanel : _enemyRow;
        }

        private VisualElement FirstTargetElement(CardInstance card)
        {
            if (card?.TargetSnapshot == null)
            {
                return null;
            }

            for (int i = 0; i < card.TargetSnapshot.Count; i++)
            {
                if (TryGetCombatantElement(card.TargetSnapshot[i], out VisualElement element))
                {
                    return element;
                }
            }

            return null;
        }

        private void PlayElementBeat(VisualElement element, string className, int durationMs)
        {
            if (element == null || string.IsNullOrWhiteSpace(className))
            {
                return;
            }

            int version = ++_feedbackBeatVersion;
            _feedbackBeatVersions[element] = version;
            RemoveFeedbackBeatClasses(element);
            ScheduleFeedback(() =>
            {
                if (!_feedbackBeatVersions.TryGetValue(element, out int currentVersion) || currentVersion != version)
                {
                    return;
                }

                element.AddToClassList(className);
                ScheduleFeedback(() =>
                {
                    if (!_feedbackBeatVersions.TryGetValue(element, out int removeVersion) || removeVersion != version)
                    {
                        return;
                    }

                    element.RemoveFromClassList(className);
                    _feedbackBeatVersions.Remove(element);
                }, UIPreferences.ReducedMotion ? 80 : durationMs);
            }, 0);
        }

        private void SpawnFloatingText(VisualElement anchor, string text, string className, int cascadeOffset = 0)
        {
            if (!TryGetElementWorldRect(anchor, out Rect anchorRect))
            {
                return;
            }

            SpawnFloatingText(anchorRect, text, className, cascadeOffset);
        }

        private void SpawnFloatingText(CombatantState anchor, string text, string className, int cascadeOffset = 0)
        {
            if (!TryGetCombatantFeedbackRect(anchor, out Rect anchorRect))
            {
                return;
            }

            SpawnFloatingText(anchorRect, text, className, cascadeOffset);
        }

        private void SpawnFloatingText(Rect anchorRect, string text, string className, int cascadeOffset = 0)
        {
            if (_feedbackLayer == null || !TryGetFeedbackLocalRect(anchorRect, out Rect localRect))
            {
                return;
            }

            PruneFeedbackLayer();
            Label label = new(text);
            label.AddToClassList("float-number");
            if (!string.IsNullOrWhiteSpace(className))
            {
                string[] classes = className.Split(' ');
                for (int i = 0; i < classes.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(classes[i]))
                    {
                        label.AddToClassList(classes[i]);
                    }
                }
            }

            float offsetX = (((cascadeOffset % 5) - 2) * 14f);
            float offsetY = ((cascadeOffset % 3) * 5f);
            label.style.left = localRect.center.x - 30f + offsetX;
            label.style.top = localRect.center.y - 16f + offsetY;
            _feedbackLayer.Add(label);

            if (UIPreferences.ReducedMotion)
            {
                label.AddToClassList("float-static");
                ScheduleFeedback(() => label.RemoveFromHierarchy(), 420);
                return;
            }

            ScheduleFeedback(() =>
            {
                label.style.top = localRect.center.y - 42f + offsetY;
                label.style.opacity = 0f;
            }, 20);
            ScheduleFeedback(() => label.RemoveFromHierarchy(), 520);
        }

        private void SpawnImpactMarker(VisualElement anchor, bool critical, bool mitigated, int cascadeOffset, HitMagnitudeTier tier = HitMagnitudeTier.Moderate, bool shieldOnly = false)
        {
            if (!TryGetElementWorldRect(anchor, out Rect anchorRect))
            {
                return;
            }

            SpawnImpactMarker(anchorRect, critical, mitigated, cascadeOffset, tier, shieldOnly);
        }

        private void SpawnImpactMarker(CombatantState anchor, bool critical, bool mitigated, int cascadeOffset, HitMagnitudeTier tier = HitMagnitudeTier.Moderate, bool shieldOnly = false)
        {
            if (!TryGetCombatantFeedbackRect(anchor, out Rect anchorRect))
            {
                return;
            }

            SpawnImpactMarker(anchorRect, critical, mitigated, cascadeOffset, tier, shieldOnly);
        }

        private void SpawnImpactMarker(Rect anchorRect, bool critical, bool mitigated, int cascadeOffset, HitMagnitudeTier tier = HitMagnitudeTier.Moderate, bool shieldOnly = false)
        {
            if (_feedbackLayer == null || !TryGetFeedbackLocalRect(anchorRect, out Rect localRect))
            {
                return;
            }

            PruneFeedbackLayer();
            Label marker = new(shieldOnly ? "shield" : critical ? "crit" : mitigated ? "clang" : "hit");
            marker.AddToClassList("impact-marker");
            marker.AddToClassList(shieldOnly || mitigated ? "impact-marker-clang" : critical ? "impact-marker-crit" : "impact-marker-hit");
            marker.AddToClassList(TierImpactClass(tier));

            float lane = (cascadeOffset % 4) * 18f;
            marker.style.left = localRect.xMax - 54f;
            marker.style.top = localRect.y + 8f + lane;
            _feedbackLayer.Add(marker);

            if (UIPreferences.ReducedMotion)
            {
                ScheduleFeedback(() => marker.RemoveFromHierarchy(), 260);
                return;
            }

            ScheduleFeedback(() =>
            {
                marker.style.left = localRect.xMax - 34f;
                marker.style.opacity = 0f;
            }, 20);
            ScheduleFeedback(() => marker.RemoveFromHierarchy(), 420);
        }

        private void TriggerHitStop(HitMagnitudeTier tier)
        {
            if (UIPreferences.ReducedMotion || tier < HitMagnitudeTier.Major || Time.frameCount == _lastHitStopFrame)
            {
                return;
            }

            _lastHitStopFrame = Time.frameCount;
            int version = ++_hitStopVersion;
            float duration = tier == HitMagnitudeTier.Massive ? 0.16f : 0.09f;
            Time.timeScale = 0.04f;
            ScheduleFeedback(() =>
            {
                if (_hitStopVersion == version)
                {
                    Time.timeScale = 1f;
                }
            }, Mathf.RoundToInt(duration * 1000f));
        }

        private void TriggerRootShake(HitMagnitudeTier tier, bool playerHit)
        {
            if (UIPreferences.ReducedMotion || tier < HitMagnitudeTier.Major || _root == null)
            {
                return;
            }

            int version = ++_rootShakeVersion;
            _root.RemoveFromClassList("root-shake-major");
            _root.RemoveFromClassList("root-shake-massive");
            _root.RemoveFromClassList("root-shake-player");
            _root.AddToClassList(tier == HitMagnitudeTier.Massive ? "root-shake-massive" : "root-shake-major");
            _root.EnableInClassList("root-shake-player", playerHit);
            ScheduleFeedback(() =>
            {
                if (_rootShakeVersion != version)
                {
                    return;
                }

                _root.RemoveFromClassList("root-shake-major");
                _root.RemoveFromClassList("root-shake-massive");
                _root.RemoveFromClassList("root-shake-player");
            }, tier == HitMagnitudeTier.Massive ? 220 : 140);
        }

        private void PruneFeedbackLayer()
        {
            if (_feedbackLayer == null)
            {
                return;
            }

            while (_feedbackLayer.childCount >= MaxFeedbackElements)
            {
                _feedbackLayer.ElementAt(0).RemoveFromHierarchy();
            }
        }

        private void FlyCardGhost(CardInstance card, VisualElement source, VisualElement destination, string className)
        {
            if (_feedbackLayer == null
                || card == null
                || !TryGetElementWorldRect(source, out Rect sourceWorldRect)
                || !TryGetElementWorldRect(destination, out Rect destinationWorldRect)
                || !TryGetFeedbackLocalRect(sourceWorldRect, out Rect sourceRect)
                || !TryGetFeedbackLocalRect(destinationWorldRect, out Rect destinationRect))
            {
                return;
            }

            VisualElement ghost = CardFaceView(card);
            ghost.AddToClassList("feedback-card-ghost");
            ghost.AddToClassList(className);
            ghost.style.position = Position.Absolute;
            ghost.style.left = sourceRect.x;
            ghost.style.top = sourceRect.y;
            ghost.style.width = Mathf.Max(110f, sourceRect.width);
            ghost.style.height = Mathf.Max(94f, sourceRect.height);
            _feedbackLayer.Add(ghost);

            if (UIPreferences.ReducedMotion)
            {
                ghost.AddToClassList("feedback-card-instant");
                ScheduleFeedback(() => ghost.RemoveFromHierarchy(), 140);
                return;
            }

            ScheduleFeedback(() =>
            {
                ghost.style.left = destinationRect.center.x - (sourceRect.width * 0.5f);
                ghost.style.top = destinationRect.center.y - (sourceRect.height * 0.5f);
                ghost.style.opacity = 0f;
            }, 20);
            ScheduleFeedback(() => ghost.RemoveFromHierarchy(), 360);
        }

        private void SpawnDeathGhost(Rect sourceRect)
        {
            if (_feedbackLayer == null || !TryGetFeedbackLocalRect(sourceRect, out Rect localRect))
            {
                return;
            }

            float ghostWidth = Mathf.Min(sourceRect.width, 156f);
            float ghostHeight = Mathf.Min(sourceRect.height, 132f);
            VisualElement ghost = new();
            ghost.AddToClassList("death-ghost");
            ghost.style.position = Position.Absolute;
            ghost.style.left = localRect.center.x - (ghostWidth * 0.5f);
            ghost.style.top = localRect.center.y - (ghostHeight * 0.5f);
            ghost.style.width = ghostWidth;
            ghost.style.height = ghostHeight;
            _feedbackLayer.Add(ghost);

            if (UIPreferences.ReducedMotion)
            {
                ScheduleFeedback(() => ghost.RemoveFromHierarchy(), 180);
                return;
            }

            ScheduleFeedback(() =>
            {
                ghost.style.opacity = 0f;
                ghost.style.scale = new Scale(new Vector3(0.92f, 0.92f, 1f));
            }, 20);
            ScheduleFeedback(() => ghost.RemoveFromHierarchy(), 360);
        }

        private void SpawnOverflowTravel(CombatantState source, CombatantState destination, int amount)
        {
            if (_feedbackLayer == null
                || !TryGetCombatantFeedbackRect(source, out Rect sourceWorldRect)
                || !TryGetCombatantFeedbackRect(destination, out Rect destinationWorldRect)
                || !TryGetFeedbackLocalRect(sourceWorldRect, out Rect sourceRect)
                || !TryGetFeedbackLocalRect(destinationWorldRect, out Rect destinationRect))
            {
                return;
            }

            PruneFeedbackLayer();
            Label spill = new($"> {Mathf.Max(0, amount)}");
            spill.AddToClassList("overflow-travel");
            spill.style.position = Position.Absolute;
            spill.style.left = sourceRect.center.x - 20f;
            spill.style.top = sourceRect.center.y - 12f;
            _feedbackLayer.Add(spill);

            if (UIPreferences.ReducedMotion)
            {
                ScheduleFeedback(() => spill.RemoveFromHierarchy(), CombatTuning.ReducedMotionOverflowTravelMs);
                return;
            }

            ScheduleFeedback(() =>
            {
                spill.style.left = destinationRect.center.x - 20f;
                spill.style.top = destinationRect.center.y - 12f;
                spill.style.opacity = 0f;
            }, 20);
            ScheduleFeedback(() => spill.RemoveFromHierarchy(), CombatTuning.OverflowTravelMs + 80);
        }

        private void FlashDamageVignette(HitMagnitudeTier tier)
        {
            if (_damageVignette == null)
            {
                return;
            }

            string className = tier >= HitMagnitudeTier.Massive
                ? "damage-vignette-massive"
                : tier >= HitMagnitudeTier.Major
                    ? "damage-vignette-crit"
                    : "damage-vignette-on";
            PlayElementBeat(_damageVignette, className, UIPreferences.ReducedMotion ? 120 : tier >= HitMagnitudeTier.Massive ? 360 : tier >= HitMagnitudeTier.Major ? 300 : 180);
        }

        private static void RemoveFeedbackBeatClasses(VisualElement element)
        {
            element.RemoveFromClassList("feedback-hit-minor");
            element.RemoveFromClassList("feedback-hit");
            element.RemoveFromClassList("feedback-hit-major");
            element.RemoveFromClassList("feedback-crit");
            element.RemoveFromClassList("feedback-clang");
            element.RemoveFromClassList("feedback-clang-major");
            element.RemoveFromClassList("feedback-block");
            element.RemoveFromClassList("feedback-status");
            element.RemoveFromClassList("feedback-boost");
            element.RemoveFromClassList("feedback-killed");
            element.RemoveFromClassList("enemy-anticipating");
            element.RemoveFromClassList("enemy-acting");
            element.RemoveFromClassList("phase-pulse");
            element.RemoveFromClassList("cycles-spent-beat");
            element.RemoveFromClassList("feedback-denied");
            element.RemoveFromClassList("queue-chip-resolve");
            element.RemoveFromClassList("stack-chip-resolve");
            element.RemoveFromClassList("hand-card-drawn");
            element.RemoveFromClassList("damage-vignette-on");
            element.RemoveFromClassList("damage-vignette-crit");
            element.RemoveFromClassList("damage-vignette-massive");
            element.RemoveFromClassList("damage-vignette-rolling-release");
            element.RemoveFromClassList("feedback-fedora-payoff");
            element.RemoveFromClassList("feedback-spawned");
            element.RemoveFromClassList("fedora-risk-snap");
            element.RemoveFromClassList("arch-btw-pop");
            element.RemoveFromClassList("arch-btw-reset");
            element.RemoveFromClassList("arch-btw-persist");
        }

        private int ScheduleCombatBeat(Action action, int holdMs = 0)
        {
            return ScheduleCombatBeat(_ => action?.Invoke(), holdMs);
        }

        private int ScheduleCombatBeat(Action<int> action, int holdMs = 0)
        {
            if (action == null)
            {
                return 0;
            }

            int beatIndex = _feedbackCascadeIndex++;
            int delay = _combatBeatCursorMs;
            int step = CombatBeatStepMs(holdMs);
            _combatBeatCursorMs += step;
            int version = ++_combatBeatCursorVersion;
            ScheduleFeedback(() => action(beatIndex), delay);
            ScheduleFeedback(() =>
            {
                if (_combatBeatCursorVersion == version)
                {
                    _combatBeatCursorMs = 0;
                }
            }, delay + step + CombatTuning.CombatBeatSettleMs);
            return delay + step;
        }

        private static int CombatBeatStepMs(int holdMs)
        {
            if (UIPreferences.ReducedMotion)
            {
                int compressed = holdMs <= 0 ? CombatTuning.ReducedMotionCombatBeatDefaultMs : holdMs / 4;
                return Mathf.Clamp(compressed, CombatTuning.ReducedMotionCombatBeatMinMs, CombatTuning.ReducedMotionCombatBeatMaxMs);
            }

            return Mathf.Max(CombatTuning.CombatBeatDefaultMs, holdMs);
        }

        private void ScheduleFeedback(Action action, int delayMs)
        {
            if (action == null || _root == null)
            {
                return;
            }

            _root.schedule.Execute(action).StartingIn(Mathf.Max(0, delayMs));
        }

        private static void ApplyStatusStateClasses(VisualElement element, CombatantState state)
        {
            if (element == null)
            {
                return;
            }

            RemoveStatusStateClasses(element);
            if (state == null)
            {
                return;
            }

            for (int i = 0; i < state.Statuses.Count; i++)
            {
                ApplyStatusStateClass(element, state.Statuses[i].Type, true);
            }
        }

        private static void ApplyStatusStateClass(VisualElement element, StatusType statusType, bool enabled)
        {
            string className = StatusStateClass(statusType);
            if (element == null || string.IsNullOrEmpty(className))
            {
                return;
            }

            element.EnableInClassList(className, enabled);
        }

        private static void RemoveStatusStateClasses(VisualElement element)
        {
            ApplyStatusStateClass(element, StatusType.MemoryLeak, false);
            ApplyStatusStateClass(element, StatusType.Segfault, false);
            ApplyStatusStateClass(element, StatusType.RaceCondition, false);
            ApplyStatusStateClass(element, StatusType.Deprecated, false);
            ApplyStatusStateClass(element, StatusType.DependencyError, false);
            ApplyStatusStateClass(element, StatusType.Deadlock, false);
            ApplyStatusStateClass(element, StatusType.UnattendedUpgrades, false);
        }

        private void HandleCombatLog(string message)
        {
            if (_logLabel != null)
            {
                _logLabel.text = message ?? string.Empty;
            }
        }

        private VisualElement CardChip(CardInstance card)
        {
            VisualElement chip = new();
            chip.AddToClassList("card-chip");
            chip.AddToClassList(LanguageClass(card.Definition.Language));
            Label name = new(DisplayName(card.Definition));
            name.AddToClassList("card-chip-name");
            Label meta = new($"{GetDisplayCardCost(card)}c / {TrackText(card.Definition.ResolutionTrack)}");
            meta.AddToClassList("card-chip-meta");
            chip.Add(name);
            chip.Add(meta);
            return chip;
        }

        private static VisualElement EmptyState(string text)
        {
            Label label = new(text);
            label.AddToClassList("empty-state");
            return label;
        }

        private int GetDisplayCardCost(CardInstance card)
        {
            return _combatManager == null ? CombatManager.GetCardCost(card) : _combatManager.GetEffectiveCardCost(card);
        }

        private static VisualElement CreatePanel(string title, string subtitle, float width)
        {
            VisualElement panel = new();
            panel.AddToClassList("panel");
            if (width > 0f)
            {
                panel.style.width = width;
                panel.style.minWidth = width;
            }

            panel.Add(PanelHeader(title, subtitle));
            return panel;
        }

        private static VisualElement PanelHeader(string title, string subtitle)
        {
            VisualElement header = new();
            header.AddToClassList("panel-header");
            Label titleLabel = new(title);
            titleLabel.AddToClassList("panel-title");
            Label subtitleLabel = new(subtitle);
            subtitleLabel.AddToClassList("panel-subtitle");
            header.Add(titleLabel);
            header.Add(subtitleLabel);
            return header;
        }

        private void ApplyAccent(VisualElement element)
        {
            element.style.borderTopColor = _distroAccent;
        }

        private static VisualElement TurnStat(string labelText, string valueText)
        {
            VisualElement row = new();
            row.AddToClassList("turn-stat-row");
            Label label = new(labelText);
            label.AddToClassList("turn-stat-label");
            Label value = new(valueText);
            value.AddToClassList("turn-stat-value");
            row.Add(label);
            row.Add(value);
            return row;
        }

        private static Label OverlayTitle(string text)
        {
            Label label = new(text);
            label.AddToClassList("overlay-title");
            return label;
        }

        private static Label OverlayLine(string text)
        {
            Label label = new(text);
            label.AddToClassList("overlay-line");
            return label;
        }

        private static VisualElement DeathStat(string labelText, string valueText)
        {
            VisualElement row = new();
            row.AddToClassList("death-stat-row");
            Label label = new(labelText);
            label.AddToClassList("death-stat-label");
            Label value = new(valueText);
            value.AddToClassList("death-stat-value");
            row.Add(label);
            row.Add(value);
            return row;
        }

        private static VisualElement RewardReadout(string iconText, int amount, string currencyName, bool subdued)
        {
            VisualElement item = new();
            item.AddToClassList("death-reward-item");
            item.AddToClassList($"death-reward-{currencyName}");
            item.EnableInClassList("death-reward-subdued", subdued);

            Label icon = new(iconText);
            icon.AddToClassList("death-reward-icon");
            Label value = new($"+{amount}");
            value.AddToClassList("death-reward-value");
            Label name = new(currencyName);
            name.AddToClassList("death-reward-name");

            item.Add(icon);
            item.Add(value);
            item.Add(name);
            return item;
        }

        private static VisualElement Tag(string text, string className)
        {
            Label tag = new(text);
            tag.AddToClassList("tag");
            if (!string.IsNullOrEmpty(className))
            {
                tag.AddToClassList(className);
            }

            return tag;
        }

        private static string BuildPips(int current, int max)
        {
            int safeMax = Mathf.Max(0, max);
            int safeCurrent = Mathf.Clamp(current, 0, safeMax);
            return string.Concat(Repeat(FilledCycle, safeCurrent), Repeat(EmptyCycle, safeMax - safeCurrent));
        }

        private static string Repeat(string value, int count)
        {
            if (count <= 0)
            {
                return string.Empty;
            }

            string result = string.Empty;
            for (int i = 0; i < count; i++)
            {
                result += value;
            }

            return result;
        }

        private static string DurationText(int duration)
        {
            return duration == -1 ? "inf" : $"{duration}t";
        }

        private static string PhaseText(TurnPhase phase)
        {
            return phase switch
            {
                TurnPhase.Boot => "boot",
                TurnPhase.Allocate => "allocate",
                TurnPhase.Execute => "execute",
                TurnPhase.Interpret => "interpret",
                TurnPhase.EnemyProcess => "enemy",
                TurnPhase.GarbageCollection => "cleanup",
                _ => phase.ToString()
            };
        }

        private static string FormatWalletWithAccrual(int walletAmount, int accruedAmount)
        {
            return accruedAmount > 0 ? $"{walletAmount} (+{accruedAmount})" : walletAmount.ToString();
        }

        private static string TrackText(ResolutionTrack track)
        {
            return track switch
            {
                ResolutionTrack.Native => "Native",
                ResolutionTrack.InterpreterQueue => "Queue",
                ResolutionTrack.LazyStack => "Stack",
                _ => track.ToString()
            };
        }

        private static string IntentIcon(EnemyIntent intent)
        {
            if (!string.IsNullOrWhiteSpace(intent.IconKey))
            {
                return intent.IconKey;
            }

            return intent.Kind switch
            {
                EnemyIntentKind.Attack => "!",
                EnemyIntentKind.StatusAttack => "!",
                EnemyIntentKind.Defend => "#",
                EnemyIntentKind.Buff => "+",
                EnemyIntentKind.Special => "*",
                _ => "?"
            };
        }

        private static string IntentText(EnemyIntent intent)
        {
            return intent.Kind switch
            {
                EnemyIntentKind.Attack => "attack",
                EnemyIntentKind.StatusAttack => $"{intent.DisplayLabel} x{intent.StatusStacks}",
                EnemyIntentKind.Defend => "defend",
                EnemyIntentKind.Buff => "buff",
                EnemyIntentKind.Special => intent.DisplayLabel,
                _ => intent.DisplayLabel
            };
        }

        private static string IntentClass(EnemyIntentKind kind)
        {
            return kind switch
            {
                EnemyIntentKind.Attack => "intent-attack",
                EnemyIntentKind.StatusAttack => "intent-attack",
                EnemyIntentKind.Defend => "intent-defend",
                _ => "intent-special"
            };
        }

        private static string StatusStateClass(StatusType statusType)
        {
            return statusType switch
            {
                StatusType.MemoryLeak => "status-state-leaking",
                StatusType.Segfault => "status-state-segfault",
                StatusType.RaceCondition => "status-state-race",
                StatusType.Deprecated => "status-state-deprecated",
                StatusType.DependencyError => "status-state-dependency-error",
                StatusType.Deadlock => "status-state-deadlock",
                StatusType.UnattendedUpgrades => "status-state-upgrading",
                _ => string.Empty
            };
        }

        private static string OfferKindText(RepositoryOfferKind kind)
        {
            return kind switch
            {
                RepositoryOfferKind.NewCard => "card",
                RepositoryOfferKind.CardUpgrade => "upgrade",
                RepositoryOfferKind.StatUpgrade => "stat",
                _ => "offer"
            };
        }

        private static string OfferKindIcon(RepositoryOfferKind kind)
        {
            return kind switch
            {
                RepositoryOfferKind.NewCard => "+",
                RepositoryOfferKind.CardUpgrade => "▲",
                RepositoryOfferKind.StatUpgrade => "◆",
                _ => "?"
            };
        }

        private static string OfferTileClass(RepositoryOfferKind kind)
        {
            return kind switch
            {
                RepositoryOfferKind.NewCard => "repository-tile-card",
                RepositoryOfferKind.CardUpgrade => "repository-tile-upgrade",
                RepositoryOfferKind.StatUpgrade => "repository-tile-stat",
                _ => "repository-tile-generic"
            };
        }

        private static string StatOfferIcon(RunStatUpgradeKind kind)
        {
            return kind switch
            {
                RunStatUpgradeKind.Heal => "♥",
                RunStatUpgradeKind.MaxCycles => "●",
                RunStatUpgradeKind.MaxUptime => "▰",
                RunStatUpgradeKind.Ram => "RAM",
                _ => "◆"
            };
        }

        private static string OfferCommandText(RepositoryOffer offer)
        {
            return offer.Kind switch
            {
                RepositoryOfferKind.CardUpgrade => $"apt upgrade {offer.CommandName}",
                RepositoryOfferKind.StatUpgrade => $"apt install {offer.CommandName}",
                _ => $"apt install {offer.CommandName}"
            };
        }

        private static string OfferActionText(RepositoryOfferKind kind)
        {
            return kind switch
            {
                RepositoryOfferKind.CardUpgrade => "apt upgrade",
                RepositoryOfferKind.StatUpgrade => "apt apply",
                _ => "apt install"
            };
        }

        private static string MeterFillClass(MeterTone tone, float ratio)
        {
            if (tone == MeterTone.Uptime)
            {
                if (ratio <= 0.25f)
                {
                    return "meter-fill-danger";
                }

                if (ratio <= 0.5f)
                {
                    return "meter-fill-warning";
                }
            }

            return tone switch
            {
                MeterTone.Beneficial => "meter-fill-beneficial",
                MeterTone.Muted => "metric-value-muted",
                _ => "meter-fill"
            };
        }

        private static string LanguageClass(Language language)
        {
            return language switch
            {
                Language.C => "language-c",
                Language.CPlusPlus => "language-cpp",
                Language.Rust => "language-rust",
                Language.Python => "language-python",
                Language.JavaScript => "language-javascript",
                Language.TypeScript => "language-typescript",
                Language.Haskell => "language-haskell",
                Language.Assembly => "language-assembly",
                Language.Java => "language-java",
                Language.Go => "language-go",
                Language.Ruby => "language-ruby",
                Language.Php => "language-php",
                _ => "track-tag"
            };
        }

        private static string CardRarityClass(Rarity rarity)
        {
            return rarity switch
            {
                Rarity.Rare => "hand-card-rare",
                Rarity.Epic => "hand-card-epic",
                Rarity.Legendary => "hand-card-legendary",
                _ => string.Empty
            };
        }

        private static string RarityTagClass(Rarity rarity)
        {
            return rarity switch
            {
                Rarity.Rare => "rarity-rare",
                Rarity.Epic => "rarity-epic",
                Rarity.Legendary => "rarity-legendary",
                _ => "track-tag"
            };
        }

        private static string DisplayName(DistroDefinition distro)
        {
            return distro == null ? "--" : string.IsNullOrWhiteSpace(distro.DisplayName) ? distro.Id : distro.DisplayName;
        }

        private static string DisplayName(CardDefinition card)
        {
            return card == null ? "--" : string.IsNullOrWhiteSpace(card.DisplayName) ? card.Id : card.DisplayName;
        }

        private enum MeterTone
        {
            Uptime,
            Beneficial,
            Muted
        }
    }
}
