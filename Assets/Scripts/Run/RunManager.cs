using KernelPanic.Combat;
using KernelPanic.Core;
using UnityEngine;

namespace KernelPanic.Run
{
    /// <summary>
    /// Coordinates run lifecycle, wave progression, and run-level event handling.
    /// </summary>
    public sealed class RunManager : MonoBehaviour
    {
        [SerializeField] private CombatManager combatManager;
        [SerializeField] private int currentWaveNumber;
        [SerializeField] private bool isRunActive;
        [SerializeField] private bool isPlayerDead;

        public int CurrentWaveNumber => currentWaveNumber;
        public bool IsRunActive => isRunActive;
        public bool IsPlayerDead => isPlayerDead;

        private void OnEnable()
        {
            GameEvents.WaveCleared += HandleWaveCleared;
            GameEvents.RunEnded += HandleRunEnded;
        }

        private void OnDisable()
        {
            GameEvents.WaveCleared -= HandleWaveCleared;
            GameEvents.RunEnded -= HandleRunEnded;
        }

        public void StartRun(RunConfig config)
        {
            isRunActive = true;
            isPlayerDead = false;
            currentWaveNumber = 1;
            StartCombat();
        }

        public void StartCombat()
        {
            combatManager?.StartCombat();
        }

        private void HandleWaveCleared(WaveClearedEvent payload)
        {
        }

        private void HandleRunEnded(RunEndedEvent payload)
        {
            isRunActive = false;
            isPlayerDead = payload.PlayerDied;
        }
    }
}
