using System.Collections.Generic;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Runtime-only enemy placeholder used until encounter and EnemyDefinition assets are wired.
    /// </summary>
    public sealed class EnemyInstance
    {
        private readonly List<EnemyIntent> _intentPool = new();

        public EnemyInstance(string name, int maxUptime, IEnumerable<EnemyIntent> intentPool)
            : this(new EnemyArchetypeDescriptor(name, name, maxUptime, maxUptime, EnemyBehaviorFlags.None, new List<EnemyIntent>(intentPool ?? System.Array.Empty<EnemyIntent>())), maxUptime)
        {
        }

        public EnemyInstance(EnemyArchetypeDescriptor archetype, int maxUptime)
        {
            Archetype = archetype ?? EnemyArchetypeCatalog.Get("zombie_process");
            Name = Archetype.DisplayName;
            State = new CombatantState(maxUptime, 0, 0);
            if (Archetype.IntentPool != null)
            {
                _intentPool.AddRange(Archetype.IntentPool);
            }

            CountdownRemaining = HasBehavior(EnemyBehaviorFlags.Countdown) ? EnemyArchetypeCatalog.CronInitialCountdown : 0;
        }

        public string Name { get; }
        public EnemyArchetypeDescriptor Archetype { get; }
        public string ArchetypeId => Archetype.Id;
        public int CurrentUptime => State.CurrentUptime;
        public int MaxUptime => State.MaxUptime;
        public CombatantState State { get; }
        public IReadOnlyList<EnemyIntent> IntentPool => _intentPool;
        public EnemyIntent CurrentIntent { get; private set; }
        public int TurnsAlive { get; private set; }
        public int CountdownRemaining { get; private set; }
        public bool IntentRevealed { get; private set; }
        public bool HasRevived { get; private set; }
        public bool PendingRevive { get; private set; }
        public bool Reaped { get; private set; }
        public bool LethalHitThisTurn { get; private set; }
        public bool HasPendingMarker => PendingRevive || HasRevived;
        public EnemyIntent DisplayIntent => BuildDisplayIntent();

        public void PickNextIntent()
        {
            if (PendingRevive)
            {
                CurrentIntent = new EnemyIntent(EnemyIntentKind.Special, 0, 0, KernelPanic.Core.Language.C, "reviving", "~", false, KernelPanic.Core.StatusType.MemoryLeak, 0, -1, "next");
                return;
            }

            if (HasBehavior(EnemyBehaviorFlags.Countdown))
            {
                CurrentIntent = CountdownRemaining > 0
                    ? new EnemyIntent(EnemyIntentKind.Special, CountdownRemaining, CountdownRemaining, KernelPanic.Core.Language.C, "countdown", "@")
                    : _intentPool.Count > 0 ? _intentPool[0] : default;
                return;
            }

            if (_intentPool.Count == 0)
            {
                CurrentIntent = default;
                return;
            }

            int index = RandomRoll.RollRange(0, _intentPool.Count - 1, new RollContext(State));
            CurrentIntent = _intentPool[index];
            if (HasBehavior(EnemyBehaviorFlags.Grow))
            {
                int growth = TurnsAlive * EnemyArchetypeCatalog.MemoryLeakAttackGrowthPerTurn;
                CurrentIntent = CurrentIntent.WithValues(CurrentIntent.MinValue + growth, CurrentIntent.MaxValue + growth, "growing leak");
            }
        }

        public bool HasBehavior(EnemyBehaviorFlags behavior)
        {
            return (Archetype.BehaviorFlags & behavior) != 0;
        }

        public void MarkDamaged()
        {
            if (HasBehavior(EnemyBehaviorFlags.ObfuscateIntent))
            {
                IntentRevealed = true;
            }
        }

        public void MarkTurnSurvived()
        {
            TurnsAlive++;
        }

        public void ResetTurnLethalMarker()
        {
            LethalHitThisTurn = false;
        }

        public void MarkLethalHit()
        {
            LethalHitThisTurn = true;
        }

        public void MarkPendingRevive()
        {
            PendingRevive = true;
            State.IsDefeated = false;
            State.CurrentUptime = 0;
            State.Shield = 0;
            CurrentIntent = new EnemyIntent(EnemyIntentKind.Special, 0, 0, KernelPanic.Core.Language.C, "reviving", "~", false, KernelPanic.Core.StatusType.MemoryLeak, 0, -1, "next");
        }

        public void ReviveAtHalfUptime()
        {
            PendingRevive = false;
            HasRevived = true;
            State.IsDefeated = false;
            State.CurrentUptime = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.CeilToInt(State.MaxUptime * 0.5f));
            PickNextIntent();
        }

        public void MarkReaped()
        {
            PendingRevive = false;
            Reaped = true;
            State.IsDefeated = true;
            State.CurrentUptime = 0;
        }

        public void AdvanceCountdownAfterAction()
        {
            if (!HasBehavior(EnemyBehaviorFlags.Countdown))
            {
                return;
            }

            CountdownRemaining = CurrentIntent.Kind == EnemyIntentKind.Attack
                ? EnemyArchetypeCatalog.CronInitialCountdown
                : UnityEngine.Mathf.Max(0, CountdownRemaining - 1);
        }

        private EnemyIntent BuildDisplayIntent()
        {
            if (PendingRevive)
            {
                return new EnemyIntent(EnemyIntentKind.Special, 0, 0, KernelPanic.Core.Language.C, "reviving", "~", false, KernelPanic.Core.StatusType.MemoryLeak, 0, -1, "next");
            }

            if (HasBehavior(EnemyBehaviorFlags.ObfuscateIntent) && !IntentRevealed)
            {
                return new EnemyIntent(EnemyIntentKind.Special, 0, 0, KernelPanic.Core.Language.C, "unknown", "?", false, KernelPanic.Core.StatusType.MemoryLeak, 0, -1, "?");
            }

            return CurrentIntent;
        }
    }
}
