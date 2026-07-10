using KernelPanic.Core;
using KernelPanic.Data;
using KernelPanic.Run;
using KernelPanic.Meta;
using KernelPanic.UI;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Owns the combat turn phase state machine and coordinates combat lifecycle transitions.
    /// </summary>
    public sealed class CombatManager : MonoBehaviour
    {
        [SerializeField] private TurnPhase currentPhase = TurnPhase.Boot;

        private readonly DeckController _deckController = new();
        private readonly InterpreterQueue _interpreterQueue = new();
        private readonly LazyStack _lazyStack = new();
        private readonly NativeTrack _nativeTrack = new();
        private readonly StatusEffectController _statusEffects = new();
        private readonly DamagePipeline _damagePipeline = new();
        private readonly List<EnemyInstance> _enemies = new();
        private readonly HashSet<string> _loggedEffectTodos = new();
        private readonly List<CardDefinition> _generatedCardPool = new();
        private readonly List<DelayedCombatEffect> _delayedEffects = new();
        private RunManager _runManager;
        private RunConfig _runConfig;
        private HandController _handController;
        private CombatantState _playerState;
        private int _selectedEnemyIndex = -1;
        private CardInstance _pendingTargetCard;
        private bool _awaitingWaveContinue;
        private bool _runLost;
        private bool _skipNextAllocateDraw;
        private bool _ubuntuEmptyHandRefillUsed;
        private int _fedoraCardsDiscountedThisTurn;
        private int _fedoraCrashChance = CombatTuning.FedoraBleedingEdgeBaseCrashChance;
        private int _cardsPlayedThisTurn;
        private int _cardsPlayedThisWave;
        private int _cCardsPlayedThisTurn;
        private int _turnNumberThisWave;
        private bool _packageShieldBonusTriggeredThisTurn;
        private bool _packageTimeshiftTriggeredThisWave;
        private bool _packageInterpreterQueueShieldTriggeredThisWave;
        private bool _packageNativeDamageTriggeredThisWave;
        private int _javaCardsPlayedThisCombat;
        private int _javaCardsDiscountThisTurn;
        private int _rawhideBonusCharges;
        private int _queuedRepeatCharges;
        private int _nextTurnCycleBonus;
        private int _nextRacePairId = 1;
        private int _activeKernelRamPenalty;
        private Coroutine _phaseCoroutine;

        public TurnPhase CurrentPhase => currentPhase;
        public RunConfig RunConfig => _runConfig;
        public DeckController DeckController => _deckController;
        public HandController HandController => _handController;
        public CombatantState PlayerState => _playerState;
        public InterpreterQueue InterpreterQueue => _interpreterQueue;
        public LazyStack LazyStack => _lazyStack;
        public NativeTrack NativeTrack => _nativeTrack;
        public DamagePipeline DamagePipeline => _damagePipeline;
        public IReadOnlyList<EnemyInstance> Enemies => _enemies;
        public int SelectedEnemyIndex => _selectedEnemyIndex;
        public CardInstance PendingTargetCard => _pendingTargetCard;
        public bool AwaitingWaveContinue => _awaitingWaveContinue;
        public bool RunLost => _runLost;
        public int CurrentWaveNumber => _runManager == null ? 1 : _runManager.CurrentWaveNumber;
        public int ActiveKernelRamPenalty => _activeKernelRamPenalty;

        public event Action StateChanged;
        public event Action<string> CombatLog;

        private void Awake()
        {
            _runManager = GetComponent<RunManager>();
            _damagePipeline.ResistanceResolver = ResolveDamageResistance;
        }

        private void OnEnable()
        {
            GameEvents.DamageDealt += HandlePackageDamageDealt;
        }

        private void OnDisable()
        {
            GameEvents.DamageDealt -= HandlePackageDamageDealt;
        }

        public void StartCombat()
        {
            if (_runConfig == null)
            {
                Debug.LogWarning("CombatManager.StartCombat called without a RunConfig.");
                return;
            }

            SetPhase(TurnPhase.Boot);
        }

        public void StartCombat(RunConfig config)
        {
            _runConfig = config;
            StartCombat();
        }

        public void AdvancePhase()
        {
            if (IsCombatPaused)
            {
                return;
            }

            TurnPhase nextPhase = GetNextPhase(currentPhase);
            SetPhase(nextPhase);
        }

        public void EndPlayerTurn()
        {
            if (currentPhase == TurnPhase.Execute && !IsCombatPaused)
            {
                _pendingTargetCard = null;
                _selectedEnemyIndex = -1;
                SetPhase(TurnPhase.Interpret);
            }
        }

        public void ContinueToNextWave()
        {
            if (!_awaitingWaveContinue || _runLost)
            {
                return;
            }

            _awaitingWaveContinue = false;
            DecayJavaWarmupForNextWave();
            StartCoroutine(ContinueToNextWaveSequenced());
        }

        private IEnumerator ContinueToNextWaveSequenced()
        {
            yield return StartWaveSequenced(preservePlayerUptime: true);
            SetPhase(TurnPhase.Allocate);
        }

        public void SelectEnemy(int index)
        {
            if (IsCombatPaused)
            {
                return;
            }

            if (index < 0 || index >= _enemies.Count)
            {
                _selectedEnemyIndex = -1;
                StateChanged?.Invoke();
                return;
            }

            _selectedEnemyIndex = index;
            if (_pendingTargetCard != null)
            {
                CardInstance card = _pendingTargetCard;
                _pendingTargetCard = null;
                PlayCard(card);
                return;
            }

            StateChanged?.Invoke();
        }

        public bool PlayCard(CardInstance card)
        {
            if (currentPhase != TurnPhase.Execute || IsCombatPaused || card == null || _handController == null || _playerState == null)
            {
                return false;
            }

            if (card.Definition.IsToken)
            {
                Log($"{GetCardName(card)} is unplayable");
                return false;
            }

            if (card.IsBroken)
            {
                Log($"{GetCardName(card)} is corrupted");
                return false;
            }

            if (card.IsLocked)
            {
                Log($"{GetCardName(card)} is locked; pay {EnemyArchetypeCatalog.DrmUnlockCycleCost} Cycle to unlock");
                return false;
            }

            bool fedoraBonus = CanApplyFedoraBonus(card);
            int cost = GetEffectiveCardCost(card);
            if (fedoraBonus)
            {
                cost = Mathf.Max(0, cost - 1);
            }

            if (_playerState.Cycles < cost)
            {
                Log($"not enough cycles for {GetCardName(card)}");
                return false;
            }

            if (RequiresTarget(card) && _selectedEnemyIndex < 0)
            {
                _pendingTargetCard = card;
                Log($"select target for {GetCardName(card)}");
                StateChanged?.Invoke();
                return false;
            }

            if (!_handController.Remove(card))
            {
                return false;
            }

            _playerState.Cycles -= cost;
            ResolutionTrack track = card.Definition.ResolutionTrack;
            card.MarkPlayedThisTurn(_cardsPlayedThisTurn == 0);
            _cardsPlayedThisTurn++;
            _cardsPlayedThisWave++;
            if (IsDistro("arch"))
            {
                _playerState.ArchBtwStacks++;
            }

            _playerState.HasPreviousPlayedCardLanguage = _playerState.HasLastPlayedCardLanguage;
            if (_playerState.HasLastPlayedCardLanguage)
            {
                _playerState.PreviousPlayedCardLanguage = _playerState.LastPlayedCardLanguage;
            }

            _playerState.LastPlayedCardLanguage = card.Definition.Language;
            _playerState.HasLastPlayedCardLanguage = true;
            if (card.Definition.Language == Language.Java)
            {
                _javaCardsPlayedThisCombat++;
            }

            GameEvents.RaiseCardPlayed(new CardPlayedEvent(card, track));
            ApplyTelemetryCardPlayed();
            LogPlayIntent(card);
            card.SetTargetSnapshot(CaptureTargets(card));

            if (fedoraBonus)
            {
                bool firstFedoraBonusThisTurn = _fedoraCardsDiscountedThisTurn == 0;
                _fedoraCardsDiscountedThisTurn++;
                bool rawhideChargeUsed = _rawhideBonusCharges > 0;
                if (rawhideChargeUsed)
                {
                    _rawhideBonusCharges--;
                }

                if (!IsCrashImmune(card) && RandomRoll.RollRange(1, 100, new RollContext(_playerState)) <= _fedoraCrashChance)
                {
                    _fedoraCrashChance = CombatTuning.FedoraBleedingEdgeBaseCrashChance;
                    if (IsDistro("fedora") && _runConfig.DistroVersion >= 4)
                    {
                        _playerState.Cycles += 1;
                    }

                    _deckController.Discard(card);
                    _pendingTargetCard = null;
                    _selectedEnemyIndex = -1;
                    Log($"{GetCardName(card)} crashed under bleeding edge");
                    StateChanged?.Invoke();
                    return true;
                }

                _fedoraCrashChance = Mathf.Min(
                    CombatTuning.FedoraBleedingEdgeMaxCrashChance,
                    _fedoraCrashChance + CombatTuning.FedoraBleedingEdgeCrashChanceStep);
                card.MarkFedoraNonCrashBonus();
                _playerState.DamageMultiplierPercent = _runConfig.DistroVersion >= 2 ? 175 : 150;
                ApplyFedoraGrowth(card, rawhideChargeUsed, firstFedoraBonusThisTurn);
            }

            if (card.Definition.Language == Language.C && _cCardsPlayedThisTurn == 0 && HasPackageEffect(PackageEffectKind.FirstCThisTurnDamageMultiplier, out PackageEffectData cPackageEffect))
            {
                _playerState.CurrentCardDamageMultiplierPercent = Mathf.Max(100, cPackageEffect.Amount);
            }

            switch (track)
            {
                case ResolutionTrack.InterpreterQueue:
                    card.MarkQueued();
                    _interpreterQueue.Enqueue(card);
                    break;
                case ResolutionTrack.LazyStack:
                    _lazyStack.Enqueue(card);
                    break;
                default:
                    ExecuteCardEffects(card, ResolveTargets(card));
                    ResolveCardRiders(card);
                    _deckController.Discard(card);
                    GameEvents.RaiseCardResolved(new CardResolvedEvent(card, ResolutionTrack.Native));
                    _selectedEnemyIndex = -1;
                    break;
            }

            _playerState.DamageMultiplierPercent = 100;
            _playerState.CurrentCardDamageMultiplierPercent = 100;
            if (card.Definition.Language == Language.C)
            {
                _cCardsPlayedThisTurn++;
            }

            ApplyPackageCardPlayedEffects(card);

            _pendingTargetCard = null;
            TryApplyUbuntuEmptyHandRefill();
            StateChanged?.Invoke();
            return true;
        }

        public static int GetCardCost(CardInstance card)
        {
            if (card?.Definition == null)
            {
                return 0;
            }

            return Mathf.Max(0, card.Definition.CycleCost + card.PermanentCostDelta + card.TemporaryCostDelta);
        }

        public int GetEffectiveCardCost(CardInstance card)
        {
            int cost = GetCardCost(card);
            if (card?.Definition != null && card.Definition.Language == Language.Java)
            {
                cost -= _javaCardsPlayedThisCombat + _javaCardsDiscountThisTurn;
            }

            cost = ApplyPackageCostRules(card, cost);
            return Mathf.Max(0, cost);
        }

        public bool RequiresTarget(CardInstance card)
        {
            return card?.Definition != null && CardEffectFactory.RequiresSingleTarget(card.Definition) && LivingEnemyStates().Count > 0;
        }

        public void LogEffectTodo(CardInstance card, string message)
        {
            string cardId = card?.Definition == null ? string.Empty : card.Definition.Id;
            string key = $"{cardId}:{message}";
            if (_loggedEffectTodos.Add(key))
            {
                Log(message);
            }
        }

        public bool TryCorruptRandomHandCard(string sourceLabel)
        {
            CardInstance card = RandomHandCard(cardInstance => cardInstance != null && !cardInstance.IsBroken);
            if (card == null)
            {
                Log($"{sourceLabel}: no card available to corrupt");
                return false;
            }

            card.IsBroken = true;
            Log($"{sourceLabel}: corrupted {GetCardName(card)}");
            StateChanged?.Invoke();
            return true;
        }

        public bool TryUnlockCard(CardInstance card)
        {
            if (currentPhase != TurnPhase.Execute || card == null || !card.IsLocked || _playerState == null)
            {
                return false;
            }

            int cost = EnemyArchetypeCatalog.DrmUnlockCycleCost;
            if (_playerState.Cycles < cost)
            {
                Log($"not enough cycles to unlock {GetCardName(card)}");
                return false;
            }

            _playerState.Cycles -= cost;
            card.IsLocked = false;
            Log($"unlocked {GetCardName(card)}");
            StateChanged?.Invoke();
            return true;
        }

        public void ReportEffectResult(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Log(message);
            }
        }

        public void AddNextTurnCycleBonus(int amount)
        {
            _nextTurnCycleBonus += Mathf.Max(0, amount);
        }

        public void AddQueuedRepeatCharges(int amount)
        {
            _queuedRepeatCharges += Mathf.Max(0, amount);
            if (amount > 0)
            {
                ReportEffectResult($"next {amount} queued card(s) resolve twice");
            }
        }

        public void AddJavaCostDiscountThisTurn(int amount)
        {
            int safeAmount = Mathf.Max(0, amount);
            if (safeAmount <= 0)
            {
                return;
            }

            _javaCardsDiscountThisTurn += safeAmount;
            ReportEffectResult($"Java cards cost -{safeAmount} this turn");
        }

        public int ConsumeQueuedResolutionCount(CardInstance card)
        {
            if (_queuedRepeatCharges <= 0 || card == null)
            {
                return 1;
            }

            _queuedRepeatCharges--;
            return 2;
        }

        public void GrantRawhideBonus(int charges)
        {
            _rawhideBonusCharges += Mathf.Max(0, charges);
        }

        public void AddIncomingAttackHalfCharges(int charges)
        {
            if (_playerState == null)
            {
                return;
            }

            _playerState.IncomingAttackHalfCharges += Mathf.Max(0, charges);
            ReportEffectResult($"next {charges} enemy attack(s) halved");
        }

        public void HandleCardExhausted(CardInstance card)
        {
            if (card == null || !HasPackageEffect(PackageEffectKind.ExhaustShield, out PackageEffectData effect))
            {
                return;
            }

            GrantPlayerShield(Mathf.Max(0, effect.Amount), "logrotate");
        }

        public void ScheduleUpdateManagerRepeat(int damageAmount)
        {
            _delayedEffects.Add(DelayedCombatEffect.UpdateManagerRepeat(damageAmount, 2));
            ReportEffectResult("update manager scheduled repeat");
        }

        public void ScheduleTimeshiftRestore(int uptime)
        {
            _delayedEffects.Add(DelayedCombatEffect.TimeshiftRestore(uptime, 2));
            ReportEffectResult($"timeshift snapshot recorded {uptime} uptime");
        }

        public bool TryCreateGeneratedCardById(string id, out CardInstance generatedCard)
        {
            generatedCard = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            IReadOnlyList<CardDefinition> source = _generatedCardPool.Count > 0 ? _generatedCardPool : _runConfig?.StartingDeck;
            if (source == null)
            {
                return false;
            }

            for (int i = 0; i < source.Count; i++)
            {
                CardDefinition definition = source[i];
                if (definition != null && string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    generatedCard = new CardInstance(definition);
                    return true;
                }
            }

            return false;
        }

        public void SetGeneratedCardPool(IEnumerable<CardDefinition> cardPool)
        {
            _generatedCardPool.Clear();
            if (cardPool == null)
            {
                return;
            }

            foreach (CardDefinition card in cardPool)
            {
                if (card != null)
                {
                    _generatedCardPool.Add(card);
                }
            }
        }

        public bool TryCreateGeneratedCard(Language language, Rarity rarity, out CardInstance generatedCard)
        {
            generatedCard = null;

            List<CardDefinition> pool = new();
            HashSet<string> seenIds = new();
            IReadOnlyList<CardDefinition> source = _generatedCardPool.Count > 0 ? _generatedCardPool : _runConfig?.StartingDeck;
            if (source == null)
            {
                return false;
            }

            for (int i = 0; i < source.Count; i++)
            {
                CardDefinition definition = source[i];
                if (definition == null || definition.IsToken || definition.IsRunOnly || definition.Language != language || definition.Rarity != rarity)
                {
                    continue;
                }

                if (seenIds.Add(definition.Id))
                {
                    pool.Add(definition);
                }
            }

            if (pool.Count == 0)
            {
                return false;
            }

            int index = RandomRoll.RollRange(0, pool.Count - 1, new RollContext(_playerState));
            CardDefinition selected = pool[index];
            generatedCard = new CardInstance(selected)
            {
                TemporaryCostDelta = -selected.CycleCost
            };
            return true;
        }

        public void DrawCards(int count, string label)
        {
            if (count <= 0)
            {
                return;
            }

            StartCoroutine(DrawCardsToHandSequenced(count, label));
        }

        private void SetPhase(TurnPhase nextPhase)
        {
            if (IsCombatPaused && nextPhase != TurnPhase.Boot)
            {
                return;
            }

            TurnPhase previousPhase = currentPhase;
            currentPhase = nextPhase;
            GameEvents.RaisePhaseChanged(new PhaseChangedEvent(previousPhase, nextPhase));

            if (_phaseCoroutine != null)
            {
                StopCoroutine(_phaseCoroutine);
            }

            _phaseCoroutine = StartCoroutine(EnterPhase(nextPhase));
        }

        private static TurnPhase GetNextPhase(TurnPhase phase)
        {
            return phase switch
            {
                TurnPhase.Boot => TurnPhase.Allocate,
                TurnPhase.Allocate => TurnPhase.Execute,
                TurnPhase.Execute => TurnPhase.Interpret,
                TurnPhase.Interpret => TurnPhase.EnemyProcess,
                TurnPhase.EnemyProcess => TurnPhase.GarbageCollection,
                _ => TurnPhase.Allocate
            };
        }

        private IEnumerator EnterPhase(TurnPhase phase)
        {
            switch (phase)
            {
                case TurnPhase.Boot:
                    yield return BootCombatSequenced();
                    break;
                case TurnPhase.Allocate:
                    yield return AllocateTurnSequenced();
                    break;
                case TurnPhase.Execute:
                    StateChanged?.Invoke();
                    yield break;
                case TurnPhase.Interpret:
                    yield return InterpretQueueSequenced();
                    break;
                case TurnPhase.EnemyProcess:
                    yield return ProcessEnemiesSequenced();
                    break;
                case TurnPhase.GarbageCollection:
                    GarbageCollect();
                    break;
            }

            if (!IsCombatPaused)
            {
                yield return Wait(CombatTuning.PhaseTransitionDelaySeconds);
                AdvancePhase();
            }
        }

        private static IEnumerator Wait(float seconds)
        {
            if (!UIPreferences.ReducedMotion && seconds > 0f)
            {
                yield return new WaitForSeconds(seconds);
            }
        }

        private IEnumerator BootCombatSequenced()
        {
            _runLost = false;
            _awaitingWaveContinue = false;
            _loggedEffectTodos.Clear();
            _ubuntuEmptyHandRefillUsed = false;
            _fedoraCrashChance = CombatTuning.FedoraBleedingEdgeBaseCrashChance;
            _fedoraCardsDiscountedThisTurn = 0;
            _cardsPlayedThisTurn = 0;
            _cardsPlayedThisWave = 0;
            _cCardsPlayedThisTurn = 0;
            _turnNumberThisWave = 0;
            _packageShieldBonusTriggeredThisTurn = false;
            _packageTimeshiftTriggeredThisWave = false;
            _packageInterpreterQueueShieldTriggeredThisWave = false;
            _packageNativeDamageTriggeredThisWave = false;
            _javaCardsPlayedThisCombat = 0;
            _javaCardsDiscountThisTurn = 0;
            _rawhideBonusCharges = 0;
            _queuedRepeatCharges = 0;
            _nextTurnCycleBonus = 0;
            _delayedEffects.Clear();
            RandomRoll.Seed(_runConfig.RunSeed);
            _playerState = new CombatantState(_runManager.EffectiveMaxUptime(), _runManager.EffectiveRam(), _runManager.EffectiveMaxCycles());
            ApplyVersionState(_playerState);
            ApplyPackageState(_playerState);
            yield return StartWaveSequenced(preservePlayerUptime: false);
            Log($"booted {_runConfig.Distro.DisplayName} with {_runManager.RunDeck.Count} cards");
            StateChanged?.Invoke();
        }

        private IEnumerator StartWaveSequenced(bool preservePlayerUptime)
        {
            int carriedUptime = _playerState == null ? 0 : _playerState.CurrentUptime;
            if (!preservePlayerUptime || _playerState == null)
            {
                _playerState = new CombatantState(_runManager.EffectiveMaxUptime(), _runManager.EffectiveRam(), _runManager.EffectiveMaxCycles());
                ApplyVersionState(_playerState);
                ApplyPackageState(_playerState);
            }
            else
            {
                _playerState.MaxUptime = _runManager.EffectiveMaxUptime();
                _playerState.CurrentUptime = Mathf.Clamp(carriedUptime, 1, _playerState.MaxUptime);
                _playerState.Ram = _runManager.EffectiveRam();
                _playerState.MaxCycles = _runManager.EffectiveMaxCycles();
                _playerState.Cycles = 0;
                _playerState.Shield = 0;
                _playerState.IncomingAttackHalfCharges = 0;
                _playerState.IsDefeated = false;
                _playerState.MutableStatuses.Clear();
                ApplyVersionState(_playerState);
                ApplyPackageState(_playerState);
            }

            _handController = new HandController(_playerState.Ram);
            _selectedEnemyIndex = -1;
            _pendingTargetCard = null;
            _cardsPlayedThisWave = 0;
            _turnNumberThisWave = 0;
            _nextRacePairId = 1;
            _packageShieldBonusTriggeredThisTurn = false;
            _packageTimeshiftTriggeredThisWave = false;
            ResetArchWaveState();

            List<CardInstance> startingCards = new();
            for (int i = 0; i < _runManager.RunDeck.Count; i++)
            {
                CardInstance card = _runManager.RunDeck[i];
                if (card != null)
                {
                    card.ResetCombatState();
                    startingCards.Add(card);
                }
            }

            _deckController.Initialize(startingCards);
            _interpreterQueue.Clear();
            _enemies.Clear();
            SpawnStructuralEnemies();
            RecalculateKernelRamPressure();
            PickEnemyIntents();
            StateChanged?.Invoke();
            yield return DrawOpeningHandSequenced();
            ApplyPackageWaveStartEffects();
            _skipNextAllocateDraw = true;
        }

        private IEnumerator AllocateTurnSequenced()
        {
            if (CheckLoss())
            {
                yield break;
            }

            RevivePendingEnemies();
            UnlockHandCards("license check expired");
            _playerState.Cycles = _playerState.MaxCycles;
            _fedoraCardsDiscountedThisTurn = 0;
            _cardsPlayedThisTurn = 0;
            _cCardsPlayedThisTurn = 0;
            _turnNumberThisWave++;
            _packageShieldBonusTriggeredThisTurn = false;
            _javaCardsDiscountThisTurn = 0;
            ApplyPackageTurnStartEffects();
            if (_nextTurnCycleBonus > 0)
            {
                _playerState.Cycles += _nextTurnCycleBonus;
                Log($"deferred cycle grant: +{_nextTurnCycleBonus}");
                _nextTurnCycleBonus = 0;
            }
            _statusEffects.Tick(_playerState, StatusTickTiming.StartOfTurn, _playerState, _damagePipeline);
            StateChanged?.Invoke();
            if (CheckLoss())
            {
                yield break;
            }

            TryApplyUbuntuAptUpdate();

            if (_skipNextAllocateDraw)
            {
                _skipNextAllocateDraw = false;
            }
            else
            {
                yield return DrawForTurnSequenced();
            }

            StateChanged?.Invoke();
        }

        private IEnumerator InterpretQueueSequenced()
        {
            while (_interpreterQueue.TryDequeue(out CardInstance card))
            {
                LogPlayIntent(card);
                int resolutionCount = ConsumeQueuedResolutionCount(card);
                for (int repeatIndex = 0; repeatIndex < resolutionCount; repeatIndex++)
                {
                    ExecuteCardEffects(card, ResolveQueuedTargets(card));
                    if (CheckWinOrLoss())
                    {
                        yield break;
                    }
                }

                ResolveCardRiders(card);
                _deckController.Discard(card);
                GameEvents.RaiseCardResolved(new CardResolvedEvent(card, ResolutionTrack.InterpreterQueue));
                StateChanged?.Invoke();
                if (CheckWinOrLoss())
                {
                    yield break;
                }

                yield return Wait(CombatTuning.QueueCardResolveDelaySeconds);
            }

            CheckWinOrLoss();
            StateChanged?.Invoke();
        }

        private IEnumerator ProcessEnemiesSequenced()
        {
            List<EnemyInstance> actors = BuildEnemyActionOrder();
            for (int i = 0; i < actors.Count; i++)
            {
                EnemyInstance enemy = actors[i];
                if (enemy.State.IsDefeated)
                {
                    continue;
                }

                _statusEffects.Tick(enemy.State, StatusTickTiming.StartOfTurn, _playerState, _damagePipeline);
                RemoveDefeatedEnemies();
                if (CheckWinOrLoss())
                {
                    yield break;
                }

                if (enemy.State.IsDefeated)
                {
                    continue;
                }

                Log($"{enemy.Name} executes {enemy.DisplayIntent.DisplayText}");
                GameEvents.RaiseEnemyWouldAct(new EnemyWouldActEvent(enemy));
                StateChanged?.Invoke();
                yield return Wait(CombatTuning.EnemyTelegraphDelaySeconds);

                ExecuteEnemyIntent(enemy, _enemies.IndexOf(enemy));
                ResolvePassiveEnemyAfterAction(enemy);
                if (!enemy.State.IsDefeated)
                {
                    enemy.MarkTurnSurvived();
                }

                GameEvents.RaiseEnemyActed(new EnemyActedEvent(enemy, enemy.CurrentIntent));
                RemoveDefeatedEnemies();
                StateChanged?.Invoke();
                if (CheckWinOrLoss())
                {
                    yield break;
                }

                yield return Wait(CombatTuning.EnemyActionDelaySeconds);
            }

            PickEnemyIntents();
            ResetEnemyTurnLethalMarkers();
            StateChanged?.Invoke();
        }

        private void GarbageCollect()
        {
            _selectedEnemyIndex = -1;
            _pendingTargetCard = null;
            _statusEffects.Tick(_playerState, StatusTickTiming.EndOfTurn, _playerState, _damagePipeline);
            if (CheckLoss())
            {
                return;
            }

            ResolveDelayedEndOfTurnEffects();
            if (CheckWinOrLoss())
            {
                return;
            }

            ResetArchBtwAtEndOfTurn();
            RemoveDefeatedEnemies();
            CheckWinOrLoss();
            StateChanged?.Invoke();
        }

        private IEnumerator DrawOpeningHandSequenced()
        {
            int openingDraw = Mathf.Min(CombatTuning.OpeningHandSize, _handController == null ? _playerState.Ram : _handController.RamCapacity);
            yield return DrawCardsToHandSequenced(openingDraw, "opening hand");
        }

        private IEnumerator DrawForTurnSequenced()
        {
            int requested = CombatTuning.DrawPerTurn;
            if (CombatTuning.MinimumHandFloor > 0 && _handController.Cards.Count < CombatTuning.MinimumHandFloor && _deckController.AvailableToDrawCount > 0)
            {
                requested = Mathf.Max(requested, CombatTuning.MinimumHandFloor - _handController.Cards.Count);
            }

            yield return DrawCardsToHandSequenced(requested, "turn draw");
        }

        private IEnumerator DrawCardsToHandSequenced(int requestedCount, string label)
        {
            if (_handController == null || _playerState == null || requestedCount <= 0)
            {
                yield break;
            }

            int room = _handController.RemainingRam;
            if (room <= 0)
            {
                Log($"{label}: hand full");
                yield break;
            }

            IReadOnlyList<CardInstance> drawn = _deckController.Draw(requestedCount);
            int added = 0;
            for (int i = 0; i < drawn.Count; i++)
            {
                if (_handController.Add(drawn[i]))
                {
                    added++;
                    StateChanged?.Invoke();
                    yield return Wait(CombatTuning.CardDrawDelaySeconds);
                }
                else
                {
                    _deckController.AddToDrawPile(drawn[i], shuffle: false);
                }
            }

            if (added == 0 && _deckController.AvailableToDrawCount == 0)
            {
                Log($"{label}: no cards to draw");
            }
            else if (added < requestedCount)
            {
                Log($"{label}: drew {added} (hand full)");
            }
            else
            {
                Log($"{label}: drew {added}");
            }
        }

        private void TryApplyUbuntuAptUpdate()
        {
            if (!IsDistro("ubuntu") || _handController == null || _playerState == null || _handController.RemainingRam <= 0)
            {
                return;
            }

            int lookCount = _runConfig.DistroVersion >= 2 ? 3 : 2;
            if (!_deckController.TryDrawCheapestFromTop(lookCount, out CardInstance card))
            {
                return;
            }

            if (_runConfig.DistroVersion >= 4)
            {
                card.TemporaryCostDelta -= 1;
            }

            if (_handController.Add(card))
            {
                Log($"apt update: staged {GetCardName(card)} from top {lookCount}");
            }
            else
            {
                _deckController.AddToDrawPile(card, shuffle: false);
            }
        }

        private void TryApplyUbuntuEmptyHandRefill()
        {
            if (!IsDistro("ubuntu") || _runConfig.DistroVersion < 5 || _ubuntuEmptyHandRefillUsed || _handController == null || _handController.Cards.Count > 0)
            {
                return;
            }

            _ubuntuEmptyHandRefillUsed = true;
            StartCoroutine(DrawCardsToHandSequenced(_playerState.Ram, "ubuntu 24.04 refill"));
        }

        private bool CanApplyFedoraBonus(CardInstance card)
        {
            return CardEffectFactory.CanReceiveBleedingEdgeDamageBonus(card?.Definition)
                && CardCanBenefitFromBleedingEdgeNow(card)
                && (CanApplyFedoraPassiveBonus() || _rawhideBonusCharges > 0);
        }

        private bool CardCanBenefitFromBleedingEdgeNow(CardInstance card)
        {
            string id = card?.Definition?.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            if (id == "arch_consult_wiki")
            {
                return _handController == null || !HasBrokenHandCard();
            }

            if (id == "fedora_dnf_autoremove")
            {
                return HasJunkHandCard(card);
            }

            return true;
        }

        private bool HasBrokenHandCard()
        {
            if (_handController == null)
            {
                return false;
            }

            for (int i = 0; i < _handController.Cards.Count; i++)
            {
                if (_handController.Cards[i]?.IsBroken == true)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasJunkHandCard(CardInstance playedCard)
        {
            if (_handController == null)
            {
                return false;
            }

            for (int i = 0; i < _handController.Cards.Count; i++)
            {
                CardInstance card = _handController.Cards[i];
                if (card != playedCard && IsJunkForDnfAutoremove(card))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsJunkForDnfAutoremove(CardInstance card)
        {
            if (card == null)
            {
                return false;
            }

            string id = card.Definition?.Id ?? string.Empty;
            string name = card.Definition?.DisplayName ?? string.Empty;
            return card.Definition != null && card.Definition.IsToken
                || card.IsBroken
                || id.IndexOf("junk", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("nop", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("junk", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("NOP", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("broken", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DecayJavaWarmupForNextWave()
        {
            if (_javaCardsPlayedThisCombat <= 0)
            {
                return;
            }

            _javaCardsPlayedThisCombat = Mathf.Max(0, _javaCardsPlayedThisCombat - 1);
            Log($"Java JIT cooled: discount now -{_javaCardsPlayedThisCombat}");
        }

        private bool CanApplyFedoraPassiveBonus()
        {
            if (!IsDistro("fedora") || _playerState == null)
            {
                return false;
            }

            int limit = _runConfig.DistroVersion >= 5 || HasOnDistroPackageEffect(PackageEffectKind.DnfFedoraPassive, out PackageEffectData dnfEffect) && dnfEffect.EnableFedoraSecondCardPassive ? 2 : 1;
            return _fedoraCardsDiscountedThisTurn < limit;
        }

        private void ApplyFedoraGrowth(CardInstance card, bool rawhideChargeUsed, bool firstFedoraBonusThisTurn)
        {
            if (!IsDistro("fedora") || _runConfig.DistroVersion < 3 || card == null || !firstFedoraBonusThisTurn)
            {
                return;
            }

            int growth = rawhideChargeUsed ? 2 : 1;
            if (card.ApplyCombatMagnitudeBonus(growth))
            {
                Log($"rawhide growth: {GetCardName(card)} +{growth} effect");
            }
        }

        private int ApplyPackageCostRules(CardInstance card, int cost)
        {
            if (card == null || cost <= 0)
            {
                return cost;
            }

            IReadOnlyList<PackageInstance> packages = _runConfig?.EquippedPackages;
            if (packages == null)
            {
                return cost;
            }

            int nextTurnCard = _cardsPlayedThisTurn + 1;
            int nextWaveCard = _cardsPlayedThisWave + 1;
            for (int i = 0; i < packages.Count; i++)
            {
                PackageInstance package = packages[i];
                if (package?.Definition == null)
                {
                    continue;
                }

                PackageEffectData effect = package.EffectFor(_runConfig?.Distro?.Id);
                switch (effect.Kind)
                {
                    case PackageEffectKind.FirstCardEachWaveCostReduction:
                        if (nextWaveCard == 1)
                        {
                            cost -= Mathf.Max(0, effect.Amount);
                        }
                        break;
                    case PackageEffectKind.EveryNthCardEachWaveFree:
                        if (effect.Threshold > 0 && nextWaveCard % effect.Threshold == 0)
                        {
                            cost = 0;
                        }
                        break;
                    case PackageEffectKind.FirstCardsEachTurnCostReduction:
                        if (nextTurnCard <= Mathf.Max(0, effect.Threshold))
                        {
                            cost -= Mathf.Max(0, effect.Amount);
                        }
                        break;
                }
            }

            return cost;
        }

        private void ApplyPackageState(CombatantState state)
        {
            if (state == null)
            {
                return;
            }

            state.JavaScriptFlatDamageBonus = 0;
            IReadOnlyList<PackageInstance> packages = _runConfig?.EquippedPackages;
            if (packages == null)
            {
                return;
            }

            for (int i = 0; i < packages.Count; i++)
            {
                PackageEffectData effect = packages[i] == null ? default : packages[i].EffectFor(_runConfig?.Distro?.Id);
                if (effect.Kind == PackageEffectKind.JavaScriptFlatDamage)
                {
                    state.JavaScriptFlatDamageBonus += Mathf.Max(0, effect.Amount);
                }
            }
        }

        private void ApplyPackageWaveStartEffects()
        {
            IReadOnlyList<PackageInstance> packages = _runConfig?.EquippedPackages;
            if (packages == null)
            {
                return;
            }

            for (int i = 0; i < packages.Count; i++)
            {
                PackageInstance package = packages[i];
                if (package?.Definition == null)
                {
                    continue;
                }

                PackageEffectData effect = package.EffectFor(_runConfig?.Distro?.Id);
                switch (effect.Kind)
                {
                    case PackageEffectKind.WaveDraw:
                        StartCoroutine(DrawCardsToHandSequenced(Mathf.Max(0, effect.Amount), package.Definition.DisplayName));
                        break;
                    case PackageEffectKind.WaveStartShield:
                        GrantPlayerShield(Mathf.Max(0, effect.Amount), package.Definition.DisplayName);
                        break;
                    case PackageEffectKind.FirstTurnEachWaveShield:
                        if (_turnNumberThisWave == 0)
                        {
                            GrantPlayerShield(Mathf.Max(0, effect.Amount), package.Definition.DisplayName);
                        }
                        break;
                    case PackageEffectKind.FirstTurnFirstWaveDraw:
                        if (CurrentWaveNumber == 1 && _turnNumberThisWave == 0)
                        {
                            StartCoroutine(DrawCardsToHandSequenced(Mathf.Max(0, effect.Amount), package.Definition.DisplayName));
                        }
                        break;
                    case PackageEffectKind.WaveGenerateBasicCard:
                        GeneratePackageLanguageCard(package.Definition.DisplayName);
                        break;
                }
            }
        }

        private void ApplyPackageTurnStartEffects()
        {
            if (HasPackageEffect(PackageEffectKind.EveryNthTurnShield, out PackageEffectData effect)
                && effect.Threshold > 0
                && _turnNumberThisWave % effect.Threshold == 0)
            {
                GrantPlayerShield(Mathf.Max(0, effect.Amount), "cron");
            }

            if (HasPackageEffect(PackageEffectKind.EveryNthTurnDraw, out effect)
                && effect.Threshold > 0
                && _turnNumberThisWave % effect.Threshold == 0)
            {
                StartCoroutine(DrawCardsToHandSequenced(Mathf.Max(0, effect.Amount), "udev"));
            }

            if (_turnNumberThisWave == 1
                && HasPackageEffect(PackageEffectKind.FirstTurnEachWaveCycle, out effect)
                && effect.Amount > 0)
            {
                _playerState.Cycles += effect.Amount;
                Log($"apt-fast: gained {effect.Amount} Cycle");
            }

            if (HasPackageEffect(PackageEffectKind.StartTurnNoDebuffShield, out effect)
                && !HasHarmfulStatus(_playerState))
            {
                GrantPlayerShield(Mathf.Max(0, effect.Amount), "auditd");
            }

            if (HasPackageEffect(PackageEffectKind.TurnStartGenerateLanguageCard, out effect))
            {
                GenerateAurPackageCard(effect);
            }
        }

        private void ApplyPackageCardPlayedEffects(CardInstance card)
        {
            if (card == null || _playerState == null)
            {
                return;
            }

            if (HasPackageEffect(PackageEffectKind.ThirdCardEachTurnGenerate, out PackageEffectData aptEffect)
                && _cardsPlayedThisTurn == Mathf.Max(1, aptEffect.Threshold))
            {
                GeneratePackageLanguageCard("apt");
                if (aptEffect.RefundCycle)
                {
                    _playerState.Cycles += 1;
                    Log("apt refunded 1 Cycle");
                }
            }

            if (HasPackageEffect(PackageEffectKind.DnfFedoraPassive, out PackageEffectData dnfEffect)
                && _cardsPlayedThisTurn == 1
                && dnfEffect.Amount > 0)
            {
                _playerState.FlatEffectBonus += dnfEffect.Amount;
                Log($"DNF wave buff +{dnfEffect.Amount} damage");
            }

            if (!_packageInterpreterQueueShieldTriggeredThisWave
                && card.Definition.ResolutionTrack == ResolutionTrack.InterpreterQueue
                && HasPackageEffect(PackageEffectKind.FirstInterpreterQueueCardEachWaveShield, out PackageEffectData queueEffect))
            {
                _packageInterpreterQueueShieldTriggeredThisWave = true;
                GrantPlayerShield(Mathf.Max(0, queueEffect.Amount), "dbus");
            }

            if (!_packageNativeDamageTriggeredThisWave
                && card.Definition.ResolutionTrack == ResolutionTrack.Native
                && HasPackageEffect(PackageEffectKind.FirstNativeCardEachWaveFlatDamage, out PackageEffectData nativeEffect)
                && nativeEffect.Amount > 0)
            {
                _packageNativeDamageTriggeredThisWave = true;
                _playerState.FlatEffectBonus += nativeEffect.Amount;
                Log($"make: first Native card +{nativeEffect.Amount} damage");
            }

            if (HasPackageEffect(PackageEffectKind.EveryNthCardEachWaveCycle, out PackageEffectData cycleEffect)
                && cycleEffect.Threshold > 0
                && _cardsPlayedThisWave % cycleEffect.Threshold == 0
                && cycleEffect.Amount > 0)
            {
                _playerState.Cycles += cycleEffect.Amount;
                Log($"systemd-timer: gained {cycleEffect.Amount} Cycle");
            }
        }

        public void GrantPlayerShield(int amount, string label)
        {
            if (_playerState == null || amount <= 0)
            {
                return;
            }

            int total = amount;
            if (!_packageShieldBonusTriggeredThisTurn
                && HasPackageEffect(PackageEffectKind.FirstShieldEachTurnBonus, out PackageEffectData effect))
            {
                int bonus = Mathf.Max(0, effect.Amount);
                if (bonus > 0)
                {
                    total += bonus;
                    _packageShieldBonusTriggeredThisTurn = true;
                    Log($"SELinux shield +{bonus}");
                }
            }

            _playerState.Shield += total;
            if (!string.IsNullOrWhiteSpace(label))
            {
                Log($"{label}: gained {total} Shield");
            }
        }

        private void GeneratePackageLanguageCard(string label)
        {
            if (_handController == null || _handController.RemainingRam <= 0)
            {
                return;
            }

            Language language = RandomRoll.RollRange(0, 1, new RollContext(_playerState)) == 0
                ? _runConfig.PrimaryLanguage
                : _runConfig.SecondaryLanguage;

            if (!TryCreateGeneratedCard(language, Rarity.Common, out CardInstance generated))
            {
                Log($"{label}: TODO card-generation pool unavailable");
                return;
            }

            if (_handController.Add(generated))
            {
                Log($"{label}: generated {GetCardName(generated)} at 0c");
                StateChanged?.Invoke();
            }
        }

        private void GenerateAurPackageCard(PackageEffectData effect)
        {
            if (_handController == null || _handController.RemainingRam <= 0)
            {
                return;
            }

            int maxRarity = Mathf.Clamp(effect.Threshold, 0, (int)Rarity.Legendary);
            List<CardDefinition> pool = new();
            HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<CardDefinition> source = _generatedCardPool.Count > 0 ? _generatedCardPool : _runConfig?.StartingDeck;
            if (source == null)
            {
                Log("AUR: TODO card-generation pool unavailable");
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                CardDefinition definition = source[i];
                if (definition == null
                    || definition.IsToken
                    || definition.IsRunOnly
                    || (int)definition.Rarity > maxRarity
                    || definition.Language != Language.C && definition.Language != Language.Rust)
                {
                    continue;
                }

                if (seenIds.Add(definition.Id))
                {
                    pool.Add(definition);
                }
            }

            if (pool.Count == 0)
            {
                Log("AUR: TODO card-generation pool unavailable");
                return;
            }

            int index = RandomRoll.RollRange(0, pool.Count - 1, new RollContext(_playerState));
            CardDefinition selected = pool[index];
            CardInstance generated = new(selected)
            {
                TemporaryCostDelta = -Mathf.Clamp(effect.Amount, 0, selected.CycleCost)
            };

            if (_handController.Add(generated))
            {
                string discount = effect.Amount > 0 ? " at -1c" : string.Empty;
                Log($"AUR: generated {GetCardName(generated)}{discount}");
                StateChanged?.Invoke();
            }
        }

        private void HandlePackageDamageDealt(DamageDealtEvent payload)
        {
            HandleEnemyDamageDealt(payload);

            if (payload.Target == _playerState && _playerState != null && _playerState.ArchRollingReleaseRecoveredThisHit)
            {
                _playerState.ArchRollingReleaseRecoveredThisHit = false;
                string recovery = "rolling release: recovered at 1 Uptime";
                if (_playerState.ArchRollingReleaseShieldOnSave > 0 || _playerState.ArchRollingReleaseCyclesOnSave > 0)
                {
                    recovery += $", +{_playerState.ArchRollingReleaseShieldOnSave} Shield, +{_playerState.ArchRollingReleaseCyclesOnSave} Cycle";
                }

                Log(recovery);
                StateChanged?.Invoke();
            }

            if (payload.Target != _playerState
                || payload.UptimeDamage <= 0
                || _packageTimeshiftTriggeredThisWave
                || !HasPackageEffect(PackageEffectKind.WaveThresholdRestore, out PackageEffectData effect))
            {
                return;
            }

            int thresholdPercent = Mathf.Clamp(effect.Threshold, 1, 100);
            int restoreTarget = Mathf.CeilToInt(_playerState.MaxUptime * (thresholdPercent / 100f));
            if (_playerState.CurrentUptime >= restoreTarget)
            {
                return;
            }

            _packageTimeshiftTriggeredThisWave = true;
            _playerState.CurrentUptime = restoreTarget;
            if (effect.CleanseDebuffs)
            {
                _statusEffects.CleanseHarmful(_playerState);
            }

            Log($"Timeshift restored uptime to {thresholdPercent}%");
            StateChanged?.Invoke();
        }

        private void HandleEnemyDamageDealt(DamageDealtEvent payload)
        {
            EnemyInstance enemy = FindEnemy(payload.Target);
            if (enemy == null)
            {
                return;
            }

            if (payload.Amount > 0)
            {
                enemy.MarkDamaged();
            }

            if (!enemy.HasBehavior(EnemyBehaviorFlags.Revive)
                || payload.UptimeDamage <= 0
                || enemy.State.CurrentUptime > 0)
            {
                return;
            }

            int uptimeBeforeHit = enemy.State.CurrentUptime + payload.UptimeDamage;
            int reapThreshold = Mathf.CeilToInt(enemy.MaxUptime * (EnemyArchetypeCatalog.ZombieReapThresholdPercent / 100f));
            bool reaped = enemy.HasRevived || enemy.LethalHitThisTurn || uptimeBeforeHit <= reapThreshold;
            enemy.MarkLethalHit();
            if (reaped)
            {
                enemy.MarkReaped();
                Log($"{enemy.Name} reaped");
                return;
            }

            enemy.MarkPendingRevive();
            Log($"{enemy.Name} dropped to 0 uptime; reviving next exchange");
            StateChanged?.Invoke();
        }

        private EnemyInstance FindEnemy(CombatantState state)
        {
            if (state == null)
            {
                return null;
            }

            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i].State == state)
                {
                    return _enemies[i];
                }
            }

            return null;
        }

        private void RevivePendingEnemies()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyInstance enemy = _enemies[i];
                if (!enemy.PendingRevive)
                {
                    continue;
                }

                enemy.ReviveAtHalfUptime();
                Log($"{enemy.Name} revived at {enemy.CurrentUptime} uptime");
            }
        }

        private void ResetEnemyTurnLethalMarkers()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                _enemies[i].ResetTurnLethalMarker();
            }
        }

        private void ResetArchBtwAtEndOfTurn()
        {
            if (!IsDistro("arch") || _playerState == null || _playerState.ArchBtwStacks <= 0 || ArchBtwPersistsThisWave())
            {
                return;
            }

            _playerState.ArchBtwStacks = 0;
        }

        private void ResetArchWaveState()
        {
            if (_playerState == null)
            {
                return;
            }

            _playerState.ArchBtwStacks = 0;
            _playerState.ArchRollingReleaseSavesRemaining = IsDistro("arch") ? _runConfig.DistroVersion >= 5 ? 2 : 1 : 0;
            _playerState.ArchRollingReleaseAvailableThisWave = _playerState.ArchRollingReleaseSavesRemaining > 0;
            _playerState.ArchRollingReleaseRecoveredThisHit = false;
            _playerState.HasLastPlayedCardLanguage = false;
            _playerState.HasPreviousPlayedCardLanguage = false;
        }

        private bool ArchBtwPersistsThisWave()
        {
            return IsDistro("arch")
                && (_runConfig.DistroVersion >= 5
                || HasOnDistroPackageEffect(PackageEffectKind.FirstCThisTurnDamageMultiplier, out PackageEffectData effect)
                && effect.PersistArchBtw);
        }

        private bool HasOnDistroPackageEffect(PackageEffectKind kind, out PackageEffectData effect)
        {
            effect = default;
            IReadOnlyList<PackageInstance> packages = _runConfig?.EquippedPackages;
            if (packages == null)
            {
                return false;
            }

            for (int i = 0; i < packages.Count; i++)
            {
                PackageInstance package = packages[i];
                if (package?.Definition == null || !package.Definition.IsIntendedFor(_runConfig?.Distro?.Id))
                {
                    continue;
                }

                effect = package.EffectFor(_runConfig?.Distro?.Id);
                if (effect.Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasPackageEffect(PackageEffectKind kind, out PackageEffectData effect)
        {
            effect = default;
            IReadOnlyList<PackageInstance> packages = _runConfig?.EquippedPackages;
            if (packages == null)
            {
                return false;
            }

            for (int i = 0; i < packages.Count; i++)
            {
                PackageInstance package = packages[i];
                if (package?.Definition == null)
                {
                    continue;
                }

                effect = package.EffectFor(_runConfig?.Distro?.Id);
                if (effect.Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasHarmfulStatus(CombatantState state)
        {
            if (state == null)
            {
                return false;
            }

            for (int i = 0; i < state.Statuses.Count; i++)
            {
                if (!StatusEffectController.GetDescriptor(state.Statuses[i].Type).IsBeneficial)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCrashImmune(CardInstance card)
        {
            string id = card?.Definition?.Id;
            return string.Equals(id, "fedora_selinux", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyVersionState(CombatantState state)
        {
            if (state == null)
            {
                return;
            }

            state.ForceMaxRolls = IsDistro("mint");
            state.IgnoreDamageMultipliers = IsDistro("mint");
            state.AllowFlatDamageBuffs = IsDistro("mint") && _runConfig.DistroVersion >= 4;
            state.FlatEffectBonus = IsDistro("mint") && _runConfig.DistroVersion >= 2 ? 2 : 0;
            state.DamageMultiplierPercent = 100;
            state.CurrentCardDamageMultiplierPercent = 100;
            state.ArchBtwDamagePerStack = IsDistro("arch") && _runConfig.DistroVersion >= 2 ? 2 : 1;
            state.ArchMakepkgBtwMultiplier = IsDistro("arch") && _runConfig.DistroVersion >= 3 ? 4 : 3;
            state.ArchRollingReleaseShieldOnSave = IsDistro("arch") && _runConfig.DistroVersion >= 4 ? 15 : 0;
            state.ArchRollingReleaseCyclesOnSave = IsDistro("arch") && _runConfig.DistroVersion >= 4 ? 2 : 0;
        }

        private bool IsDistro(string id)
        {
            return string.Equals(_runConfig?.Distro?.Id, id, StringComparison.OrdinalIgnoreCase);
        }

        public bool CanPlayerReceiveCardShield(CardInstance card)
        {
            return CanPlayerReceiveCardBuff(card);
        }

        public bool CanPlayerReceiveCardHeal(CardInstance card)
        {
            return CanPlayerReceiveCardBuff(card);
        }

        public bool CanPlayerReceiveCardBuff(CardInstance card)
        {
            return !IsDistro("mint") || IsCurrentDistroExclusiveCard(card);
        }

        private bool IsCurrentDistroExclusiveCard(CardInstance card)
        {
            CardDefinition definition = card?.Definition;
            IReadOnlyList<CardDefinition> exclusiveCards = _runConfig?.Distro?.ExclusiveCards;
            if (definition == null || exclusiveCards == null)
            {
                return false;
            }

            for (int i = 0; i < exclusiveCards.Count; i++)
            {
                CardDefinition exclusive = exclusiveCards[i];
                if (exclusive == null)
                {
                    continue;
                }

                if (ReferenceEquals(exclusive, definition)
                    || string.Equals(exclusive.Id, definition.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private CombatContext BuildContext(CardInstance card, IReadOnlyList<CombatantState> targets)
        {
            return new CombatContext(card, _playerState, targets, this, _damagePipeline, _statusEffects, _deckController, _handController, _enemies);
        }

        private void ExecuteCardEffects(CardInstance card, IReadOnlyList<CombatantState> targets)
        {
            IReadOnlyList<ICardEffect> effects = CardEffectFactory.CreateEffects(card?.Definition);
            CombatContext context = BuildContext(card, targets);
            for (int i = 0; i < effects.Count; i++)
            {
                effects[i].Execute(context);
                RemoveDefeatedEnemies();
                if (CheckWinOrLoss())
                {
                    return;
                }
            }
        }

        private void ResolveCardRiders(CardInstance card)
        {
            if (card == null || card.DrawRider <= 0 || _handController == null || _playerState == null)
            {
                return;
            }

            StartCoroutine(DrawCardsToHandSequenced(card.DrawRider, "upgrade rider"));
        }

        private IReadOnlyList<CombatantState> CaptureTargets(CardInstance card)
        {
            if (card?.Definition == null)
            {
                return Array.Empty<CombatantState>();
            }

            if (CardEffectFactory.TargetsAllEnemies(card.Definition))
            {
                return LivingEnemyStates();
            }

            if (_selectedEnemyIndex >= 0 && _selectedEnemyIndex < _enemies.Count && !_enemies[_selectedEnemyIndex].State.IsDefeated)
            {
                return new[] { _enemies[_selectedEnemyIndex].State };
            }

            if (RequiresEnemyTarget(card))
            {
                CombatantState firstLiving = FirstLivingEnemyState();
                return firstLiving == null ? Array.Empty<CombatantState>() : new[] { firstLiving };
            }

            return Array.Empty<CombatantState>();
        }

        private IReadOnlyList<CombatantState> ResolveTargets(CardInstance card)
        {
            List<CombatantState> targets = card.TargetSnapshot.Where(target => target != null && !target.IsDefeated).ToList();
            if (targets.Count == 0 && RequiresEnemyTarget(card))
            {
                CombatantState firstLiving = FirstLivingEnemyState();
                if (firstLiving != null)
                {
                    targets.Add(firstLiving);
                }
            }

            return targets;
        }

        private IReadOnlyList<CombatantState> ResolveQueuedTargets(CardInstance card)
        {
            List<CombatantState> targets = ResolveTargets(card).ToList();
            if (targets.Count == 0 && CardEffectFactory.TargetsAllEnemies(card?.Definition))
            {
                targets.AddRange(LivingEnemyStates());
            }

            // Queued cards snapshot targets when played. If those targets die before Interpret, retarget to living enemies.
            return targets;
        }

        private bool RequiresEnemyTarget(CardInstance card)
        {
            if (card?.Definition == null)
            {
                return false;
            }

            return CardEffectFactory.RequiresSingleTarget(card.Definition)
                || CardEffectFactory.TargetsAllEnemies(card.Definition)
                || card.Definition.Id == "lang_py_print";
        }

        private List<CombatantState> LivingEnemyStates()
        {
            return _enemies
                .Where(enemy => enemy.State != null && !enemy.State.IsDefeated)
                .Select(enemy => enemy.State)
                .ToList();
        }

        private CombatantState FirstLivingEnemyState()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i].State != null && !_enemies[i].State.IsDefeated)
                {
                    return _enemies[i].State;
                }
            }

            return null;
        }

        private void RemoveDefeatedEnemies()
        {
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                if (_enemies[i].State.IsDefeated)
                {
                    EnemyInstance enemy = _enemies[i];
                    if (enemy.PendingRevive)
                    {
                        continue;
                    }

                    Log($"{enemy.Name} exited");
                    _runManager?.AddBits(CombatTuning.BitsPerKill);
                    HandleEnemyDeath(enemy);
                    _enemies.RemoveAt(i);
                    RecalculateKernelRamPressure();
                }
            }

            if (_selectedEnemyIndex >= _enemies.Count)
            {
                _selectedEnemyIndex = -1;
            }
        }

        private void HandleEnemyDeath(EnemyInstance enemy)
        {
            if (enemy == null)
            {
                return;
            }

            if (enemy.HasBehavior(EnemyBehaviorFlags.SegfaultOnDeath))
            {
                ApplySegfaultDeathPayload(enemy);
            }

            if (enemy.HasBehavior(EnemyBehaviorFlags.RacePair))
            {
                BreakRacePair(enemy);
            }

            if (enemy.HasBehavior(EnemyBehaviorFlags.LeavesOrphan))
            {
                SpawnOrphanFromDeath(enemy);
            }
        }

        private void ApplySegfaultDeathPayload(EnemyInstance enemy)
        {
            _statusEffects.Apply(_playerState, StatusType.Segfault, 1, -1, enemy.State, skipNextTick: true);
            TryCorruptRandomHandCard("segfault");
        }

        private CardInstance RandomHandCard(Predicate<CardInstance> predicate)
        {
            if (_handController == null || _handController.Cards.Count == 0)
            {
                return null;
            }

            List<CardInstance> candidates = new();
            for (int i = 0; i < _handController.Cards.Count; i++)
            {
                CardInstance candidate = _handController.Cards[i];
                if (predicate == null || predicate(candidate))
                {
                    candidates.Add(candidate);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            int index = RandomRoll.RollRange(0, candidates.Count - 1, new RollContext(_playerState));
            return candidates[index];
        }

        private void UnlockHandCards(string reason)
        {
            if (_handController == null)
            {
                return;
            }

            int unlocked = 0;
            for (int i = 0; i < _handController.Cards.Count; i++)
            {
                CardInstance card = _handController.Cards[i];
                if (card != null && card.IsLocked)
                {
                    card.IsLocked = false;
                    unlocked++;
                }
            }

            if (unlocked > 0)
            {
                Log($"{reason}: unlocked {unlocked} card(s)");
            }
        }

        private void RecalculateKernelRamPressure()
        {
            if (_handController == null || _playerState == null || _runManager == null)
            {
                return;
            }

            int penalty = 0;
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyInstance enemy = _enemies[i];
                if (enemy.HasBehavior(EnemyBehaviorFlags.RamPressure) && !enemy.State.IsDefeated)
                {
                    penalty += EnemyArchetypeCatalog.KernelPanicRamPenalty;
                }
            }

            _activeKernelRamPenalty = penalty;
            int baseRam = _runManager.EffectiveRam();
            int effectiveRam = Mathf.Max(1, baseRam - penalty);
            _playerState.Ram = effectiveRam;
            _handController.SetRamCapacity(effectiveRam);
        }

        private void ApplyTelemetryCardPlayed()
        {
            int collectors = 0;
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyInstance enemy = _enemies[i];
                if (enemy.HasBehavior(EnemyBehaviorFlags.TelemetryCollector) && !enemy.State.IsDefeated)
                {
                    enemy.AddTelemetryStack();
                    collectors++;
                }
            }

            if (collectors > 0)
            {
                Log($"telemetry collector profiled card: +{EnemyArchetypeCatalog.TelemetryDamageGrowthPerCard} damage");
                StateChanged?.Invoke();
            }
        }

        private void LockRandomHandCard(EnemyInstance source)
        {
            CardInstance card = RandomHandCard(cardInstance => cardInstance != null && !cardInstance.IsBroken && !cardInstance.IsLocked);
            if (card == null)
            {
                Log("license check: no unlockable hand card");
                return;
            }

            card.IsLocked = true;
            Log($"{source.Name} locked {GetCardName(card)}");
        }

        private void BreakRacePair(EnemyInstance defeated)
        {
            EnemyInstance survivor = RacePartner(defeated);
            if (survivor == null)
            {
                return;
            }

            survivor.EnrageRaceSurvivor();
            Log($"{survivor.Name} race link broke; survivor enraged");
        }

        private void SpawnOrphanFromDeath(EnemyInstance source)
        {
            EnemyInstance orphan = SpawnEnemy("orphan_process", 0);
            orphan.MarkSpawnedFromDeath();
            Log($"{source.Name} left an orphan process");
        }

        private void SpawnStructuralEnemies()
        {
            int wave = CurrentWaveNumber;
            int countGrowth = ((wave - 1) / 2) * CombatTuning.AdditionalEnemiesPerWave;
            int count = Mathf.Clamp(CombatTuning.BaseEnemiesPerWave + countGrowth, CombatTuning.BaseEnemiesPerWave, CombatTuning.MaxEnemiesPerWave);
            int waveUptimeBonus = Mathf.Max(0, wave - 1) * CombatTuning.EnemyUptimeGrowthPerWave;
            bool rootkitSpawned = false;
            bool eliteSpawned = TrySpawnEliteEncounter(wave, waveUptimeBonus);
            for (int i = eliteSpawned ? 1 : 0; i < count; i++)
            {
                string archetypeId = CreatePlaceholderArchetypeId(wave, i);
                if (archetypeId == "rootkit")
                {
                    if (rootkitSpawned)
                    {
                        archetypeId = CreateRootkitFallbackArchetypeId(wave, i);
                    }
                    else
                    {
                        rootkitSpawned = true;
                    }
                }

                if (archetypeId == "race_condition" && i + 1 < count)
                {
                    SpawnRacePair(waveUptimeBonus);
                    i++;
                    continue;
                }

                if (archetypeId == "race_condition")
                {
                    archetypeId = "segfault";
                }

                SpawnEnemy(archetypeId, waveUptimeBonus);
            }
        }

        private bool TrySpawnEliteEncounter(int wave, int uptimeBonus)
        {
            if (wave < 5)
            {
                return false;
            }

            bool guaranteedIntro = wave == 5;
            bool rolledElite = RandomRoll.RollRange(1, 100, RollContext.None) <= 35;
            if (!guaranteedIntro && !rolledElite)
            {
                return false;
            }

            SpawnEnemy(CreateEliteArchetypeId(), uptimeBonus);
            Log("elite process escorted by fodder");
            // TODO: Replace this single-elite chance with authored elite encounter assets.
            return true;
        }

        private static string CreateEliteArchetypeId()
        {
            string[] pool = { "daemon", "kernel_panic", "telemetry_collector", "drm_guardian" };
            int index = RandomRoll.RollRange(0, pool.Length - 1, RollContext.None);
            return pool[index];
        }

        private void SpawnRacePair(int uptimeBonus)
        {
            int pairId = _nextRacePairId++;
            EnemyInstance first = SpawnEnemy("race_condition", uptimeBonus);
            EnemyInstance second = SpawnEnemy("race_condition", uptimeBonus);
            first.SetPairId(pairId);
            second.SetPairId(pairId);
            Log("race condition pair spawned");
        }

        private EnemyInstance SpawnEnemy(string archetypeId, int uptimeBonus)
        {
            EnemyArchetypeDescriptor archetype = EnemyArchetypeCatalog.Get(archetypeId);
            int baseUptime = RandomRoll.RollRange(archetype.BaseUptimeMin, archetype.BaseUptimeMax, RollContext.None);
            EnemyInstance enemy = new(archetype, baseUptime + Mathf.Max(0, uptimeBonus));
            _enemies.Add(enemy);
            return enemy;
        }

        private static string CreatePlaceholderArchetypeId(int wave, int slotIndex)
        {
            // TODO: Replace this structural mix with encounter/difficulty assets.
            string[] pool = wave switch
            {
                <= 1 => new[] { "zombie_process", "memory_leak" },
                2 => new[] { "zombie_process", "memory_leak", "fork_bomb", "firewalld" },
                3 => new[] { "zombie_process", "memory_leak", "fork_bomb", "firewalld", "segfault" },
                4 => new[] { "zombie_process", "memory_leak", "fork_bomb", "firewalld", "cron_job", "segfault", "race_condition" },
                _ => new[] { "zombie_process", "memory_leak", "fork_bomb", "firewalld", "daemon", "cron_job", "segfault", "race_condition", "rootkit" }
            };

            if (slotIndex == 0)
            {
                if (wave >= 5 && wave % 2 == 1)
                {
                    return "rootkit";
                }

                return wave % 2 == 0 ? "firewalld" : "zombie_process";
            }

            if (slotIndex == 1 && wave >= 2)
            {
                if (wave >= 4 && wave % 4 == 0)
                {
                    return "race_condition";
                }

                return wave % 3 == 0 ? "fork_bomb" : "memory_leak";
            }

            int index = RandomRoll.RollRange(0, pool.Length - 1, RollContext.None);
            return pool[index];
        }

        private static string CreateRootkitFallbackArchetypeId(int wave, int slotIndex)
        {
            return slotIndex % 2 == 0
                ? "firewalld"
                : wave % 3 == 0 ? "fork_bomb" : "memory_leak";
        }

        private void PickEnemyIntents()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (!_enemies[i].State.IsDefeated)
                {
                    _enemies[i].PickNextIntent();
                }
            }
        }

        private List<EnemyInstance> BuildEnemyActionOrder()
        {
            List<EnemyInstance> actors = _enemies
                .Where(enemy => enemy.State != null && !enemy.State.IsDefeated)
                .ToList();

            for (int i = 0; i < actors.Count - 1; i++)
            {
                EnemyInstance enemy = actors[i];
                if (!IsRacePairLinked(enemy))
                {
                    continue;
                }

                EnemyInstance partner = RacePartner(enemy);
                int partnerIndex = partner == null ? -1 : actors.IndexOf(partner);
                if (partnerIndex <= i)
                {
                    continue;
                }

                if (RandomRoll.RollRange(0, 1, new RollContext(enemy.State)) == 1)
                {
                    actors[i] = partner;
                    actors[partnerIndex] = enemy;
                }
            }

            return actors;
        }

        private void ExecuteEnemyIntent(EnemyInstance enemy, int enemyIndex)
        {
            EnemyIntent intent = enemy.CurrentIntent;
            int value = intent.MinValue == intent.MaxValue
                ? intent.MinValue
                : RandomRoll.RollRange(intent.MinValue, intent.MaxValue, new RollContext(enemy.State));

            switch (intent.Kind)
            {
                case EnemyIntentKind.Attack:
                    value = ApplyEnemyAttackBonuses(enemy, value);
                    value = ApplyIncomingAttackMitigation(value);
                    DamageResult attackResult = _damagePipeline.DealDamage(new DamageRequest(enemy.State, _playerState, value, intent.DamageType, intent.TrueDamage, false));
                    GameEvents.RaisePlayerDamaged(new PlayerDamagedEvent(enemy, attackResult.FinalAmount));
                    break;
                case EnemyIntentKind.Defend:
                    if (enemy.HasBehavior(EnemyBehaviorFlags.DefendAllies))
                    {
                        DefendSelfAndAdjacent(enemyIndex, value);
                    }
                    else
                    {
                        enemy.State.Shield += value;
                    }
                    break;
                case EnemyIntentKind.StatusAttack:
                    value = ApplyEnemyAttackBonuses(enemy, value);
                    DamageResult statusResult = _damagePipeline.DealDamage(new DamageRequest(enemy.State, _playerState, value, intent.DamageType, intent.TrueDamage, false));
                    GameEvents.RaisePlayerDamaged(new PlayerDamagedEvent(enemy, statusResult.FinalAmount));
                    if (intent.StatusStacks > 0)
                    {
                        _statusEffects.Apply(_playerState, intent.StatusType, intent.StatusStacks, intent.StatusDuration, enemy.State, skipNextTick: true);
                    }
                    break;
                case EnemyIntentKind.Buff:
                    Log("TODO: buff intent needs enemy buff/status support.");
                    break;
                case EnemyIntentKind.Special:
                    ExecuteSpecialEnemyIntent(enemy, value);
                    break;
            }

            enemy.AdvanceCountdownAfterAction();
        }

        private void ExecuteSpecialEnemyIntent(EnemyInstance enemy, int value)
        {
            if (enemy.HasBehavior(EnemyBehaviorFlags.CardLocker))
            {
                LockRandomHandCard(enemy);
                value = ApplyIncomingAttackMitigation(value);
                DamageResult result = _damagePipeline.DealDamage(new DamageRequest(enemy.State, _playerState, value, enemy.CurrentIntent.DamageType, enemy.CurrentIntent.TrueDamage, false));
                GameEvents.RaisePlayerDamaged(new PlayerDamagedEvent(enemy, result.FinalAmount));
                return;
            }

            if (enemy.HasBehavior(EnemyBehaviorFlags.SegfaultOnDeath))
            {
                enemy.AdvanceSegfaultCountdown();
                if (enemy.CountdownRemaining <= 0)
                {
                    _damagePipeline.DealDamage(new DamageRequest(enemy.State, enemy.State, enemy.CurrentUptime, Language.C, true, false));
                    Log($"{enemy.Name} dereferenced null");
                }

                return;
            }

            Log("TODO: special intent needs enemy-specific behavior hooks.");
        }

        private void ResolvePassiveEnemyAfterAction(EnemyInstance enemy)
        {
            if (enemy == null || enemy.State.IsDefeated)
            {
                return;
            }

            if (enemy.HasBehavior(EnemyBehaviorFlags.Split))
            {
                TrySplitForkBomb(enemy);
            }
        }

        private int ApplyEnemyAttackBonuses(EnemyInstance enemy, int value)
        {
            int modified = value;
            if (IsRacePairLinked(enemy))
            {
                modified += EnemyArchetypeCatalog.RacePairDamageBonus;
            }

            if (enemy != null && enemy.RaceEnrageStacks > 0)
            {
                modified += enemy.RaceEnrageStacks;
            }

            return modified;
        }

        private void TrySplitForkBomb(EnemyInstance source)
        {
            int totalForkBombs = _enemies.Count(enemy => enemy.ArchetypeId == source.ArchetypeId && !enemy.State.IsDefeated);
            if (totalForkBombs >= EnemyArchetypeCatalog.ForkBombTotalCap)
            {
                Log("fork bomb: split capped");
                return;
            }

            EnemyArchetypeDescriptor archetype = EnemyArchetypeCatalog.Get(source.ArchetypeId);
            int uptime = Mathf.Max(1, Mathf.CeilToInt(source.MaxUptime * 0.5f));
            EnemyInstance copy = new(archetype, uptime);
            copy.PickNextIntent();
            _enemies.Add(copy);
            Log("fork bomb split");
        }

        private bool IsRacePairLinked(EnemyInstance enemy)
        {
            return enemy != null
                && enemy.HasBehavior(EnemyBehaviorFlags.RacePair)
                && RacePartner(enemy) != null;
        }

        private EnemyInstance RacePartner(EnemyInstance enemy)
        {
            if (enemy == null || enemy.PairId < 0)
            {
                return null;
            }

            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyInstance candidate = _enemies[i];
                if (candidate != enemy
                    && candidate.PairId == enemy.PairId
                    && !candidate.State.IsDefeated)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void DefendSelfAndAdjacent(int enemyIndex, int amount)
        {
            int applied = 0;
            for (int i = enemyIndex - 1; i <= enemyIndex + 1; i++)
            {
                if (i < 0 || i >= _enemies.Count || _enemies[i].State.IsDefeated)
                {
                    continue;
                }

                _enemies[i].State.Shield += amount;
                applied++;
            }

            Log($"firewalld shielded {applied} process(es) for {amount}");
        }

        private int ApplyIncomingAttackMitigation(int value)
        {
            if (_playerState == null || _playerState.IncomingAttackHalfCharges <= 0)
            {
                return value;
            }

            _playerState.IncomingAttackHalfCharges--;
            int mitigated = Mathf.CeilToInt(Mathf.Max(0, value) * 0.5f);
            Log($"SELinux enforcing halved attack {value}->{mitigated}");
            return mitigated;
        }

        private int ResolveDamageResistance(DamageRequest request, int amount)
        {
            EnemyInstance target = FindEnemy(request.Target);
            if (target == null
                || !target.HasBehavior(EnemyBehaviorFlags.RootkitMasked)
                || !HasOtherLivingEnemy(target))
            {
                return amount;
            }

            if (amount <= 0)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.CeilToInt(amount * (EnemyArchetypeCatalog.RootkitMaskedDamagePercent / 100f)));
        }

        private bool HasOtherLivingEnemy(EnemyInstance target)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyInstance enemy = _enemies[i];
                if (enemy != target && enemy.State != null && !enemy.State.IsDefeated)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResolveDelayedEndOfTurnEffects()
        {
            for (int i = _delayedEffects.Count - 1; i >= 0; i--)
            {
                DelayedCombatEffect effect = _delayedEffects[i];
                effect.GarbageCollectionsRemaining--;
                if (effect.GarbageCollectionsRemaining > 0)
                {
                    _delayedEffects[i] = effect;
                    continue;
                }

                _delayedEffects.RemoveAt(i);
                switch (effect.Kind)
                {
                    case DelayedCombatEffectKind.UpdateManagerRepeat:
                        DealToAllEnemies(effect.Amount, Language.Python, "update manager repeat");
                        break;
                    case DelayedCombatEffectKind.TimeshiftRestore:
                        RestoreTimeshift(effect.Amount);
                        break;
                }
            }
        }

        private void DealToAllEnemies(int amount, Language language, string label)
        {
            List<CombatantState> targets = LivingEnemyStates();
            for (int i = 0; i < targets.Count; i++)
            {
                _damagePipeline.DealDamage(new DamageRequest(_playerState, targets[i], amount, language, false, true));
            }

            Log($"{label}: dealt {amount} to all enemies");
            RemoveDefeatedEnemies();
        }

        private void RestoreTimeshift(int snapshot)
        {
            if (_playerState == null || _playerState.IsDefeated || _playerState.CurrentUptime >= snapshot)
            {
                return;
            }

            int target = snapshot;
            if (IsDistro("mint") && _runConfig.DistroVersion >= 3)
            {
                target = Mathf.Min(_playerState.MaxUptime, snapshot + 2);
            }

            if (IsDistro("mint") && _runConfig.DistroVersion >= 5)
            {
                target = _playerState.MaxUptime;
            }

            int restored = target - _playerState.CurrentUptime;
            _playerState.CurrentUptime = target;
            Log($"timeshift restored {restored} uptime");
        }

        private bool CheckWinOrLoss()
        {
            return CheckLoss() || CheckWin();
        }

        private bool CheckLoss()
        {
            if (_runLost || _playerState == null || !_playerState.IsDefeated)
            {
                return _runLost;
            }

            _runLost = true;
            _awaitingWaveContinue = false;
            _selectedEnemyIndex = -1;
            _pendingTargetCard = null;
            GameEvents.RaiseEncounterLost(new EncounterLostEvent(CurrentWaveNumber));
            GameEvents.RaiseRunEnded(new RunEndedEvent(playerDied: true, entropyEarned: 0));
            Log("kernel panic: player uptime reached 0");
            StateChanged?.Invoke();
            return true;
        }

        private bool CheckWin()
        {
            if (_awaitingWaveContinue || _runLost || _enemies.Count > 0)
            {
                return _awaitingWaveContinue;
            }

            _awaitingWaveContinue = true;
            _selectedEnemyIndex = -1;
            _pendingTargetCard = null;
            int clearedWave = CurrentWaveNumber;
            GameEvents.RaiseEncounterWon(new EncounterWonEvent(clearedWave));
            GameEvents.RaiseWaveCleared(new WaveClearedEvent(clearedWave));
            Log("wave cleared -> repository available");
            StateChanged?.Invoke();
            return true;
        }

        private void LogPlayIntent(CardInstance card)
        {
            string notes = card?.Definition == null || string.IsNullOrWhiteSpace(card.Definition.DesignNotes)
                ? "no-op scaffold"
                : card.Definition.DesignNotes;
            Log($"{GetCardName(card)} would resolve: {notes}");
        }

        private void Log(string message)
        {
            CombatLog?.Invoke(message);
        }

        private static string GetCardName(CardInstance card)
        {
            if (card?.Definition == null)
            {
                return "--";
            }

            return string.IsNullOrWhiteSpace(card.Definition.DisplayName) ? card.Definition.Id : card.Definition.DisplayName;
        }

        private bool IsCombatPaused => _awaitingWaveContinue || _runLost;

        private enum DelayedCombatEffectKind
        {
            UpdateManagerRepeat,
            TimeshiftRestore
        }

        private struct DelayedCombatEffect
        {
            private DelayedCombatEffect(DelayedCombatEffectKind kind, int amount, int garbageCollectionsRemaining)
            {
                Kind = kind;
                Amount = amount;
                GarbageCollectionsRemaining = garbageCollectionsRemaining;
            }

            public DelayedCombatEffectKind Kind { get; }
            public int Amount { get; }
            public int GarbageCollectionsRemaining { get; set; }

            public static DelayedCombatEffect UpdateManagerRepeat(int amount, int garbageCollectionsRemaining)
            {
                return new DelayedCombatEffect(DelayedCombatEffectKind.UpdateManagerRepeat, amount, garbageCollectionsRemaining);
            }

            public static DelayedCombatEffect TimeshiftRestore(int uptime, int garbageCollectionsRemaining)
            {
                return new DelayedCombatEffect(DelayedCombatEffectKind.TimeshiftRestore, uptime, garbageCollectionsRemaining);
            }
        }
    }
}
