using System;
using System.Collections.Generic;
using KernelPanic.Data;

namespace KernelPanic.Meta
{
    /// <summary>
    /// Provides persistent banner pull logic and currency accounting.
    /// </summary>
    public sealed class GachaService
    {
        public const string BeginnerBannerId = "beginner";
        public const string StandardBannerId = "standard";
        public const string LimitedBannerId = "limited";
        public const int BeginnerMaxPulls = 50;
        public const int BeginnerSinglePullCost = 1;
        public const int BeginnerTenPullCost = 8;
        public const int FourStarHardPity = 10;
        public const int FiveStarSoftPityStart = 66;
        public const int FiveStarHardPity = 80;
        public const int EntropyPerPullToken = 100;

        private const double FourStarBaseChance = 0.12d;
        private const double FiveStarBaseChance = 0.008d;
        private const double FiveStarSoftPityStep = 0.07d;
        private const double DistroChanceOnFeaturedTier = 0.5d;
        private const double LimitedStandardFiveStarChance = 1d / 3d;

        private readonly List<DistroDefinition> bannerPool = new();
        private readonly Dictionary<GachaCurrencyType, int> currencyBalances = new();
        private readonly Random random;
        private GachaBannerState beginnerState = new(BeginnerBannerId);

        public GachaService()
            : this(new Random())
        {
        }

        public GachaService(Random random)
        {
            this.random = random ?? new Random();
            currencyBalances[GachaCurrencyType.StandardPull] = 0;
            currencyBalances[GachaCurrencyType.LimitedPull] = 0;
        }

        public int PullTokens => GetCurrencyBalance(GachaCurrencyType.StandardPull);
        public int LimitedPullTokens => GetCurrencyBalance(GachaCurrencyType.LimitedPull);
        public int RootCredits { get; private set; }
        public IReadOnlyList<DistroDefinition> BannerPool => bannerPool;
        public GachaBannerState BeginnerState => beginnerState;
        public bool IsBeginnerBannerAvailable => !beginnerState.exhausted && beginnerState.totalPulls < BeginnerMaxPulls && bannerPool.Count > 0;

        public event Action BannerPoolChanged;
        public event Action Changed;

        public void LoadProgress(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            data.EnsureLists();
            RootCredits = Math.Max(0, data.rootCredits);
            SetCurrencyBalanceSilently(GachaCurrencyType.StandardPull, data.standardPullCurrency);
            SetCurrencyBalanceSilently(GachaCurrencyType.LimitedPull, data.limitedPullCurrency);
            beginnerState = data.beginnerBannerState ?? new GachaBannerState(BeginnerBannerId);
            beginnerState.bannerId = BeginnerBannerId;
            beginnerState.EnsureLists();
            if (beginnerState.totalPulls >= BeginnerMaxPulls)
            {
                beginnerState.exhausted = true;
            }

            beginnerState.fiveStarPityCounter = Math.Max(0, beginnerState.fiveStarPityCounter);

            Changed?.Invoke();
        }

        public void WriteProgress(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            data.EnsureLists();
            data.rootCredits = RootCredits;
            data.standardPullCurrency = GetCurrencyBalance(GachaCurrencyType.StandardPull);
            data.limitedPullCurrency = GetCurrencyBalance(GachaCurrencyType.LimitedPull);
            data.beginnerBannerState = CloneState(beginnerState);
        }

        public int GetCurrencyBalance(GachaCurrencyType currencyType)
        {
            return currencyBalances.TryGetValue(currencyType, out int balance) ? balance : 0;
        }

        public void SetCurrencyBalance(GachaCurrencyType currencyType, int amount)
        {
            SetCurrencyBalanceSilently(currencyType, amount);
            Changed?.Invoke();
        }

        public void AddCurrency(GachaCurrencyType currencyType, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            SetCurrencyBalance(currencyType, GetCurrencyBalance(currencyType) + amount);
        }

