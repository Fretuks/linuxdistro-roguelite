namespace KernelPanic.Core
{
    /// <summary>
    /// Describes the high-level phases of one combat turn.
    /// </summary>
    public enum TurnPhase
    {
        Boot,
        Allocate,
        Execute,
        Interpret,
        EnemyProcess,
        GarbageCollection
    }
}
