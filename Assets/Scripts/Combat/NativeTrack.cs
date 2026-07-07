using KernelPanic.Core;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Resolves cards immediately when they are added to the native track.
    /// </summary>
    public sealed class NativeTrack : IResolutionTrack
    {
        public void Enqueue(CardInstance card)
        {
            if (card != null)
            {
                GameEvents.RaiseCardPlayed(new CardPlayedEvent(card, ResolutionTrack.Native));
            }
        }

        public void Resolve(CombatContext context)
        {
            // Native cards resolve immediately in CombatManager.PlayCard; this track has no queue.
        }
    }
}
