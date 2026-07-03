using System;
using System.Collections.Generic;
using KernelPanic.Data;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// Renders the main menu's neofetch-style featured unit panel.
    /// </summary>
    [Serializable]
    public sealed class FeaturedUnitPanel
    {
        private const string EmptyAccent = "5cff91";

        private VisualElement panel;
        private Label titleLabel;
        private Label asciiLabel;
        private VisualElement asciiPlaceholder;
        private Label unitNameLabel;
        private Label languagesLabel;
        private Label passiveLabel;
        private Label bestWaveLabel;
        private Label emptyTitleLabel;
        private Label emptyHintLabel;
        private VisualElement populatedState;
        private VisualElement emptyState;
        private FontAsset monospaceFont;
        private AsciiArtFitter asciiFitter;
        private IReadOnlyList<DistroDefinition> units = Array.Empty<DistroDefinition>();
        private int selectedIndex;

        public void Bind(VisualElement root, FontAsset artFont)
        {
            monospaceFont = artFont;
            panel = root.Q<VisualElement>("FeaturedUnitPanel");
            titleLabel = root.Q<Label>("FeaturedUnitTitle");
            asciiLabel = root.Q<Label>("FeaturedUnitAscii");
            unitNameLabel = root.Q<Label>("FeaturedUnitName");
            languagesLabel = root.Q<Label>("FeaturedUnitLanguages");
            passiveLabel = root.Q<Label>("FeaturedUnitPassive");
            bestWaveLabel = root.Q<Label>("FeaturedUnitBestWave");
            emptyTitleLabel = root.Q<Label>("FeaturedUnitEmptyTitle");
            emptyHintLabel = root.Q<Label>("FeaturedUnitEmptyHint");
            populatedState = root.Q<VisualElement>("FeaturedUnitPopulatedState");
            emptyState = root.Q<VisualElement>("FeaturedUnitEmptyState");

            DistroArtPresenter.ConfigureArtLabel(asciiLabel, monospaceFont);
            asciiFitter = new AsciiArtFitter(asciiLabel, monospaceFont);
            asciiPlaceholder = DistroArtPresenter.CreatePlaceholder();
            asciiPlaceholder.AddToClassList("hidden");
            populatedState.Insert(0, asciiPlaceholder);
        }

        public void Refresh(IReadOnlyList<DistroDefinition> ownedUnits)
        {
            units = ownedUnits ?? Array.Empty<DistroDefinition>();
            Refresh();
        }

        public void Refresh()
        {
            if (units == null || units.Count == 0)
            {
                ShowEmptyState();
                return;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, units.Count - 1);
            ShowUnit(units[selectedIndex]);
        }

        public void SelectNext()
        {
            int unitCount = units?.Count ?? 0;
            if (unitCount < 2)
            {
                return;
            }

            selectedIndex = (selectedIndex + 1) % unitCount;
            Refresh();
        }

        private void ShowEmptyState()
        {
            populatedState.AddToClassList("hidden");
            emptyState.RemoveFromClassList("hidden");

            titleLabel.text = "[ neofetch ]";
            emptyTitleLabel.text = "no units installed";
            emptyHintLabel.text = "run: curl gacha.sh | sh";
            SetAccent(EmptyAccent);
        }

        private void ShowUnit(DistroDefinition unit)
        {
            populatedState.RemoveFromClassList("hidden");
            emptyState.AddToClassList("hidden");

            string displayName = string.IsNullOrWhiteSpace(unit.DisplayName) ? unit.name : unit.DisplayName;
            titleLabel.text = "[ neofetch ]";
            asciiFitter.SetArt(DistroArtPresenter.Render(asciiLabel, asciiPlaceholder, unit));
            unitNameLabel.text = displayName;
            languagesLabel.text = $"{unit.PrimaryLanguage} / {unit.SecondaryLanguage}";
            passiveLabel.text = string.IsNullOrWhiteSpace(unit.PassiveName) ? "--" : unit.PassiveName;
            bestWaveLabel.text = "--"; // TODO: Bind best-wave stats from SaveService when stats exist.
            SetAccent(ColorUtility.ToHtmlStringRGB(unit.AccentColor));
        }

        private void SetAccent(string htmlColor)
        {
            string color = $"#{htmlColor}";
            Color parsedColor = ColorUtility.TryParseHtmlString(color, out Color parsed) ? parsed : Color.green;
            panel.style.borderTopColor = new StyleColor(parsedColor);
            panel.style.borderRightColor = new StyleColor(parsedColor);
            panel.style.borderBottomColor = new StyleColor(parsedColor);
            panel.style.borderLeftColor = new StyleColor(parsedColor);
            titleLabel.style.color = new StyleColor(parsedColor);
            unitNameLabel.style.color = new StyleColor(parsedColor);
        }
    }
}
