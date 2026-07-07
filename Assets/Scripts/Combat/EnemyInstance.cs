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
        {
            Name = string.IsNullOrWhiteSpace(name) ? "unknown_process" : name;
            State = new CombatantState(maxUptime, 0, 0);
            if (intentPool != null)
            {
                _intentPool.AddRange(intentPool);
            }
        }

        public string Name { get; }
        public int CurrentUptime => State.CurrentUptime;
        public int MaxUptime => State.MaxUptime;
        public CombatantState State { get; }
        public IReadOnlyList<EnemyIntent> IntentPool => _intentPool;
        public EnemyIntent CurrentIntent { get; private set; }

        public void PickNextIntent()
        {
            if (_intentPool.Count == 0)
            {
                CurrentIntent = default;
                return;
            }

            int index = RandomRoll.RollRange(0, _intentPool.Count - 1, new RollContext(State));
            CurrentIntent = _intentPool[index];
        }
    }
}
