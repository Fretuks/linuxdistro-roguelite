using KernelPanic.Core;

namespace KernelPanic.Combat
{
    public enum EnemyIntentKind
    {
        Attack,
        Defend,
        Buff,
        StatusAttack,
        Special
    }

    /// <summary>
    /// Runtime enemy intent shape. Future EnemyDefinition assets should provide pools of this data.
    /// </summary>
    public readonly struct EnemyIntent
    {
        public EnemyIntent(
            EnemyIntentKind kind,
            int minValue,
            int maxValue,
            Language damageType,
            string displayLabel,
            string iconKey,
            bool trueDamage = false,
            StatusType statusType = StatusType.MemoryLeak,
            int statusStacks = 0,
            int statusDuration = -1,
            string valueTextOverride = null)
        {
            Kind = kind;
            MinValue = minValue;
            MaxValue = maxValue;
            DamageType = damageType;
            DisplayLabel = string.IsNullOrWhiteSpace(displayLabel) ? kind.ToString() : displayLabel;
            IconKey = string.IsNullOrWhiteSpace(iconKey) ? "?" : iconKey;
            TrueDamage = trueDamage;
            StatusType = statusType;
            StatusStacks = statusStacks;
            StatusDuration = statusDuration;
            ValueTextOverride = valueTextOverride;
        }

        public EnemyIntentKind Kind { get; }
        public int MinValue { get; }
        public int MaxValue { get; }
        public Language DamageType { get; }
        public string DisplayLabel { get; }
        public string IconKey { get; }
        public bool TrueDamage { get; }
        public StatusType StatusType { get; }
        public int StatusStacks { get; }
        public int StatusDuration { get; }
        public string ValueTextOverride { get; }

        public string ValueText => string.IsNullOrWhiteSpace(ValueTextOverride)
            ? MinValue == MaxValue ? MinValue.ToString() : $"{MinValue}-{MaxValue}"
            : ValueTextOverride;
        public string DisplayText => $"{IconKey} {DisplayLabel} {ValueText}";

        public EnemyIntent WithValues(int minValue, int maxValue, string displayLabel = null, string iconKey = null, string valueTextOverride = null)
        {
            return new EnemyIntent(
                Kind,
                minValue,
                maxValue,
                DamageType,
                displayLabel ?? DisplayLabel,
                iconKey ?? IconKey,
                TrueDamage,
                StatusType,
                StatusStacks,
                StatusDuration,
                valueTextOverride);
        }
    }
}
