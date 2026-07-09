using System;
using System.Collections.Generic;
using KernelPanic.Core;
using KernelPanic.Data;

namespace KernelPanic.Run
{
    /// <summary>
    /// Scene-transition handoff for a just-configured run. This is intentionally a plain static
    /// context instead of a ScriptableObject asset because the payload is transient runtime state,
    /// not reusable authored content.
    /// </summary>
    public static class RunContext
    {
        private static PendingRun pendingRun;

        public static bool HasPendingRun => pendingRun != null;

        public static void Set(DistroDefinition distro, IReadOnlyList<CardDefinition> equippedCards, Language primaryLanguage, Language secondaryLanguage)
        {
            Set(distro, equippedCards, Array.Empty<PackageDefinition>(), primaryLanguage, secondaryLanguage, 1);
        }

        public static void Set(DistroDefinition distro, IReadOnlyList<CardDefinition> equippedCards, Language primaryLanguage, Language secondaryLanguage, int distroVersion)
        {
            Set(distro, equippedCards, Array.Empty<PackageDefinition>(), primaryLanguage, secondaryLanguage, distroVersion);
        }

        public static void Set(DistroDefinition distro, IReadOnlyList<CardDefinition> equippedCards, IReadOnlyList<PackageDefinition> equippedPackages, Language primaryLanguage, Language secondaryLanguage, int distroVersion)
        {
            pendingRun = new PendingRun(distro, equippedCards, ToInstances(equippedPackages), primaryLanguage, secondaryLanguage, Environment.TickCount, distroVersion);
        }

        public static void Set(DistroDefinition distro, IReadOnlyList<CardDefinition> equippedCards, IReadOnlyList<PackageInstance> equippedPackages, Language primaryLanguage, Language secondaryLanguage, int distroVersion)
        {
            pendingRun = new PendingRun(distro, equippedCards, equippedPackages, primaryLanguage, secondaryLanguage, Environment.TickCount, distroVersion);
        }

        public static bool TryCreateRunConfig(LanguageDeckDatabase languageDeckDatabase, out RunConfig config)
        {
            if (pendingRun == null || pendingRun.Distro == null)
            {
                config = null;
                return false;
            }

            config = pendingRun.CreateRunConfig(languageDeckDatabase);
            pendingRun = null;
            return true;
        }

        private sealed class PendingRun
        {
            private readonly DistroDefinition distro;
            private readonly List<CardDefinition> equippedCards = new();
            private readonly List<PackageInstance> equippedPackages = new();
            private readonly Language primaryLanguage;
            private readonly Language secondaryLanguage;
            private readonly int runSeed;
            private readonly int distroVersion;

            public PendingRun(DistroDefinition distro, IReadOnlyList<CardDefinition> cards, IReadOnlyList<PackageInstance> packages, Language primaryLanguage, Language secondaryLanguage, int runSeed, int distroVersion)
            {
                this.distro = distro;
                this.primaryLanguage = primaryLanguage;
                this.secondaryLanguage = secondaryLanguage;
                this.runSeed = runSeed;
                this.distroVersion = distroVersion;

                if (cards == null)
                {
                    return;
                }

                for (int i = 0; i < cards.Count; i++)
                {
                    if (cards[i] != null && !cards[i].IsRunOnly)
                    {
                        equippedCards.Add(cards[i]);
                    }
                }

                if (packages == null)
                {
                    return;
                }

                for (int i = 0; i < packages.Count; i++)
                {
                    if (packages[i]?.Definition != null)
                    {
                        equippedPackages.Add(packages[i]);
                    }
                }
            }

            public DistroDefinition Distro => distro;

            public RunConfig CreateRunConfig(LanguageDeckDatabase languageDeckDatabase)
            {
                List<CardDefinition> startingDeck = new(equippedCards);
                AddLanguageDeck(languageDeckDatabase, primaryLanguage, startingDeck);
                AddLanguageDeck(languageDeckDatabase, secondaryLanguage, startingDeck);
                return new RunConfig(distro, primaryLanguage, secondaryLanguage, startingDeck, equippedPackages, runSeed, distroVersion);
            }

            private static void AddLanguageDeck(LanguageDeckDatabase languageDeckDatabase, Language language, List<CardDefinition> target)
            {
                LanguageDeckDefinition deck = languageDeckDatabase == null ? null : languageDeckDatabase.FindByLanguage(language);
                if (deck == null)
                {
                    return;
                }

                for (int entryIndex = 0; entryIndex < deck.Entries.Count; entryIndex++)
                {
                    LanguageDeckDefinition.LanguageDeckEntry entry = deck.Entries[entryIndex];
                    for (int copy = 0; copy < entry.Count; copy++)
                    {
                        if (entry.Card != null && !entry.Card.IsRunOnly)
                        {
                            target.Add(entry.Card);
                        }
                    }
                }
            }
        }

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
