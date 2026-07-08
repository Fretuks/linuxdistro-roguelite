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

        private readonly List<CompletedGachaReward> rewards = new();
        private readonly List<PullCardView> cardViews = new();
        private UIDocument document;
        private VisualElement root;
        private Label commandLabel;
        private Label costLabel;
        private Label flourishLabel;
        private Label skipHint;
        private VisualElement resultGrid;
        private VisualElement effectLayer;
        private VisualElement goldShimmer;
        private VisualElement summaryPanel;
        private Label summaryTotalsLabel;
        private Button continueButton;
        private string completedHeader;
        private bool skipRequested;
        private bool revealComplete;
        private bool failed;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            root = document.rootVisualElement;
            root.Clear();
            root.focusable = true;
            root.AddToClassList("gambling-root");
            root.EnableInClassList("reduced-motion", UIPreferences.ReducedMotion);
            LoadStyles();
            BuildLayout();
            root.RegisterCallback<KeyDownEvent>(HandleKeyDown);
            root.RegisterCallback<PointerDownEvent>(HandlePointerDown);
        }

        private void OnDestroy()
        {
            if (root == null)
            {
                return;
            }

            root.UnregisterCallback<KeyDownEvent>(HandleKeyDown);
            root.UnregisterCallback<PointerDownEvent>(HandlePointerDown);
        }

        private void Start()
        {
            root.Focus();
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
                root.styleSheets.Add(styleSheet);
            }
        }

        private void BuildLayout()
        {
            VisualElement shell = new();
            shell.AddToClassList("gambling-shell");
            root.Add(shell);

            VisualElement frame = new();
            frame.AddToClassList("gambling-frame");
            shell.Add(frame);

            VisualElement header = new();
            header.AddToClassList("gambling-header");
            frame.Add(header);

            VisualElement titleBlock = new();
            titleBlock.AddToClassList("gambling-title-block");
            header.Add(titleBlock);

            commandLabel = new("git pull");
            commandLabel.AddToClassList("gambling-command");
            titleBlock.Add(commandLabel);

            costLabel = new();
            costLabel.AddToClassList("gambling-cost");
            titleBlock.Add(costLabel);

            skipHint = new("[tap/key] reveal all");
            skipHint.AddToClassList("gambling-skip");
            header.Add(skipHint);

            flourishLabel = new();
            flourishLabel.AddToClassList("gambling-flourish");
            frame.Add(flourishLabel);

            resultGrid = new VisualElement();
            resultGrid.AddToClassList("gambling-results");
            frame.Add(resultGrid);

            goldShimmer = new VisualElement();
            goldShimmer.AddToClassList("gold-shimmer");
            goldShimmer.pickingMode = PickingMode.Ignore;
            frame.Add(goldShimmer);

            effectLayer = new VisualElement();
            effectLayer.AddToClassList("effect-layer");
            effectLayer.pickingMode = PickingMode.Ignore;
            frame.Add(effectLayer);

            summaryPanel = new VisualElement();
            summaryPanel.AddToClassList("summary-panel");
            summaryPanel.AddToClassList("hidden");
            frame.Add(summaryPanel);

            continueButton = new Button(ContinueToMenu) { text = "> continue" };
            continueButton.AddToClassList("continue-button");
            continueButton.AddToClassList("continue-button-pending");
            continueButton.SetEnabled(false);
            frame.Add(continueButton);
        }

        private IEnumerator RunCutscene()
        {
            if (!GachaPullContext.TryConsumePending(out PendingGachaPull pending))
            {
                failed = true;
                commandLabel.text = "git pull failed";
                costLabel.text = "no pending pull";
                ShowContinue();
                yield break;
            }

            commandLabel.text = pending.PullCount == 10 ? "git pull --ten" : "git pull --single";
            int pullCost = pending.PullCount == 10 ? GachaService.BeginnerTenPullCost : GachaService.BeginnerSinglePullCost;
            costLabel.text = pending.EntropyTokenCount > 0
                ? $"cost: {pullCost} Commits / entropy fallback: {pending.EntropyTokenCount * GachaService.EntropyPerPullToken}"
                : $"cost: {pullCost} Commits";
            flourishLabel.text = "$ fetch-pack | verify | unpack";

            CompletedGachaPull completed = ResolvePull(pending);
            GachaPullContext.SetCompleted(completed);
            completedHeader = completed.HeaderText;
            rewards.Clear();
            rewards.AddRange(completed.Rewards);

            if (rewards.Count == 0)
            {
                failed = true;
                costLabel.text = completed.HeaderText;
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
            flourishLabel.text = "$ unpacking sealed objects";
            for (int i = 0; i < rewards.Count; i++)
            {
                if (skipRequested)
                {
                    RevealAll();
                    yield break;
                }

                CompletedGachaReward reward = rewards[i];
                PullCardView card = cardViews[i];
                int holdMs = GetAnticipationMs(reward.Stars);
                if (holdMs > 0)
                {
                    ApplyGridFocus(i, reward.Stars);
                    card.ShowHold(reward.Stars >= 5 ? "........ signed tag" : "... promoted object", reward.Stars);
                    yield return WaitOrSkip(holdMs);
                    if (skipRequested)
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

            PlayerCollection collection = BuildCollection(saveData, pending.DistroDatabase);
            GachaService gacha = new();
            RestoreBannerPool(gacha, saveData, pending.DistroDatabase);
            gacha.LoadProgress(saveData);

            GachaPullResult result = pending.BannerId == GachaService.BeginnerBannerId
                ? gacha.PerformBeginnerPull(pending.PullCount, wallet, pending.EntropyTokenCount)
                : GachaPullResult.Failed(pending.BannerId, "remote not implemented yet");

            if (!result.Success)
            {
                return new CompletedGachaPull(pending.BannerId, $"git pull failed: {result.FailureReason}", Array.Empty<CompletedGachaReward>());
            }

            List<string> header = new()
            {
                $"spent {result.CurrencySpent} {GachaService.FormatCurrencyName(result.CurrencyType)}"
            };
            if (result.EntropySpent > 0)
            {
                header.Add($"spent {result.EntropySpent} entropy");
            }

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
                completedRewards.Add(new CompletedGachaReward(i + 1, reward.StarRating, reward.DisplayName, reward.RewardType, outcomeKind, merges, languages, reward.Guaranteed, reward.PityTriggered));
            }

            WriteState(saveService, saveData, wallet, collection, gacha);
            return new CompletedGachaPull(result.BannerId, string.Join(" / ", header), completedRewards);
        }

        private void BuildRows()
        {
            resultGrid.Clear();
            cardViews.Clear();
            for (int i = 0; i < rewards.Count; i++)
            {
                PullCardView card = new(rewards[i]);
                cardViews.Add(card);
                resultGrid.Add(card.Root);
            }
        }

        private void RevealAll()
        {
            skipRequested = true;
            ClearGridFocus();
            goldShimmer.RemoveFromClassList("gold-shimmer-on");
            for (int i = 0; i < cardViews.Count; i++)
            {
                cardViews[i].HideHold();
                cardViews[i].Reveal(allowFlash: false, persistentMotion: !UIPreferences.ReducedMotion);
            }

            CompleteReveal();
        }

        private void CompleteReveal()
        {
            if (revealComplete)
            {
                return;
            }

            revealComplete = true;
            skipHint.text = "complete";
            flourishLabel.text = completedHeader;
            flourishLabel.AddToClassList("gambling-flourish-complete");
            BuildSummary(animate: !skipRequested && !UIPreferences.ReducedMotion);
            ShowContinue();
        }

        private void BuildSummary(bool animate)
        {
            summaryPanel.Clear();
            summaryPanel.RemoveFromClassList("hidden");
            summaryTotalsLabel = new Label { name = "PullSummaryTotals" };
            summaryTotalsLabel.AddToClassList("summary-totals");
            summaryPanel.Add(summaryTotalsLabel);

            SummaryTotals totals = CalculateTotals();
            if (!animate)
            {
                summaryTotalsLabel.text = FormatTotals(totals.Units, totals.Merges, totals.LanguagesText);
                return;
            }

            summaryTotalsLabel.AddToClassList("summary-tick");
            for (int step = 0; step <= SummaryTickSteps; step++)
            {
                int capturedStep = step;
                int delay = Mathf.RoundToInt((SummaryTickMs / (float)SummaryTickSteps) * capturedStep);
                summaryTotalsLabel.schedule.Execute(() =>
                {
                    float t = capturedStep / (float)SummaryTickSteps;
                    int units = Mathf.RoundToInt(Mathf.Lerp(0, totals.Units, t));
                    int merges = Mathf.RoundToInt(Mathf.Lerp(0, totals.Merges, t));
                    summaryTotalsLabel.text = FormatTotals(units, merges, capturedStep == SummaryTickSteps ? totals.LanguagesText : "...");
                    if (capturedStep == SummaryTickSteps)
                    {
                        summaryTotalsLabel.RemoveFromClassList("summary-tick");
                    }
                }).StartingIn(delay);
            }
        }

        private SummaryTotals CalculateTotals()
        {
            int units = 0;
            int merges = 0;
            HashSet<Language> languages = new();
            for (int i = 0; i < rewards.Count; i++)
            {
                if (rewards[i].OutcomeKind == PullOutcomeKind.Granted && rewards[i].IsCharacter)
                {
                    units++;
                }

                merges += rewards[i].MergesAwarded;
                for (int languageIndex = 0; languageIndex < rewards[i].LanguagesUnlocked.Count; languageIndex++)
                {
                    languages.Add(rewards[i].LanguagesUnlocked[languageIndex]);
                }
            }

            return new SummaryTotals(units, merges, FormatLanguages(languages));
        }

        private static string FormatTotals(int units, int merges, string languagesText)
        {
            string unitText = units == 0 ? "none" : units.ToString();
            string mergeText = merges == 0 ? "none" : merges.ToString();
            return $"units gained: {unitText}   merges: {mergeText}   languages unlocked: {languagesText}";
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

        private static PlayerCollection BuildCollection(SaveData saveData, DistroDatabase distroDatabase)
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

            return collection;
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

            if (saveData.starterChosen && gacha.BannerPool.Count == 0)
            {
                IReadOnlyList<DistroDefinition> distros = distroDatabase.AllDistros;
                for (int i = 0; i < distros.Count; i++)
                {
                    gacha.AddToBannerPool(distros[i]);
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
            while (!skipRequested && elapsed < duration)
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

            for (int i = 0; i < cardViews.Count; i++)
            {
                cardViews[i].SetDimmed(i != focusedIndex);
            }
        }

        private void ClearGridFocus()
        {
            for (int i = 0; i < cardViews.Count; i++)
            {
                cardViews[i].SetDimmed(false);
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
            if (source == null || goldShimmer?.parent == null)
            {
                return;
            }

            Rect sourceBounds = source.worldBound;
            Rect parentBounds = goldShimmer.parent.worldBound;
            float left = Mathf.Max(0f, sourceBounds.x - parentBounds.x);
            float top = Mathf.Max(0f, sourceBounds.y - parentBounds.y);
            goldShimmer.style.left = left;
            goldShimmer.style.top = top;
            goldShimmer.style.width = sourceBounds.width;
            goldShimmer.style.height = sourceBounds.height;
            goldShimmer.AddToClassList("gold-shimmer-on");
            goldShimmer.schedule.Execute(() => goldShimmer.RemoveFromClassList("gold-shimmer-on")).StartingIn(LegendaryShimmerMs);
        }

        private void EmitSparks(VisualElement source, Color color, int count, float radius)
        {
            if (effectLayer == null || source == null)
            {
                return;
            }

            Rect sourceBounds = source.worldBound;
            Rect layerBounds = effectLayer.worldBound;
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
            effectLayer.Add(spark);
            return spark;
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            if (revealComplete)
            {
                ContinueToMenu();
                evt.StopPropagation();
                return;
            }

            skipRequested = true;
            evt.StopPropagation();
        }

        private void HandlePointerDown(PointerDownEvent evt)
        {
            if (revealComplete)
            {
                return;
            }

            skipRequested = true;
            evt.StopPropagation();
        }

        private void ShowContinue()
        {
            continueButton.SetEnabled(true);
            continueButton.RemoveFromClassList("continue-button-pending");
            if (failed)
            {
                skipHint.text = "failed";
            }
        }

        private void ContinueToMenu()
        {
            SceneManager.LoadSceneAsync(SceneLoader.MainMenuSceneName);
        }

        private readonly struct SummaryTotals
        {
            public SummaryTotals(int units, int merges, string languagesText)
            {
                Units = units;
                Merges = merges;
                LanguagesText = languagesText;
            }

            public int Units { get; }
            public int Merges { get; }
            public string LanguagesText { get; }
        }

        private sealed class PullCardView
        {
            private readonly CompletedGachaReward reward;
            private readonly Label rarityLabel;
            private readonly Label nameLabel;
            private readonly Label statusLabel;
            private readonly Label typeIconLabel;
            private IVisualElementScheduledItem glowPulse;
            private bool glowOn;

            public PullCardView(CompletedGachaReward reward)
            {
                this.reward = reward;
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

                rarityLabel = new(rarity.Stars);
                rarityLabel.AddToClassList("pull-rarity");
                rarityLabel.AddToClassList(rarity.ClassName);
                top.Add(rarityLabel);

                typeIconLabel = new(reward.IsCharacter ? "@" : "#");
                typeIconLabel.tooltip = reward.TypeText;
                typeIconLabel.AddToClassList("pull-type-icon");
                top.Add(typeIconLabel);

                nameLabel = new(reward.DisplayName);
                nameLabel.AddToClassList("pull-name");
                Root.Add(nameLabel);

                statusLabel = new(reward.StatusText);
                statusLabel.AddToClassList("pull-status");
                statusLabel.EnableInClassList("pull-status-new", reward.OutcomeKind == PullOutcomeKind.Granted);
                statusLabel.visible = !string.IsNullOrWhiteSpace(reward.StatusText);
                if (statusLabel.visible)
                {
                    Root.Add(statusLabel);
                }
            }

            public VisualElement Root { get; }

            public void ShowHold(string holdText, int stars)
            {
                Root.RemoveFromClassList("pull-card-hidden");
                Root.AddToClassList("pull-card-held");
                Root.EnableInClassList("pull-card-held-rare", stars == 4);
                Root.EnableInClassList("pull-card-held-legendary", stars >= 5);
                rarityLabel.text = "...";
                nameLabel.text = holdText;
                statusLabel.text = "";
                statusLabel.visible = false;
                typeIconLabel.visible = false;
            }

            public void HideHold()
            {
                Root.RemoveFromClassList("pull-card-held");
                Root.RemoveFromClassList("pull-card-held-rare");
                Root.RemoveFromClassList("pull-card-held-legendary");
            }

            public void Reveal(bool allowFlash, bool persistentMotion)
            {
                RarityStyle rarity = RarityPresentation.ForStars(reward.Stars);
                Root.RemoveFromClassList("pull-card-hidden");
                Root.RemoveFromClassList("pull-card-held");
                Root.RemoveFromClassList("pull-card-held-rare");
                Root.RemoveFromClassList("pull-card-held-legendary");
                Root.AddToClassList("pull-card-revealed");
                Root.EnableInClassList("pull-card-glow-4", persistentMotion && reward.Stars == 4);
                Root.EnableInClassList("pull-card-glow-5", persistentMotion && reward.Stars >= 5);
                SetPersistentGlow(persistentMotion && reward.Stars >= 5);
                rarityLabel.text = reward.Stars >= 5 ? $"{rarity.Badge} {rarity.Stars}" : rarity.Stars;
                nameLabel.text = reward.DisplayName;
                typeIconLabel.text = reward.IsCharacter ? "@" : "#";
                typeIconLabel.visible = true;
                statusLabel.text = reward.StatusText;
                statusLabel.visible = !string.IsNullOrWhiteSpace(reward.StatusText);
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
                if (!statusLabel.visible)
                {
                    return;
                }

                statusLabel.AddToClassList("pull-status-pop");
                statusLabel.schedule.Execute(() => statusLabel.RemoveFromClassList("pull-status-pop")).StartingIn(durationMs);
            }

            private void SetPersistentGlow(bool enabled)
            {
                if (!enabled)
                {
                    glowPulse?.Pause();
                    Root.RemoveFromClassList("pull-card-glow-on");
                    return;
                }

                if (glowPulse != null)
                {
                    glowPulse.Resume();
                    return;
                }

                glowPulse = Root.schedule.Execute(() =>
                {
                    glowOn = !glowOn;
                    Root.EnableInClassList("pull-card-glow-on", glowOn);
                }).Every(900);
            }
        }
    }
}
