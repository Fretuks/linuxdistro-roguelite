using KernelPanic.Core;
using UnityEngine;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Owns the combat turn phase state machine and coordinates combat lifecycle transitions.
    /// </summary>
    public sealed class CombatManager : MonoBehaviour
    {
        [SerializeField] private TurnPhase currentPhase = TurnPhase.Boot;

        public TurnPhase CurrentPhase => currentPhase;

        public void StartCombat()
        {
            SetPhase(TurnPhase.Boot);
        }

        public void AdvancePhase()
        {
            TurnPhase nextPhase = GetNextPhase(currentPhase);
            SetPhase(nextPhase);
        }

        public void EndPlayerTurn()
        {
            SetPhase(TurnPhase.Interpret);
        }

        private void SetPhase(TurnPhase nextPhase)
        {
            TurnPhase previousPhase = currentPhase;
            currentPhase = nextPhase;
            GameEvents.RaisePhaseChanged(new PhaseChangedEvent(previousPhase, nextPhase));
            EnterPhase(nextPhase);
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

        private void EnterPhase(TurnPhase phase)
        {
        }
    }
}
