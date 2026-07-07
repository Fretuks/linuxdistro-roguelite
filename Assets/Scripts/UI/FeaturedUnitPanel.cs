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

        private VisualElement _panel;
        private Label _titleLabel;
        private Label _asciiLabel;
        private VisualElement _asciiPlaceholder;
        private Label _unitNameLabel;
        private Label _languagesLabel;
        private Label _passiveLabel;
        private Label _bestWaveLabel;
        private Label _emptyTitleLabel;
        private Label _emptyHintLabel;
        private VisualElement _populatedState;
        private VisualElement _emptyState;
        private FontAsset _monospaceFont;
        private AsciiArtFitter _asciiFitter;
        private IReadOnlyList<DistroDefinition> _units = Array.Empty<DistroDefinition>();
        private int _selectedIndex;

        public void Bind(VisualElement root, FontAsset artFont)
        {
            _monospaceFont = artFont;
            _panel = root.Q<VisualElement>("FeaturedUnitPanel");
            _titleLabel = root.Q<Label>("FeaturedUnitTitle");
            _asciiLabel = root.Q<Label>("FeaturedUnitAscii");
            _unitNameLabel = root.Q<Label>("FeaturedUnitName");
            _languagesLabel = root.Q<Label>("FeaturedUnitLanguages");
            _passiveLabel = root.Q<Label>("FeaturedUnitPassive");
            _bestWaveLabel = root.Q<Label>("FeaturedUnitBestWave");
            _emptyTitleLabel = root.Q<Label>("FeaturedUnitEmptyTitle");
            _emptyHintLabel = root.Q<Label>("FeaturedUnitEmptyHint");
            _populatedState = root.Q<VisualElement>("FeaturedUnitPopulatedState");
            _emptyState = root.Q<VisualElement>("FeaturedUnitEmptyState");

            DistroArtPresenter.ConfigureArtLabel(_asciiLabel, _monospaceFont);
            _asciiFitter = new AsciiArtFitter(_asciiLabel, _monospaceFont);
            _asciiPlaceholder = DistroArtPresenter.CreatePlaceholder();
            _asciiPlaceholder.AddToClassList("hidden");
            _populatedState.Insert(0, _asciiPlaceholder);
        }

        public void Refresh(IReadOnlyList<DistroDefinition> ownedUnits)
        {
            Refresh(ownedUnits, null);
        }

        public void Refresh(IReadOnlyList<DistroDefinition> ownedUnits, DistroDefinition featuredUnit)
        {
            _units = ownedUnits ?? Array.Empty<DistroDefinition>();
            Refresh(featuredUnit);
        }

        public void Refresh()
        {
            Refresh((DistroDefinition)null);
        }

        private void Refresh(DistroDefinition featuredUnit)
        {
            if (_units == null || _units.Count == 0)
            {
                ShowEmptyState();
                return;
            }

            _selectedIndex = ResolveSelectedIndex(featuredUnit);
            ShowUnit(_units[_selectedIndex]);
        }

        public void SelectNext()
        {
            int unitCount = _units?.Count ?? 0;
            if (unitCount < 2)
            {
                return;
            }

            _selectedIndex = (_selectedIndex + 1) % unitCount;
            Refresh();
        }

        private int ResolveSelectedIndex(DistroDefinition featuredUnit)
        {
            if (featuredUnit != null)
            {
                for (int i = 0; i < _units.Count; i++)
                {
                    if (_units[i] == featuredUnit)
                    {
                        return i;
                    }
                }
            }

            return Mathf.Clamp(_selectedIndex, 0, _units.Count - 1);
        }

        private void ShowEmptyState()
        {
            _populatedState.AddToClassList("hidden");
            _emptyState.RemoveFromClassList("hidden");

            _titleLabel.text = "[ neofetch ]";
            _emptyTitleLabel.text = "no units installed";
            _emptyHintLabel.text = "run: curl gacha.sh | sh";
            SetAccent(EmptyAccent);
        }

        private void ShowUnit(DistroDefinition unit)
        {
            _populatedState.RemoveFromClassList("hidden");
            _emptyState.AddToClassList("hidden");

            _titleLabel.text = "[ neofetch ]";
            _asciiFitter.SetArt(DistroArtPresenter.Render(_asciiLabel, _asciiPlaceholder, unit));
            _unitNameLabel.text = DistroPresentation.DisplayName(unit);
            _languagesLabel.text = DistroPresentation.FormatLanguages(unit);
            _passiveLabel.text = unit.Passive == null || string.IsNullOrWhiteSpace(unit.Passive.Name) ? "--" : unit.Passive.Name;
            _bestWaveLabel.text = "--"; // TODO: Bind best-wave stats from SaveService when stats exist.
            SetAccent(ColorUtility.ToHtmlStringRGB(unit.AccentColor));
        }

        private void SetAccent(string htmlColor)
        {
            string color = $"#{htmlColor}";
            Color parsedColor = ColorUtility.TryParseHtmlString(color, out Color parsed) ? parsed : Color.green;
            _panel.style.borderTopColor = new StyleColor(parsedColor);
            _panel.style.borderRightColor = new StyleColor(parsedColor);
            _panel.style.borderBottomColor = new StyleColor(parsedColor);
            _panel.style.borderLeftColor = new StyleColor(parsedColor);
            _titleLabel.style.color = new StyleColor(parsedColor);
            _unitNameLabel.style.color = new StyleColor(parsedColor);
        }
    }
}
