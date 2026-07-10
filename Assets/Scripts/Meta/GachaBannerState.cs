using System;
using System.Collections.Generic;

namespace KernelPanic.Meta
{
    /// <summary>
    /// Stores per-banner counters that must persist between sessions.
    /// </summary>
    [Serializable]
    public sealed class GachaBannerState
    {
        public string bannerId;
        public int totalPulls;
        public int pityCounter;
        public int fiveStarPityCounter;
        public bool exhausted;
        public bool featuredFiveStarGuaranteed;
        public int fiveStarSelectorsClaimed;
        public List<string> guaranteedDistroIds = new();

        public GachaBannerState()
        {
        }

        public GachaBannerState(string bannerId)
        {
            this.bannerId = bannerId;
        }

        public void EnsureLists()
        {
            guaranteedDistroIds ??= new List<string>();
        }
    }
}
