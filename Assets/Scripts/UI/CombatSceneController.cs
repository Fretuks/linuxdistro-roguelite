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
    /// ! attack/status attack, # defend, + buff, * special, ? hidden, ~ reviving, : split, @ countdown.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(RunManager))]
    [RequireComponent(typeof(CombatManager))]
    public sealed class CombatSceneController : MonoBehaviour
    {
        private const string StyleResourcePath = "CombatScene";
        private const string SharedScrollbarStyleResourcePath = "TerminalScrollbars";
        private const string SharedRarityStyleResourcePath = "RarityPresentation";
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
        private readonly HashSet<CardInstance> _knownHandCards = new();
        private int _queueCascadeIndex;
        private int _feedbackCascadeIndex;
        private int _feedbackBeatVersion;

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
            GameEvents.CombatantDefeated += HandleCombatantDefeated;
            GameEvents.EnemyWouldAct += HandleEnemyWouldAct;
            GameEvents.PlayerDamaged += HandlePlayerDamaged;
            GameEvents.StatusApplied += HandleStatusApplied;
            GameEvents.StatusExpired += HandleStatusEnded;
            GameEvents.StatusCleansed += HandleStatusEnded;
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
            SettleRunRewardsIfNeeded();
            _combatManager.StateChanged -= Refresh;
            _combatManager.CombatLog -= HandleCombatLog;
            _runManager.RepositoryChanged -= Refresh;
            GameEvents.CardPlayed -= HandleCardPlayed;
            GameEvents.CardResolved -= HandleCardResolved;
            GameEvents.PhaseChanged -= HandlePhaseChanged;
            GameEvents.DamageDealt -= HandleDamageDealt;
            GameEvents.CombatantDefeated -= HandleCombatantDefeated;
            GameEvents.EnemyWouldAct -= HandleEnemyWouldAct;
            GameEvents.PlayerDamaged -= HandlePlayerDamaged;
            GameEvents.StatusApplied -= HandleStatusApplied;
            GameEvents.StatusExpired -= HandleStatusEnded;
            GameEvents.StatusCleansed -= HandleStatusEnded;
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
            _enemyRow = new();
            _enemyRow.AddToClassList("enemy-row");
            enemiesPanel.Add(_enemyRow);

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
            DetectBeneficialResourceFeedback(state, _playerPanel);
            ApplyStatusStateClasses(_playerPanel, state);
            _playerPanel.Add(MeterBlock("uptime", state.CurrentUptime, state.MaxUptime, MeterTone.Uptime));
            _playerPanel.Add(MeterBlock("shield", state.Shield, Mathf.Max(1, state.Shield), state.Shield > 0 ? MeterTone.Beneficial : MeterTone.Muted));
            _playerPanel.Add(CycleBlock(state.Cycles, state.MaxCycles));

            Label ram = new($"ram hand cap {_combatManager.HandController.UsedRam}/{_combatManager.HandController.RamCapacity}");
            ram.AddToClassList("ram-note");
            _playerPanel.Add(ram);
            _playerPanel.Add(StatusBlock(state, true));
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
            if (_combatManager.PlayerState != null)
            {
                _combatantElements[_combatManager.PlayerState] = _playerPanel;
            }

            for (int i = 0; i < _combatManager.Enemies.Count; i++)
            {
                int index = i;
                EnemyInstance enemy = _combatManager.Enemies[i];
                Button card = new(() => _combatManager.SelectEnemy(index));
                card.text = string.Empty;
                card.AddToClassList("enemy-card");
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

                _combatantElements[enemy.State] = card;
                DetectBeneficialResourceFeedback(enemy.State, card);
                ApplyStatusStateClasses(card, enemy.State);
                card.Add(MeterBlock("uptime", enemy.CurrentUptime, enemy.MaxUptime, MeterTone.Uptime));
                if (enemy.State.Shield > 0)
                {
                    Label shield = new($"# shield {enemy.State.Shield}");
                    shield.AddToClassList("enemy-shield");
                    card.Add(shield);
                }

                card.Add(StatusPipRow(enemy.State));
                card.Add(IntentPanel(enemy.DisplayIntent));
                _enemyRow.Add(card);
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
                _overlay.Add(OverlayTitle("kernel panic"));
                _overlay.Add(OverlayLine("fatal: player uptime reached 0"));
                _overlay.Add(OverlayLine($"wave {_runManager.CurrentWaveNumber} reached  seed {_runManager.CurrentConfig?.RunSeed ?? 0}"));
                _overlay.Add(OverlayLine($"waves cleared: {_runManager.WavesCleared}"));
                _overlay.Add(OverlayLine($"+{_runManager.AccruedBandwidth} bandwidth"));
                _overlay.Add(OverlayLine($"+{_runManager.AccruedEntropy} entropy"));
                Button returnButton = new(() => SceneLoader.LoadMainMenu(_root)) { text = "> return to menu" };
                returnButton.AddToClassList("primary-action");
                _overlay.Add(returnButton);
                return;
            }

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
            repo.Add(BuildRepositoryHeader());

            ScrollView body = new(ScrollViewMode.Vertical);
            body.AddToClassList("repository-body");

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
            Button reroll = new(() => _runManager.RerollRepositoryOffers(cardDatabase, languageDeckDatabase))
            {
                text = $"apt update - reroll  ({_runManager.RerollCost} bits)"
            };
            reroll.AddToClassList("primary-action");
            reroll.AddToClassList("repository-footer-action");
            reroll.EnableInClassList("repo-unaffordable", _runManager.Bits < _runManager.RerollCost);
            reroll.SetEnabled(_runManager.Bits >= _runManager.RerollCost);
            footer.Add(reroll);

            Button leave = new(() => _combatManager.ContinueToNextWave()) { text = "> boot next-wave" };
            leave.AddToClassList("primary-action");
            leave.AddToClassList("repository-leave-action");
            VisualElement leaveBlock = new();
            leaveBlock.AddToClassList("repository-leave-block");
            leaveBlock.Add(leave);
            Label banked = new($"{_runManager.Bits} bits banked");
            banked.AddToClassList("repository-banked-hint");
            leaveBlock.Add(banked);
            footer.Add(leaveBlock);
            repo.Add(footer);
            return repo;
        }

        private VisualElement BuildRepositoryHeader()
        {
            VisualElement header = new();
            header.AddToClassList("repository-header");

            VisualElement copy = new();
            copy.AddToClassList("repository-header-copy");
            Label title = new($"repository :: wave {_runManager.CurrentWaveNumber - 1} cleared");
            title.AddToClassList("overlay-title");
            Label subtitle = new($"$ apt browse    {_runManager.RepositoryOffers.Count}/{CombatTuning.ShopSize} offers");
            subtitle.AddToClassList("overlay-line");
            copy.Add(title);
            copy.Add(subtitle);

            VisualElement wallet = new();
            wallet.AddToClassList("repository-wallet");
            Label walletLabel = new("bits wallet");
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
                text = offer.Sold ? "installed" : OfferCommandText(offer)
            };
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
            Label title = new($"apt remove  ({CombatTuning.RemoveCardCost} bits)");
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
            Button button = new(() => PlayCardWithFeedback(card));
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

        private static VisualElement StatusBlock(CombatantState state, bool includeTooltips)
        {
            VisualElement block = new();
            Label title = new("statuses");
            title.AddToClassList("status-section-title");
            block.Add(title);
            block.Add(StatusPipRow(state));

            if (includeTooltips)
            {
                for (int i = 0; i < state.Statuses.Count; i++)
                {
                    StatusDescriptor descriptor = StatusEffectController.GetDescriptor(state.Statuses[i].Type);
                    Label tooltip = new(descriptor.Tooltip);
                    tooltip.AddToClassList("status-tooltip");
                    block.Add(tooltip);
                }
            }

            return block;
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
            ScheduleFeedback(() => PlayElementBeat(_phaseLabel, "phase-pulse", 260), 0);
        }

        private void HandleDamageDealt(DamageDealtEvent payload)
        {
            bool isPlayer = payload.Target == _combatManager.PlayerState;
            VisualElement target = CombatantElement(payload.Target);
            ScheduleCombatBeat(beatIndex =>
            {
                string text = payload.WasFullyBlocked ? "clang" : payload.UptimeDamage <= 0 ? "blocked" : $"-{Mathf.RoundToInt(payload.UptimeDamage)}";
                string tone = payload.WasFullyBlocked || payload.WasMitigated ? "float-muted" : "float-damage";
                string beatClass = payload.WasFullyBlocked || payload.WasMitigated ? "feedback-clang" : payload.WasCritical ? "feedback-crit" : "feedback-hit";
                int beatDuration = payload.WasCritical ? 360 : payload.WasMitigated ? 260 : 240;

                if (payload.WasCritical || payload.UptimeDamage >= 8)
                {
                    tone += " float-large";
                }

                SpawnFloatingText(target, text, tone, beatIndex);
                SpawnImpactMarker(target, payload.WasCritical, payload.WasMitigated, beatIndex);
                PlayElementBeat(target, beatClass, beatDuration);
                if (isPlayer && payload.UptimeDamage > 0)
                {
                    FlashDamageVignette(payload.WasCritical);
                }
            });
        }

        private void HandleCombatantDefeated(CombatantDefeatedEvent payload)
        {
            VisualElement target = CombatantElement(payload.Combatant);
            Rect targetBounds = target?.worldBound ?? Rect.zero;
            ScheduleCombatBeat(() =>
            {
                SpawnFloatingText(target, "killed", "float-kill float-large", 0);
                SpawnDeathGhost(targetBounds);
                if (target != _enemyRow)
                {
                    PlayElementBeat(target, "feedback-killed", 420);
                }
            });
        }

        private void HandleEnemyWouldAct(EnemyWouldActEvent payload)
        {
            ScheduleCombatBeat(() => PlayElementBeat(CombatantElement(payload.Enemy?.State), "enemy-anticipating", 340));
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
                VisualElement target = CombatantElement(payload.Target);
                ApplyStatusStateClass(target, payload.StatusType, true);
                SpawnFloatingText(target, $"{descriptor.IconKey} x{payload.Stacks}", descriptor.IsBeneficial ? "float-heal" : "float-status");
                PlayElementBeat(target, descriptor.IsBeneficial ? "feedback-boost" : "feedback-status", 220);
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
                return _enemyRow;
            }

            for (int i = 0; i < card.TargetSnapshot.Count; i++)
            {
                VisualElement element = CombatantElement(card.TargetSnapshot[i]);
                if (element != null)
                {
                    return element;
                }
            }

            return _enemyRow;
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
            if (_feedbackLayer == null || anchor == null)
            {
                return;
            }

            Rect anchorRect = anchor.worldBound;
            Rect rootRect = _root.worldBound;
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
            label.style.left = anchorRect.center.x - rootRect.x - 30f + offsetX;
            label.style.top = anchorRect.center.y - rootRect.y - 16f + offsetY;
            _feedbackLayer.Add(label);

            if (UIPreferences.ReducedMotion)
            {
                label.AddToClassList("float-static");
                ScheduleFeedback(() => label.RemoveFromHierarchy(), 420);
                return;
            }

            ScheduleFeedback(() =>
            {
                label.style.top = anchorRect.center.y - rootRect.y - 42f + offsetY;
                label.style.opacity = 0f;
            }, 20);
            ScheduleFeedback(() => label.RemoveFromHierarchy(), 520);
        }

        private void SpawnImpactMarker(VisualElement anchor, bool critical, bool mitigated, int cascadeOffset)
        {
            if (_feedbackLayer == null || anchor == null)
            {
                return;
            }

            Rect anchorRect = anchor.worldBound;
            Rect rootRect = _root.worldBound;
            Label marker = new(critical ? "crit" : mitigated ? "clang" : "hit");
            marker.AddToClassList("impact-marker");
            marker.AddToClassList(critical ? "impact-marker-crit" : mitigated ? "impact-marker-clang" : "impact-marker-hit");

            float lane = (cascadeOffset % 4) * 18f;
            marker.style.left = anchorRect.xMax - rootRect.x - 54f;
            marker.style.top = anchorRect.y - rootRect.y + 8f + lane;
            _feedbackLayer.Add(marker);

            if (UIPreferences.ReducedMotion)
            {
                ScheduleFeedback(() => marker.RemoveFromHierarchy(), 260);
                return;
            }

            ScheduleFeedback(() =>
            {
                marker.style.left = anchorRect.xMax - rootRect.x - 34f;
                marker.style.opacity = 0f;
            }, 20);
            ScheduleFeedback(() => marker.RemoveFromHierarchy(), 420);
        }

        private void FlyCardGhost(CardInstance card, VisualElement source, VisualElement destination, string className)
        {
            if (_feedbackLayer == null || card == null)
            {
                return;
            }

            Rect rootRect = _root.worldBound;
            Rect sourceRect = source == null ? new Rect(rootRect.center.x - 70f, rootRect.center.y - 50f, 140f, 110f) : source.worldBound;
            Rect destinationRect = destination == null ? sourceRect : destination.worldBound;

            VisualElement ghost = CardFaceView(card);
            ghost.AddToClassList("feedback-card-ghost");
            ghost.AddToClassList(className);
            ghost.style.position = Position.Absolute;
            ghost.style.left = sourceRect.x - rootRect.x;
            ghost.style.top = sourceRect.y - rootRect.y;
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
                ghost.style.left = destinationRect.center.x - rootRect.x - (sourceRect.width * 0.5f);
                ghost.style.top = destinationRect.center.y - rootRect.y - (sourceRect.height * 0.5f);
                ghost.style.opacity = 0f;
            }, 20);
            ScheduleFeedback(() => ghost.RemoveFromHierarchy(), 360);
        }

        private void SpawnDeathGhost(Rect sourceRect)
        {
            if (_feedbackLayer == null || sourceRect.width <= 0f || sourceRect.height <= 0f)
            {
                return;
            }

            Rect rootRect = _root.worldBound;
            float ghostWidth = Mathf.Min(sourceRect.width, 156f);
            float ghostHeight = Mathf.Min(sourceRect.height, 132f);
            VisualElement ghost = new();
            ghost.AddToClassList("death-ghost");
            ghost.style.position = Position.Absolute;
            ghost.style.left = sourceRect.center.x - rootRect.x - (ghostWidth * 0.5f);
            ghost.style.top = sourceRect.center.y - rootRect.y - (ghostHeight * 0.5f);
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

        private void FlashDamageVignette(bool critical)
        {
            if (_damageVignette == null)
            {
                return;
            }

            PlayElementBeat(_damageVignette, critical ? "damage-vignette-crit" : "damage-vignette-on", UIPreferences.ReducedMotion ? 120 : critical ? 320 : 240);
        }

        private static void RemoveFeedbackBeatClasses(VisualElement element)
        {
            element.RemoveFromClassList("feedback-hit");
            element.RemoveFromClassList("feedback-crit");
            element.RemoveFromClassList("feedback-clang");
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
        }

        private void ScheduleCombatBeat(Action action, int holdMs = 0)
        {
            ScheduleCombatBeat(_ => action?.Invoke(), holdMs);
        }

        private void ScheduleCombatBeat(Action<int> action, int holdMs = 0)
        {
            if (action == null)
            {
                return;
            }

            int beatIndex = _feedbackCascadeIndex++;
            int delay = UIPreferences.ReducedMotion ? 0 : Mathf.Min(beatIndex * 120, 840);
            ScheduleFeedback(() => action(beatIndex), delay + (UIPreferences.ReducedMotion ? 0 : holdMs));
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
