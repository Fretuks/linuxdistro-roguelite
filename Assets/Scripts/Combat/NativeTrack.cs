using System;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Resolves cards immediately when they are added to the native track.
    /// </summary>
    public sealed class NativeTrack : IResolutionTrack
    {
        public void Enqueue(CardInstance card)
        {
            throw new NotImplementedException();
        }

        public void Resolve(CombatContext context)
        {
            throw new NotImplementedException();
        }
    }
}
