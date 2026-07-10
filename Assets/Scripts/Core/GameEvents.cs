using System;
using System.Collections.Generic;
using KernelPanic.Combat;

namespace KernelPanic.Core
{
    /// <summary>
    /// Provides runtime events for gameplay systems that need to observe combat and run milestones.
    /// </summary>
    public static class GameEvents
    {
        public static event Action<CardPlayedEvent> CardPlayed;
        public static event Action<CardResolvedEvent> CardResolved;
        public static event Action<PhaseChangedEvent> PhaseChanged;
        public static event Action<DamageDealtEvent> DamageDealt;
        public static event Action<OverflowDamageTravelEvent> OverflowDamageTravel;
        public static event Action<CombatantDefeatedEvent> CombatantDefeated;
        public static event Action<CombatantDeathVisualCompletedEvent> CombatantDeathVisualCompleted;
        public static event Action<DeathSpawnedEnemyEvent> DeathSpawnedEnemy;
        public static event Action<EnemyWouldActEvent> EnemyWouldAct;
        public static event Action<EnemyActedEvent> EnemyActed;
        public static event Action<PlayerDamagedEvent> PlayerDamaged;
        public static event Action<EncounterWonEvent> EncounterWon;
        public static event Action<EncounterLostEvent> EncounterLost;
        public static event Action<WaveClearedEvent> WaveCleared;
        public static event Action<WaveAdvancedEvent> WaveAdvanced;
        public static event Action<StatusAppliedEvent> StatusApplied;
        public static event Action<StatusExpiredEvent> StatusExpired;
        public static event Action<StatusCleansedEvent> StatusCleansed;
        public static event Action<RunEndedEvent> RunEnded;
        public static event Action<UbuntuAptUpdatePeekedEvent> UbuntuAptUpdatePeeked;
        public static event Action<FedoraBleedingEdgeEvent> FedoraBleedingEdgeTriggered;
        public static event Action<ArchBtwTurnEndedEvent> ArchBtwTurnEnded;

        public static void RaiseCardPlayed(CardPlayedEvent payload)
        {
            CardPlayed?.Invoke(payload);
        }

        public static void RaiseCardResolved(CardResolvedEvent payload)
        {
            CardResolved?.Invoke(payload);
        }

        public static void RaisePhaseChanged(PhaseChangedEvent payload)
        {
            PhaseChanged?.Invoke(payload);
        }

        public static void RaiseDamageDealt(DamageDealtEvent payload)
        {
            DamageDealt?.Invoke(payload);
        }

        public static void RaiseOverflowDamageTravel(OverflowDamageTravelEvent payload)
        {
            OverflowDamageTravel?.Invoke(payload);
        }

        public static void RaiseCombatantDefeated(CombatantDefeatedEvent payload)
        {
            CombatantDefeated?.Invoke(payload);
        }

        public static void RaiseCombatantDeathVisualCompleted(CombatantDeathVisualCompletedEvent payload)
        {
            CombatantDeathVisualCompleted?.Invoke(payload);
        }

        public static void RaiseDeathSpawnedEnemy(DeathSpawnedEnemyEvent payload)
        {
            DeathSpawnedEnemy?.Invoke(payload);
        }

        public static void RaiseEnemyWouldAct(EnemyWouldActEvent payload)
        {
            EnemyWouldAct?.Invoke(payload);
        }

        public static void RaiseEnemyActed(EnemyActedEvent payload)
        {
            EnemyActed?.Invoke(payload);
        }

        public static void RaisePlayerDamaged(PlayerDamagedEvent payload)
        {
            PlayerDamaged?.Invoke(payload);
        }

        public static void RaiseEncounterWon(EncounterWonEvent payload)
        {
            EncounterWon?.Invoke(payload);
        }

        public static void RaiseEncounterLost(EncounterLostEvent payload)
        {
            EncounterLost?.Invoke(payload);
        }

        public static void RaiseWaveCleared(WaveClearedEvent payload)
        {
            WaveCleared?.Invoke(payload);
        }

        public static void RaiseWaveAdvanced(WaveAdvancedEvent payload)
        {
            WaveAdvanced?.Invoke(payload);
        }

        public static void RaiseStatusApplied(StatusAppliedEvent payload)
        {
            StatusApplied?.Invoke(payload);
        }

        public static void RaiseStatusExpired(StatusExpiredEvent payload)
        {
            StatusExpired?.Invoke(payload);
        }

        public static void RaiseStatusCleansed(StatusCleansedEvent payload)
        {
            StatusCleansed?.Invoke(payload);
        }

        public static void RaiseRunEnded(RunEndedEvent payload)
        {
            RunEnded?.Invoke(payload);
        }

        public static void RaiseUbuntuAptUpdatePeeked(UbuntuAptUpdatePeekedEvent payload)
        {
            UbuntuAptUpdatePeeked?.Invoke(payload);
        }

        public static void RaiseFedoraBleedingEdgeTriggered(FedoraBleedingEdgeEvent payload)
        {
            FedoraBleedingEdgeTriggered?.Invoke(payload);
        }

