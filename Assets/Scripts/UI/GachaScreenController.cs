using System;
using System.Collections.Generic;
using KernelPanic.Data;
using KernelPanic.Meta;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// Renders banner selection and pull controls for the gacha screen.
    /// </summary>
    [Serializable]
    public sealed class GachaScreenController
    {
        private const string SelectedClassName = "selected";

        private readonly List<VisualElement> _bannerRows = new();
        private readonly string[] _bannerIds =
        {
            GachaService.BeginnerBannerId,
            GachaService.StandardBannerId,
            GachaService.LimitedBannerId
        };

        private VisualElement _bannerList;
        private VisualElement _bannerDetail;
        private DistroDatabase _distroDatabase;
        private FontAsset _monospaceFont;
        private GachaService _gachaService;
        private PlayerCollection _playerCollection;
        private EntropyWallet _wallet;
        private Func<IReadOnlyList<DistroDefinition>, PullResolutionResult> _resolvePulledDistros;
        private Action _onChanged;
        private Action _requestRootCreditExchange;
        private Action<string, int, int> _requestPullCutscene;
        private int _selectedBannerIndex;
        private int _pendingPullCount;
        private int _pendingMissingTokens;
        private bool _hasOpened;
        private string _resultBannerId;
        private string _resultText;
        private readonly List<string> _resultRewardLines = new();
        private readonly List<int> _resultRewardStars = new();
        private bool _resultRevealed;

        public void Bind(VisualElement root, DistroDatabase database, FontAsset artFont, GachaService service, PlayerCollection collection, EntropyWallet entropyWallet, Func<IReadOnlyList<DistroDefinition>, PullResolutionResult> pullResolver, Action changedCallback, Action rootCreditExchangeCallback, Action<string, int, int> pullCutsceneCallback)
        {
            _distroDatabase = database;
            _monospaceFont = artFont;
            _gachaService = service;
            _playerCollection = collection;
            _wallet = entropyWallet;
            _resolvePulledDistros = pullResolver;
            _onChanged = changedCallback;
            _requestRootCreditExchange = rootCreditExchangeCallback;
            _requestPullCutscene = pullCutsceneCallback;
            _bannerList = root.Q<VisualElement>("GachaBannerList");
            _bannerDetail = root.Q<VisualElement>("GachaBannerDetail");
        }

        public void Open()
        {
            if (!_hasOpened)
            {
                _selectedBannerIndex = 0;
                _hasOpened = true;
            }

            Refresh();
        }

        public void Refresh()
        {
            if (_bannerList == null || _bannerDetail == null || _gachaService == null)
            {
                return;
            }

            _bannerRows.Clear();
            _bannerList.Clear();

            for (int i = 0; i < _bannerIds.Length; i++)
            {
                int index = i;
                VisualElement row = BuildBannerRow(_bannerIds[i]);
                row.RegisterCallback<ClickEvent>(_ => SelectBanner(index));
                _bannerList.Add(row);
                _bannerRows.Add(row);
            }

            _selectedBannerIndex = Mathf.Clamp(_selectedBannerIndex, 0, _bannerRows.Count - 1);
            ApplySelectedBanner();
        }

        public void HandleKeyDown(KeyDownEvent evt)
        {
            // Pulls are deliberately click-only to avoid accidental currency spend.
        }

        private VisualElement BuildBannerRow(string bannerId)
        {
            VisualElement row = new();
            row.AddToClassList("gacha-banner-row");

            row.Add(new Label(">") { name = $"GachaBannerCursor_{bannerId}" });
            row.ElementAt(0).AddToClassList("command-cursor");

            row.Add(new Label(GetBannerTitle(bannerId)) { name = $"GachaBannerName_{bannerId}" });
            row.ElementAt(1).AddToClassList("command-text");

            row.Add(new Label(GetBannerStatus(bannerId)) { name = $"GachaBannerStatus_{bannerId}" });
            row.ElementAt(2).AddToClassList("command-description");

            return row;
        }

        private void SelectBanner(int index)
        {
            if (_bannerRows.Count == 0)
            {
                return;
            }

            _selectedBannerIndex = (index + _bannerRows.Count) % _bannerRows.Count;
            ApplySelectedBanner();
        }

        private void ApplySelectedBanner()
        {
            for (int i = 0; i < _bannerRows.Count; i++)
            {
                bool selected = i == _selectedBannerIndex;
                _bannerRows[i].EnableInClassList(SelectedClassName, selected);
                _bannerRows[i].Q<Label>(className: "command-cursor").visible = selected;
            }

            RenderSelectedBanner();
        }

        private void RenderSelectedBanner()
        {
            _bannerDetail.Clear();
            string bannerId = _bannerIds[_selectedBannerIndex];
            if (bannerId == GachaService.BeginnerBannerId)
            {
                RenderBeginnerBanner();
                return;
            }

            RenderFutureBanner(bannerId);
        }

        private void RenderBeginnerBanner()
        {
            GachaBannerState state = _gachaService.BeginnerState;
            _bannerDetail.Add(BuildBannerHero("beginner install media"));

            Label title = new("beginner install media");
            title.AddToClassList("gacha-detail-title");
            _bannerDetail.Add(title);

            _bannerDetail.Add(BuildInfoLine("Entropy", _wallet == null ? "--" : _wallet.Balance.ToString()));
            _bannerDetail.Add(BuildInfoLine("Commits", _gachaService.PullTokens.ToString()));
            _bannerDetail.Add(BuildInfoLine("pulls", $"{state.totalPulls}/{GachaService.BeginnerMaxPulls}"));
            _bannerDetail.Add(BuildInfoLine("4-star pity", $"{state.pityCounter}/{GachaService.FourStarHardPity}"));
            _bannerDetail.Add(BuildInfoLine("cost", $"1 Commit / {GachaService.EntropyPerCommit} Entropy per pull"));
            _bannerDetail.Add(BuildInfoLine("guarantees", FormatBeginnerGuarantees()));

            Label rules = new("Base result is a 3-star package cache. A 4-star package cache or starter distro can appear early, with one 4-star+ result guaranteed every 10 pulls.");
            rules.AddToClassList("gacha-rules");
            _bannerDetail.Add(rules);

            VisualElement actions = new();
            actions.AddToClassList("gacha-actions");

            Button singleButton = new(() => RequestPullSelectedBanner(1))
            {
                text = $"git pull --single\n1x {GachaService.FormatCurrencyName(GachaCurrencyType.StandardPull)}"
            };
            singleButton.AddToClassList("terminal-button");
            singleButton.AddToClassList("gacha-action-button");
            singleButton.SetEnabled(CanAttemptBeginnerPull(1));
            actions.Add(singleButton);

            Button tenButton = new(() => RequestPullSelectedBanner(10))
            {
                text = $"git pull --ten\n{_gachaService.GetBeginnerPullCost(10)}x {GachaService.FormatCurrencyName(GachaCurrencyType.StandardPull)}"
            };
            tenButton.AddToClassList("terminal-button");
            tenButton.AddToClassList("gacha-action-button");
            tenButton.SetEnabled(CanAttemptBeginnerPull(10));
            actions.Add(tenButton);

            _bannerDetail.Add(actions);

            if (_pendingPullCount > 0)
            {
                _bannerDetail.Add(BuildEntropyPrompt());
            }

            if (_resultBannerId == GachaService.BeginnerBannerId &&
                (_resultRewardLines.Count > 0 || !string.IsNullOrWhiteSpace(_resultText)))
            {
                _bannerDetail.Add(BuildPullResultReveal());
            }
        }

        private void RenderFutureBanner(string bannerId)
        {
            _bannerDetail.Add(BuildBannerHero(GetBannerTitle(bannerId)));

            Label title = new(GetBannerTitle(bannerId));
            title.AddToClassList("gacha-detail-title");
            _bannerDetail.Add(title);

            _bannerDetail.Add(BuildInfoLine("pull currency", $"Commits or Entropy ({GachaService.EntropyPerCommit}:1)"));
            _bannerDetail.Add(BuildInfoLine("status", "not implemented yet"));
            _bannerDetail.Add(BuildInfoLine("pity", "own counter, 10-pull 4-star+ guarantee"));
            _bannerDetail.Add(new Label("Use the beginner implementation as the template: define the banner pool, persist a separate GachaBannerState, then route single/ten pulls through GachaService.") { name = "FutureBannerHint" });
            _bannerDetail.ElementAt(_bannerDetail.childCount - 1).AddToClassList("gacha-rules");

            if (_resultBannerId == bannerId && !string.IsNullOrWhiteSpace(_resultText))
            {
                Label result = new(_resultText);
                result.AddToClassList("gacha-result");
                _bannerDetail.Add(result);
            }
        }

        private void PullSelectedBanner(int pullCount, int entropyTokenCount)
        {
            string bannerId = _bannerIds[_selectedBannerIndex];
            if (_requestPullCutscene != null)
            {
                _requestPullCutscene.Invoke(bannerId, pullCount, entropyTokenCount);
                return;
            }

            if (bannerId != GachaService.BeginnerBannerId)
            {
                _resultBannerId = bannerId;
                _resultText = $"git pull origin {bannerId}: remote not implemented yet";
                _resultRewardLines.Clear();
                _resultRewardStars.Clear();
                RenderSelectedBanner();
                return;
            }

            PullPaymentSource source = entropyTokenCount > 0 ? PullPaymentSource.Entropy : PullPaymentSource.Commits;
            GachaPullResult result = _gachaService.PerformBeginnerPull(pullCount, _wallet, source);
            if (!result.Success)
            {
                _resultBannerId = bannerId;
                _resultText = $"git pull failed: {result.FailureReason}";
                _resultRewardLines.Clear();
                _resultRewardStars.Clear();
                RenderSelectedBanner();
                return;
            }

            List<string> header = new();
            header.Add(result.EntropySpent > 0
                ? $"git pull complete: spent {result.EntropySpent} Entropy"
                : $"git pull complete: spent {result.CurrencySpent} {GachaService.FormatCurrencyName(result.CurrencyType)}");

            List<DistroDefinition> distroRewards = new();
            for (int i = 0; i < result.Rewards.Count; i++)
            {
                if (result.Rewards[i].Distro != null)
                {
                    distroRewards.Add(result.Rewards[i].Distro);
                }
            }

            PullResolutionResult resolution = _resolvePulledDistros?.Invoke(distroRewards);
            int distroOutcomeIndex = 0;
            _resultRewardLines.Clear();
            _resultRewardStars.Clear();
            for (int i = 0; i < result.Rewards.Count; i++)
            {
                GachaReward reward = result.Rewards[i];
                PullResolutionOutcome? outcome = null;
                if (reward.Distro != null && resolution != null && distroOutcomeIndex < resolution.Outcomes.Count)
                {
                    outcome = resolution.Outcomes[distroOutcomeIndex];
                    distroOutcomeIndex++;
                }

                string suffix = reward.Guaranteed ? " guaranteed" : reward.PityTriggered ? " pity" : string.Empty;
                string outcomeText = FormatPullOutcome(outcome);
                _resultRewardLines.Add($"{i + 1:00}: {reward.StarRating}★ {FormatRewardDisplayName(reward)}{suffix}{outcomeText}");
                _resultRewardStars.Add(reward.StarRating);
            }

            _resultBannerId = bannerId;
            _resultText = string.Join("\n", header);
            _resultRevealed = false;
            _onChanged?.Invoke();
            Refresh();
        }

        public void SetCompletedPullResult(CompletedGachaPull result)
        {
            if (result == null)
            {
                return;
            }

            _resultBannerId = result.BannerId;
            _resultText = result.HeaderText;
            _resultRewardLines.Clear();
            _resultRewardStars.Clear();
            for (int i = 0; i < result.RewardLines.Count; i++)
            {
                _resultRewardLines.Add(result.RewardLines[i]);
            }

            for (int i = 0; i < result.RewardStars.Count; i++)
            {
                _resultRewardStars.Add(result.RewardStars[i]);
            }

            _resultRevealed = true;
        }

        private VisualElement BuildPullResultReveal()
        {
            VisualElement container = new();
            container.AddToClassList("gacha-result-block");

            if (!string.IsNullOrWhiteSpace(_resultText))
            {
                Label header = new(_resultText);
                header.AddToClassList("gacha-result");
                container.Add(header);
            }

            if (_resultRewardLines.Count == 0)
            {
                return container;
            }

            VisualElement list = new();
            list.AddToClassList("gacha-result-list");
            container.Add(list);

            bool animate = !_resultRevealed && !UIPreferences.ReducedMotion;
            for (int i = 0; i < _resultRewardLines.Count; i++)
            {
                int stars = _resultRewardStars[i];
                VisualElement card = new();
                card.AddToClassList("gacha-result-card");
                card.AddToClassList(RarityClassForStars(stars));
                card.Add(new Label(_resultRewardLines[i]));

                if (!animate)
                {
                    card.AddToClassList("gacha-result-card-shown");
                    if (stars >= 5)
                    {
                        card.AddToClassList("gacha-result-card-flash");
                    }

                    list.Add(card);
                    continue;
                }

                card.AddToClassList("gacha-result-card-hidden");
                list.Add(card);
                // Scheduled off _bannerDetail (already attached to the panel) rather than the
                // freshly created card, which has no panel to tick a scheduler on until this
                // whole tree is attached by the caller.
                _bannerDetail.schedule.Execute(() =>
                {
                    card.RemoveFromClassList("gacha-result-card-hidden");
                    card.AddToClassList("gacha-result-card-shown");
                    if (stars >= 5)
                    {
                        card.AddToClassList("gacha-result-card-flash");
                    }
                }).StartingIn(90 * i + 60);
            }

            _resultRevealed = true;
            return container;
        }

        private static string RarityClassForStars(int stars)
        {
            return stars switch
            {
                >= 5 => "gacha-result-card-legendary",
                4 => "gacha-result-card-rare",
                _ => "gacha-result-card-common"
            };
        }

        private static string FormatPullOutcome(PullResolutionOutcome? outcome)
        {
            if (!outcome.HasValue)
            {
                return string.Empty;
            }

            PullResolutionOutcome value = outcome.Value;
            return value.Kind switch
            {
                PullOutcomeKind.Granted => " granted",
                PullOutcomeKind.Dupe => $" duplicate +{value.MergesAwarded} {value.UnitId} merges",
                PullOutcomeKind.DupeOverflow => $" max-version duplicate +{value.MergesAwarded} {value.UnitId} merges",
                _ => string.Empty
            };
        }

        private static string FormatRewardDisplayName(GachaReward reward)
        {
            if (reward.RewardType != GachaRewardType.Package || string.IsNullOrWhiteSpace(reward.DisplayName))
            {
                return reward.DisplayName;
            }

            string trimmed = reward.DisplayName.Trim();
            return trimmed.StartsWith("3-star ", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("4-star ", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("5-star ", StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(7)
                : trimmed;
        }

        private void RequestPullSelectedBanner(int pullCount)
        {
            string bannerId = _bannerIds[_selectedBannerIndex];
            if (bannerId != GachaService.BeginnerBannerId)
            {
                PullSelectedBanner(pullCount, 0);
                return;
            }

            int cost = _gachaService.GetBeginnerPullCost(pullCount);
            int missingTokens = _gachaService.GetMissingPullTokens(GachaCurrencyType.StandardPull, cost);
            if (missingTokens <= 0)
            {
                ClearEntropyPrompt();
                PullSelectedBanner(pullCount, 0);
                return;
            }

            int entropyCost = cost * GachaService.EntropyPerCommit;
            if (_wallet == null || _wallet.Balance < entropyCost)
            {
                if (_gachaService.RootCredits > 0)
                {
                    _requestRootCreditExchange?.Invoke();
                    return;
                }

                _resultBannerId = bannerId;
                _resultText = $"git pull failed: need {cost} {GachaService.FormatCurrencyName(GachaCurrencyType.StandardPull)} or {entropyCost} Entropy";
                RenderSelectedBanner();
                return;
            }

            _pendingPullCount = pullCount;
            _pendingMissingTokens = cost;
            _resultBannerId = bannerId;
            _resultText = null;
            RenderSelectedBanner();
        }

        private VisualElement BuildEntropyPrompt()
        {
            VisualElement prompt = new();
            prompt.AddToClassList("gacha-entropy-prompt");

            int entropyCost = _pendingMissingTokens * GachaService.EntropyPerCommit;
            Label copy = new($"spend {entropyCost} Entropy instead of {_pendingMissingTokens} Commits?");
            copy.AddToClassList("gacha-result");
            prompt.Add(copy);

            VisualElement actions = new();
            actions.AddToClassList("gacha-actions");

            Button cancel = new(() =>
            {
                ClearEntropyPrompt();
                RenderSelectedBanner();
            })
            {
                text = "git pull --abort"
            };
            cancel.AddToClassList("terminal-button");
            cancel.AddToClassList("gacha-action-button");
            actions.Add(cancel);

            Button confirm = new(() =>
            {
                int pullCount = _pendingPullCount;
                int entropyPulls = _pendingMissingTokens;
                ClearEntropyPrompt();
                PullSelectedBanner(pullCount, entropyPulls);
            })
            {
                text = $"git pull --use-entropy={entropyCost}"
            };
            confirm.AddToClassList("terminal-button");
            confirm.AddToClassList("gacha-action-button");
            actions.Add(confirm);

            prompt.Add(actions);
            return prompt;
        }

        private bool CanAttemptBeginnerPull(int pullCount)
        {
            if (_gachaService.CanPullBeginner(pullCount, out _))
            {
                return true;
            }

            if (!_gachaService.IsBeginnerBannerAvailable || pullCount > GachaService.BeginnerMaxPulls - _gachaService.BeginnerState.totalPulls)
            {
                return false;
            }

            int cost = _gachaService.GetBeginnerPullCost(pullCount);
            int missingTokens = _gachaService.GetMissingPullTokens(GachaCurrencyType.StandardPull, cost);
            return missingTokens > 0 &&
                   ((_wallet != null && _wallet.Balance >= cost * GachaService.EntropyPerCommit) ||
                    _gachaService.RootCredits > 0);
        }

        private void ClearEntropyPrompt()
        {
            _pendingPullCount = 0;
            _pendingMissingTokens = 0;
        }

        private VisualElement BuildBannerHero(string bannerTitle)
        {
            VisualElement hero = new();
            hero.AddToClassList("gacha-hero");

            VisualElement artRow = new();
            artRow.AddToClassList("gacha-hero-art-row");

            IReadOnlyList<DistroDefinition> distros = _distroDatabase == null ? Array.Empty<DistroDefinition>() : _distroDatabase.AllDistros;
            int artCount = Mathf.Min(3, distros.Count);
            for (int i = 0; i < artCount; i++)
            {
                artRow.Add(BuildDistroArtCard(distros[i]));
            }

            if (artCount == 0)
            {
                Label empty = new("ascii art unavailable");
                empty.AddToClassList("gacha-rules");
                artRow.Add(empty);
            }

            Label title = new(bannerTitle);
            title.AddToClassList("gacha-hero-title");

            hero.Add(artRow);
            hero.Add(title);
            return hero;
        }

        private VisualElement BuildDistroArtCard(DistroDefinition distro)
        {
            VisualElement card = new();
            card.AddToClassList("gacha-art-card");

            VisualElement artShell = new();
            artShell.AddToClassList("gacha-art-shell");

            Label artLabel = new();
            DistroArtPresenter.ConfigureArtLabel(artLabel, _monospaceFont);
            artLabel.AddToClassList("gacha-ascii-art");
            VisualElement placeholder = DistroArtPresenter.CreatePlaceholder();
            placeholder.AddToClassList("gacha-ascii-placeholder");
            AsciiArtFitter artFitter = new(artLabel, _monospaceFont);
            artFitter.SetArt(DistroArtPresenter.Render(artLabel, placeholder, distro));
            if (distro != null)
            {
                artLabel.style.color = new StyleColor(distro.AccentColor);
            }

            artShell.Add(placeholder);
            artShell.Add(artLabel);
            card.Add(artShell);

            Label name = new(distro == null ? "--" : DistroPresentation.DisplayName(distro));
            name.AddToClassList("gacha-art-name");
            if (distro != null)
            {
                name.style.color = new StyleColor(distro.AccentColor);
            }

            card.Add(name);
            return card;
        }

        private string FormatBeginnerGuarantees()
        {
            IReadOnlyList<string> ids = _gachaService.BeginnerState.guaranteedDistroIds;
            string first = ids.Count > 0 ? ids[0] : "--";
            string second = ids.Count > 1 ? ids[1] : "--";
            return $"20={first}, 40={second}, 50=guaranteed 5-star";
        }

        private static VisualElement BuildInfoLine(string key, string value)
        {
            VisualElement row = new();
            row.AddToClassList("kv-row");
            row.Add(new Label(key) { name = $"Gacha{key}Key" });
            row.ElementAt(0).AddToClassList("kv-key");
            row.Add(new Label(value) { name = $"Gacha{key}Value" });
            row.ElementAt(1).AddToClassList("kv-value");
            return row;
        }

        private string GetBannerStatus(string bannerId)
        {
            return bannerId switch
            {
                GachaService.BeginnerBannerId => _gachaService.IsBeginnerBannerAvailable ? $"{_gachaService.BeginnerState.totalPulls}/{GachaService.BeginnerMaxPulls}" : "closed",
                GachaService.StandardBannerId => "soon",
                GachaService.LimitedBannerId => "soon",
                _ => "--"
            };
        }

        private static string GetBannerTitle(string bannerId)
        {
            return bannerId switch
            {
                GachaService.BeginnerBannerId => "beginner install media",
                GachaService.StandardBannerId => "stable branch",
                GachaService.LimitedBannerId => "feature branch",
                _ => bannerId
            };
        }
    }
}
