namespace KernelPanic.Combat
{
    /// <summary>
    /// Represents a runtime card effect that can apply itself to a combat context.
    /// </summary>
    public interface ICardEffect
    {
        void Execute(CombatContext context);
    }
}
