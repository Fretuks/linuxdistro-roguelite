using KernelPanic.Combat;
using KernelPanic.Core;
using KernelPanic.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KernelPanic.Run
{
    /// <summary>
    /// Coordinates run lifecycle, wave progression, and run-level event handling.
    /// </summary>
    public sealed class RunManager : MonoBehaviour
    {
        [SerializeField] private CombatManager combatManager;
        [SerializeField] private int currentWaveNumber;
        [SerializeField] private bool isRunActive;
        [SerializeField] private bool isPlayerDead;

        private readonly List<CardInstance> runDeck = new();
        private readonly List<RepositoryOffer> repositoryOffers = new();
        private readonly HashSet<string> soldOfferKeys = new(StringComparer.OrdinalIgnoreCase);
        private int bits;
        private int rerollsThisVisit;
        private int maxUptimeBonus;
        private int maxCyclesBonus;
        private int ramBonus;
        private int wavesCleared;
        private int accruedBandwidth;
        private int accruedEntropy;
        private bool repositoryVisitActive;
        private bool rewardsSettled;

        public int CurrentWaveNumber => currentWaveNumber;
        public bool IsRunActive => isRunActive;
        public bool IsPlayerDead => isPlayerDead;
        public RunConfig CurrentConfig { get; private set; }
        public IReadOnlyList<CardInstance> RunDeck => runDeck;
        public IReadOnlyList<RepositoryOffer> RepositoryOffers => repositoryOffers;
        public bool RepositoryVisitActive => repositoryVisitActive;
        public int Bits => bits;
        public int RerollCost => CombatTuning.RerollBaseCost + (rerollsThisVisit * CombatTuning.RerollCostStep);
        public int MaxUptimeBonus => maxUptimeBonus;
        public int MaxCyclesBonus => maxCyclesBonus;
        public int RamBonus => ramBonus;
        public int WavesCleared => wavesCleared;
        public int AccruedBandwidth => accruedBandwidth;
        public int AccruedEntropy => accruedEntropy;
        public bool RewardsSettled => rewardsSettled;

        public event Action RepositoryChanged;

        private void OnEnable()
        {
            GameEvents.WaveCleared += HandleWaveCleared;
            GameEvents.RunEnded += HandleRunEnded;
        }

        private void OnDisable()
        {
            GameEvents.WaveCleared -= HandleWaveCleared;
            GameEvents.RunEnded -= HandleRunEnded;
        }

        public void StartRun(RunConfig config)
        {
            CurrentConfig = config;
            isRunActive = true;
            isPlayerDead = false;
            currentWaveNumber = 1;
            bits = 0;
            rerollsThisVisit = 0;
            maxUptimeBonus = 0;
            maxCyclesBonus = 0;
            ramBonus = 0;
            wavesCleared = 0;
            accruedBandwidth = 0;
            accruedEntropy = 0;
            rewardsSettled = false;
            InitializeRunDeck(config);
            StartCombat();
        }

        public void StartCombat()
        {
            combatManager?.StartCombat(CurrentConfig);
        }

        public void AddBits(int amount)
        {
            if (!isRunActive || amount <= 0)
            {
                return;
            }

            bits += amount;
            RepositoryChanged?.Invoke();
        }

        public void GenerateRepositoryOffers(CardDatabase cardDatabase, LanguageDeckDatabase languageDeckDatabase)
        {
            repositoryOffers.Clear();
            rerollsThisVisit = 0;
            soldOfferKeys.Clear();
            FillRepositoryOffers(cardDatabase, languageDeckDatabase);
            repositoryVisitActive = true;
            RepositoryChanged?.Invoke();
        }

        public bool RerollRepositoryOffers(CardDatabase cardDatabase, LanguageDeckDatabase languageDeckDatabase)
        {
            int cost = RerollCost;
            if (bits < cost)
            {
                return false;
            }

            bits -= cost;
            rerollsThisVisit++;
            repositoryOffers.Clear();
            FillRepositoryOffers(cardDatabase, languageDeckDatabase);
            repositoryVisitActive = true;
            RepositoryChanged?.Invoke();
            return true;
        }

        public bool BuyOffer(RepositoryOffer offer, CombatantState playerState)
        {
            if (offer == null || offer.Sold || bits < offer.Price)
            {
                return false;
            }

            bool applied = offer.Kind switch
            {
                RepositoryOfferKind.NewCard => AddCardToRunDeck(offer.CardDefinition),
                RepositoryOfferKind.CardUpgrade => ApplyCardUpgrade(offer.TargetCard, offer.CardUpgradeKind),
                RepositoryOfferKind.StatUpgrade => ApplyStatUpgrade(offer.StatUpgradeKind, playerState),
                _ => false
            };

            if (!applied)
            {
                return false;
            }

            bits -= offer.Price;
            offer.MarkSold();
            soldOfferKeys.Add(OfferKey(offer));
            RepositoryChanged?.Invoke();
            return true;
        }

        public bool RemoveCard(CardInstance card)
        {
            if (card == null || bits < CombatTuning.RemoveCardCost || runDeck.Count <= 1)
            {
                return false;
            }

            if (!runDeck.Remove(card))
            {
                return false;
            }

            bits -= CombatTuning.RemoveCardCost;
            RepositoryChanged?.Invoke();
            return true;
        }

        public int EffectiveMaxUptime()
        {
            return Mathf.Max(1, (CurrentConfig?.Distro?.BaseUptime ?? 1) + maxUptimeBonus);
        }

        public int EffectiveRam()
        {
            return Mathf.Max(1, (CurrentConfig?.Distro?.BaseRam ?? 1) + ramBonus);
        }

        public int EffectiveMaxCycles()
        {
            return Mathf.Max(1, (CurrentConfig?.Distro?.BaseCyclesPerTurn ?? 1) + maxCyclesBonus);
        }

        public bool TrySettleRunRewards(out int bandwidth, out int entropy)
        {
            bandwidth = accruedBandwidth;
            entropy = accruedEntropy;
            if (rewardsSettled)
            {
                return false;
            }

            rewardsSettled = true;
            return bandwidth > 0 || entropy > 0;
        }

        private void HandleWaveCleared(WaveClearedEvent payload)
        {
            int clearedWave = Mathf.Max(1, payload.WaveNumber);
            wavesCleared = Mathf.Max(wavesCleared, clearedWave);
            accruedBandwidth += CalculateBandwidthReward(clearedWave);
            accruedEntropy += CalculateEntropyReward(clearedWave);
            currentWaveNumber = Mathf.Max(currentWaveNumber + 1, payload.WaveNumber + 1);
            repositoryOffers.Clear();
            rerollsThisVisit = 0;
            soldOfferKeys.Clear();
            repositoryVisitActive = false;
            GameEvents.RaiseWaveAdvanced(new WaveAdvancedEvent(currentWaveNumber));
        }

        private void HandleRunEnded(RunEndedEvent payload)
        {
            isRunActive = false;
            isPlayerDead = payload.PlayerDied;
            bits = 0;
            repositoryOffers.Clear();
            repositoryVisitActive = false;
            runDeck.Clear();
            maxUptimeBonus = 0;
            maxCyclesBonus = 0;
            ramBonus = 0;
            RepositoryChanged?.Invoke();
        }

        private static int CalculateBandwidthReward(int waveNumber)
        {
            int safeWave = Mathf.Max(1, waveNumber);
            return Mathf.Max(0, CombatTuning.BandwidthBase + ((safeWave - 1) * CombatTuning.BandwidthPerWaveStep));
        }

        private static int CalculateEntropyReward(int waveNumber)
        {
            return waveNumber >= CombatTuning.EntropyStartWave ? Mathf.Max(0, CombatTuning.EntropyPerWave) : 0;
        }

        private void InitializeRunDeck(RunConfig config)
        {
            runDeck.Clear();
            if (config?.StartingDeck == null)
            {
                return;
            }

            for (int i = 0; i < config.StartingDeck.Count; i++)
            {
                CardDefinition definition = config.StartingDeck[i];
                if (definition != null && !definition.IsRunOnly)
                {
                    runDeck.Add(new CardInstance(definition));
                }
            }
        }

        private void FillRepositoryOffers(CardDatabase cardDatabase, LanguageDeckDatabase languageDeckDatabase)
        {
            int guard = 0;
            while (repositoryOffers.Count < CombatTuning.ShopSize && guard < 80)
            {
                guard++;
                RepositoryOffer offer = CreateRandomOffer(cardDatabase, languageDeckDatabase);
                if (offer == null || soldOfferKeys.Contains(OfferKey(offer)) || repositoryOffers.Any(existing => IsDuplicateOffer(existing, offer)))
                {
                    continue;
                }

                repositoryOffers.Add(offer);
            }
        }

        private RepositoryOffer CreateRandomOffer(CardDatabase cardDatabase, LanguageDeckDatabase languageDeckDatabase)
        {
            int roll = UnityEngine.Random.Range(0, 100);
            if (roll < 45)
            {
                return CreateCardOffer(cardDatabase, languageDeckDatabase);
            }

            if (roll < 75)
            {
                return CreateUpgradeOffer();
            }

            return CreateStatOffer();
        }

        private RepositoryOffer CreateCardOffer(CardDatabase cardDatabase, LanguageDeckDatabase languageDeckDatabase)
        {
            List<CardDefinition> pool = BuildCardOfferPool(cardDatabase, languageDeckDatabase);
            if (pool.Count == 0)
            {
                return null;
            }

            CardDefinition selected = pool[UnityEngine.Random.Range(0, pool.Count)];
            return RepositoryOffer.NewCard(selected, selected.IsRunOnly ? CombatTuning.NewCardOfferCost + 1 : CombatTuning.NewCardOfferCost);
        }

        private RepositoryOffer CreateUpgradeOffer()
        {
            List<CardInstance> candidates = runDeck.Where(card => card != null && card.CanReceiveRepositoryUpgrade).ToList();
            if (candidates.Count == 0)
            {
                return CreateStatOffer();
            }

            CardInstance target = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            CardUpgradeKind kind = (CardUpgradeKind)UnityEngine.Random.Range(0, 3);
            if (kind == CardUpgradeKind.CostDown && CombatManager.GetCardCost(target) <= 0)
            {
                kind = CardUpgradeKind.MagnitudeUp;
            }

            return RepositoryOffer.CardUpgrade(target, kind, CombatTuning.UpgradeOfferCost);
        }

        private static RepositoryOffer CreateStatOffer()
        {
            RunStatUpgradeKind kind = (RunStatUpgradeKind)UnityEngine.Random.Range(0, 4);
            return RepositoryOffer.StatUpgrade(kind, CombatTuning.StatUpgradeCost);
        }

        private List<CardDefinition> BuildCardOfferPool(CardDatabase cardDatabase, LanguageDeckDatabase languageDeckDatabase)
        {
            List<CardDefinition> pool = new();
            AddLanguageCards(languageDeckDatabase, CurrentConfig?.PrimaryLanguage ?? Language.Python, pool, 3);
            AddLanguageCards(languageDeckDatabase, CurrentConfig?.SecondaryLanguage ?? Language.JavaScript, pool, 3);

            if (cardDatabase != null)
            {
                for (int i = 0; i < cardDatabase.AllCards.Count; i++)
                {
                    CardDefinition card = cardDatabase.AllCards[i];
                    if (card == null || card.IsToken)
                    {
                        continue;
                    }

                    if (card.IsRunOnly || card.Language == CurrentConfig.PrimaryLanguage || card.Language == CurrentConfig.SecondaryLanguage)
                    {
                        pool.Add(card);
                    }
                }
            }

            return pool;
        }

        private static void AddLanguageCards(LanguageDeckDatabase languageDeckDatabase, Language language, List<CardDefinition> pool, int weight)
        {
            LanguageDeckDefinition deck = languageDeckDatabase == null ? null : languageDeckDatabase.FindByLanguage(language);
            if (deck == null)
            {
                return;
            }

            for (int i = 0; i < deck.Entries.Count; i++)
            {
                CardDefinition card = deck.Entries[i].Card;
                if (card == null || card.IsToken || card.IsRunOnly)
                {
                    continue;
                }

                for (int copy = 0; copy < Mathf.Max(1, weight); copy++)
                {
                    pool.Add(card);
                }
            }
        }

        private static bool IsDuplicateOffer(RepositoryOffer left, RepositoryOffer right)
        {
            if (left.Kind != right.Kind)
            {
                return false;
            }

            return left.Kind switch
            {
                RepositoryOfferKind.NewCard => left.CardDefinition == right.CardDefinition,
                RepositoryOfferKind.CardUpgrade => left.TargetCard == right.TargetCard && left.CardUpgradeKind == right.CardUpgradeKind,
                RepositoryOfferKind.StatUpgrade => left.StatUpgradeKind == right.StatUpgradeKind,
                _ => false
            };
        }

        private static string OfferKey(RepositoryOffer offer)
        {
            if (offer == null)
            {
                return string.Empty;
            }

            return offer.Kind switch
            {
                RepositoryOfferKind.NewCard => $"card:{offer.CardDefinition?.Id}",
                RepositoryOfferKind.CardUpgrade => $"upgrade:{offer.TargetCard?.Definition?.Id}:{offer.TargetCard?.GetHashCode()}:{offer.CardUpgradeKind}",
                RepositoryOfferKind.StatUpgrade => $"stat:{offer.StatUpgradeKind}",
                _ => offer.DisplayName
            };
        }

        private bool AddCardToRunDeck(CardDefinition card)
        {
            if (card == null)
            {
                return false;
            }

            runDeck.Add(new CardInstance(card));
            return true;
        }

        private static bool ApplyCardUpgrade(CardInstance card, CardUpgradeKind kind)
        {
            if (card == null)
            {
                return false;
            }

            return kind switch
            {
                CardUpgradeKind.CostDown => card.ApplyCostUpgrade(-1),
                CardUpgradeKind.MagnitudeUp => card.ApplyMagnitudeUpgrade(CombatTuning.UpgradeMagnitudeBonus),
                CardUpgradeKind.DrawRider => card.ApplyDrawRider(1),
                _ => false
            };
        }

        private bool ApplyStatUpgrade(RunStatUpgradeKind kind, CombatantState playerState)
        {
            switch (kind)
            {
                case RunStatUpgradeKind.MaxCycles:
                    maxCyclesBonus += CombatTuning.StatUpgradeMaxCycles;
                    if (playerState != null)
                    {
                        playerState.MaxCycles += CombatTuning.StatUpgradeMaxCycles;
                        playerState.Cycles += CombatTuning.StatUpgradeMaxCycles;
                    }
                    return true;
                case RunStatUpgradeKind.MaxUptime:
                    maxUptimeBonus += CombatTuning.StatUpgradeMaxUptime;
                    if (playerState != null)
                    {
                        playerState.MaxUptime += CombatTuning.StatUpgradeMaxUptime;
                    }
                    return true;
                case RunStatUpgradeKind.Heal:
                    if (playerState == null)
                    {
                        return false;
                    }

                    playerState.CurrentUptime = Mathf.Min(playerState.MaxUptime, playerState.CurrentUptime + CombatTuning.StatUpgradeHeal);
                    return true;
                case RunStatUpgradeKind.Ram:
                    ramBonus += CombatTuning.StatUpgradeRam;
                    if (playerState != null)
                    {
                        playerState.Ram += CombatTuning.StatUpgradeRam;
                    }
                    return true;
                default:
                    return false;
            }
        }
    }
}
