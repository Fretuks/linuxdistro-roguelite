using System.Collections.Generic;
using KernelPanic.Core;
using KernelPanic.Data;

namespace KernelPanic.Run
{
    /// <summary>
    /// Describes the player choices and starting deck used to begin a run.
    /// </summary>
    public sealed class RunConfig
    {
        public RunConfig(DistroDefinition distro, Language primaryLanguage, Language secondaryLanguage, IReadOnlyList<CardDefinition> startingDeck)
            : this(distro, primaryLanguage, secondaryLanguage, startingDeck, 0, 1)
        {
        }

        public RunConfig(DistroDefinition distro, Language primaryLanguage, Language secondaryLanguage, IReadOnlyList<CardDefinition> startingDeck, int runSeed)
            : this(distro, primaryLanguage, secondaryLanguage, startingDeck, runSeed, 1)
        {
        }

        public RunConfig(DistroDefinition distro, Language primaryLanguage, Language secondaryLanguage, IReadOnlyList<CardDefinition> startingDeck, int runSeed, int distroVersion)
        {
            Distro = distro;
            PrimaryLanguage = primaryLanguage;
            SecondaryLanguage = secondaryLanguage;
            StartingDeck = startingDeck;
            RunSeed = runSeed;
            DistroVersion = UnityEngine.Mathf.Clamp(distroVersion, 1, KernelPanic.Meta.GachaTuning.MaxVersion);
        }

        public DistroDefinition Distro { get; }
        public Language PrimaryLanguage { get; }
        public Language SecondaryLanguage { get; }
        public IReadOnlyList<CardDefinition> StartingDeck { get; }
        public int RunSeed { get; }
        public int DistroVersion { get; }
    }
}
