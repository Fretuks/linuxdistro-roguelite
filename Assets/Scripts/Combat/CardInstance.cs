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
        public int QueuePlayCount { get; private set; }
        public bool WasFirstCardThisTurn { get; private set; }
        public bool HadFedoraNonCrashBonus { get; private set; }
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

        public bool ApplyCombatMagnitudeBonus(int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            MagnitudeBonus += amount;
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

        public void MarkQueued()
        {
            QueuePlayCount++;
        }

        public void MarkPlayedThisTurn(bool wasFirst)
        {
            WasFirstCardThisTurn = wasFirst;
            HadFedoraNonCrashBonus = false;
        }

        public void MarkFedoraNonCrashBonus()
        {
            HadFedoraNonCrashBonus = true;
        }

        public CardInstance CopyForCombat()
        {
            CardInstance copy = new(Definition)
            {
                TemporaryCostDelta = TemporaryCostDelta,
                IsBroken = IsBroken,
                IsLocked = IsLocked
            };

            copy.PermanentCostDelta = PermanentCostDelta;
            copy.MagnitudeBonus = MagnitudeBonus;
            copy.DrawRider = DrawRider;
            copy.UpgradeLevel = UpgradeLevel;
            copy.QueuePlayCount = QueuePlayCount;
            copy.WasFirstCardThisTurn = WasFirstCardThisTurn;
            copy.HadFedoraNonCrashBonus = HadFedoraNonCrashBonus;
            copy.SetTargetSnapshot(TargetSnapshot);
            return copy;
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
            QueuePlayCount = 0;
            WasFirstCardThisTurn = false;
            HadFedoraNonCrashBonus = false;
            IsBroken = false;
            IsLocked = false;
            ClearTargetSnapshot();
        }
    }
}
