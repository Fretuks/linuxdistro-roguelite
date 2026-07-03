using KernelPanic.Data;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Represents one runtime card and its per-combat mutable state.
    /// </summary>
    public sealed class CardInstance
    {
        public CardInstance(CardDefinition definition)
        {
            Definition = definition;
        }

        public CardDefinition Definition { get; }
        public int TemporaryCostDelta { get; set; }
        public bool IsBroken { get; set; }
        public bool IsLocked { get; set; }
    }
}