        public void AddRootCredits(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            RootCredits += amount;
            Changed?.Invoke();
        }

        public bool ConvertRootCreditsToEntropy(EntropyWallet wallet, int amount, out string failureReason)
        {
            if (wallet == null)
            {
                failureReason = "entropy wallet unavailable";
                return false;
            }

            if (amount <= 0)
            {
                failureReason = "no root-credits selected";
                return false;
            }

            if (RootCredits < amount)
            {
                failureReason = $"need {amount} root-credits";
                return false;
            }

            RootCredits -= amount;
            wallet.Add(amount);
            failureReason = null;
            Changed?.Invoke();
            return true;
        }

        public void AddToBannerPool(DistroDefinition distro)
        {
            if (distro == null || bannerPool.Contains(distro))
            {
                return;
            }

            bannerPool.Add(distro);
            BannerPoolChanged?.Invoke();
            Changed?.Invoke();
        }

        public void SetBeginnerGuaranteedDistros(IReadOnlyList<DistroDefinition> distros)
        {
            beginnerState.guaranteedDistroIds.Clear();
            if (distros != null)
            {
                for (int i = 0; i < distros.Count; i++)
                {
                    DistroDefinition distro = distros[i];
                    if (distro != null && !string.IsNullOrWhiteSpace(distro.Id))
                    {
                        beginnerState.guaranteedDistroIds.Add(distro.Id);
                    }
                }
            }

            Changed?.Invoke();
        }

        public int GetBeginnerPullCost(int pullCount)
        {
            return pullCount == 10 ? BeginnerTenPullCost : BeginnerSinglePullCost;
        }

        public bool CanPullBeginner(int pullCount, out string failureReason)
        {
            if (pullCount != 1 && pullCount != 10)
            {
                failureReason = "only single and ten pulls are supported";
                return false;
            }

            if (!IsBeginnerBannerAvailable)
            {
                failureReason = "beginner banner unavailable";
                return false;
            }

            int remainingPulls = BeginnerMaxPulls - beginnerState.totalPulls;
            if (pullCount > remainingPulls)
            {
                failureReason = $"only {remainingPulls} beginner pull(s) remain";
                return false;
            }

            int cost = GetBeginnerPullCost(pullCount);
            if (GetCurrencyBalance(GachaCurrencyType.StandardPull) < cost)
            {
                failureReason = $"need {cost} {FormatCurrencyName(GachaCurrencyType.StandardPull)}";
                return false;
            }

            failureReason = null;
            return true;
        }

        public int GetMissingPullTokens(GachaCurrencyType currencyType, int pullCost)
        {
            return Math.Max(0, pullCost - GetCurrencyBalance(currencyType));
        }

        public bool CanCoverMissingPullTokensWithEntropy(EntropyWallet wallet, int missingPullTokens)
        {
            return missingPullTokens <= 0 || (wallet != null && wallet.Balance >= missingPullTokens * EntropyPerPullToken);
        }

        public GachaPullResult PerformBeginnerPull(int pullCount)
        {
            return PerformBeginnerPull(pullCount, null, 0);
        }

        public GachaPullResult PerformBeginnerPull(int pullCount, EntropyWallet wallet, int entropyTokenCount)
        {
            if (!CanPullBeginnerWithEntropy(pullCount, wallet, entropyTokenCount, out string failureReason))
            {
                return GachaPullResult.Failed(BeginnerBannerId, failureReason);
            }

            int cost = GetBeginnerPullCost(pullCount);
            int tokenSpend = cost - entropyTokenCount;
            if (tokenSpend > 0)
            {
                SetCurrencyBalanceSilently(GachaCurrencyType.StandardPull, GetCurrencyBalance(GachaCurrencyType.StandardPull) - tokenSpend);
            }

            int entropySpent = entropyTokenCount * EntropyPerPullToken;
            if (entropySpent > 0)
            {
                wallet.Spend(entropySpent);
            }

            List<GachaReward> rewards = new();
            for (int i = 0; i < pullCount; i++)
            {
                beginnerState.totalPulls++;
                rewards.Add(RollBeginnerReward(beginnerState.totalPulls));
            }

            if (beginnerState.totalPulls >= BeginnerMaxPulls)
            {
                beginnerState.exhausted = true;
            }

            Changed?.Invoke();
            return new GachaPullResult(true, BeginnerBannerId, GachaCurrencyType.StandardPull, tokenSpend, entropySpent, rewards, null);
        }

