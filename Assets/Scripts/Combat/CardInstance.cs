using KernelPanic.Data;
using System.Collections.Generic;
using System.Linq;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Represents one runtime card and its per-combat mutable state.
    /// </summary>
    public sealed class CardInstance
    {
        private readonly List<CombatantState> _targetSnapshot = new();

        public CardInstance(CardDefinition definition)
        {
            Definition = definition;
        }

        public CardDefinition Definition { get; }
        public IReadOnlyList<CombatantState> TargetSnapshot => _targetSnapshot;
        public int TemporaryCostDelta { get; set; }
        public int PermanentCostDelta { get; private set; }
        public int MagnitudeBonus { get; private set; }
        public int DrawRider { get; private set; }
        public int UpgradeLevel { get; private set; }
        public bool IsBroken { get; set; }
        public bool IsLocked { get; set; }
        public bool CanUpgrade => UpgradeLevel == 0;
        public bool CanReceiveRepositoryUpgrade => UpgradeLevel < 3;
        public bool IsUpgraded => UpgradeLevel > 0;

        public bool Upgrade()
        {
            if (!CanUpgrade)
            {
                return false;
            }

            UpgradeLevel = 1;
            ApplyCostUpgrade(-1);
            ApplyMagnitudeUpgrade(1);

            return true;
        }

        public bool ApplyCostUpgrade(int delta)
        {
            if (Definition == null || Definition.CycleCost + PermanentCostDelta + TemporaryCostDelta <= 0)
            {
                return false;
            }

            PermanentCostDelta += delta;
            UpgradeLevel++;
            return true;
        }

        public bool ApplyMagnitudeUpgrade(int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            MagnitudeBonus += amount;
            UpgradeLevel++;
            return true;
        }

        public bool ApplyDrawRider(int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            DrawRider += amount;
            UpgradeLevel++;
            return true;
        }

        public void SetTargetSnapshot(IEnumerable<CombatantState> targets)
        {
            _targetSnapshot.Clear();
            if (targets == null)
            {
                return;
            }

            _targetSnapshot.AddRange(targets.Where(target => target != null && !target.IsDefeated));
        }

        public void ClearTargetSnapshot()
        {
            _targetSnapshot.Clear();
        }

        public void ResetCombatState()
        {
            TemporaryCostDelta = 0;
            IsBroken = false;
            IsLocked = false;
            ClearTargetSnapshot();
        }
    }
}
