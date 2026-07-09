using System;
using System.Collections.Generic;
using KernelPanic.Data;
using KernelPanic.Core;

namespace KernelPanic.Meta
{
    /// <summary>
    /// Transient scene-transition handoff for gacha pull cutscenes.
    /// </summary>
    public static class GachaPullContext
    {
        private static PendingGachaPull pendingPull;
        private static CompletedGachaPull completedPull;

        public static bool HasPendingPull => pendingPull != null;
        public static bool HasCompletedPull => completedPull != null;

        public static void SetPending(string bannerId, int pullCount, int entropyTokenCount, DistroDatabase distroDatabase, PackageDatabase packageDatabase, string focusUnitId)
        {
            pendingPull = new PendingGachaPull(bannerId, pullCount, entropyTokenCount, distroDatabase, packageDatabase, focusUnitId);
            completedPull = null;
        }

        public static bool TryConsumePending(out PendingGachaPull pull)
        {
            pull = pendingPull;
            pendingPull = null;
            return pull != null;
        }

        public static void SetCompleted(CompletedGachaPull pull)
        {
            completedPull = pull;
        }

        public static bool TryConsumeCompleted(out CompletedGachaPull pull)
        {
            pull = completedPull;
            completedPull = null;
            return pull != null;
        }
    }

    public sealed class PendingGachaPull
    {
        public PendingGachaPull(string bannerId, int pullCount, int entropyTokenCount, DistroDatabase distroDatabase, PackageDatabase packageDatabase, string focusUnitId)
        {
            BannerId = string.IsNullOrWhiteSpace(bannerId) ? GachaService.BeginnerBannerId : bannerId;
            PullCount = Math.Max(1, pullCount);
            EntropyTokenCount = Math.Max(0, entropyTokenCount);
            DistroDatabase = distroDatabase;
            PackageDatabase = packageDatabase;
            FocusUnitId = focusUnitId;
        }

        public string BannerId { get; }
        public int PullCount { get; }
        public int EntropyTokenCount { get; }
        public DistroDatabase DistroDatabase { get; }
        public PackageDatabase PackageDatabase { get; }
        public string FocusUnitId { get; }
    }

    public sealed class CompletedGachaPull
    {
        public CompletedGachaPull(string bannerId, string headerText, IReadOnlyList<CompletedGachaReward> rewards)
        {
            BannerId = string.IsNullOrWhiteSpace(bannerId) ? GachaService.BeginnerBannerId : bannerId;
            HeaderText = headerText;
            Rewards = rewards ?? Array.Empty<CompletedGachaReward>();
        }

        public string BannerId { get; }
        public string HeaderText { get; }
        public IReadOnlyList<CompletedGachaReward> Rewards { get; }

        public IReadOnlyList<string> RewardLines
        {
            get
            {
                List<string> lines = new();
                for (int i = 0; i < Rewards.Count; i++)
                {
                    CompletedGachaReward reward = Rewards[i];
                    string status = string.IsNullOrWhiteSpace(reward.StatusText) ? string.Empty : $" {reward.StatusText}";
                    string languages = string.IsNullOrWhiteSpace(reward.LanguageUnlockText) ? string.Empty : $" {reward.LanguageUnlockText}";
                    lines.Add($"{reward.Index:00}: {reward.Stars}★ {reward.DisplayName} [{reward.TypeText}]{status}{languages}");
                }

                return lines;
            }
        }

        public IReadOnlyList<int> RewardStars
        {
            get
            {
                List<int> stars = new();
                for (int i = 0; i < Rewards.Count; i++)
                {
                    stars.Add(Rewards[i].Stars);
                }

                return stars;
            }
        }
    }

    public sealed class CompletedGachaReward
    {
        public CompletedGachaReward(int index, int stars, string displayName, GachaRewardType rewardType, PullOutcomeKind outcomeKind, int mergesAwarded, IReadOnlyList<Language> languagesUnlocked, bool guaranteed, bool pityTriggered)
        {
            Index = Math.Max(1, index);
            Stars = Math.Max(3, stars);
            RewardType = rewardType;
            DisplayName = NormalizeDisplayName(displayName, rewardType);
            OutcomeKind = outcomeKind;
            MergesAwarded = Math.Max(0, mergesAwarded);
            LanguagesUnlocked = languagesUnlocked ?? Array.Empty<Language>();
            Guaranteed = guaranteed;
            PityTriggered = pityTriggered;
        }

        public int Index { get; }
        public int Stars { get; }
        public string DisplayName { get; }
        public GachaRewardType RewardType { get; }
        public PullOutcomeKind OutcomeKind { get; }

        // Holds distro dupe merges, or (when RewardType is Package) the Cache a duplicate package
        // pull was auto-scrapped for. See StatusText for which unit applies.
        public int MergesAwarded { get; }
        public IReadOnlyList<Language> LanguagesUnlocked { get; }
        public bool Guaranteed { get; }
        public bool PityTriggered { get; }
        public bool IsCharacter => RewardType == GachaRewardType.Distro || RewardType == GachaRewardType.FutureStandardFiveStarDistro;

        public string TypeText => IsCharacter ? "character" : "package";

        public string StatusText
        {
            get
            {
                string dupeUnit = RewardType == GachaRewardType.Package ? "cache" : "merges";
                return OutcomeKind switch
                {
                    PullOutcomeKind.Granted => "NEW",
                    PullOutcomeKind.Dupe => $"→ {MergesAwarded} {dupeUnit}",
                    PullOutcomeKind.DupeOverflow => $"→ {MergesAwarded} {dupeUnit}",
                    _ => Guaranteed ? "guaranteed" : PityTriggered ? "pity" : string.Empty
                };
            }
        }

        public string LanguageUnlockText
        {
            get
            {
                if (LanguagesUnlocked.Count == 0)
                {
                    return string.Empty;
                }

                List<string> names = new();
                for (int i = 0; i < LanguagesUnlocked.Count; i++)
                {
                    names.Add(LanguagesUnlocked[i].ToString());
                }

                return $"unlocks {string.Join(", ", names)}";
            }
        }

        private static string NormalizeDisplayName(string displayName, GachaRewardType rewardType)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return "unknown result";
            }

            if (rewardType != GachaRewardType.Package)
            {
                return displayName;
            }

            string trimmed = displayName.Trim();
            return trimmed.StartsWith("3-star ", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("4-star ", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("5-star ", StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(7)
                : trimmed;
        }
    }
}
