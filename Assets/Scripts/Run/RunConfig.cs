using System.Collections.Generic;
using System;
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
            : this(distro, primaryLanguage, secondaryLanguage, startingDeck, Array.Empty<PackageDefinition>(), 0, 1)
        {
        }

        public RunConfig(DistroDefinition distro, Language primaryLanguage, Language secondaryLanguage, IReadOnlyList<CardDefinition> startingDeck, int runSeed)
            : this(distro, primaryLanguage, secondaryLanguage, startingDeck, Array.Empty<PackageDefinition>(), runSeed, 1)
        {
        }

        public RunConfig(DistroDefinition distro, Language primaryLanguage, Language secondaryLanguage, IReadOnlyList<CardDefinition> startingDeck, int runSeed, int distroVersion)
            : this(distro, primaryLanguage, secondaryLanguage, startingDeck, Array.Empty<PackageDefinition>(), runSeed, distroVersion)
        {
        }

        public RunConfig(DistroDefinition distro, Language primaryLanguage, Language secondaryLanguage, IReadOnlyList<CardDefinition> startingDeck, IReadOnlyList<PackageDefinition> equippedPackages, int runSeed, int distroVersion)
            : this(distro, primaryLanguage, secondaryLanguage, startingDeck, ToInstances(equippedPackages), runSeed, distroVersion)
        {
        }

        public RunConfig(DistroDefinition distro, Language primaryLanguage, Language secondaryLanguage, IReadOnlyList<CardDefinition> startingDeck, IReadOnlyList<PackageInstance> equippedPackages, int runSeed, int distroVersion)
        {
            Distro = distro;
            PrimaryLanguage = primaryLanguage;
            SecondaryLanguage = secondaryLanguage;
            StartingDeck = startingDeck;
            EquippedPackages = equippedPackages ?? Array.Empty<PackageInstance>();
            RunSeed = runSeed;
            DistroVersion = UnityEngine.Mathf.Clamp(distroVersion, 1, KernelPanic.Meta.GachaTuning.MaxVersion);
        }

        public DistroDefinition Distro { get; }
        public Language PrimaryLanguage { get; }
        public Language SecondaryLanguage { get; }
        public IReadOnlyList<CardDefinition> StartingDeck { get; }
        public IReadOnlyList<PackageInstance> EquippedPackages { get; }
        public int RunSeed { get; }
        public int DistroVersion { get; }

        private static IReadOnlyList<PackageInstance> ToInstances(IReadOnlyList<PackageDefinition> packages)
        {
            if (packages == null)
            {
                return Array.Empty<PackageInstance>();
            }

            List<PackageInstance> instances = new();
            for (int i = 0; i < packages.Count; i++)
            {
                if (packages[i] != null)
                {
                    instances.Add(new PackageInstance(packages[i], 0));
                }
            }

            return instances;
        }
    }
}
