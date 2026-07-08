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

        private readonly DeckController deckController = new();
        private readonly InterpreterQueue interpreterQueue = new();
        private readonly LazyStack lazyStack = new();
        private readonly NativeTrack nativeTrack = new();
        private readonly StatusEffectController statusEffects = new();
        private readonly DamagePipeline damagePipeline = new();
        private readonly List<EnemyInstance> enemies = new();
        private readonly HashSet<string> loggedEffectTodos = new();
        private readonly List<CardDefinition> generatedCardPool = new();
        private readonly List<DelayedCombatEffect> delayedEffects = new();
        private RunManager runManager;
        private RunConfig runConfig;
        private HandController handController;
        private CombatantState playerState;
        private int selectedEnemyIndex = -1;
        private CardInstance pendingTargetCard;
        private bool awaitingWaveContinue;
        private bool runLost;
        private bool skipNextAllocateDraw;
        private bool ubuntuEmptyHandRefillUsed;
        private int fedoraCardsDiscountedThisTurn;
        private int fedoraCrashChance = 10;
        private int cardsPlayedThisTurn;
        private int javaCardsPlayedThisCombat;
        private int javaCardsDiscountThisTurn;
        private int rawhideBonusCharges;
        private int queuedRepeatCharges;
        private int nextTurnCycleBonus;
        private Coroutine phaseCoroutine;

        public TurnPhase CurrentPhase => currentPhase;
        public RunConfig RunConfig => runConfig;
        public DeckController DeckController => deckController;
        public HandController HandController => handController;
        public CombatantState PlayerState => playerState;
        public InterpreterQueue InterpreterQueue => interpreterQueue;
        public LazyStack LazyStack => lazyStack;
        public NativeTrack NativeTrack => nativeTrack;
        public DamagePipeline DamagePipeline => damagePipeline;
        public IReadOnlyList<EnemyInstance> Enemies => enemies;
        public int SelectedEnemyIndex => selectedEnemyIndex;
        public CardInstance PendingTargetCard => pendingTargetCard;
        public bool AwaitingWaveContinue => awaitingWaveContinue;
        public bool RunLost => runLost;
        public int CurrentWaveNumber => runManager == null ? 1 : runManager.CurrentWaveNumber;

        public event Action StateChanged;
        public event Action<string> CombatLog;

        private void Awake()
        {
            runManager = GetComponent<RunManager>();
        }

        public void StartCombat()
        {
            if (runConfig == null)
            {
                Debug.LogWarning("CombatManager.StartCombat called without a RunConfig.");
                return;
            }

            SetPhase(TurnPhase.Boot);
        }

        public void StartCombat(RunConfig config)
        {
            runConfig = config;
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
                pendingTargetCard = null;
                selectedEnemyIndex = -1;
                SetPhase(TurnPhase.Interpret);
            }
        }

        public void ContinueToNextWave()
        {
            if (!awaitingWaveContinue || runLost)
            {
                return;
            }

            awaitingWaveContinue = false;
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

            if (index < 0 || index >= enemies.Count)
            {
                selectedEnemyIndex = -1;
                StateChanged?.Invoke();
                return;
            }

            selectedEnemyIndex = index;
            if (pendingTargetCard != null)
            {
                CardInstance card = pendingTargetCard;
                pendingTargetCard = null;
                PlayCard(card);
                return;
            }

            StateChanged?.Invoke();
        }

        public bool PlayCard(CardInstance card)
        {
            if (currentPhase != TurnPhase.Execute || IsCombatPaused || card == null || handController == null || playerState == null)
            {
                return false;
            }

            if (card.Definition.IsToken)
            {
                Log($"{GetCardName(card)} is unplayable");
                return false;
            }

            bool fedoraBonus = CanApplyFedoraBonus();
            int cost = GetEffectiveCardCost(card);
            if (fedoraBonus)
            {
                cost = Mathf.Max(0, cost - 1);
            }

            if (playerState.Cycles < cost)
            {
                Log($"not enough cycles for {GetCardName(card)}");
                return false;
            }

            if (RequiresTarget(card) && selectedEnemyIndex < 0)
            {
                pendingTargetCard = card;
                Log($"select target for {GetCardName(card)}");
                StateChanged?.Invoke();
                return false;
            }

            if (!handController.Remove(card))
            {
                return false;
            }

            playerState.Cycles -= cost;
            ResolutionTrack track = card.Definition.ResolutionTrack;
            card.MarkPlayedThisTurn(cardsPlayedThisTurn == 0);
            cardsPlayedThisTurn++;
            if (card.Definition.Language == Language.Java)
            {
                javaCardsPlayedThisCombat++;
            }

            GameEvents.RaiseCardPlayed(new CardPlayedEvent(card, track));
            LogPlayIntent(card);
            card.SetTargetSnapshot(CaptureTargets(card));

            if (fedoraBonus)
            {
                fedoraCardsDiscountedThisTurn++;
                bool rawhideChargeUsed = rawhideBonusCharges > 0;
                if (rawhideChargeUsed)
                {
                    rawhideBonusCharges--;
                }

                if (!IsCrashImmune(card) && RandomRoll.RollRange(1, 100, new RollContext(playerState)) <= fedoraCrashChance)
                {
                    fedoraCrashChance = 10;
                    if (IsDistro("fedora") && runConfig.DistroVersion >= 4)
                    {
                        playerState.Cycles += 1;
                    }

                    deckController.Discard(card);
                    pendingTargetCard = null;
                    selectedEnemyIndex = -1;
                    Log($"{GetCardName(card)} crashed under bleeding edge");
                    StateChanged?.Invoke();
                    return true;
                }

                fedoraCrashChance = Mathf.Min(90, fedoraCrashChance + 10);
                card.MarkFedoraNonCrashBonus();
                playerState.DamageMultiplierPercent = runConfig.DistroVersion >= 2 ? 175 : 150;
                ApplyFedoraGrowth(card, rawhideChargeUsed);
            }

            switch (track)
            {
                case ResolutionTrack.InterpreterQueue:
                    card.MarkQueued();
                    interpreterQueue.Enqueue(card);
                    break;
                case ResolutionTrack.LazyStack:
                    lazyStack.Enqueue(card);
                    break;
                default:
                    ExecuteCardEffects(card, ResolveTargets(card));
                    ResolveCardRiders(card);
                    deckController.Discard(card);
                    GameEvents.RaiseCardResolved(new CardResolvedEvent(card, ResolutionTrack.Native));
                    selectedEnemyIndex = -1;
                    break;
            }

            playerState.DamageMultiplierPercent = 100;

            pendingTargetCard = null;
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
                cost -= javaCardsPlayedThisCombat + javaCardsDiscountThisTurn;
            }

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
            if (loggedEffectTodos.Add(key))
            {
                Log(message);
            }
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
            nextTurnCycleBonus += Mathf.Max(0, amount);
        }

        public void AddQueuedRepeatCharges(int amount)
        {
            queuedRepeatCharges += Mathf.Max(0, amount);
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

            javaCardsDiscountThisTurn += safeAmount;
            ReportEffectResult($"Java cards cost -{safeAmount} this turn");
        }

        public int ConsumeQueuedResolutionCount(CardInstance card)
        {
            if (queuedRepeatCharges <= 0 || card == null)
            {
                return 1;
            }

            queuedRepeatCharges--;
            return 2;
        }

        public void GrantRawhideBonus(int charges)
        {
            rawhideBonusCharges += Mathf.Max(0, charges);
        }

        public void AddIncomingAttackHalfCharges(int charges)
        {
            if (playerState == null)
            {
                return;
            }

            playerState.IncomingAttackHalfCharges += Mathf.Max(0, charges);
            ReportEffectResult($"next {charges} enemy attack(s) halved");
        }

        public void ScheduleUpdateManagerRepeat(int damageAmount)
        {
            delayedEffects.Add(DelayedCombatEffect.UpdateManagerRepeat(damageAmount, 2));
            ReportEffectResult("update manager scheduled repeat");
        }

        public void ScheduleTimeshiftRestore(int uptime)
        {
            delayedEffects.Add(DelayedCombatEffect.TimeshiftRestore(uptime, 2));
            ReportEffectResult($"timeshift snapshot recorded {uptime} uptime");
        }

        public bool TryCreateGeneratedCardById(string id, out CardInstance generatedCard)
        {
            generatedCard = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            IReadOnlyList<CardDefinition> source = generatedCardPool.Count > 0 ? generatedCardPool : runConfig?.StartingDeck;
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
            generatedCardPool.Clear();
            if (cardPool == null)
            {
                return;
            }

            foreach (CardDefinition card in cardPool)
            {
                if (card != null)
                {
                    generatedCardPool.Add(card);
                }
            }
        }

        public bool TryCreateGeneratedCard(Language language, Rarity rarity, out CardInstance generatedCard)
        {
            generatedCard = null;

            List<CardDefinition> pool = new();
            HashSet<string> seenIds = new();
            IReadOnlyList<CardDefinition> source = generatedCardPool.Count > 0 ? generatedCardPool : runConfig?.StartingDeck;
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

            int index = RandomRoll.RollRange(0, pool.Count - 1, new RollContext(playerState));
            CardDefinition selected = pool[index];
            generatedCard = new CardInstance(selected)
            {
                TemporaryCostDelta = -selected.CycleCost
            };
            return true;
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

            if (phaseCoroutine != null)
            {
                StopCoroutine(phaseCoroutine);
            }

            phaseCoroutine = StartCoroutine(EnterPhase(nextPhase));
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
            runLost = false;
            awaitingWaveContinue = false;
            loggedEffectTodos.Clear();
            ubuntuEmptyHandRefillUsed = false;
            fedoraCrashChance = 10;
            fedoraCardsDiscountedThisTurn = 0;
            cardsPlayedThisTurn = 0;
            javaCardsPlayedThisCombat = 0;
            javaCardsDiscountThisTurn = 0;
            rawhideBonusCharges = 0;
            queuedRepeatCharges = 0;
            nextTurnCycleBonus = 0;
            delayedEffects.Clear();
            RandomRoll.Seed(runConfig.RunSeed);
            playerState = new CombatantState(runManager.EffectiveMaxUptime(), runManager.EffectiveRam(), runManager.EffectiveMaxCycles());
            ApplyVersionState(playerState);
            yield return StartWaveSequenced(preservePlayerUptime: false);
            Log($"booted {runConfig.Distro.DisplayName} with {runManager.RunDeck.Count} cards");
            StateChanged?.Invoke();
        }

        private IEnumerator StartWaveSequenced(bool preservePlayerUptime)
        {
            int carriedUptime = playerState == null ? 0 : playerState.CurrentUptime;
            if (!preservePlayerUptime || playerState == null)
            {
                playerState = new CombatantState(runManager.EffectiveMaxUptime(), runManager.EffectiveRam(), runManager.EffectiveMaxCycles());
                ApplyVersionState(playerState);
            }
            else
            {
                playerState.MaxUptime = runManager.EffectiveMaxUptime();
                playerState.CurrentUptime = Mathf.Clamp(carriedUptime, 1, playerState.MaxUptime);
                playerState.Ram = runManager.EffectiveRam();
                playerState.MaxCycles = runManager.EffectiveMaxCycles();
                playerState.Cycles = 0;
                playerState.Shield = 0;
                playerState.IncomingAttackHalfCharges = 0;
                playerState.IsDefeated = false;
                playerState.MutableStatuses.Clear();
                ApplyVersionState(playerState);
            }

            handController = new HandController(playerState.Ram);
            selectedEnemyIndex = -1;
            pendingTargetCard = null;

            List<CardInstance> startingCards = new();
            for (int i = 0; i < runManager.RunDeck.Count; i++)
            {
                CardInstance card = runManager.RunDeck[i];
                if (card != null)
                {
                    card.ResetCombatState();
                    startingCards.Add(card);
                }
            }

            deckController.Initialize(startingCards);
            interpreterQueue.Clear();
            enemies.Clear();
            SpawnStructuralEnemies();
            PickEnemyIntents();
            StateChanged?.Invoke();
            yield return DrawOpeningHandSequenced();
            skipNextAllocateDraw = true;
        }

        private IEnumerator AllocateTurnSequenced()
        {
            if (CheckLoss())
            {
                yield break;
            }

            playerState.Cycles = playerState.MaxCycles;
            fedoraCardsDiscountedThisTurn = 0;
            cardsPlayedThisTurn = 0;
            javaCardsDiscountThisTurn = 0;
            if (nextTurnCycleBonus > 0)
            {
                playerState.Cycles += nextTurnCycleBonus;
                Log($"deferred cycle grant: +{nextTurnCycleBonus}");
                nextTurnCycleBonus = 0;
            }
            statusEffects.Tick(playerState, StatusTickTiming.StartOfTurn, playerState, damagePipeline);
            StateChanged?.Invoke();
            if (CheckLoss())
            {
                yield break;
            }

            TryApplyUbuntuAptUpdate();

            if (skipNextAllocateDraw)
            {
                skipNextAllocateDraw = false;
            }
            else
            {
                yield return DrawForTurnSequenced();
            }

            StateChanged?.Invoke();
        }

        private IEnumerator InterpretQueueSequenced()
        {
            while (interpreterQueue.TryDequeue(out CardInstance card))
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
                deckController.Discard(card);
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
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyInstance enemy = enemies[i];
                if (enemy.State.IsDefeated)
                {
                    continue;
                }

                statusEffects.Tick(enemy.State, StatusTickTiming.StartOfTurn, playerState, damagePipeline);
                RemoveDefeatedEnemies();
                if (CheckWinOrLoss())
                {
                    yield break;
                }

                if (enemy.State.IsDefeated)
                {
                    continue;
                }

                Log($"{enemy.Name} executes {enemy.CurrentIntent.DisplayText}");
                GameEvents.RaiseEnemyWouldAct(new EnemyWouldActEvent(enemy));
                StateChanged?.Invoke();
                yield return Wait(CombatTuning.EnemyTelegraphDelaySeconds);

                ExecuteEnemyIntent(enemy);
                GameEvents.RaiseEnemyActed(new EnemyActedEvent(enemy, enemy.CurrentIntent));
                StateChanged?.Invoke();
                if (CheckLoss())
                {
                    yield break;
                }

                yield return Wait(CombatTuning.EnemyActionDelaySeconds);
            }

            PickEnemyIntents();
            StateChanged?.Invoke();
        }

        private void GarbageCollect()
        {
            selectedEnemyIndex = -1;
            pendingTargetCard = null;
            statusEffects.Tick(playerState, StatusTickTiming.EndOfTurn, playerState, damagePipeline);
            if (CheckLoss())
            {
                return;
            }

            ResolveDelayedEndOfTurnEffects();
            if (CheckWinOrLoss())
            {
                return;
            }

            RemoveDefeatedEnemies();
            CheckWinOrLoss();
            StateChanged?.Invoke();
        }

        private IEnumerator DrawOpeningHandSequenced()
        {
            int openingDraw = Mathf.Min(CombatTuning.OpeningHandSize, playerState.Ram);
            yield return DrawCardsToHandSequenced(openingDraw, "opening hand");
        }

        private IEnumerator DrawForTurnSequenced()
        {
            int requested = CombatTuning.DrawPerTurn;
            if (CombatTuning.MinimumHandFloor > 0 && handController.Cards.Count < CombatTuning.MinimumHandFloor && deckController.AvailableToDrawCount > 0)
            {
                requested = Mathf.Max(requested, CombatTuning.MinimumHandFloor - handController.Cards.Count);
            }

            yield return DrawCardsToHandSequenced(requested, "turn draw");
        }

        private IEnumerator DrawCardsToHandSequenced(int requestedCount, string label)
        {
            if (handController == null || playerState == null || requestedCount <= 0)
            {
                yield break;
            }

            int room = handController.RemainingRam;
            if (room <= 0)
            {
                Log($"{label}: hand full");
                yield break;
            }

            IReadOnlyList<CardInstance> drawn = deckController.Draw(requestedCount);
            int added = 0;
            for (int i = 0; i < drawn.Count; i++)
            {
                if (handController.Add(drawn[i]))
                {
                    added++;
                    StateChanged?.Invoke();
                    yield return Wait(CombatTuning.CardDrawDelaySeconds);
                }
                else
                {
                    deckController.AddToDrawPile(drawn[i], shuffle: false);
                }
            }

            if (added == 0 && deckController.AvailableToDrawCount == 0)
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
            if (!IsDistro("ubuntu") || handController == null || playerState == null || handController.RemainingRam <= 0)
            {
                return;
            }

            int lookCount = runConfig.DistroVersion >= 2 ? 3 : 2;
            if (!deckController.TryDrawCheapestFromTop(lookCount, out CardInstance card))
            {
                return;
            }

            if (runConfig.DistroVersion >= 4)
            {
                card.TemporaryCostDelta -= 1;
            }

            if (handController.Add(card))
            {
                Log($"apt update: staged {GetCardName(card)} from top {lookCount}");
            }
            else
            {
                deckController.AddToDrawPile(card, shuffle: false);
            }
        }

        private void TryApplyUbuntuEmptyHandRefill()
        {
            if (!IsDistro("ubuntu") || runConfig.DistroVersion < 5 || ubuntuEmptyHandRefillUsed || handController == null || handController.Cards.Count > 0)
            {
                return;
            }

            ubuntuEmptyHandRefillUsed = true;
            StartCoroutine(DrawCardsToHandSequenced(playerState.Ram, "ubuntu 24.04 refill"));
        }

        private bool CanApplyFedoraBonus()
        {
            return CanApplyFedoraPassiveBonus() || rawhideBonusCharges > 0;
        }

        private void DecayJavaWarmupForNextWave()
        {
            if (javaCardsPlayedThisCombat <= 0)
            {
                return;
            }

            javaCardsPlayedThisCombat = Mathf.Max(0, javaCardsPlayedThisCombat - 1);
            Log($"Java JIT cooled: discount now -{javaCardsPlayedThisCombat}");
        }

        private bool CanApplyFedoraPassiveBonus()
        {
            if (!IsDistro("fedora") || playerState == null)
            {
                return false;
            }

            int limit = runConfig.DistroVersion >= 5 ? 2 : 1;
            return fedoraCardsDiscountedThisTurn < limit;
        }

        private void ApplyFedoraGrowth(CardInstance card, bool rawhideChargeUsed)
        {
            if (!IsDistro("fedora") || runConfig.DistroVersion < 3 || card == null || !card.WasFirstCardThisTurn)
            {
                return;
            }

            int growth = rawhideChargeUsed ? 2 : 1;
            if (card.ApplyCombatMagnitudeBonus(growth))
            {
                Log($"rawhide growth: {GetCardName(card)} +{growth} effect");
            }
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

            state.forceMaxRolls = IsDistro("mint");
            state.IgnoreDamageMultipliers = IsDistro("mint");
            state.AllowFlatDamageBuffs = IsDistro("mint") && runConfig.DistroVersion >= 4;
            state.FlatEffectBonus = IsDistro("mint") && runConfig.DistroVersion >= 2 ? 2 : 0;
            state.DamageMultiplierPercent = 100;
        }

        private bool IsDistro(string id)
        {
            return string.Equals(runConfig?.Distro?.Id, id, StringComparison.OrdinalIgnoreCase);
        }

        private CombatContext BuildContext(CardInstance card, IReadOnlyList<CombatantState> targets)
        {
            return new CombatContext(card, playerState, targets, this, damagePipeline, statusEffects, deckController, handController, enemies);
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
            if (card == null || card.DrawRider <= 0 || handController == null || playerState == null)
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

            if (selectedEnemyIndex >= 0 && selectedEnemyIndex < enemies.Count && !enemies[selectedEnemyIndex].State.IsDefeated)
            {
                return new[] { enemies[selectedEnemyIndex].State };
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
            return enemies
                .Where(enemy => enemy.State != null && !enemy.State.IsDefeated)
                .Select(enemy => enemy.State)
                .ToList();
        }

        private CombatantState FirstLivingEnemyState()
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].State != null && !enemies[i].State.IsDefeated)
                {
                    return enemies[i].State;
                }
            }

            return null;
        }

        private void RemoveDefeatedEnemies()
        {
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                if (enemies[i].State.IsDefeated)
                {
                    Log($"{enemies[i].Name} exited");
                    runManager?.AddBits(CombatTuning.BitsPerKill);
                    enemies.RemoveAt(i);
                }
            }

            if (selectedEnemyIndex >= enemies.Count)
            {
                selectedEnemyIndex = -1;
            }
        }

        private void SpawnStructuralEnemies()
        {
            int wave = CurrentWaveNumber;
            int count = Mathf.Clamp(CombatTuning.BaseEnemiesPerWave + ((wave - 1) * CombatTuning.AdditionalEnemiesPerWave), CombatTuning.BaseEnemiesPerWave, CombatTuning.MaxEnemiesPerWave);
            int waveUptimeBonus = Mathf.Max(0, wave - 1) * CombatTuning.EnemyUptimeGrowthPerWave;
            for (int i = 0; i < count; i++)
            {
                // TODO: Replace this low-HP additive wave stub with the real encounter/difficulty system.
                int baseUptime = RandomRoll.RollRange(CombatTuning.BaseEnemyUptimeMin, CombatTuning.BaseEnemyUptimeMax, RollContext.None);
                int uptime = baseUptime + waveUptimeBonus;
                enemies.Add(new EnemyInstance("zombie_process", uptime, CreateZombieProcessIntentPool(wave, i)));
            }
        }

        private static IReadOnlyList<EnemyIntent> CreateZombieProcessIntentPool(int wave, int slotIndex)
        {
            // TODO: Replace this code-shaped pool with EnemyDefinition-provided intent data.
            int attack = GetZombieProcessAttack(wave, slotIndex);
            return new[]
            {
                new EnemyIntent(EnemyIntentKind.Attack, attack, attack, Language.C, "attack", ">"),
                new EnemyIntent(EnemyIntentKind.Defend, 3 + wave, 3 + wave, Language.C, "defend", "#"),
                new EnemyIntent(EnemyIntentKind.StatusAttack, CombatTuning.EnemyStatusAttackDamage, CombatTuning.EnemyStatusAttackDamage, Language.C, "leak", "!", false, StatusType.MemoryLeak, 1, CombatTuning.EnemyStatusDuration)
            };
        }

        private static int GetZombieProcessAttack(int wave, int slotIndex)
        {
            int safeWave = Mathf.Max(1, wave);
            int waveGrowth = (safeWave - 1) / Mathf.Max(1, CombatTuning.EnemyAttackGrowthEveryWaves);
            int slotVariance = CombatTuning.EnemySlotAttackVariance <= 0 ? 0 : slotIndex % (CombatTuning.EnemySlotAttackVariance + 1);
            return CombatTuning.BaseEnemyAttack + waveGrowth + slotVariance;
        }

        private void PickEnemyIntents()
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                if (!enemies[i].State.IsDefeated)
                {
                    enemies[i].PickNextIntent();
                }
            }
        }

        private void ExecuteEnemyIntent(EnemyInstance enemy)
        {
            EnemyIntent intent = enemy.CurrentIntent;
            int value = intent.MinValue == intent.MaxValue
                ? intent.MinValue
                : RandomRoll.RollRange(intent.MinValue, intent.MaxValue, new RollContext(enemy.State));

            switch (intent.Kind)
            {
                case EnemyIntentKind.Attack:
                    value = ApplyIncomingAttackMitigation(value);
                    DamageResult attackResult = damagePipeline.DealDamage(new DamageRequest(enemy.State, playerState, value, intent.DamageType, intent.TrueDamage, false));
                    GameEvents.RaisePlayerDamaged(new PlayerDamagedEvent(enemy, attackResult.FinalAmount));
                    break;
                case EnemyIntentKind.Defend:
                    enemy.State.Shield += value;
                    break;
                case EnemyIntentKind.StatusAttack:
                    DamageResult statusResult = damagePipeline.DealDamage(new DamageRequest(enemy.State, playerState, value, intent.DamageType, intent.TrueDamage, false));
                    GameEvents.RaisePlayerDamaged(new PlayerDamagedEvent(enemy, statusResult.FinalAmount));
                    if (intent.StatusStacks > 0)
                    {
                        statusEffects.Apply(playerState, intent.StatusType, intent.StatusStacks, intent.StatusDuration, enemy.State, skipNextTick: true);
                    }
                    break;
                case EnemyIntentKind.Buff:
                    Log("TODO: buff intent needs enemy buff/status support.");
                    break;
                case EnemyIntentKind.Special:
                    Log("TODO: special intent needs enemy-specific behavior hooks.");
                    break;
            }
        }

        private int ApplyIncomingAttackMitigation(int value)
        {
            if (playerState == null || playerState.IncomingAttackHalfCharges <= 0)
            {
                return value;
            }

            playerState.IncomingAttackHalfCharges--;
            int mitigated = Mathf.CeilToInt(Mathf.Max(0, value) * 0.5f);
            Log($"SELinux enforcing halved attack {value}->{mitigated}");
            return mitigated;
        }

        private void ResolveDelayedEndOfTurnEffects()
        {
            for (int i = delayedEffects.Count - 1; i >= 0; i--)
            {
                DelayedCombatEffect effect = delayedEffects[i];
                effect.GarbageCollectionsRemaining--;
                if (effect.GarbageCollectionsRemaining > 0)
                {
                    delayedEffects[i] = effect;
                    continue;
                }

                delayedEffects.RemoveAt(i);
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
                damagePipeline.DealDamage(new DamageRequest(playerState, targets[i], amount, language, false, false));
            }

            Log($"{label}: dealt {amount} to all enemies");
            RemoveDefeatedEnemies();
        }

        private void RestoreTimeshift(int snapshot)
        {
            if (playerState == null || playerState.IsDefeated || playerState.CurrentUptime >= snapshot)
            {
                return;
            }

            int target = snapshot;
            if (IsDistro("mint") && runConfig.DistroVersion >= 3)
            {
                target = Mathf.Min(playerState.MaxUptime, snapshot + 2);
            }

            if (IsDistro("mint") && runConfig.DistroVersion >= 5)
            {
                target = playerState.MaxUptime;
            }

            int restored = target - playerState.CurrentUptime;
            playerState.CurrentUptime = target;
            Log($"timeshift restored {restored} uptime");
        }

        private bool CheckWinOrLoss()
        {
            return CheckLoss() || CheckWin();
        }

        private bool CheckLoss()
        {
            if (runLost || playerState == null || !playerState.IsDefeated)
            {
                return runLost;
            }

            runLost = true;
            awaitingWaveContinue = false;
            selectedEnemyIndex = -1;
            pendingTargetCard = null;
            GameEvents.RaiseEncounterLost(new EncounterLostEvent(CurrentWaveNumber));
            GameEvents.RaiseRunEnded(new RunEndedEvent(playerDied: true, entropyEarned: 0));
            Log("kernel panic: player uptime reached 0");
            StateChanged?.Invoke();
            return true;
        }

        private bool CheckWin()
        {
            if (awaitingWaveContinue || runLost || enemies.Count > 0)
            {
                return awaitingWaveContinue;
            }

            awaitingWaveContinue = true;
            selectedEnemyIndex = -1;
            pendingTargetCard = null;
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

        private bool IsCombatPaused => awaitingWaveContinue || runLost;

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
