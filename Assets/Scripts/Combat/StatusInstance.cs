using KernelPanic.Core;

namespace KernelPanic.Combat
{
    public enum StatusStackingRule
    {
        Intensity,
        Duration,
        Refresh,
        Unique
    }

    public enum StatusTickTiming
    {
        None,
        StartOfTurn,
        EndOfTurn
    }

    public sealed class StatusInstance
    {
        public StatusInstance(StatusType type, int stacks, int duration, CombatantState source = null, bool skipNextTick = false)
        {
            Type = type;
            Stacks = stacks;
            Duration = duration;
            Source = source;
            SkipNextTick = skipNextTick;
        }

        public StatusType Type { get; }
        public int Stacks { get; set; }
        public int Duration { get; set; }
        public CombatantState Source { get; set; }
        public bool SkipNextTick { get; set; }
    }

    public readonly struct StatusDescriptor
    {
        public StatusDescriptor(
            StatusType type,
            string displayName,
            string iconKey,
            bool isBeneficial,
            StatusStackingRule stackingRule,
            StatusTickTiming tickTiming,
            string tooltip)
        {
            Type = type;
            DisplayName = displayName;
            IconKey = iconKey;
            IsBeneficial = isBeneficial;
            StackingRule = stackingRule;
            TickTiming = tickTiming;
            Tooltip = tooltip;
        }

        public StatusType Type { get; }
        public string DisplayName { get; }
        public string IconKey { get; }
        public bool IsBeneficial { get; }
        public StatusStackingRule StackingRule { get; }
        public StatusTickTiming TickTiming { get; }
        public string Tooltip { get; }
    }
}
