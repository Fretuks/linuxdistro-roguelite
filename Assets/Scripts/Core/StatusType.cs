namespace KernelPanic.Core
{
    /// <summary>
    /// Identifies a combat status effect category.
    /// </summary>
    public enum StatusType
    {
        Segfault,
        MemoryLeak,
        RaceCondition,
        Deprecated,
        DependencyError,
        Deadlock
    }
}
