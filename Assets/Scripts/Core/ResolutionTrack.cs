namespace KernelPanic.Core
{
    /// <summary>
    /// Identifies the combat resolution track a card uses when played.
    /// </summary>
    public enum ResolutionTrack
    {
        Native,
        InterpreterQueue,
        LazyStack
    }
}
