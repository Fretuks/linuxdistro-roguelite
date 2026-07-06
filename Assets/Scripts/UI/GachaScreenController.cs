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

        private readonly List<VisualElement> bannerRows = new();
        private readonly string[] bannerIds =
        {
            GachaService.BeginnerBannerId,
            GachaService.StandardBannerId,
            GachaService.LimitedBannerId
        };

        private VisualElement bannerList;
        private VisualElement bannerDetail;
        private DistroDatabase distroDatabase;
        private FontAsset monospaceFont;
        private GachaService gachaService;
        private PlayerCollection playerCollection;
        private EntropyWallet wallet;
        private Action onChanged;
        private Action requestRootCreditExchange;
        private int selectedBannerIndex;
        private int pendingPullCount;
        private int pendingMissingTokens;
        private bool hasOpened;
        private string resultBannerId;
        private string resultText;

        public void Bind(VisualElement root, DistroDatabase database, FontAsset artFont, GachaService service, PlayerCollection collection, EntropyWallet entropyWallet, Action changedCallback, Action rootCreditExchangeCallback)
        {
            distroDatabase = database;
            monospaceFont = artFont;
            gachaService = service;
            playerCollection = collection;
            wallet = entropyWallet;
            onChanged = changedCallback;
            requestRootCreditExchange = rootCreditExchangeCallback;
            bannerList = root.Q<VisualElement>("GachaBannerList");
            bannerDetail = root.Q<VisualElement>("GachaBannerDetail");
        }

        public void Open()
        {
            if (!hasOpened)
            {
                selectedBannerIndex = 0;
                hasOpened = true;
            }

            Refresh();
        }

        public void Refresh()
        {
            if (bannerList == null || bannerDetail == null || gachaService == null)
            {
                return;
            }

            bannerRows.Clear();
            bannerList.Clear();

            for (int i = 0; i < bannerIds.Length; i++)
            {
                int index = i;
                VisualElement row = BuildBannerRow(bannerIds[i]);
                row.RegisterCallback<ClickEvent>(_ => SelectBanner(index));
                bannerList.Add(row);
                bannerRows.Add(row);
            }

            selectedBannerIndex = Mathf.Clamp(selectedBannerIndex, 0, bannerRows.Count - 1);
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
            if (bannerRows.Count == 0)
            {
                return;
            }

            selectedBannerIndex = (index + bannerRows.Count) % bannerRows.Count;
            ApplySelectedBanner();
        }

        private void ApplySelectedBanner()
        {
            for (int i = 0; i < bannerRows.Count; i++)
            {
                bool selected = i == selectedBannerIndex;
                bannerRows[i].EnableInClassList(SelectedClassName, selected);
                bannerRows[i].Q<Label>(className: "command-cursor").visible = selected;
            }

            RenderSelectedBanner();
        }

        private void RenderSelectedBanner()
        {
            bannerDetail.Clear();
            string bannerId = bannerIds[selectedBannerIndex];
            if (bannerId == GachaService.BeginnerBannerId)
            {
                RenderBeginnerBanner();
                return;
            }

            RenderFutureBanner(bannerId);
        }

        private void RenderBeginnerBanner()
        {
            GachaBannerState state = gachaService.BeginnerState;
            bannerDetail.Add(BuildBannerHero("beginner install media"));

            Label title = new("beginner install media");
            title.AddToClassList("gacha-detail-title");
            bannerDetail.Add(title);

            bannerDetail.Add(BuildInfoLine("entropy", wallet == null ? "--" : wallet.Balance.ToString()));
            bannerDetail.Add(BuildInfoLine("pulls", $"{state.totalPulls}/{GachaService.BeginnerMaxPulls}"));
            bannerDetail.Add(BuildInfoLine("4-star pity", $"{state.pityCounter}/{GachaService.FourStarHardPity}"));
            bannerDetail.Add(BuildInfoLine("ten pull", $"{GachaService.BeginnerTenPullCost} stable-pull-tokens for 10 pulls"));
            bannerDetail.Add(BuildInfoLine("guarantees", FormatBeginnerGuarantees()));

            Label rules = new("Base result is a 3-star equipment cache. A 4-star equipment cache or starter distro can appear early, with one 4-star+ result guaranteed every 10 pulls.");
            rules.AddToClassList("gacha-rules");
            bannerDetail.Add(rules);

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
                text = $"git pull --ten\n8x {GachaService.FormatCurrencyName(GachaCurrencyType.StandardPull)}"
            };
            tenButton.AddToClassList("terminal-button");
            tenButton.AddToClassList("gacha-action-button");
            tenButton.SetEnabled(CanAttemptBeginnerPull(10));
            actions.Add(tenButton);

            bannerDetail.Add(actions);

            if (pendingPullCount > 0)
            {
                bannerDetail.Add(BuildEntropyPrompt());
            }

            if (resultBannerId == GachaService.BeginnerBannerId && !string.IsNullOrWhiteSpace(resultText))
            {
                Label result = new(resultText);
                result.AddToClassList("gacha-result");
                bannerDetail.Add(result);
            }
        }

        private void RenderFutureBanner(string bannerId)
        {
            bannerDetail.Add(BuildBannerHero(GetBannerTitle(bannerId)));

            Label title = new(GetBannerTitle(bannerId));
            title.AddToClassList("gacha-detail-title");
            bannerDetail.Add(title);

            string currency = bannerId == GachaService.StandardBannerId
                ? GachaService.FormatCurrencyName(GachaCurrencyType.StandardPull)
                : GachaService.FormatCurrencyName(GachaCurrencyType.LimitedPull);
            bannerDetail.Add(BuildInfoLine("pull currency", currency));
            bannerDetail.Add(BuildInfoLine("status", "not implemented yet"));
            bannerDetail.Add(BuildInfoLine("pity", "own counter, 10-pull 4-star+ guarantee"));
            bannerDetail.Add(new Label("Use the beginner implementation as the template: define the banner pool, persist a separate GachaBannerState, then route single/ten pulls through GachaService.") { name = "FutureBannerHint" });
            bannerDetail.ElementAt(bannerDetail.childCount - 1).AddToClassList("gacha-rules");

            if (resultBannerId == bannerId && !string.IsNullOrWhiteSpace(resultText))
            {
                Label result = new(resultText);
                result.AddToClassList("gacha-result");
                bannerDetail.Add(result);
            }
        }

        private void PullSelectedBanner(int pullCount, int entropyTokenCount)
        {
            string bannerId = bannerIds[selectedBannerIndex];
            if (bannerId != GachaService.BeginnerBannerId)
            {
                resultBannerId = bannerId;
                resultText = $"git pull origin {bannerId}: remote not implemented yet";
                RenderSelectedBanner();
                return;
            }

            GachaPullResult result = gachaService.PerformBeginnerPull(pullCount, wallet, entropyTokenCount);
            if (!result.Success)
            {
                resultBannerId = bannerId;
                resultText = $"git pull failed: {result.FailureReason}";
                RenderSelectedBanner();
                return;
            }

            List<string> lines = new();
            lines.Add($"git pull complete: spent {result.CurrencySpent} {GachaService.FormatCurrencyName(result.CurrencyType)}");
            if (result.EntropySpent > 0)
            {
                lines.Add($"entropy fallback: spent {result.EntropySpent} entropy");
            }

            for (int i = 0; i < result.Rewards.Count; i++)
            {
                GachaReward reward = result.Rewards[i];
                bool duplicate = reward.Distro != null && playerCollection.GetOwnedCount(reward.Distro.Id) > 0;
                if (reward.Distro != null)
                {
                    playerCollection.Add(reward.Distro);
                }

                string suffix = reward.Guaranteed ? " guaranteed" : reward.PityTriggered ? " pity" : string.Empty;
                string duplicateText = duplicate ? " duplicate" : string.Empty;
                lines.Add($"{i + 1:00}: {reward.StarRating}-star {reward.DisplayName}{suffix}{duplicateText}");
            }

            resultBannerId = bannerId;
            resultText = string.Join("\n", lines);
            onChanged?.Invoke();
            Refresh();
        }

        private void RequestPullSelectedBanner(int pullCount)
        {
            string bannerId = bannerIds[selectedBannerIndex];
            if (bannerId != GachaService.BeginnerBannerId)
            {
                PullSelectedBanner(pullCount, 0);
                return;
            }

            int cost = gachaService.GetBeginnerPullCost(pullCount);
            int missingTokens = gachaService.GetMissingPullTokens(GachaCurrencyType.StandardPull, cost);
            if (missingTokens <= 0)
            {
                ClearEntropyPrompt();
                PullSelectedBanner(pullCount, 0);
                return;
            }

            if (!gachaService.CanCoverMissingPullTokensWithEntropy(wallet, missingTokens))
            {
                if (gachaService.RootCredits > 0)
                {
                    requestRootCreditExchange?.Invoke();
                    return;
                }

                resultBannerId = bannerId;
                resultText = $"git pull failed: need {missingTokens} {GachaService.FormatCurrencyName(GachaCurrencyType.StandardPull)} or {missingTokens * GachaService.EntropyPerPullToken} entropy";
                RenderSelectedBanner();
                return;
            }

            pendingPullCount = pullCount;
            pendingMissingTokens = missingTokens;
            resultBannerId = bannerId;
            resultText = null;
            RenderSelectedBanner();
        }

        private VisualElement BuildEntropyPrompt()
        {
            VisualElement prompt = new();
            prompt.AddToClassList("gacha-entropy-prompt");

            int entropyCost = pendingMissingTokens * GachaService.EntropyPerPullToken;
            Label copy = new($"missing {pendingMissingTokens} stable-pull-token(s). spend {entropyCost} entropy to complete git pull?");
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
                int pullCount = pendingPullCount;
                int missingTokens = pendingMissingTokens;
                ClearEntropyPrompt();
                PullSelectedBanner(pullCount, missingTokens);
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
            if (gachaService.CanPullBeginner(pullCount, out _))
            {
                return true;
            }

            if (!gachaService.IsBeginnerBannerAvailable || pullCount > GachaService.BeginnerMaxPulls - gachaService.BeginnerState.totalPulls)
            {
                return false;
            }

            int missingTokens = gachaService.GetMissingPullTokens(GachaCurrencyType.StandardPull, gachaService.GetBeginnerPullCost(pullCount));
            return missingTokens > 0 &&
                   (gachaService.CanCoverMissingPullTokensWithEntropy(wallet, missingTokens) ||
                    gachaService.RootCredits > 0);
        }

        private void ClearEntropyPrompt()
        {
            pendingPullCount = 0;
            pendingMissingTokens = 0;
        }

        private VisualElement BuildBannerHero(string bannerTitle)
        {
            VisualElement hero = new();
            hero.AddToClassList("gacha-hero");

            VisualElement artRow = new();
            artRow.AddToClassList("gacha-hero-art-row");

            IReadOnlyList<DistroDefinition> distros = distroDatabase == null ? Array.Empty<DistroDefinition>() : distroDatabase.AllDistros;
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
            DistroArtPresenter.ConfigureArtLabel(artLabel, monospaceFont);
            artLabel.AddToClassList("gacha-ascii-art");
            VisualElement placeholder = DistroArtPresenter.CreatePlaceholder();
            placeholder.AddToClassList("gacha-ascii-placeholder");
            AsciiArtFitter artFitter = new(artLabel, monospaceFont);
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
            IReadOnlyList<string> ids = gachaService.BeginnerState.guaranteedDistroIds;
            string first = ids.Count > 0 ? ids[0] : "--";
            string second = ids.Count > 1 ? ids[1] : "--";
            return $"20={first}, 40={second}, 50=future standard 5-star";
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
                GachaService.BeginnerBannerId => gachaService.IsBeginnerBannerAvailable ? $"{gachaService.BeginnerState.totalPulls}/{GachaService.BeginnerMaxPulls}" : "closed",
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
