using System;
using System.Collections.Generic;
using KernelPanic.Data;

namespace KernelPanic.Meta
{
    /// <summary>
    /// Provides the contract for spending entropy on persistent unlock pulls.
    /// </summary>
    public sealed class GachaService
    {
        private readonly List<DistroDefinition> bannerPool = new();

        public int PullTokens => 0; // TODO: Replace with the real pull-token wallet when that meta system exists.
        public IReadOnlyList<DistroDefinition> BannerPool => bannerPool;

        public event Action BannerPoolChanged;

        public void AddToBannerPool(DistroDefinition distro)
        {
            if (distro == null || bannerPool.Contains(distro))
            {
                return;
            }

            bannerPool.Add(distro);
            BannerPoolChanged?.Invoke();
        }

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