        public static string FormatCurrencyName(GachaCurrencyType currencyType)
        {
            return currencyType switch
            {
                GachaCurrencyType.LimitedPull => "feature-pull-token",
                _ => "stable-pull-token"
            };
        }

        public static double GetFiveStarChance(GachaBannerState state)
        {
            return GetFiveStarChance(state == null ? 0 : state.fiveStarPityCounter);
        }

        public static double GetFiveStarChance(int fiveStarPityCounter)
        {
            int pityPullNumber = Math.Max(0, fiveStarPityCounter) + 1;
            if (pityPullNumber >= FiveStarHardPity)
            {
                return 1d;
            }

            if (pityPullNumber < FiveStarSoftPityStart)
            {
                return FiveStarBaseChance;
            }

            int softPitySteps = pityPullNumber - FiveStarSoftPityStart + 1;
            return Math.Min(1d, FiveStarBaseChance + softPitySteps * FiveStarSoftPityStep);
        }

        public GachaPullResult PerformPull(EntropyWallet wallet, int cost)
        {
            return PerformBeginnerPull(1);
        }

        private GachaReward RollBeginnerReward(int pullNumber)
        {
            if (pullNumber == 20)
            {
                AdvanceFiveStarPityAfterNonFiveStar(beginnerState);
                return RewardGuaranteedBeginnerDistro(0, "20-pull starter guarantee");
            }

            if (pullNumber == 40)
            {
                AdvanceFiveStarPityAfterNonFiveStar(beginnerState);
                return RewardGuaranteedBeginnerDistro(1, "40-pull starter guarantee");
            }

            if (pullNumber == 50)
            {
                beginnerState.pityCounter = 0;
                return GachaReward.FutureStandardFiveStar("future standard 5-star distro", "50-pull beginner capstone");
            }

            if (TryRollFiveStar(beginnerState, out bool fiveStarPityTriggered))
            {
                return RewardRandomFiveStar(fiveStarPityTriggered);
            }

            bool pityTriggered = beginnerState.pityCounter >= FourStarHardPity - 1;
            bool hitFourStarOrCharacter = pityTriggered || random.NextDouble() < FourStarBaseChance;
            if (!hitFourStarOrCharacter)
            {
                beginnerState.pityCounter++;
                return GachaReward.Equipment(3, "3-star equipment", false, false);
            }

            beginnerState.pityCounter = 0;
            if (bannerPool.Count > 0 && random.NextDouble() < DistroChanceOnFeaturedTier)
            {
                DistroDefinition distro = bannerPool[random.Next(bannerPool.Count)];
                return GachaReward.DistroReward(distro, 4, pityTriggered, false);
            }

            return GachaReward.Equipment(4, "4-star equipment", pityTriggered, false);
        }

        private bool TryRollFiveStar(GachaBannerState state, out bool pityTriggered)
        {
            state ??= beginnerState;
            int pityPullNumber = Math.Max(0, state.fiveStarPityCounter) + 1;
            pityTriggered = pityPullNumber >= FiveStarSoftPityStart;

            double chance = GetFiveStarChance(state);
            bool hit = chance >= 1d || random.NextDouble() < chance;
            if (!hit)
            {
                state.fiveStarPityCounter++;
                return false;
            }

            state.fiveStarPityCounter = 0;
            state.pityCounter = 0;
            return true;
        }

