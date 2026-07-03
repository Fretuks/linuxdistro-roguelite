using System;

namespace KernelPanic.Meta
{
    /// <summary>
    /// Provides save and load contracts for persistent meta progression.
    /// </summary>
    public sealed class SaveService
    {
        public void Save(SaveData data)
        {
            throw new NotImplementedException();
        }

        public SaveData Load()
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Stores persistent progression data for serialization.
    /// </summary>
    public sealed class SaveData
    {
        public int EntropyBalance { get; set; }
    }
}
