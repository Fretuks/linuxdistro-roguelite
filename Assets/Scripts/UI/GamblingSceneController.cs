using System;
using System.Collections;
using System.Collections.Generic;
using KernelPanic.Core;
using KernelPanic.Data;
using KernelPanic.Meta;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// UI Toolkit gacha pull cutscene rendered by GamblingScene.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GamblingSceneController : MonoBehaviour
    {
        private const string GamblingStyleResourcePath = "GamblingScene";
        private const string RarityStyleResourcePath = "RarityPresentation";
        private const string SharedScrollbarStyleResourcePath = "TerminalScrollbars";
        private const int FlourishMs = 420;
        private const int CommonRevealMs = 150;
        private const int RareRevealMs = 240;
        private const int LegendaryRevealMs = 420;
        private const int RareAnticipationMs = 520;
        private const int LegendaryAnticipationMs = 820;
        private const int RareSparkCount = 6;
        private const int LegendarySparkCount = 18;
        private const int SparkLifetimeMs = 520;
        private const int PopClassMs = 280;
        private const int LegendaryShimmerMs = 480;
        private const int SummaryTickSteps = 8;
        private const int SummaryTickMs = 220;

        private readonly List<CompletedGachaReward> _rewards = new();
        private readonly List<PullCardView> _cardViews = new();
        private UIDocument _document;
        private VisualElement _root;
        private Label _commandLabel;
        private Label _costLabel;
        private Label _flourishLabel;
        private Label _skipHint;
        private VisualElement _resultGrid;
        private VisualElement _effectLayer;
        private VisualElement _goldShimmer;
        private VisualElement _summaryPanel;
        private Label _summaryTotalsLabel;
        private Button _continueButton;
        private string _completedHeader;
        private bool _skipRequested;
        private bool _revealComplete;
        private bool _failed;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _root = _document.rootVisualElement;
            _root.Clear();
            _root.focusable = true;
            _root.AddToClassList("gambling-root");
            _root.EnableInClassList("reduced-motion", UIPreferences.ReducedMotion);
            LoadStyles();
            BuildLayout();
            _root.RegisterCallback<KeyDownEvent>(HandleKeyDown);
            _root.RegisterCallback<PointerDownEvent>(HandlePointerDown);
        }

        private void OnDestroy()
        {
            if (_root == null)
            {
                return;
            }

            _root.UnregisterCallback<KeyDownEvent>(HandleKeyDown);
            _root.UnregisterCallback<PointerDownEvent>(HandlePointerDown);
        }

        private void Start()
        {
            _root.Focus();
            StartCoroutine(RunCutscene());
        }

        private void LoadStyles()
        {
            AddStyle(RarityStyleResourcePath);
            AddStyle(GamblingStyleResourcePath);
            AddStyle(SharedScrollbarStyleResourcePath);
        }

        private void AddStyle(string resourcePath)
        {
            StyleSheet styleSheet = Resources.Load<StyleSheet>(resourcePath);
            if (styleSheet != null)
            {
                _root.styleSheets.Add(styleSheet);
            }
        }

        private void BuildLayout()
        {
            VisualElement shell = new();
            shell.AddToClassList("gambling-shell");
            _root.Add(shell);

            VisualElement frame = new();
            frame.AddToClassList("gambling-frame");
            shell.Add(frame);

            VisualElement header = new();
            header.AddToClassList("gambling-header");
            frame.Add(header);

            VisualElement titleBlock = new();
            titleBlock.AddToClassList("gambling-title-block");
            header.Add(titleBlock);

            _commandLabel = new("git pull");
            _commandLabel.AddToClassList("gambling-command");
            titleBlock.Add(_commandLabel);

            _costLabel = new();
            _costLabel.AddToClassList("gambling-cost");
            titleBlock.Add(_costLabel);

            _skipHint = new("[tap/key] reveal all");
            _skipHint.AddToClassList("gambling-skip");
            header.Add(_skipHint);

            _flourishLabel = new();
            _flourishLabel.AddToClassList("gambling-flourish");
            frame.Add(_flourishLabel);

            _resultGrid = new VisualElement();
            _resultGrid.AddToClassList("gambling-results");
            frame.Add(_resultGrid);

            _goldShimmer = new VisualElement();
            _goldShimmer.AddToClassList("gold-shimmer");
            _goldShimmer.pickingMode = PickingMode.Ignore;
            frame.Add(_goldShimmer);

            _effectLayer = new VisualElement();
            _effectLayer.AddToClassList("effect-layer");
            _effectLayer.pickingMode = PickingMode.Ignore;
            frame.Add(_effectLayer);

            _summaryPanel = new VisualElement();
            _summaryPanel.AddToClassList("summary-panel");
            _summaryPanel.AddToClassList("hidden");
            frame.Add(_summaryPanel);

            _continueButton = new Button(ContinueToMenu) { text = "> continue" };
            _continueButton.AddToClassList("continue-button");
            _continueButton.AddToClassList("continue-button-pending");
            _continueButton.SetEnabled(false);
            frame.Add(_continueButton);
        }

        private IEnumerator RunCutscene()
        {
            if (!GachaPullContext.TryConsumePending(out PendingGachaPull pending))
            {
                _failed = true;
                _commandLabel.text = "git pull failed";
                _costLabel.text = "no pending pull";
                ShowContinue();
                yield break;
            }

            _commandLabel.text = pending.PullCount == 10 ? "git pull --ten" : "git pull --single";
            int pullCost = pending.PullCount == 10 ? GachaService.BeginnerTenPullCost : GachaService.BeginnerSinglePullCost;
            _costLabel.text = pending.EntropyTokenCount > 0
                ? $"cost: {pending.EntropyTokenCount * GachaService.EntropyPerCommit} Entropy"
                : $"cost: {pullCost} Commits / {pullCost * GachaService.EntropyPerCommit} Entropy";
            _flourishLabel.text = "$ fetch-pack | verify | unpack";

            CompletedGachaPull completed = ResolvePull(pending);
            GachaPullContext.SetCompleted(completed);
            _completedHeader = completed.HeaderText;
            _rewards.Clear();
            _rewards.AddRange(completed.Rewards);

            if (_rewards.Count == 0)
            {
                _failed = true;
                _costLabel.text = completed.HeaderText;
                ShowContinue();
                yield break;
            }

            BuildRows();
            if (UIPreferences.ReducedMotion)
            {
                RevealAll();
                yield break;
            }

            yield return WaitOrSkip(FlourishMs);
            _flourishLabel.text = "$ unpacking sealed objects";
            for (int i = 0; i < _rewards.Count; i++)
            {
                if (_skipRequested)
                {
                    RevealAll();
                    yield break;
                }

                CompletedGachaReward reward = _rewards[i];
                PullCardView card = _cardViews[i];
                int holdMs = GetAnticipationMs(reward.Stars);
                if (holdMs > 0)
                {
                    ApplyGridFocus(i, reward.Stars);
                    card.ShowHold(reward.Stars >= 5 ? "........ signed tag" : "... promoted object", reward.Stars);
                    yield return WaitOrSkip(holdMs);
                    if (_skipRequested)
                    {
                        RevealAll();
                        yield break;
                    }

                    card.HideHold();
                }

                card.Reveal(allowFlash: reward.Stars >= 4, persistentMotion: true);
                PlayRevealJuice(card, reward);
                yield return WaitOrSkip(GetRevealMs(reward.Stars));
                ClearGridFocus();
            }

            CompleteReveal();
        }

        private CompletedGachaPull ResolvePull(PendingGachaPull pending)
        {
            if (pending.DistroDatabase == null)
            {
                return new CompletedGachaPull(pending.BannerId, "git pull failed: distro database unavailable", Array.Empty<CompletedGachaReward>());
            }

            SaveService saveService = new();
            SaveData saveData = saveService.Load();
            saveData.EnsureLists();

            EntropyWallet wallet = new();
            wallet.SetBalance(saveData.entropyBalance);

            PlayerCollection collection = BuildCollection(saveData, pending.DistroDatabase, pending.PackageDatabase);
            GachaService gacha = new();
            RestoreBannerPool(gacha, saveData, pending.DistroDatabase);
            gacha.LoadProgress(saveData);

            GachaPullResult result = pending.BannerId == GachaService.BeginnerBannerId
                ? gacha.PerformBeginnerPull(pending.PullCount, wallet, pending.EntropyTokenCount > 0 ? PullPaymentSource.Entropy : PullPaymentSource.Commits)
                : GachaPullResult.Failed(pending.BannerId, "remote not implemented yet");

            if (!result.Success)
            {
                return new CompletedGachaPull(pending.BannerId, $"git pull failed: {result.FailureReason}", Array.Empty<CompletedGachaReward>());
            }

            List<string> header = new()
            {
                result.EntropySpent > 0
                    ? $"spent {result.EntropySpent} Entropy"
                    : $"spent {result.CurrencySpent} {GachaService.FormatCurrencyName(result.CurrencyType)}"
            };

            List<DistroDefinition> distroRewards = new();
            for (int i = 0; i < result.Rewards.Count; i++)
            {
                if (result.Rewards[i].Distro != null)
                {
                    distroRewards.Add(result.Rewards[i].Distro);
                }
            }

            PullResolutionResult resolution = PullResolver.Resolve(distroRewards, new PullResolutionContext(saveData, collection, pending.DistroDatabase, pending.FocusUnitId));
            List<CompletedGachaReward> completedRewards = new();
            int distroOutcomeIndex = 0;
            for (int i = 0; i < result.Rewards.Count; i++)
            {
                GachaReward reward = result.Rewards[i];
                PullResolutionOutcome? outcome = null;
                if (reward.Distro != null && resolution != null && distroOutcomeIndex < resolution.Outcomes.Count)
                {
                    outcome = resolution.Outcomes[distroOutcomeIndex];
                    distroOutcomeIndex++;
                }

                PullOutcomeKind outcomeKind = outcome?.Kind ?? PullOutcomeKind.Invalid;
                int merges = outcome?.MergesAwarded ?? 0;
                IReadOnlyList<Language> languages = outcome?.LanguagesNewlyUnlocked ?? Array.Empty<Language>();
                string displayName = reward.DisplayName;
                if (reward.RewardType == GachaRewardType.Package)
                {
                    PackageDefinition package = GrantRandomPackageByRarity(pending.PackageDatabase, collection, saveData, reward.StarRating, out bool duplicatePackage, out int cacheAwarded);
                    if (package != null)
                    {
                        displayName = string.IsNullOrWhiteSpace(package.DisplayName) ? package.Id : package.DisplayName;
                        outcomeKind = duplicatePackage ? PullOutcomeKind.Dupe : PullOutcomeKind.Granted;
                        merges = cacheAwarded;
                    }
                }

                completedRewards.Add(new CompletedGachaReward(i + 1, reward.StarRating, displayName, reward.RewardType, outcomeKind, merges, languages, reward.Guaranteed, reward.PityTriggered));
            }

            WriteState(saveService, saveData, wallet, collection, gacha);
            return new CompletedGachaPull(result.BannerId, string.Join(" / ", header), completedRewards);
        }

        private void BuildRows()
        {
            _resultGrid.Clear();
            _cardViews.Clear();
            for (int i = 0; i < _rewards.Count; i++)
            {
                PullCardView card = new(_rewards[i]);
                _cardViews.Add(card);
                _resultGrid.Add(card.Root);
            }
        }

        private void RevealAll()
        {
            _skipRequested = true;
            ClearGridFocus();
            _goldShimmer.RemoveFromClassList("gold-shimmer-on");
            for (int i = 0; i < _cardViews.Count; i++)
            {
                _cardViews[i].HideHold();
                _cardViews[i].Reveal(allowFlash: false, persistentMotion: !UIPreferences.ReducedMotion);
            }

            CompleteReveal();
        }

        private void CompleteReveal()
        {
            if (_revealComplete)
            {
                return;
            }

            _revealComplete = true;
            _skipHint.text = "complete";
            _flourishLabel.text = _completedHeader;
            _flourishLabel.AddToClassList("gambling-flourish-complete");
            BuildSummary(animate: !_skipRequested && !UIPreferences.ReducedMotion);
            ShowContinue();
        }

        private void BuildSummary(bool animate)
        {
            _summaryPanel.Clear();
            _summaryPanel.RemoveFromClassList("hidden");
            _summaryTotalsLabel = new Label { name = "PullSummaryTotals" };
            _summaryTotalsLabel.AddToClassList("summary-totals");
            _summaryPanel.Add(_summaryTotalsLabel);

            SummaryTotals totals = CalculateTotals();
            if (!animate)
            {
                _summaryTotalsLabel.text = FormatTotals(totals.Units, totals.Merges, totals.Cache, totals.LanguagesText);
                return;
            }

            _summaryTotalsLabel.AddToClassList("summary-tick");
            for (int step = 0; step <= SummaryTickSteps; step++)
            {
                int capturedStep = step;
                int delay = Mathf.RoundToInt((SummaryTickMs / (float)SummaryTickSteps) * capturedStep);
                _summaryTotalsLabel.schedule.Execute(() =>
                {
                    float t = capturedStep / (float)SummaryTickSteps;
                    int units = Mathf.RoundToInt(Mathf.Lerp(0, totals.Units, t));
                    int merges = Mathf.RoundToInt(Mathf.Lerp(0, totals.Merges, t));
                    int cache = Mathf.RoundToInt(Mathf.Lerp(0, totals.Cache, t));
                    _summaryTotalsLabel.text = FormatTotals(units, merges, cache, capturedStep == SummaryTickSteps ? totals.LanguagesText : "...");
                    if (capturedStep == SummaryTickSteps)
                    {
                        _summaryTotalsLabel.RemoveFromClassList("summary-tick");
                    }
                }).StartingIn(delay);
            }
        }

        private SummaryTotals CalculateTotals()
        {
            int units = 0;
            int merges = 0;
            int cache = 0;
            HashSet<Language> languages = new();
            for (int i = 0; i < _rewards.Count; i++)
            {
                if (_rewards[i].OutcomeKind == PullOutcomeKind.Granted && _rewards[i].IsCharacter)
                {
                    units++;
                }

                if (_rewards[i].RewardType == GachaRewardType.Package)
                {
                    cache += _rewards[i].MergesAwarded;
                }
                else
                {
                    merges += _rewards[i].MergesAwarded;
                }

                for (int languageIndex = 0; languageIndex < _rewards[i].LanguagesUnlocked.Count; languageIndex++)
                {
                    languages.Add(_rewards[i].LanguagesUnlocked[languageIndex]);
                }
            }

            return new SummaryTotals(units, merges, cache, FormatLanguages(languages));
        }

        private static string FormatTotals(int units, int merges, int cache, string languagesText)
        {
            string unitText = units == 0 ? "none" : units.ToString();
            string mergeText = merges == 0 ? "none" : merges.ToString();
            string cacheText = cache == 0 ? "none" : cache.ToString();
            return $"units gained: {unitText}   merges: {mergeText}   cache: {cacheText}   languages unlocked: {languagesText}";
        }

        private static string FormatLanguages(HashSet<Language> languages)
        {
            if (languages.Count == 0)
            {
                return "none";
            }

            List<string> names = new();
            foreach (Language language in languages)
            {
                names.Add(language.ToString());
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", names);
        }

        private static PlayerCollection BuildCollection(SaveData saveData, DistroDatabase distroDatabase, PackageDatabase packageDatabase)
        {
            PlayerCollection collection = new();
            for (int i = 0; i < saveData.ownedUnits.Count; i++)
            {
                OwnedUnitSaveEntry entry = saveData.ownedUnits[i];
                DistroDefinition unit = entry == null ? null : distroDatabase.FindById(entry.id);
                if (unit != null)
                {
                    collection.AddSilently(unit, entry.version);
                }
            }

            for (int i = 0; i < saveData.ownedPackages.Count; i++)
            {
                OwnedPackageSaveEntry entry = saveData.ownedPackages[i];
                PackageDefinition package = packageDatabase == null ? null : packageDatabase.FindById(entry?.id);
                if (package != null)
                {
                    collection.AddPackageSilently(package, entry.upgradeLevel);
                }
            }

            return collection;
        }

        private static PackageDefinition GrantRandomPackageByRarity(PackageDatabase packageDatabase, PlayerCollection collection, SaveData saveData, int rarity, out bool duplicate, out int cacheAwarded)
        {
            duplicate = false;
            cacheAwarded = 0;
            int count = packageDatabase == null ? 0 : packageDatabase.CountByRarity(rarity);
            if (count <= 0)
            {
                return null;
            }

            int index = UnityEngine.Random.Range(0, count);
            PackageDefinition package = packageDatabase.FindByRarity(rarity, index);
            if (package == null)
            {
                return null;
            }

            duplicate = collection.IsPackageOwned(package.Id);
            if (duplicate)
            {
                // Packages are unique-per-type: a dupe never creates a second instance, it
                // auto-scraps into Cache immediately, mirroring the distro dupe -> Merges path.
                cacheAwarded = PackageTuning.GetCacheForRarity(package.Rarity);
                saveData.cacheBalance += cacheAwarded;
            }
            else
            {
                collection.AddPackageSilently(package);
                saveData.ownedPackages.Add(new OwnedPackageSaveEntry { id = package.Id, upgradeLevel = 0 });
                saveData.ownedPackageIds.Add(package.Id);
            }

            return package;
        }

        private static void RestoreBannerPool(GachaService gacha, SaveData saveData, DistroDatabase distroDatabase)
        {
            for (int i = 0; i < saveData.bannerPoolIds.Count; i++)
            {
                DistroDefinition unit = distroDatabase.FindById(saveData.bannerPoolIds[i]);
                if (unit != null)
                {
                    gacha.AddToBannerPool(unit);
                }
            }

            if (saveData.starterChosen)
            {
                IReadOnlyList<DistroDefinition> distros = distroDatabase.AllDistros;
                for (int i = 0; i < distros.Count; i++)
                {
                    DistroDefinition distro = distros[i];
                    if (GachaService.IsBeginnerInstallMediaDistro(distro))
                    {
                        gacha.AddToBannerPool(distro);
                    }
                }
            }
        }

        private static void WriteState(SaveService saveService, SaveData saveData, EntropyWallet wallet, PlayerCollection collection, GachaService gacha)
        {
            Dictionary<string, int> mergeBalances = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < saveData.ownedUnits.Count; i++)
            {
                OwnedUnitSaveEntry entry = saveData.ownedUnits[i];
                if (entry != null && !string.IsNullOrWhiteSpace(entry.id))
                {
                    mergeBalances[entry.id] = Math.Max(0, entry.merges);
                }
            }

            saveData.entropyBalance = wallet.Balance;
            saveData.ownedUnits.Clear();
            saveData.ownedPackages.Clear();
            saveData.ownedPackageIds.Clear();
            saveData.ownedUnitIds.Clear();
            saveData.bannerPoolIds.Clear();
            gacha.WriteProgress(saveData);

            for (int i = 0; i < collection.OwnedUnits.Count; i++)
            {
                DistroDefinition unit = collection.OwnedUnits[i];
                if (unit != null && !string.IsNullOrWhiteSpace(unit.Id))
                {
                    saveData.ownedUnits.Add(new OwnedUnitSaveEntry
                    {
                        id = unit.Id,
                        version = Mathf.Clamp(collection.GetVersion(unit.Id), 1, GachaTuning.MaxVersion),
                        merges = mergeBalances.TryGetValue(unit.Id, out int merges) ? merges : 0
                    });
                }
            }

            for (int i = 0; i < collection.OwnedPackageInstances.Count; i++)
            {
                OwnedPackageInstance package = collection.OwnedPackageInstances[i];
                if (package != null && !string.IsNullOrWhiteSpace(package.PackageId))
                {
                    saveData.ownedPackages.Add(new OwnedPackageSaveEntry { id = package.PackageId, upgradeLevel = package.UpgradeLevel });
                    saveData.ownedPackageIds.Add(package.PackageId);
                }
            }

            for (int i = 0; i < gacha.BannerPool.Count; i++)
            {
                DistroDefinition unit = gacha.BannerPool[i];
                if (unit != null && !string.IsNullOrWhiteSpace(unit.Id))
                {
                    saveData.bannerPoolIds.Add(unit.Id);
                }
            }

            saveService.Save(saveData);
        }

        private IEnumerator WaitOrSkip(int milliseconds)
        {
            float elapsed = 0f;
            float duration = Mathf.Max(0f, milliseconds / 1000f);
            while (!_skipRequested && elapsed < duration)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private static int GetAnticipationMs(int stars)
        {
            return stars >= 5 ? LegendaryAnticipationMs : stars >= 4 ? RareAnticipationMs : 0;
        }

        private static int GetRevealMs(int stars)
        {
            return stars >= 5 ? LegendaryRevealMs : stars >= 4 ? RareRevealMs : CommonRevealMs;
        }

        private void ApplyGridFocus(int focusedIndex, int stars)
        {
            if (UIPreferences.ReducedMotion || stars < 5)
            {
                return;
            }

            for (int i = 0; i < _cardViews.Count; i++)
            {
                _cardViews[i].SetDimmed(i != focusedIndex);
            }
        }

        private void ClearGridFocus()
        {
            for (int i = 0; i < _cardViews.Count; i++)
            {
                _cardViews[i].SetDimmed(false);
            }
        }

        private void PlayRevealJuice(PullCardView card, CompletedGachaReward reward)
        {
            if (UIPreferences.ReducedMotion)
            {
                return;
            }

            if (reward.Stars >= 5)
            {
                card.PlayTemporaryClass("pull-card-legendary-pop", PopClassMs);
                PlayGoldShimmer(card.Root);
                EmitSparks(card.Root, RarityPresentation.ForStars(reward.Stars).Color, LegendarySparkCount, 76f);
            }
            else if (reward.Stars >= 4)
            {
                card.PlayTemporaryClass("pull-card-rare-pop", PopClassMs);
                EmitSparks(card.Root, RarityPresentation.ForStars(reward.Stars).Color, RareSparkCount, 44f);
            }

            if (reward.OutcomeKind == PullOutcomeKind.Granted)
            {
                card.PulseStatus(PopClassMs);
            }
        }

        private void PlayGoldShimmer(VisualElement source)
        {
            if (source == null || _goldShimmer?.parent == null)
            {
                return;
            }

            Rect sourceBounds = source.worldBound;
            Rect parentBounds = _goldShimmer.parent.worldBound;
            float left = Mathf.Max(0f, sourceBounds.x - parentBounds.x);
            float top = Mathf.Max(0f, sourceBounds.y - parentBounds.y);
            _goldShimmer.style.left = left;
            _goldShimmer.style.top = top;
            _goldShimmer.style.width = sourceBounds.width;
            _goldShimmer.style.height = sourceBounds.height;
            _goldShimmer.AddToClassList("gold-shimmer-on");
            _goldShimmer.schedule.Execute(() => _goldShimmer.RemoveFromClassList("gold-shimmer-on")).StartingIn(LegendaryShimmerMs);
        }

        private void EmitSparks(VisualElement source, Color color, int count, float radius)
        {
            if (_effectLayer == null || source == null)
            {
                return;
            }

            Rect sourceBounds = source.worldBound;
            Rect layerBounds = _effectLayer.worldBound;
            float centerX = sourceBounds.center.x - layerBounds.x;
            float centerY = sourceBounds.center.y - layerBounds.y;
            float halfWidth = sourceBounds.width * 0.5f;
            float halfHeight = sourceBounds.height * 0.5f;
            for (int i = 0; i < count; i++)
            {
                VisualElement spark = CreateSpark();

                float angle = (Mathf.PI * 2f * i / count) + UnityEngine.Random.Range(-0.18f, 0.18f);
                float dirX = Mathf.Cos(angle);
                float dirY = Mathf.Sin(angle);
                float edgeDistanceX = Mathf.Approximately(dirX, 0f) ? float.MaxValue : halfWidth / Mathf.Abs(dirX);
                float edgeDistanceY = Mathf.Approximately(dirY, 0f) ? float.MaxValue : halfHeight / Mathf.Abs(dirY);
                float edgeDistance = Mathf.Min(edgeDistanceX, edgeDistanceY);
                float startX = centerX + (dirX * (edgeDistance + 10f)) - 4f;
                float startY = centerY + (dirY * (edgeDistance + 10f)) - 4f;
                float distance = UnityEngine.Random.Range(radius * 0.55f, radius);
                float targetX = startX + (dirX * distance);
                float targetY = startY + (dirY * distance);

                spark.style.backgroundColor = color;
                spark.style.left = startX;
                spark.style.top = startY;
                spark.style.opacity = 1f;
                spark.style.scale = new Scale(new Vector2(1.35f, 1.35f));
                spark.RemoveFromClassList("spark-active");
                spark.schedule.Execute(() =>
                {
                    spark.AddToClassList("spark-active");
                    spark.style.left = targetX;
                    spark.style.top = targetY;
                    spark.style.opacity = 0f;
                    spark.style.scale = new Scale(new Vector2(0.25f, 0.25f));
                }).StartingIn(35);
                spark.schedule.Execute(() =>
                {
                    spark.RemoveFromHierarchy();
                }).StartingIn(SparkLifetimeMs);
            }
        }

        private VisualElement CreateSpark()
        {
            VisualElement spark = new();
            spark.pickingMode = PickingMode.Ignore;
            spark.AddToClassList("spark");
            _effectLayer.Add(spark);
            return spark;
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            if (_revealComplete)
            {
                ContinueToMenu();
                evt.StopPropagation();
                return;
            }

            _skipRequested = true;
            evt.StopPropagation();
        }

        private void HandlePointerDown(PointerDownEvent evt)
        {
            if (_revealComplete)
            {
                return;
            }

            _skipRequested = true;
            evt.StopPropagation();
        }

        private void ShowContinue()
        {
            _continueButton.SetEnabled(true);
            _continueButton.RemoveFromClassList("continue-button-pending");
            if (_failed)
            {
                _skipHint.text = "failed";
            }
        }

        private void ContinueToMenu()
        {
            SceneManager.LoadSceneAsync(SceneLoader.MainMenuSceneName);
        }

        private readonly struct SummaryTotals
        {
            public SummaryTotals(int units, int merges, int cache, string languagesText)
            {
                Units = units;
                Merges = merges;
                Cache = cache;
                LanguagesText = languagesText;
            }

            public int Units { get; }
            public int Merges { get; }
            public int Cache { get; }
            public string LanguagesText { get; }
        }

        private sealed class PullCardView
        {
            private readonly CompletedGachaReward _reward;
            private readonly Label _rarityLabel;
            private readonly Label _nameLabel;
            private readonly Label _statusLabel;
            private readonly Label _typeIconLabel;
            private IVisualElementScheduledItem _glowPulse;
            private bool _glowOn;

            public PullCardView(CompletedGachaReward reward)
            {
                this._reward = reward;
                RarityStyle rarity = RarityPresentation.ForStars(reward.Stars);
                Root = new VisualElement();
                Root.AddToClassList("pull-card");
                Root.AddToClassList("pull-card-hidden");
                Root.AddToClassList(rarity.ClassName);

                VisualElement top = new();
                top.AddToClassList("pull-card-top");
                Root.Add(top);

                Label indexLabel = new($"{reward.Index:00}");
                indexLabel.AddToClassList("pull-index");
                top.Add(indexLabel);

                _rarityLabel = new(rarity.Stars);
                _rarityLabel.AddToClassList("pull-rarity");
                _rarityLabel.AddToClassList(rarity.ClassName);
                top.Add(_rarityLabel);

                _typeIconLabel = new(reward.IsCharacter ? "@" : "#");
                _typeIconLabel.tooltip = reward.TypeText;
                _typeIconLabel.AddToClassList("pull-type-icon");
                top.Add(_typeIconLabel);

                _nameLabel = new(reward.DisplayName);
                _nameLabel.AddToClassList("pull-name");
                Root.Add(_nameLabel);

                _statusLabel = new(reward.StatusText);
                _statusLabel.AddToClassList("pull-status");
                _statusLabel.EnableInClassList("pull-status-new", reward.OutcomeKind == PullOutcomeKind.Granted);
                _statusLabel.visible = !string.IsNullOrWhiteSpace(reward.StatusText);
                if (_statusLabel.visible)
                {
                    Root.Add(_statusLabel);
                }
            }

            public VisualElement Root { get; }

            public void ShowHold(string holdText, int stars)
            {
                Root.RemoveFromClassList("pull-card-hidden");
                Root.AddToClassList("pull-card-held");
                Root.EnableInClassList("pull-card-held-rare", stars == 4);
                Root.EnableInClassList("pull-card-held-legendary", stars >= 5);
                _rarityLabel.text = "...";
                _nameLabel.text = holdText;
                _statusLabel.text = "";
                _statusLabel.visible = false;
                _typeIconLabel.visible = false;
            }

            public void HideHold()
            {
                Root.RemoveFromClassList("pull-card-held");
                Root.RemoveFromClassList("pull-card-held-rare");
                Root.RemoveFromClassList("pull-card-held-legendary");
            }

            public void Reveal(bool allowFlash, bool persistentMotion)
            {
                RarityStyle rarity = RarityPresentation.ForStars(_reward.Stars);
                Root.RemoveFromClassList("pull-card-hidden");
                Root.RemoveFromClassList("pull-card-held");
                Root.RemoveFromClassList("pull-card-held-rare");
                Root.RemoveFromClassList("pull-card-held-legendary");
                Root.AddToClassList("pull-card-revealed");
                Root.EnableInClassList("pull-card-glow-4", persistentMotion && _reward.Stars == 4);
                Root.EnableInClassList("pull-card-glow-5", persistentMotion && _reward.Stars >= 5);
                SetPersistentGlow(persistentMotion && _reward.Stars >= 5);
                _rarityLabel.text = _reward.Stars >= 5 ? $"{rarity.Badge} {rarity.Stars}" : rarity.Stars;
                _nameLabel.text = _reward.DisplayName;
                _typeIconLabel.text = _reward.IsCharacter ? "@" : "#";
                _typeIconLabel.visible = true;
                _statusLabel.text = _reward.StatusText;
                _statusLabel.visible = !string.IsNullOrWhiteSpace(_reward.StatusText);
                if (allowFlash)
                {
                    Root.AddToClassList("pull-card-flash");
                    Root.schedule.Execute(() => Root.RemoveFromClassList("pull-card-flash")).StartingIn(PopClassMs);
                }
            }

            public void SetDimmed(bool dimmed)
            {
                Root.EnableInClassList("pull-card-dimmed", dimmed);
            }

            public void PlayTemporaryClass(string className, int durationMs)
            {
                Root.AddToClassList(className);
                Root.schedule.Execute(() => Root.RemoveFromClassList(className)).StartingIn(durationMs);
            }

            public void PulseStatus(int durationMs)
            {
                if (!_statusLabel.visible)
                {
                    return;
                }

                _statusLabel.AddToClassList("pull-status-pop");
                _statusLabel.schedule.Execute(() => _statusLabel.RemoveFromClassList("pull-status-pop")).StartingIn(durationMs);
            }

            private void SetPersistentGlow(bool enabled)
            {
                if (!enabled)
                {
                    _glowPulse?.Pause();
                    Root.RemoveFromClassList("pull-card-glow-on");
                    return;
                }

                if (_glowPulse != null)
                {
                    _glowPulse.Resume();
                    return;
                }

                _glowPulse = Root.schedule.Execute(() =>
                {
                    _glowOn = !_glowOn;
                    Root.EnableInClassList("pull-card-glow-on", _glowOn);
                }).Every(900);
            }
        }
    }
}
