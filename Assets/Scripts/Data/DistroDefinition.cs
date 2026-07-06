using System.Collections.Generic;
using KernelPanic.Core;
using UnityEngine;

namespace KernelPanic.Data
{
    /// <summary>
    /// Defines a playable distro character and its starting combat constraints.
    /// </summary>
    public sealed class DistroDefinition : ScriptableObject
    {
        [System.Serializable]
        public struct LanguageBlurb
        {
            [SerializeField] private Language lang;
            [SerializeField] private string blurb;

            public Language Lang => lang;
            public string Blurb => blurb;
        }

        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Language primaryLanguage;
        [SerializeField] private Language secondaryLanguage;
        [SerializeField] private PassiveDefinition passive;
        [SerializeField, TextArea] private string description;
        [SerializeField, TextArea] private string playstyleSummary;
        [SerializeField] private List<LanguageBlurb> languageBlurbs = new();
        [SerializeField] private TextAsset asciiArt;
        [SerializeField] private Color accentColor = new(0.36f, 1f, 0.57f);
        [SerializeField] private int baseUptime;
        [SerializeField] private int baseRam;
        [SerializeField] private int baseCyclesPerTurn;
        [SerializeField] private List<CardDefinition> exclusiveCards = new();

        public string Id => id;
        public string DisplayName => displayName;
        public Language PrimaryLanguage => primaryLanguage;
        public Language SecondaryLanguage => secondaryLanguage;
        public PassiveDefinition Passive => passive;
        public string Description => description;
        public string PlaystyleSummary => playstyleSummary;
        public IReadOnlyList<LanguageBlurb> LanguageBlurbs => languageBlurbs;
        public TextAsset AsciiArt => asciiArt;
        public Color AccentColor => accentColor;
        public int BaseUptime => baseUptime;
        public int BaseRam => baseRam;
        public int BaseCyclesPerTurn => baseCyclesPerTurn;
        public IReadOnlyList<CardDefinition> ExclusiveCards => exclusiveCards;
    }
}