        private static void AdvanceFiveStarPityAfterNonFiveStar(GachaBannerState state)
        {
            if (state != null)
            {
                state.fiveStarPityCounter++;
            }
        }

        private GachaReward RewardRandomFiveStar(bool pityTriggered)
        {
            if (bannerPool.Count == 0 || random.NextDouble() >= DistroChanceOnFeaturedTier)
            {
                return GachaReward.Equipment(5, "5-star equipment", pityTriggered, false);
            }

            DistroDefinition distro = bannerPool.Count == 0 ? null : bannerPool[random.Next(bannerPool.Count)];
            return GachaReward.DistroReward(distro, 5, pityTriggered, false);
        }

        public bool ResolveFeaturedTierIsDistro()
        {
            return random.NextDouble() < DistroChanceOnFeaturedTier;
        }

        public bool ResolveLimitedFiveStarUsesStandardPool(GachaBannerState limitedState)
        {
            if (limitedState == null)
            {
                return false;
            }

            if (limitedState.featuredFiveStarGuaranteed)
            {
                limitedState.featuredFiveStarGuaranteed = false;
                return false;
            }

            bool useStandardPool = random.NextDouble() < LimitedStandardFiveStarChance;
            limitedState.featuredFiveStarGuaranteed = useStandardPool;
            return useStandardPool;
        }

        private GachaReward RewardGuaranteedBeginnerDistro(int guaranteeIndex, string reason)
        {
            beginnerState.pityCounter = 0;
            DistroDefinition distro = null;
            if (guaranteeIndex >= 0 && guaranteeIndex < beginnerState.guaranteedDistroIds.Count)
            {
                distro = FindBannerPoolDistro(beginnerState.guaranteedDistroIds[guaranteeIndex]);
            }

            distro ??= bannerPool.Count == 0 ? null : bannerPool[Math.Min(guaranteeIndex, bannerPool.Count - 1)];
            return distro == null
                ? GachaReward.Equipment(4, "4-star equipment", true, true)
                : GachaReward.DistroReward(distro, 4, true, true, reason);
        }

        private DistroDefinition FindBannerPoolDistro(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            for (int i = 0; i < bannerPool.Count; i++)
            {
                DistroDefinition distro = bannerPool[i];
                if (distro != null && string.Equals(distro.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return distro;
                }
            }

            return null;
        }

        private void SetCurrencyBalanceSilently(GachaCurrencyType currencyType, int amount)
        {
            currencyBalances[currencyType] = Math.Max(0, amount);
        }

        private bool CanPullBeginnerWithEntropy(int pullCount, EntropyWallet wallet, int entropyTokenCount, out string failureReason)
        {
            if (pullCount != 1 && pullCount != 10)
            {
                failureReason = "only single and ten pulls are supported";
                return false;
            }

            if (!IsBeginnerBannerAvailable)
            {
                failureReason = "beginner banner unavailable";
                return false;
            }

            int remainingPulls = BeginnerMaxPulls - beginnerState.totalPulls;
            if (pullCount > remainingPulls)
            {
                failureReason = $"only {remainingPulls} beginner pull(s) remain";
                return false;
            }

            int cost = GetBeginnerPullCost(pullCount);
            if (entropyTokenCount < 0 || entropyTokenCount > cost)
            {
                failureReason = "invalid entropy substitute amount";
                return false;
            }

            int tokenSpend = cost - entropyTokenCount;
            if (GetCurrencyBalance(GachaCurrencyType.StandardPull) < tokenSpend)
            {
                failureReason = $"need {tokenSpend} {FormatCurrencyName(GachaCurrencyType.StandardPull)}";
                return false;
            }

            int entropySpend = entropyTokenCount * EntropyPerPullToken;
            if (entropySpend > 0 && (wallet == null || wallet.Balance < entropySpend))
            {
                failureReason = $"need {entropySpend} entropy";
                return false;
            }

            failureReason = null;
            return true;
        }

        private static GachaBannerState CloneState(GachaBannerState state)
        {
            state ??= new GachaBannerState(BeginnerBannerId);
            state.EnsureLists();
            GachaBannerState clone = new(state.bannerId)
            {
                totalPulls = state.totalPulls,
                pityCounter = state.pityCounter,
                fiveStarPityCounter = state.fiveStarPityCounter,
                exhausted = state.exhausted,
                featuredFiveStarGuaranteed = state.featuredFiveStarGuaranteed
            };
            clone.guaranteedDistroIds.AddRange(state.guaranteedDistroIds);
            return clone;
        }
    }

