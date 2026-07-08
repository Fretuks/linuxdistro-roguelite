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
    /// ! attack/status attack, # defend, + buff, * special.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(RunManager))]
    [RequireComponent(typeof(CombatManager))]
    public sealed class CombatSceneController : MonoBehaviour
    {
        private const string StyleResourcePath = "CombatScene";
        private const string SharedScrollbarStyleResourcePath = "TerminalScrollbars";
        private const string FilledCycle = "●";
        private const string EmptyCycle = "○";

        [SerializeField] private DistroDatabase distroDatabase;
        [SerializeField] private CardDatabase cardDatabase;
        [SerializeField] private LanguageDeckDatabase languageDeckDatabase;

        private UIDocument document;
        private RunManager runManager;
        private CombatManager combatManager;
        private SaveService saveService;
        private VisualElement root;
        private VisualElement statusBar;
        private Label waveLabel;
        private Label phaseLabel;
        private Label entropyLabel;
        private Label bitsLabel;
        private Label bandwidthLabel;
        private Label computeCreditsLabel;
        private Label seedLabel;
        private VisualElement playerPanel;
        private VisualElement interpreterStrip;
        private VisualElement lazyStackPile;
        private VisualElement tokenArea;
        private VisualElement enemyRow;
        private VisualElement handRow;
        private VisualElement turnResourceGrid;
        private Label logLabel;
        private VisualElement feedbackLayer;
        private VisualElement damageVignette;
        private VisualElement overlay;
        private Color distroAccent;
        private readonly Dictionary<CombatantState, VisualElement> combatantElements = new();
        private readonly Dictionary<CardInstance, VisualElement> handCardElements = new();
        private readonly Dictionary<CardInstance, VisualElement> queueChipElements = new();
        private readonly Dictionary<CardInstance, VisualElement> stackChipElements = new();
        private readonly Dictionary<CombatantState, int> previousUptime = new();
        private readonly Dictionary<CombatantState, int> previousShield = new();
        private readonly HashSet<CardInstance> knownHandCards = new();
        private int queueCascadeIndex;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            runManager = GetComponent<RunManager>();
            combatManager = GetComponent<CombatManager>();
            saveService = new SaveService();
            root = document.rootVisualElement;
            LoadStyles();
            ApplyTerminalFont();
            BuildLayout();
        }

        private void ApplyTerminalFont()
        {
            var font = TerminalFontResolver.Resolve(null);
            if (font != null)
            {
                root.style.unityFontDefinition = new StyleFontDefinition(font);
            }
        }

        private void OnEnable()
        {
            root?.EnableInClassList("reduced-motion", UIPreferences.ReducedMotion);
            combatManager.StateChanged += Refresh;
            combatManager.CombatLog += HandleCombatLog;
            runManager.RepositoryChanged += Refresh;
            GameEvents.CardPlayed += HandleCardPlayed;
            GameEvents.CardResolved += HandleCardResolved;
            GameEvents.PhaseChanged += HandlePhaseChanged;
            GameEvents.DamageDealt += HandleDamageDealt;
            GameEvents.CombatantDefeated += HandleCombatantDefeated;
            GameEvents.EnemyWouldAct += HandleEnemyWouldAct;
            GameEvents.PlayerDamaged += HandlePlayerDamaged;
            GameEvents.StatusApplied += HandleStatusApplied;
        }

        private void Start()
        {
            if (!TryBuildRunConfig(out RunConfig config))
            {
                HandleMissingRunConfig();
                return;
            }

            distroAccent = config.Distro == null ? Color.white : config.Distro.AccentColor;
            combatManager.SetGeneratedCardPool(BuildGeneratedCardPool());
            runManager.StartRun(config);
            Refresh();
        }

        private IReadOnlyList<CardDefinition> BuildGeneratedCardPool()
        {
            List<CardDefinition> cards = new();
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
            combatManager.StateChanged -= Refresh;
            combatManager.CombatLog -= HandleCombatLog;
            runManager.RepositoryChanged -= Refresh;
            GameEvents.CardPlayed -= HandleCardPlayed;
            GameEvents.CardResolved -= HandleCardResolved;
            GameEvents.PhaseChanged -= HandlePhaseChanged;
            GameEvents.DamageDealt -= HandleDamageDealt;
            GameEvents.CombatantDefeated -= HandleCombatantDefeated;
            GameEvents.EnemyWouldAct -= HandleEnemyWouldAct;
            GameEvents.PlayerDamaged -= HandlePlayerDamaged;
            GameEvents.StatusApplied -= HandleStatusApplied;
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
            root.Clear();
            root.AddToClassList("combat-root");
            Label error = new("combat bootstrap failed: no RunContext and no editor fallback data");
            error.AddToClassList("overlay-line");
            root.Add(error);
            Debug.LogError("GameScene could not start: no RunContext and no Ubuntu editor fallback data.");
        }

        private void LoadStyles()
        {
            StyleSheet styleSheet = Resources.Load<StyleSheet>(StyleResourcePath);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            StyleSheet scrollbarStyleSheet = Resources.Load<StyleSheet>(SharedScrollbarStyleResourcePath);
            if (scrollbarStyleSheet != null)
            {
                root.styleSheets.Add(scrollbarStyleSheet);
            }
        }

        private void BuildLayout()
        {
            root.Clear();
            root.AddToClassList("combat-root");
            root.style.flexGrow = 1;

            statusBar = BuildStatusBar();
            root.Add(statusBar);

            VisualElement main = new();
            main.AddToClassList("main-layout");
            root.Add(main);

            playerPanel = CreatePanel("player", "htop process monitor", 242);
            main.Add(playerPanel);

            VisualElement fieldPanel = CreatePanel("field", "resolution tracks", 0);
            fieldPanel.AddToClassList("field-panel");
            main.Add(fieldPanel);
            interpreterStrip = AddTrackZone(fieldPanel, "interpreter queue", "FIFO queued scripts", "track-zone-queue");
            lazyStackPile = AddTrackZone(fieldPanel, "lazy stack", "LIFO delayed work", "track-zone-stack");
            tokenArea = AddTrackZone(fieldPanel, "goroutines", "token lane", "track-zone-token");

            VisualElement enemiesPanel = CreatePanel("enemy processes", "intent telemetry", 438);
            main.Add(enemiesPanel);
            enemyRow = new();
            enemyRow.AddToClassList("enemy-row");
            enemiesPanel.Add(enemyRow);

            VisualElement bottom = new();
            bottom.AddToClassList("bottom-layout");
            root.Add(bottom);

            VisualElement handPanel = CreatePanel("hand", "executable cards", 0);
            handPanel.AddToClassList("hand-panel");
            bottom.Add(handPanel);
            handRow = new();
            handRow.AddToClassList("hand-row");
            handPanel.Add(handRow);

            VisualElement commandPanel = CreatePanel("turn", "command", 240);
            commandPanel.AddToClassList("turn-panel");
            bottom.Add(commandPanel);
            turnResourceGrid = new();
            turnResourceGrid.AddToClassList("turn-resource-grid");
            commandPanel.Add(turnResourceGrid);

            Button endTurn = new(() => combatManager.EndPlayerTurn()) { text = "> end-turn" };
            endTurn.AddToClassList("primary-action");
            commandPanel.Add(endTurn);

            logLabel = new();
            logLabel.AddToClassList("log-line");
            commandPanel.Add(logLabel);

            feedbackLayer = new();
            feedbackLayer.AddToClassList("feedback-layer");
            feedbackLayer.pickingMode = PickingMode.Ignore;
            root.Add(feedbackLayer);

            damageVignette = new();
            damageVignette.AddToClassList("damage-vignette");
            damageVignette.pickingMode = PickingMode.Ignore;
            root.Add(damageVignette);

            overlay = new();
            overlay.AddToClassList("overlay");
            overlay.style.display = DisplayStyle.None;
            root.Add(overlay);
        }

        private VisualElement BuildStatusBar()
        {
            VisualElement bar = new();
            bar.AddToClassList("combat-status-bar");

            waveLabel = AddStatusReadout(bar, "wave", "--", false);
            phaseLabel = AddStatusReadout(bar, "phase", "--", true);
            bitsLabel = AddStatusReadout(bar, "bits", "0", false);
            entropyLabel = AddStatusReadout(bar, "entropy", "0", false);
            bandwidthLabel = AddStatusReadout(bar, "bandwidth", "0", false);
            computeCreditsLabel = AddStatusReadout(bar, "compute credits", "TODO", false);

            VisualElement spacer = new();
            spacer.AddToClassList("status-spacer");
            bar.Add(spacer);

            seedLabel = new("--");
            seedLabel.AddToClassList("seed-value");
            bar.Add(seedLabel);
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
            if (combatManager.PlayerState == null)
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
            SaveData data = saveService.Load();
            RunConfig config = runManager.CurrentConfig;
            waveLabel.text = runManager.CurrentWaveNumber.ToString();
            phaseLabel.text = PhaseText(combatManager.CurrentPhase);
            bitsLabel.text = runManager.Bits.ToString();
            entropyLabel.text = FormatWalletWithAccrual(data.entropyBalance, runManager.AccruedEntropy);
            bandwidthLabel.text = FormatWalletWithAccrual(data.standardPullCurrency, runManager.AccruedBandwidth);
            computeCreditsLabel.text = "TODO";
            seedLabel.text = $"seed {config?.RunSeed ?? 0}";
        }

        private void RefreshPlayer()
        {
            playerPanel.Clear();
            combatantElements.Remove(combatManager.PlayerState);
            combatantElements[combatManager.PlayerState] = playerPanel;
            playerPanel.Add(PanelHeader(DisplayName(runManager.CurrentConfig.Distro), "htop"));
            ApplyAccent(playerPanel);

            CombatantState state = combatManager.PlayerState;
            DetectBeneficialResourceFeedback(state, playerPanel);
            playerPanel.Add(MeterBlock("uptime", state.CurrentUptime, state.MaxUptime, MeterTone.Uptime));
            playerPanel.Add(MeterBlock("shield", state.Shield, Mathf.Max(1, state.Shield), state.Shield > 0 ? MeterTone.Beneficial : MeterTone.Muted));
            playerPanel.Add(CycleBlock(state.Cycles, state.MaxCycles));

            Label ram = new($"ram hand cap {combatManager.HandController.Cards.Count}/{combatManager.HandController.RamCapacity}");
            ram.AddToClassList("ram-note");
            playerPanel.Add(ram);
            playerPanel.Add(StatusBlock(state, true));
        }

        private void RefreshTracks()
        {
            queueChipElements.Clear();
            stackChipElements.Clear();
            FillCardStrip(interpreterStrip, combatManager.InterpreterQueue.Cards, "queue empty");
            FillCardStrip(lazyStackPile, combatManager.LazyStack.Cards, "stack empty");
            tokenArea.Clear();
            tokenArea.Add(EmptyState("no goroutines or tokens"));
        }

        private void RefreshEnemies()
        {
            enemyRow.Clear();
            combatantElements.Clear();
            if (combatManager.PlayerState != null)
            {
                combatantElements[combatManager.PlayerState] = playerPanel;
            }

            for (int i = 0; i < combatManager.Enemies.Count; i++)
            {
                int index = i;
                EnemyInstance enemy = combatManager.Enemies[i];
                Button card = new(() => combatManager.SelectEnemy(index));
                card.text = string.Empty;
                card.AddToClassList("enemy-card");
                bool highlighted = combatManager.PendingTargetCard != null || combatManager.SelectedEnemyIndex == index;
                if (highlighted)
                {
                    card.AddToClassList("enemy-card-targeted");
                }

                Label name = new(enemy.Name);
                name.AddToClassList("enemy-name");
                card.Add(name);
                combatantElements[enemy.State] = card;
                DetectBeneficialResourceFeedback(enemy.State, card);
                card.Add(MeterBlock("uptime", enemy.CurrentUptime, enemy.MaxUptime, MeterTone.Uptime));
                if (enemy.State.Shield > 0)
                {
                    Label shield = new($"# shield {enemy.State.Shield}");
                    shield.AddToClassList("enemy-shield");
                    card.Add(shield);
                }

                card.Add(StatusPipRow(enemy.State));
                card.Add(IntentPanel(enemy.CurrentIntent));
                enemyRow.Add(card);
            }
        }

        private void RefreshHand()
        {
            handRow.Clear();
            handCardElements.Clear();
            HashSet<CardInstance> currentHandCards = new();
            IReadOnlyList<CardInstance> hand = combatManager.HandController.Cards;
            for (int i = 0; i < hand.Count; i++)
            {
                CardInstance card = hand[i];
                currentHandCards.Add(card);
                VisualElement face = CardFace(card);
                handCardElements[card] = face;
                if (!knownHandCards.Contains(card))
                {
                    PlayElementBeat(face, "hand-card-drawn", 260);
                }

                handRow.Add(face);
            }

            knownHandCards.Clear();
            foreach (CardInstance card in currentHandCards)
            {
                knownHandCards.Add(card);
            }
        }

        private void RefreshTurnPanel()
        {
            turnResourceGrid.Clear();
            CombatantState state = combatManager.PlayerState;
            IReadOnlyList<CardInstance> hand = combatManager.HandController.Cards;
            turnResourceGrid.Add(TurnStat("cycles", $"{state.Cycles}/{state.MaxCycles}"));
            turnResourceGrid.Add(TurnStat("hand", $"{hand.Count}/{combatManager.HandController.RamCapacity}"));
            turnResourceGrid.Add(TurnStat("draw", combatManager.DeckController.DrawPile.Count.ToString()));
            turnResourceGrid.Add(TurnStat("discard", combatManager.DeckController.DiscardPile.Count.ToString()));
            turnResourceGrid.Add(TurnStat("exhaust", combatManager.DeckController.ExhaustPile.Count.ToString()));
        }

        private void RefreshOverlay()
        {
            if (overlay == null)
            {
                return;
            }

            overlay.Clear();
            if (combatManager.RunLost)
            {
                SettleRunRewardsIfNeeded();
                overlay.style.display = DisplayStyle.Flex;
                overlay.Add(OverlayTitle("kernel panic"));
                overlay.Add(OverlayLine("fatal: player uptime reached 0"));
                overlay.Add(OverlayLine($"wave {runManager.CurrentWaveNumber} reached  seed {runManager.CurrentConfig?.RunSeed ?? 0}"));
                overlay.Add(OverlayLine($"waves cleared: {runManager.WavesCleared}"));
                overlay.Add(OverlayLine($"+{runManager.AccruedBandwidth} bandwidth"));
                overlay.Add(OverlayLine($"+{runManager.AccruedEntropy} entropy"));
                Button returnButton = new(SceneLoader.LoadMainMenu) { text = "> return to menu" };
                returnButton.AddToClassList("primary-action");
                overlay.Add(returnButton);
                return;
            }

            if (combatManager.AwaitingWaveContinue)
            {
                if (!runManager.RepositoryVisitActive)
                {
                    runManager.GenerateRepositoryOffers(cardDatabase, languageDeckDatabase);
                    return;
                }

                overlay.style.display = DisplayStyle.Flex;
                overlay.Add(BuildRepositoryView());
                return;
            }

            overlay.style.display = DisplayStyle.None;
        }

        private void SettleRunRewardsIfNeeded()
        {
            if (runManager == null || saveService == null)
            {
                return;
            }

            if (!runManager.TrySettleRunRewards(out int bandwidth, out int entropy))
            {
                return;
            }

            SaveData data = saveService.Load();
            data.standardPullCurrency = Math.Max(0, data.standardPullCurrency) + bandwidth;
            data.entropyBalance = Math.Max(0, data.entropyBalance) + entropy;
            saveService.Save(data);
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
            if (runManager.RepositoryOffers.Count == 0)
            {
                offers.Add(EmptyState("repository clean: no offers left this visit"));
            }
            else
            {
                for (int i = 0; i < runManager.RepositoryOffers.Count; i++)
                {
                    offers.Add(RepositoryOfferTile(runManager.RepositoryOffers[i]));
                }
            }

            body.Add(offers);
            body.Add(RemoveCardBlock());
            repo.Add(body);

            VisualElement footer = new();
            footer.AddToClassList("repository-footer");
            Button reroll = new(() => runManager.RerollRepositoryOffers(cardDatabase, languageDeckDatabase))
            {
                text = $"apt update - reroll  ({runManager.RerollCost} bits)"
            };
            reroll.AddToClassList("primary-action");
            reroll.AddToClassList("repository-footer-action");
            reroll.EnableInClassList("repo-unaffordable", runManager.Bits < runManager.RerollCost);
            reroll.SetEnabled(runManager.Bits >= runManager.RerollCost);
            footer.Add(reroll);

            Button leave = new(() => combatManager.ContinueToNextWave()) { text = "> boot next-wave" };
            leave.AddToClassList("primary-action");
            leave.AddToClassList("repository-leave-action");
            VisualElement leaveBlock = new();
            leaveBlock.AddToClassList("repository-leave-block");
            leaveBlock.Add(leave);
            Label banked = new($"{runManager.Bits} bits banked");
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
            Label title = new($"repository :: wave {runManager.CurrentWaveNumber - 1} cleared");
            title.AddToClassList("overlay-title");
            Label subtitle = new($"$ apt browse    {runManager.RepositoryOffers.Count}/{CombatTuning.ShopSize} offers");
            subtitle.AddToClassList("overlay-line");
            copy.Add(title);
            copy.Add(subtitle);

            VisualElement wallet = new();
            wallet.AddToClassList("repository-wallet");
            Label walletLabel = new("bits wallet");
            walletLabel.AddToClassList("status-label");
            Label walletValue = new($"◆ {runManager.Bits}");
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
            bool unavailable = offer.Sold || runManager.Bits < offer.Price;
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

            Button buy = new(() => runManager.BuyOffer(offer, combatManager.PlayerState))
            {
                text = offer.Sold ? "installed" : OfferCommandText(offer)
            };
            buy.AddToClassList("repository-action");
            buy.SetEnabled(!offer.Sold && runManager.Bits >= offer.Price);
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
            for (int i = 0; i < runManager.RunDeck.Count; i++)
            {
                CardInstance card = runManager.RunDeck[i];
                Button remove = new(() => runManager.RemoveCard(card))
                {
                    text = $"apt remove {DisplayName(card.Definition)}"
                };
                remove.AddToClassList("repo-remove-button");
                remove.EnableInClassList("repo-unaffordable", runManager.Bits < CombatTuning.RemoveCardCost || runManager.RunDeck.Count <= 1);
                remove.SetEnabled(runManager.Bits >= CombatTuning.RemoveCardCost && runManager.RunDeck.Count > 1);
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
            if (combatManager.PendingTargetCard == card)
            {
                button.AddToClassList("hand-card-selected");
            }

            if (CombatManager.GetCardCost(card) > combatManager.PlayerState.Cycles)
            {
                button.AddToClassList("hand-card-unaffordable");
            }

            PopulateCardFace(button, card);
            return button;
        }

        private static VisualElement CardFaceView(CardInstance card)
        {
            VisualElement face = new();
            face.AddToClassList("hand-card");
            PopulateCardFace(face, card);
            return face;
        }

        private static void PopulateCardFace(VisualElement target, CardInstance card)
        {
            string cardRarityClass = CardRarityClass(card.Definition.Rarity);
            if (!string.IsNullOrEmpty(cardRarityClass))
            {
                target.AddToClassList(cardRarityClass);
            }

            VisualElement top = new();
            top.AddToClassList("card-top-row");
            Label cost = new(CombatManager.GetCardCost(card).ToString());
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
            Label value = new($"{IntentIcon(intent.Kind)} {intent.ValueText}");
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
                if (target == interpreterStrip)
                {
                    queueChipElements[cards[i]] = chip;
                }
                else if (target == lazyStackPile)
                {
                    stackChipElements[cards[i]] = chip;
                }

                target.Add(chip);
            }
        }

        private bool PlayCardWithFeedback(CardInstance card)
        {
            int cyclesBefore = combatManager.PlayerState == null ? 0 : combatManager.PlayerState.Cycles;
            bool played = combatManager.PlayCard(card);
            if (played)
            {
                int spent = Mathf.Max(0, cyclesBefore - (combatManager.PlayerState == null ? cyclesBefore : combatManager.PlayerState.Cycles));
                if (spent > 0)
                {
                    PlayElementBeat(turnResourceGrid, "cycles-spent-beat", 220);
                }

                return true;
            }

            if (combatManager.PendingTargetCard != card && handCardElements.TryGetValue(card, out VisualElement face))
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

            VisualElement source = handCardElements.TryGetValue(payload.Card, out VisualElement cardElement) ? cardElement : null;
            VisualElement destination = payload.Track switch
            {
                ResolutionTrack.InterpreterQueue => interpreterStrip,
                ResolutionTrack.LazyStack => lazyStackPile,
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
                int delay = UIPreferences.ReducedMotion ? 0 : Mathf.Min(queueCascadeIndex * 120, 600);
                queueCascadeIndex++;
                ScheduleFeedback(() =>
                {
                    VisualElement chip = queueChipElements.TryGetValue(payload.Card, out VisualElement queuedChip) ? queuedChip : interpreterStrip;
                    PlayElementBeat(chip, "queue-chip-resolve", 260);
                    FlyCardGhost(payload.Card, chip, turnResourceGrid, "feedback-card-discard");
                }, delay);
                return;
            }

            if (payload.Track == ResolutionTrack.LazyStack)
            {
                VisualElement chip = stackChipElements.TryGetValue(payload.Card, out VisualElement stackChip) ? stackChip : lazyStackPile;
                PlayElementBeat(chip, "stack-chip-resolve", 320);
                FlyCardGhost(payload.Card, chip, turnResourceGrid, "feedback-card-discard");
            }
        }

        private void HandlePhaseChanged(PhaseChangedEvent payload)
        {
            if (payload.NextPhase == TurnPhase.Interpret)
            {
                queueCascadeIndex = 0;
            }

            ScheduleFeedback(() => PlayElementBeat(phaseLabel, "phase-pulse", 260), 0);
        }

        private void HandleDamageDealt(DamageDealtEvent payload)
        {
            ScheduleFeedback(() =>
            {
                bool isPlayer = payload.Target == combatManager.PlayerState;
                VisualElement target = CombatantElement(payload.Target);
                string text = payload.Amount <= 0 ? "blocked" : $"-{Mathf.RoundToInt(payload.Amount)}";
                string tone = payload.Amount <= 0 ? "float-muted" : "float-damage";
                if (payload.Amount >= 8)
                {
                    tone += " float-large";
                }

                SpawnFloatingText(target, text, tone);
                PlayElementBeat(target, payload.Amount <= 0 ? "feedback-block" : "feedback-hit", 240);
                if (isPlayer && payload.Amount > 0)
                {
                    FlashDamageVignette();
                }
            }, 0);
        }

        private void HandleCombatantDefeated(CombatantDefeatedEvent payload)
        {
            ScheduleFeedback(() =>
            {
                VisualElement target = CombatantElement(payload.Combatant);
                SpawnFloatingText(target, "killed", "float-kill float-large");
                SpawnDeathGhost(target);
                PlayElementBeat(target, "feedback-killed", 260);
            }, 0);
        }

        private void HandleEnemyWouldAct(EnemyWouldActEvent payload)
        {
            ScheduleFeedback(() => PlayElementBeat(CombatantElement(payload.Enemy?.State), "enemy-acting", 260), 0);
        }

        private void HandlePlayerDamaged(PlayerDamagedEvent payload)
        {
            if (payload.Amount > 0)
            {
                ScheduleFeedback(FlashDamageVignette, 0);
            }
        }

        private void HandleStatusApplied(StatusAppliedEvent payload)
        {
            ScheduleFeedback(() =>
            {
                StatusDescriptor descriptor = StatusEffectController.GetDescriptor(payload.StatusType);
                SpawnFloatingText(CombatantElement(payload.Target), $"{descriptor.IconKey} x{payload.Stacks}", descriptor.IsBeneficial ? "float-heal" : "float-status");
                PlayElementBeat(CombatantElement(payload.Target), descriptor.IsBeneficial ? "feedback-boost" : "feedback-status", 220);
            }, 0);
        }

        private void DetectBeneficialResourceFeedback(CombatantState state, VisualElement element)
        {
            if (state == null || element == null)
            {
                return;
            }

            if (previousUptime.TryGetValue(state, out int oldUptime) && state.CurrentUptime > oldUptime)
            {
                SpawnFloatingText(element, $"+{state.CurrentUptime - oldUptime}", "float-heal");
                PlayElementBeat(element, "feedback-boost", 220);
            }

            if (previousShield.TryGetValue(state, out int oldShield) && state.Shield > oldShield)
            {
                SpawnFloatingText(element, $"+{state.Shield - oldShield} shield", "float-heal");
                PlayElementBeat(element, "feedback-block", 220);
            }

            previousUptime[state] = state.CurrentUptime;
            previousShield[state] = state.Shield;
        }

        private VisualElement CombatantElement(CombatantState state)
        {
            if (state != null && combatantElements.TryGetValue(state, out VisualElement element))
            {
                return element;
            }

            return state == combatManager.PlayerState ? playerPanel : enemyRow;
        }

        private VisualElement FirstTargetElement(CardInstance card)
        {
            if (card?.TargetSnapshot == null)
            {
                return enemyRow;
            }

            for (int i = 0; i < card.TargetSnapshot.Count; i++)
            {
                VisualElement element = CombatantElement(card.TargetSnapshot[i]);
                if (element != null)
                {
                    return element;
                }
            }

            return enemyRow;
        }

        private void PlayElementBeat(VisualElement element, string className, int durationMs)
        {
            if (element == null || string.IsNullOrWhiteSpace(className))
            {
                return;
            }

            element.RemoveFromClassList(className);
            element.AddToClassList(className);
            ScheduleFeedback(() => element.RemoveFromClassList(className), UIPreferences.ReducedMotion ? 80 : durationMs);
        }

        private void SpawnFloatingText(VisualElement anchor, string text, string className)
        {
            if (feedbackLayer == null || anchor == null)
            {
                return;
            }

            Rect anchorRect = anchor.worldBound;
            Rect rootRect = root.worldBound;
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

            label.style.left = anchorRect.center.x - rootRect.x - 30f;
            label.style.top = anchorRect.center.y - rootRect.y - 16f;
            feedbackLayer.Add(label);

            if (UIPreferences.ReducedMotion)
            {
                label.AddToClassList("float-static");
                ScheduleFeedback(() => label.RemoveFromHierarchy(), 420);
                return;
            }

            ScheduleFeedback(() =>
            {
                label.style.top = anchorRect.center.y - rootRect.y - 42f;
                label.style.opacity = 0f;
            }, 20);
            ScheduleFeedback(() => label.RemoveFromHierarchy(), 520);
        }

        private void FlyCardGhost(CardInstance card, VisualElement source, VisualElement destination, string className)
        {
            if (feedbackLayer == null || card == null)
            {
                return;
            }

            Rect rootRect = root.worldBound;
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
            feedbackLayer.Add(ghost);

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

        private void SpawnDeathGhost(VisualElement source)
        {
            if (feedbackLayer == null || source == null)
            {
                return;
            }

            Rect rootRect = root.worldBound;
            Rect sourceRect = source.worldBound;
            VisualElement ghost = new();
            ghost.AddToClassList("death-ghost");
            ghost.style.position = Position.Absolute;
            ghost.style.left = sourceRect.x - rootRect.x;
            ghost.style.top = sourceRect.y - rootRect.y;
            ghost.style.width = sourceRect.width;
            ghost.style.height = sourceRect.height;
            feedbackLayer.Add(ghost);

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

        private void FlashDamageVignette()
        {
            if (damageVignette == null)
            {
                return;
            }

            PlayElementBeat(damageVignette, "damage-vignette-on", UIPreferences.ReducedMotion ? 120 : 240);
        }

        private void ScheduleFeedback(Action action, int delayMs)
        {
            if (action == null || root == null)
            {
                return;
            }

            root.schedule.Execute(action).StartingIn(Mathf.Max(0, delayMs));
        }

        private void HandleCombatLog(string message)
        {
            if (logLabel != null)
            {
                logLabel.text = message ?? string.Empty;
            }
        }

        private VisualElement CardChip(CardInstance card)
        {
            VisualElement chip = new();
            chip.AddToClassList("card-chip");
            chip.AddToClassList(LanguageClass(card.Definition.Language));
            Label name = new(DisplayName(card.Definition));
            name.AddToClassList("card-chip-name");
            Label meta = new($"{CombatManager.GetCardCost(card)}c / {TrackText(card.Definition.ResolutionTrack)}");
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
            element.style.borderTopColor = distroAccent;
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

        private static string IntentIcon(EnemyIntentKind kind)
        {
            return kind switch
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
                EnemyIntentKind.Special => "special",
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
