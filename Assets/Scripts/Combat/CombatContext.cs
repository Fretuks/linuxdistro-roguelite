using System.Collections.Generic;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Carries runtime combat references needed by card effects during resolution.
    /// </summary>
    public sealed class CombatContext
    {
        public CombatContext(
            CardInstance card,
            CombatantState source,
            IReadOnlyList<CombatantState> targets,
            CombatManager combatManager,
            DamagePipeline damagePipeline,
            StatusEffectController statusEffects,
            DeckController deckController,
            HandController handController,
            IReadOnlyList<EnemyInstance> enemies)
        {
            Card = card;
            Source = source;
            Targets = targets;
            CombatManager = combatManager;
            DamagePipeline = damagePipeline;
            StatusEffects = statusEffects;
            DeckController = deckController;
            HandController = handController;
            Enemies = enemies;
        }

        public CardInstance Card { get; }
        public CombatantState Source { get; }
        public IReadOnlyList<CombatantState> Targets { get; }
        public CombatManager CombatManager { get; }
        public DamagePipeline DamagePipeline { get; }
        public StatusEffectController StatusEffects { get; }
        public DeckController DeckController { get; }
        public HandController HandController { get; }
        public IReadOnlyList<EnemyInstance> Enemies { get; }

        internal bool SegfaultRecoilRolled { get; set; }
    }
}
