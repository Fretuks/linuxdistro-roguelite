using System;
using System.Collections.Generic;
using KernelPanic.Data;
using UnityEngine;
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
        private const string EmptyAscii = "      _\n     | |\n  ___| |__\n / __| '_ \\\n \\__ \\ | | |\n |___/_| |_|";

        [SerializeField] private List<DistroDefinition> featuredUnits = new();

        private VisualElement panel;
        private Label titleLabel;
        private Label asciiLabel;
        private Label unitNameLabel;
        private Label languagesLabel;
        private Label passiveLabel;
        private Label bestWaveLabel;
        private Label emptyTitleLabel;
        private Label emptyHintLabel;
        private VisualElement populatedState;
        private VisualElement emptyState;
        private int selectedIndex;

        public void Bind(VisualElement root)
        {
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
        }

        public void Refresh()
        {
            if (featuredUnits == null || featuredUnits.Count == 0)
            {
                ShowEmptyState();
                return;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, featuredUnits.Count - 1);
            ShowUnit(featuredUnits[selectedIndex]);
        }

        public void SelectNext()
        {
            if (featuredUnits == null || featuredUnits.Count < 2)
            {
                return;
            }

            selectedIndex = (selectedIndex + 1) % featuredUnits.Count;
            Refresh();
        }

        private void ShowEmptyState()
        {
            populatedState.AddToClassList("hidden");
            emptyState.RemoveFromClassList("hidden");

            titleLabel.text = "neofetch";
            emptyTitleLabel.text = "no units installed";
            emptyHintLabel.text = "run: curl gacha.sh | sh";
            SetAccent(EmptyAccent);
        }

        private void ShowUnit(DistroDefinition unit)
        {
            populatedState.RemoveFromClassList("hidden");
            emptyState.AddToClassList("hidden");

            string displayName = string.IsNullOrWhiteSpace(unit.DisplayName) ? unit.name : unit.DisplayName;
            titleLabel.text = displayName;
            asciiLabel.text = EmptyAscii; // TODO: Replace with unit-specific ASCII copy when unit presentation data exists.
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
        }
    }
}