        public static void RaiseArchBtwTurnEnded(ArchBtwTurnEndedEvent payload)
        {
            ArchBtwTurnEnded?.Invoke(payload);
        }
    }

    /// <summary>
    /// Describes a card that entered a resolution track.
    /// </summary>
    public readonly struct CardPlayedEvent
    {
        public CardPlayedEvent(CardInstance card, ResolutionTrack track)
        {
            Card = card;
            Track = track;
        }

        public CardInstance Card { get; }
        public ResolutionTrack Track { get; }
    }

    /// <summary>
    /// Describes a card whose effects finished resolving.
    /// </summary>
    public readonly struct CardResolvedEvent
    {
        public CardResolvedEvent(CardInstance card, ResolutionTrack track)
        {
            Card = card;
            Track = track;
        }

        public CardInstance Card { get; }
        public ResolutionTrack Track { get; }
    }

    /// <summary>
    /// Describes a transition between combat phases.
    /// </summary>
    public readonly struct PhaseChangedEvent
    {
        public PhaseChangedEvent(TurnPhase previousPhase, TurnPhase nextPhase)
        {
            PreviousPhase = previousPhase;
            NextPhase = nextPhase;
        }

        public TurnPhase PreviousPhase { get; }
        public TurnPhase NextPhase { get; }
    }

    /// <summary>
    /// Describes damage applied from one combatant to another.
    /// </summary>
    public readonly struct DamageDealtEvent
    {
        public DamageDealtEvent(CombatantState source, CombatantState target, int amount, Language language)
            : this(source, target, amount, language, amount, 0, false)
        {
        }

        public DamageDealtEvent(CombatantState source, CombatantState target, int amount, Language language, int incomingAmount, int absorbedAmount, bool wasCritical)
            : this(source, target, amount, language, incomingAmount, absorbedAmount, wasCritical, 0, amount, false)
        {
        }

        public DamageDealtEvent(CombatantState source, CombatantState target, int amount, Language language, int incomingAmount, int absorbedAmount, bool wasCritical, int shieldDamage, int uptimeDamage)
            : this(source, target, amount, language, incomingAmount, absorbedAmount, wasCritical, shieldDamage, uptimeDamage, false)
        {
        }

        public DamageDealtEvent(CombatantState source, CombatantState target, int amount, Language language, int incomingAmount, int absorbedAmount, bool wasCritical, int shieldDamage, int uptimeDamage, bool trueDamage, int archBtwBonusAmount = 0, bool archRollingReleaseSaveTriggered = false)
        {
            Source = source;
            Target = target;
            Amount = amount;
            Language = language;
            IncomingAmount = incomingAmount;
            AbsorbedAmount = absorbedAmount;
            WasCritical = wasCritical;
            ShieldDamage = shieldDamage;
            UptimeDamage = uptimeDamage;
            TrueDamage = trueDamage;
            ArchBtwBonusAmount = archBtwBonusAmount;
            ArchRollingReleaseSaveTriggered = archRollingReleaseSaveTriggered;
        }

        public CombatantState Source { get; }
        public CombatantState Target { get; }
        public int Amount { get; }
        public Language Language { get; }
        public int IncomingAmount { get; }
        public int AbsorbedAmount { get; }
        public bool WasCritical { get; }
        public int ShieldDamage { get; }
        public int UptimeDamage { get; }
        public bool TrueDamage { get; }
        public int ArchBtwBonusAmount { get; }
        public bool ArchRollingReleaseSaveTriggered { get; }
        public bool WasFullyBlocked => IncomingAmount > 0 && UptimeDamage <= 0;
        public bool WasMitigated => ShieldDamage > 0 || AbsorbedAmount > 0 || WasFullyBlocked;
    }

    /// <summary>
    /// Describes a combatant reaching zero uptime through the damage pipeline.
    /// </summary>
    public readonly struct CombatantDefeatedEvent
    {
        public CombatantDefeatedEvent(CombatantState combatant)
        {
            Combatant = combatant;
        }

        public CombatantState Combatant { get; }
    }

    public readonly struct OverflowDamageTravelEvent
    {
        public OverflowDamageTravelEvent(CombatantState source, CombatantState from, CombatantState to, int amount, Language language)
        {
            Source = source;
            From = from;
            To = to;
            Amount = amount;
            Language = language;
        }

        public CombatantState Source { get; }
        public CombatantState From { get; }
        public CombatantState To { get; }
        public int Amount { get; }
        public Language Language { get; }
    }

    public readonly struct CombatantDeathVisualCompletedEvent
    {
        public CombatantDeathVisualCompletedEvent(CombatantState combatant)
        {
            Combatant = combatant;
        }

        public CombatantState Combatant { get; }
    }

    public readonly struct DeathSpawnedEnemyEvent
    {
        public DeathSpawnedEnemyEvent(CombatantState source, EnemyInstance enemy)
        {
            Source = source;
            Enemy = enemy;
        }

        public CombatantState Source { get; }
        public EnemyInstance Enemy { get; }
    }

    /// <summary>
    /// Describes an enemy placeholder reaching its structural action point.
    /// </summary>
    public readonly struct EnemyWouldActEvent
    {
        public EnemyWouldActEvent(EnemyInstance enemy)
        {
            Enemy = enemy;
        }

        public EnemyInstance Enemy { get; }
    }

