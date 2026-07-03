using System;

namespace KernelPanic.Meta
{
    /// <summary>
    /// Provides the contract for spending entropy on persistent unlock pulls.
    /// </summary>
    public sealed class GachaService
    {
        public int PullTokens => 0; // TODO: Replace with the real pull-token wallet when that meta system exists.

        public GachaPullResult PerformPull(EntropyWallet wallet, int cost)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Represents the result of a persistent unlock pull.
    /// </summary>
    public readonly struct GachaPullResult
    {
        public GachaPullResult(bool success, string unlockId)
        {
            Success = success;
            UnlockId = unlockId;
        }

        public bool Success { get; }
        public string UnlockId { get; }
    }
}
