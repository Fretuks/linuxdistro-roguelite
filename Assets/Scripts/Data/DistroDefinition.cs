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
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Language primaryLanguage;
        [SerializeField] private Language secondaryLanguage;
        [SerializeField] private string passiveName;
        [SerializeField, TextArea] private string description;
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
        public string PassiveName => passiveName;
        public string Description => description;
        public TextAsset AsciiArt => asciiArt;
        public Color AccentColor => accentColor;
        public int BaseUptime => baseUptime;
        public int BaseRam => baseRam;
        public int BaseCyclesPerTurn => baseCyclesPerTurn;
        public IReadOnlyList<CardDefinition> ExclusiveCards => exclusiveCards;
    }
}
