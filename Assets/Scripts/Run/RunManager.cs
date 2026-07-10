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

        private readonly List<CardInstance> _runDeck = new();
        private readonly List<RepositoryOffer> _repositoryOffers = new();
        private readonly HashSet<string> _soldOfferKeys = new(StringComparer.OrdinalIgnoreCase);
        private int _bits;
        private int _rerollsThisVisit;
        private int _maxUptimeBonus;
        private int _maxCyclesBonus;
        private int _ramBonus;
        private int _packageMaxUptimeBonus;
        private int _packageMaxCyclesBonus;
        private int _packageRamBonus;
        private int _wavesCleared;
        private int _accruedBandwidth;
        private int _accruedEntropy;
        private bool _repositoryVisitActive;
        private bool _rewardsSettled;

        public int CurrentWaveNumber => currentWaveNumber;
        public bool IsRunActive => isRunActive;
        public bool IsPlayerDead => isPlayerDead;
        public RunConfig CurrentConfig { get; private set; }
        public IReadOnlyList<CardInstance> RunDeck => _runDeck;
        public IReadOnlyList<RepositoryOffer> RepositoryOffers => _repositoryOffers;
        public bool RepositoryVisitActive => _repositoryVisitActive;
        public int Bits => _bits;
        public int RerollCost => CombatTuning.RerollBaseCost + (_rerollsThisVisit * CombatTuning.RerollCostStep);
        public int MaxUptimeBonus => _maxUptimeBonus;
        public int MaxCyclesBonus => _maxCyclesBonus;
        public int RamBonus => _ramBonus;
        public int WavesCleared => _wavesCleared;
        public int AccruedBandwidth => _accruedBandwidth;
        public int AccruedEntropy => _accruedEntropy;
        public bool RewardsSettled => _rewardsSettled;

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
            _bits = 0;
            _rerollsThisVisit = 0;
            _maxUptimeBonus = 0;
            _maxCyclesBonus = 0;
            _ramBonus = 0;
            _packageMaxUptimeBonus = 0;
            _packageMaxCyclesBonus = 0;
            _packageRamBonus = 0;
            _wavesCleared = 0;
            _accruedBandwidth = 0;
            _accruedEntropy = 0;
            _rewardsSettled = false;
            ApplyKernelPackageBonuses(config);
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

            _bits += amount;
            RepositoryChanged?.Invoke();
        }

        public void GenerateRepositoryOffers(CardDatabase cardDatabase, LanguageDeckDatabase languageDeckDatabase)
        {
            _repositoryOffers.Clear();
            _rerollsThisVisit = 0;
            _soldOfferKeys.Clear();
            FillRepositoryOffers(cardDatabase, languageDeckDatabase);
            _repositoryVisitActive = true;
            RepositoryChanged?.Invoke();
        }

        public bool RerollRepositoryOffers(CardDatabase cardDatabase, LanguageDeckDatabase languageDeckDatabase)
        {
            int cost = RerollCost;
            if (_bits < cost)
            {
                return false;
            }

            _bits -= cost;
            _rerollsThisVisit++;
            _repositoryOffers.Clear();
            FillRepositoryOffers(cardDatabase, languageDeckDatabase);
            _repositoryVisitActive = true;
            RepositoryChanged?.Invoke();
            return true;
        }

        public bool BuyOffer(RepositoryOffer offer, CombatantState playerState)
        {
            if (offer == null || offer.Sold || _bits < offer.Price)
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

            _bits -= offer.Price;
            offer.MarkSold();
            _soldOfferKeys.Add(OfferKey(offer));
            RepositoryChanged?.Invoke();
            return true;
        }

        public bool RemoveCard(CardInstance card)
        {
            if (card == null || _bits < CombatTuning.RemoveCardCost || _runDeck.Count <= 1)
            {
                return false;
            }

            if (!_runDeck.Remove(card))
            {
                return false;
            }

            _bits -= CombatTuning.RemoveCardCost;
            RepositoryChanged?.Invoke();
            return true;
        }

        public int EffectiveMaxUptime()
        {
            return Mathf.Max(1, (CurrentConfig?.Distro?.BaseUptime ?? 1) + _maxUptimeBonus + _packageMaxUptimeBonus);
        }

        public int EffectiveRam()
        {
            return Mathf.Max(1, (CurrentConfig?.Distro?.BaseRam ?? 1) + _ramBonus + _packageRamBonus);
        }

        public int EffectiveMaxCycles()
        {
            return Mathf.Max(1, (CurrentConfig?.Distro?.BaseCyclesPerTurn ?? 1) + _maxCyclesBonus + _packageMaxCyclesBonus);
        }

        public static PackageStatImpact CalculatePackageStatImpact(DistroDefinition distro, IReadOnlyList<PackageInstance> packages)
        {
            PackageStatImpact impact = new(
                Mathf.Max(1, distro == null ? 1 : distro.BaseUptime),
                Mathf.Max(1, distro == null ? 1 : distro.BaseRam),
                Mathf.Max(1, distro == null ? 1 : distro.BaseCyclesPerTurn));
            if (packages == null)
            {
                return impact;
            }

            for (int i = 0; i < packages.Count; i++)
            {
                PackageInstance package = packages[i];
                if (package?.Definition == null || package.Definition.Slot != PackageSlot.Kernel)
                {
                    continue;
                }

                PackageEffectData effect = package.EffectFor(distro?.Id);
                int amount = Mathf.Max(0, effect.Amount);
                switch (effect.Kind)
                {
                    case PackageEffectKind.MaxUptime:
                        impact.AddUptime(amount, package.Definition);
                        break;
                    case PackageEffectKind.MaxCycles:
                        impact.AddCycles(amount, package.Definition);
                        break;
                    case PackageEffectKind.MaxRam:
                        impact.AddRam(amount, package.Definition);
                        break;
                }
            }

            return impact;
        }

        private void ApplyKernelPackageBonuses(RunConfig config)
        {
            PackageStatImpact impact = CalculatePackageStatImpact(config?.Distro, config?.EquippedPackages);
            _packageMaxUptimeBonus = impact.UptimeBonus;
            _packageMaxCyclesBonus = impact.CyclesBonus;
            _packageRamBonus = impact.RamBonus;
        }

        public bool TrySettleRunRewards(out int bandwidth, out int entropy)
        {
            bandwidth = _accruedBandwidth;
            entropy = _accruedEntropy;
            if (_rewardsSettled)
            {
                return false;
            }

            _rewardsSettled = true;
            return bandwidth > 0 || entropy > 0;
        }

        private void HandleWaveCleared(WaveClearedEvent payload)
        {
            int clearedWave = Mathf.Max(1, payload.WaveNumber);
            _wavesCleared = Mathf.Max(_wavesCleared, clearedWave);
            _accruedBandwidth += CalculateBandwidthReward(clearedWave);
            _accruedEntropy += CalculateEntropyReward(clearedWave);
            currentWaveNumber = Mathf.Max(currentWaveNumber + 1, payload.WaveNumber + 1);
            _repositoryOffers.Clear();
            _rerollsThisVisit = 0;
            _soldOfferKeys.Clear();
            _repositoryVisitActive = false;
            GameEvents.RaiseWaveAdvanced(new WaveAdvancedEvent(currentWaveNumber));
        }

        private void HandleRunEnded(RunEndedEvent payload)
        {
            isRunActive = false;
            isPlayerDead = payload.PlayerDied;
            _bits = 0;
            _repositoryOffers.Clear();
            _repositoryVisitActive = false;
            _runDeck.Clear();
            _maxUptimeBonus = 0;
            _maxCyclesBonus = 0;
            _ramBonus = 0;
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
            _runDeck.Clear();
            if (config?.StartingDeck == null)
            {
                return;
            }

            for (int i = 0; i < config.StartingDeck.Count; i++)
            {
                CardDefinition definition = config.StartingDeck[i];
                if (definition != null && !definition.IsRunOnly)
                {
                    _runDeck.Add(new CardInstance(definition));
                }
            }
        }

        private void FillRepositoryOffers(CardDatabase cardDatabase, LanguageDeckDatabase languageDeckDatabase)
        {
            int guard = 0;
            while (_repositoryOffers.Count < CombatTuning.ShopSize && guard < 80)
            {
                guard++;
                RepositoryOffer offer = CreateRandomOffer(cardDatabase, languageDeckDatabase);
                if (offer == null || _soldOfferKeys.Contains(OfferKey(offer)) || _repositoryOffers.Any(existing => IsDuplicateOffer(existing, offer)))
                {
                    continue;
                }

                _repositoryOffers.Add(offer);
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
            List<CardInstance> candidates = _runDeck.Where(card => card != null && card.CanReceiveRepositoryUpgrade).ToList();
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

                    if (card.Language == CurrentConfig.PrimaryLanguage || card.Language == CurrentConfig.SecondaryLanguage)
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

            _runDeck.Add(new CardInstance(card));
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
                    _maxCyclesBonus += CombatTuning.StatUpgradeMaxCycles;
                    if (playerState != null)
                    {
                        playerState.MaxCycles += CombatTuning.StatUpgradeMaxCycles;
                        playerState.Cycles += CombatTuning.StatUpgradeMaxCycles;
                    }
                    return true;
                case RunStatUpgradeKind.MaxUptime:
                    int uptimeBonus = CombatTuning.ScaleStatUpgradeMaxUptime(playerState == null ? EffectiveMaxUptime() : playerState.MaxUptime);
                    _maxUptimeBonus += uptimeBonus;
                    if (playerState != null)
                    {
                        playerState.MaxUptime += uptimeBonus;
                    }
                    return true;
                case RunStatUpgradeKind.Heal:
                    if (playerState == null)
                    {
                        return false;
                    }

                    int healAmount = CombatTuning.ScaleStatUpgradeHeal(playerState.MaxUptime);
                    playerState.CurrentUptime = Mathf.Min(playerState.MaxUptime, playerState.CurrentUptime + healAmount);
                    return true;
                case RunStatUpgradeKind.Ram:
                    _ramBonus += CombatTuning.StatUpgradeRam;
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

    public struct PackageStatImpact
    {
        private readonly List<string> _uptimeSources;
        private readonly List<string> _ramSources;
        private readonly List<string> _cyclesSources;

        public PackageStatImpact(int baseUptime, int baseRam, int baseCycles)
        {
            BaseUptime = baseUptime;
            BaseRam = baseRam;
            BaseCycles = baseCycles;
            UptimeBonus = 0;
            RamBonus = 0;
            CyclesBonus = 0;
            _uptimeSources = new List<string>();
            _ramSources = new List<string>();
            _cyclesSources = new List<string>();
        }

        public int BaseUptime { get; }
        public int BaseRam { get; }
        public int BaseCycles { get; }
        public int UptimeBonus { get; private set; }
        public int RamBonus { get; private set; }
        public int CyclesBonus { get; private set; }
        public int FinalUptime => Mathf.Max(1, BaseUptime + UptimeBonus);
        public int FinalRam => Mathf.Max(1, BaseRam + RamBonus);
        public int FinalCycles => Mathf.Max(1, BaseCycles + CyclesBonus);
        public IReadOnlyList<string> UptimeSources => _uptimeSources;
        public IReadOnlyList<string> RamSources => _ramSources;
        public IReadOnlyList<string> CyclesSources => _cyclesSources;

        public void AddUptime(int amount, PackageDefinition source)
        {
            if (amount <= 0)
            {
                return;
            }

            UptimeBonus += amount;
            AddSource(source, _uptimeSources);
        }

        public void AddRam(int amount, PackageDefinition source)
        {
            if (amount <= 0)
            {
                return;
            }

            RamBonus += amount;
            AddSource(source, _ramSources);
        }

        public void AddCycles(int amount, PackageDefinition source)
        {
            if (amount <= 0)
            {
                return;
            }

            CyclesBonus += amount;
            AddSource(source, _cyclesSources);
        }

        private static void AddSource(PackageDefinition source, List<string> sources)
        {
            string name = source == null ? null : string.IsNullOrWhiteSpace(source.DisplayName) ? source.Id : source.DisplayName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                sources.Add(name);
            }
        }
    }
}
