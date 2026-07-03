using System;
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
        public static event Action<WaveClearedEvent> WaveCleared;
        public static event Action<RunEndedEvent> RunEnded;

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

        public static void RaiseWaveCleared(WaveClearedEvent payload)
        {
            WaveCleared?.Invoke(payload);
        }

        public static void RaiseRunEnded(RunEndedEvent payload)
        {
            RunEnded?.Invoke(payload);
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
        {
            Source = source;
            Target = target;
            Amount = amount;
            Language = language;
        }

        public CombatantState Source { get; }
        public CombatantState Target { get; }
        public int Amount { get; }
        public Language Language { get; }
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
}