    public readonly struct EnemyActedEvent
    {
        public EnemyActedEvent(EnemyInstance enemy, EnemyIntent intent)
        {
            Enemy = enemy;
            Intent = intent;
        }

        public EnemyInstance Enemy { get; }
        public EnemyIntent Intent { get; }
    }

    public readonly struct PlayerDamagedEvent
    {
        public PlayerDamagedEvent(EnemyInstance enemy, int amount)
        {
            Enemy = enemy;
            Amount = amount;
        }

        public EnemyInstance Enemy { get; }
        public int Amount { get; }
    }

    public readonly struct EncounterWonEvent
    {
        public EncounterWonEvent(int waveNumber)
        {
            WaveNumber = waveNumber;
        }

        public int WaveNumber { get; }
    }

    public readonly struct EncounterLostEvent
    {
        public EncounterLostEvent(int waveNumber)
        {
            WaveNumber = waveNumber;
        }

        public int WaveNumber { get; }
    }

    /// <summary>
    /// Describes a completed enemy wave.
    /// </summary>
    public readonly struct WaveClearedEvent
    {
        public WaveClearedEvent(int waveNumber)
        {
            WaveNumber = waveNumber;
        }

        public int WaveNumber { get; }
    }

    public readonly struct WaveAdvancedEvent
    {
        public WaveAdvancedEvent(int waveNumber)
        {
            WaveNumber = waveNumber;
        }

        public int WaveNumber { get; }
    }

    public readonly struct StatusAppliedEvent
    {
        public StatusAppliedEvent(CombatantState source, CombatantState target, StatusType statusType, int stacks, int duration)
        {
            Source = source;
            Target = target;
            StatusType = statusType;
            Stacks = stacks;
            Duration = duration;
        }

        public CombatantState Source { get; }
        public CombatantState Target { get; }
        public StatusType StatusType { get; }
        public int Stacks { get; }
        public int Duration { get; }
    }

    public readonly struct StatusExpiredEvent
    {
        public StatusExpiredEvent(CombatantState target, StatusType statusType)
        {
            Target = target;
            StatusType = statusType;
        }

        public CombatantState Target { get; }
        public StatusType StatusType { get; }
    }

    public readonly struct StatusCleansedEvent
    {
        public StatusCleansedEvent(CombatantState target, StatusType statusType)
        {
            Target = target;
            StatusType = statusType;
        }

        public CombatantState Target { get; }
        public StatusType StatusType { get; }
    }

    /// <summary>
    /// Describes the end of a run and its earned meta currency.
    /// </summary>
    public readonly struct RunEndedEvent
    {
        public RunEndedEvent(bool playerDied, int entropyEarned)
        {
            PlayerDied = playerDied;
            EntropyEarned = entropyEarned;
        }

        public bool PlayerDied { get; }
        public int EntropyEarned { get; }
    }

    /// <summary>
    /// Describes an Ubuntu "apt update" peek-and-pick: the cards inspected at the top of the draw
    /// pile and which one was added to hand.
    /// </summary>
    public readonly struct UbuntuAptUpdatePeekedEvent
    {
        public UbuntuAptUpdatePeekedEvent(IReadOnlyList<PeekedCardInfo> peeked, CardInstance chosen, bool wasTie, int lookCount)
        {
            Peeked = peeked;
            Chosen = chosen;
            WasTie = wasTie;
            LookCount = lookCount;
        }

        public IReadOnlyList<PeekedCardInfo> Peeked { get; }
        public CardInstance Chosen { get; }
        public bool WasTie { get; }
        public int LookCount { get; }
    }

    /// <summary>
    /// Describes the outcome of Fedora's "Bleeding Edge" crash roll for a bonus-eligible card.
    /// </summary>
    public readonly struct FedoraBleedingEdgeEvent
    {
        public FedoraBleedingEdgeEvent(CardInstance card, bool crashed, bool firstBonusThisTurn, int damageMultiplierPercent, float crashChanceRolled, float crashChanceAfter)
        {
            Card = card;
            Crashed = crashed;
            FirstBonusThisTurn = firstBonusThisTurn;
            DamageMultiplierPercent = damageMultiplierPercent;
            CrashChanceRolled = crashChanceRolled;
            CrashChanceAfter = crashChanceAfter;
        }

        public CardInstance Card { get; }
        public bool Crashed { get; }
        public bool FirstBonusThisTurn { get; }
        public int DamageMultiplierPercent { get; }
        public float CrashChanceRolled { get; }
        public float CrashChanceAfter { get; }
    }

    /// <summary>
    /// Describes the end-of-turn resolution of Arch's btw stacks: whether they reset or persisted.
    /// </summary>
    public readonly struct ArchBtwTurnEndedEvent
    {
        public ArchBtwTurnEndedEvent(int stacksBefore, bool persisted)
        {
            StacksBefore = stacksBefore;
            Persisted = persisted;
        }

        public int StacksBefore { get; }
        public bool Persisted { get; }
    }
}