    /// <summary>
    /// Describes one reward entry from one pull.
    /// </summary>
    public readonly struct GachaReward
    {
        private GachaReward(
            GachaRewardType rewardType,
            int starRating,
            string displayName,
            DistroDefinition distro,
            bool pityTriggered,
            bool guaranteed,
            string guaranteeReason)
        {
            RewardType = rewardType;
            StarRating = starRating;
            DisplayName = displayName;
            Distro = distro;
            PityTriggered = pityTriggered;
            Guaranteed = guaranteed;
            GuaranteeReason = guaranteeReason;
        }

        public GachaRewardType RewardType { get; }
        public int StarRating { get; }
        public string DisplayName { get; }
        public DistroDefinition Distro { get; }
        public bool PityTriggered { get; }
        public bool Guaranteed { get; }
        public string GuaranteeReason { get; }

        public static GachaReward Equipment(int starRating, string displayName, bool pityTriggered, bool guaranteed)
        {
            return new GachaReward(GachaRewardType.Equipment, starRating, displayName, null, pityTriggered, guaranteed, null);
        }

        public static GachaReward DistroReward(DistroDefinition distro, int starRating, bool pityTriggered, bool guaranteed, string guaranteeReason = null)
        {
            string displayName = distro == null ? "unknown distro" : DistroDisplayName(distro);
            return new GachaReward(GachaRewardType.Distro, starRating, displayName, distro, pityTriggered, guaranteed, guaranteeReason);
        }

        public static GachaReward FutureStandardFiveStar(string displayName, string guaranteeReason)
        {
            return new GachaReward(GachaRewardType.FutureStandardFiveStarDistro, 5, displayName, null, true, true, guaranteeReason);
        }

        private static string DistroDisplayName(DistroDefinition distro)
        {
            return string.IsNullOrWhiteSpace(distro.DisplayName) ? distro.name : distro.DisplayName;
        }
    }

    /// <summary>
    /// Represents the result of a banner pull request.
    /// </summary>
    public readonly struct GachaPullResult
    {
        public GachaPullResult(
            bool success,
            string bannerId,
            GachaCurrencyType currencyType,
            int currencySpent,
            int entropySpent,
            IReadOnlyList<GachaReward> rewards,
            string failureReason)
        {
            Success = success;
            BannerId = bannerId;
            CurrencyType = currencyType;
            CurrencySpent = currencySpent;
            EntropySpent = entropySpent;
            Rewards = rewards ?? Array.Empty<GachaReward>();
            FailureReason = failureReason;
            UnlockId = Rewards.Count > 0 && Rewards[0].Distro != null ? Rewards[0].Distro.Id : null;
        }

        public bool Success { get; }
        public string BannerId { get; }
        public GachaCurrencyType CurrencyType { get; }
        public int CurrencySpent { get; }
        public int EntropySpent { get; }
        public IReadOnlyList<GachaReward> Rewards { get; }
        public string FailureReason { get; }
        public string UnlockId { get; }

        public static GachaPullResult Failed(string bannerId, string reason)
        {
            return new GachaPullResult(false, bannerId, GachaCurrencyType.StandardPull, 0, 0, Array.Empty<GachaReward>(), reason);
        }
    }
}
