namespace KernelPanic.Combat
{
    /// <summary>
    /// Provides a common contract for card resolution tracks.
    /// </summary>
    public interface IResolutionTrack
    {
        void Enqueue(CardInstance card);
        void Resolve(CombatContext context);
    }
}
