using System;
using System.Collections.Generic;
using KernelPanic.Core;
using KernelPanic.Data;
using UnityEngine;

namespace KernelPanic.Meta
{
    public static class PullResolver
    {
        public static event Action<PullResolutionResult> PullResolved;

        public static PullResolutionResult Resolve(IEnumerable<DistroDefinition> pullResults, PullResolutionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            context.SaveData ??= SaveData.CreateDefault();
            context.SaveData.EnsureLists();

            List<PullResolutionOutcome> outcomes = new();
            if (pullResults != null)
            {
                foreach (DistroDefinition unit in pullResults)
                {
                    outcomes.Add(ResolveOne(unit, context));
                }
            }

            PullResolutionResult result = new(outcomes);
            PullResolved?.Invoke(result);
            return result;
        }

        public static PullResolutionResult Resolve(IEnumerable<string> pullResultIds, PullResolutionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            List<DistroDefinition> units = new();
            if (pullResultIds != null)
            {
                foreach (string id in pullResultIds)
                {
                    units.Add(context.DistroDatabase == null ? null : context.DistroDatabase.FindById(id));
                }
            }

            return Resolve(units, context);
        }

        public static PullResolutionResult ResolveAndSave(IEnumerable<DistroDefinition> pullResults, PullResolutionContext context, SaveService saveService)
        {
            PullResolutionResult result = Resolve(pullResults, context);
            saveService?.Save(context.SaveData);
            return result;
        }

        public static PullResolutionResult ResolveAndSave(IEnumerable<string> pullResultIds, PullResolutionContext context, SaveService saveService)
        {
            PullResolutionResult result = Resolve(pullResultIds, context);
            saveService?.Save(context.SaveData);
            return result;
        }

        private static PullResolutionOutcome ResolveOne(DistroDefinition unit, PullResolutionContext context)
        {
            if (unit == null || string.IsNullOrWhiteSpace(unit.Id))
            {
                return new PullResolutionOutcome(null, PullOutcomeKind.Invalid, 0, Array.Empty<Language>());
            }

            OwnedUnitSaveEntry owned = context.SaveData.FindOwnedUnit(unit.Id);
            bool ownedInCollection = context.Collection != null && context.Collection.IsOwned(unit.Id);
            if (owned == null && !ownedInCollection)
            {
                IReadOnlyList<Language> before = SnapshotUnlockedLanguages(context.Collection);
                context.SaveData.AddOwnedUnit(unit.Id, 1);
                context.Collection?.AddSilently(unit, 1);
                IReadOnlyList<Language> unlocked = NewlyUnlockedLanguages(before, context.Collection, unit);
                return new PullResolutionOutcome(unit.Id, PullOutcomeKind.Granted, 0, unlocked);
            }

            owned ??= context.SaveData.AddOwnedUnit(unit.Id, context.Collection?.GetVersion(unit.Id) ?? 1);
            int version = Mathf.Clamp(owned.version, 1, GachaTuning.MaxVersion);
            owned.version = version;
            bool overflow = version >= GachaTuning.MaxVersion;
            int merges = CalculateDupeMerges(overflow, unit.Id, context.FocusUnitId);
            context.SaveData.merges = Math.Max(0, context.SaveData.merges) + merges;
            if (GachaTuning.DupeConsolationBandwidth > 0)
            {
                context.SaveData.standardPullCurrency += GachaTuning.DupeConsolationBandwidth;
            }

            context.Collection?.AddSilently(unit, version);
            return new PullResolutionOutcome(unit.Id, overflow ? PullOutcomeKind.DupeOverflow : PullOutcomeKind.Dupe, merges, Array.Empty<Language>());
        }

        private static int CalculateDupeMerges(bool overflow, string unitId, string focusUnitId)
        {
            float baseAmount = GachaTuning.MergesPerDupe;
            if (overflow)
            {
                baseAmount *= GachaTuning.MergesMaxVersionOverflowMultiplier;
            }

            int merges = Mathf.RoundToInt(baseAmount);
            if (!string.IsNullOrWhiteSpace(focusUnitId) && string.Equals(unitId, focusUnitId, StringComparison.OrdinalIgnoreCase))
            {
                merges += GachaTuning.MergesOnCharacterBonus;
            }

            return Math.Max(1, merges);
        }

        private static IReadOnlyList<Language> SnapshotUnlockedLanguages(PlayerCollection collection)
        {
            List<Language> unlocked = new();
            if (collection == null)
            {
                return unlocked;
            }

            IReadOnlyList<LanguageCatalogEntry> languages = LanguageCatalog.All;
            for (int i = 0; i < languages.Count; i++)
            {
                if (LanguageUnlock.IsUnlocked(languages[i].Language, collection))
                {
                    unlocked.Add(languages[i].Language);
                }
            }

            return unlocked;
        }

        private static IReadOnlyList<Language> NewlyUnlockedLanguages(IReadOnlyList<Language> before, PlayerCollection collection, DistroDefinition unit)
        {
            List<Language> unlocked = new();
            if (collection == null || unit == null)
            {
                return unlocked;
            }

            AddNewlyUnlockedLanguage(unit.PrimaryLanguage, before, collection, unlocked);
            AddNewlyUnlockedLanguage(unit.SecondaryLanguage, before, collection, unlocked);

            return unlocked;
        }

        private static void AddNewlyUnlockedLanguage(Language language, IReadOnlyList<Language> before, PlayerCollection collection, List<Language> unlocked)
        {
            if (!ContainsLanguage(unlocked, language) && !ContainsLanguage(before, language) && LanguageUnlock.IsUnlocked(language, collection))
            {
                unlocked.Add(language);
            }
        }

        private static bool ContainsLanguage(IReadOnlyList<Language> languages, Language language)
        {
            for (int i = 0; i < languages.Count; i++)
            {
                if (languages[i] == language)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class PullResolutionContext
    {
        public PullResolutionContext(SaveData saveData, PlayerCollection collection, DistroDatabase distroDatabase = null, string focusUnitId = null)
        {
            SaveData = saveData;
            Collection = collection;
            DistroDatabase = distroDatabase;
            FocusUnitId = focusUnitId;
        }

        public SaveData SaveData { get; set; }
        public PlayerCollection Collection { get; }
        public DistroDatabase DistroDatabase { get; }
        public string FocusUnitId { get; }
    }

    public sealed class PullResolutionResult
    {
        public PullResolutionResult(IReadOnlyList<PullResolutionOutcome> outcomes)
        {
            Outcomes = outcomes ?? Array.Empty<PullResolutionOutcome>();
        }

        public IReadOnlyList<PullResolutionOutcome> Outcomes { get; }
    }

    public readonly struct PullResolutionOutcome
    {
        public PullResolutionOutcome(string unitId, PullOutcomeKind kind, int mergesAwarded, IReadOnlyList<Language> languagesNewlyUnlocked)
        {
            UnitId = unitId;
            Kind = kind;
            MergesAwarded = mergesAwarded;
            LanguagesNewlyUnlocked = languagesNewlyUnlocked ?? Array.Empty<Language>();
        }

        public string UnitId { get; }
        public PullOutcomeKind Kind { get; }
        public int MergesAwarded { get; }
        public IReadOnlyList<Language> LanguagesNewlyUnlocked { get; }
    }

    public enum PullOutcomeKind
    {
        Invalid,
        Granted,
        Dupe,
        DupeOverflow
    }
}
