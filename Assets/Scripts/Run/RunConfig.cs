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
        {
            Distro = distro;
            PrimaryLanguage = primaryLanguage;
            SecondaryLanguage = secondaryLanguage;
            StartingDeck = startingDeck;
        }

        public DistroDefinition Distro { get; }
        public Language PrimaryLanguage { get; }
        public Language SecondaryLanguage { get; }
        public IReadOnlyList<CardDefinition> StartingDeck { get; }
    }
}
