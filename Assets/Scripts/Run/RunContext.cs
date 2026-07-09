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
        private static PendingRun _pendingRun;

        public static bool HasPendingRun => _pendingRun != null;

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
            _pendingRun = new PendingRun(distro, equippedCards, ToInstances(equippedPackages), primaryLanguage, secondaryLanguage, Environment.TickCount, distroVersion);
        }

        public static void Set(DistroDefinition distro, IReadOnlyList<CardDefinition> equippedCards, IReadOnlyList<PackageInstance> equippedPackages, Language primaryLanguage, Language secondaryLanguage, int distroVersion)
        {
            _pendingRun = new PendingRun(distro, equippedCards, equippedPackages, primaryLanguage, secondaryLanguage, Environment.TickCount, distroVersion);
        }

        public static bool TryCreateRunConfig(LanguageDeckDatabase languageDeckDatabase, out RunConfig config)
        {
            if (_pendingRun == null || _pendingRun.Distro == null)
            {
                config = null;
                return false;
            }

            config = _pendingRun.CreateRunConfig(languageDeckDatabase);
            _pendingRun = null;
            return true;
        }

        private sealed class PendingRun
        {
            private readonly DistroDefinition _distro;
            private readonly List<CardDefinition> _equippedCards = new();
            private readonly List<PackageInstance> _equippedPackages = new();
            private readonly Language _primaryLanguage;
            private readonly Language _secondaryLanguage;
            private readonly int _runSeed;
            private readonly int _distroVersion;

            public PendingRun(DistroDefinition distro, IReadOnlyList<CardDefinition> cards, IReadOnlyList<PackageInstance> packages, Language primaryLanguage, Language secondaryLanguage, int runSeed, int distroVersion)
            {
                this._distro = distro;
                this._primaryLanguage = primaryLanguage;
                this._secondaryLanguage = secondaryLanguage;
                this._runSeed = runSeed;
                this._distroVersion = distroVersion;

                if (cards == null)
                {
                    return;
                }

                for (int i = 0; i < cards.Count; i++)
                {
                    if (cards[i] != null && !cards[i].IsRunOnly)
                    {
                        _equippedCards.Add(cards[i]);
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
                        _equippedPackages.Add(packages[i]);
                    }
                }
            }

            public DistroDefinition Distro => _distro;

            public RunConfig CreateRunConfig(LanguageDeckDatabase languageDeckDatabase)
            {
                List<CardDefinition> startingDeck = new(_equippedCards);
                AddLanguageDeck(languageDeckDatabase, _primaryLanguage, startingDeck);
                AddLanguageDeck(languageDeckDatabase, _secondaryLanguage, startingDeck);
                return new RunConfig(_distro, _primaryLanguage, _secondaryLanguage, startingDeck, _equippedPackages, _runSeed, _distroVersion);
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
