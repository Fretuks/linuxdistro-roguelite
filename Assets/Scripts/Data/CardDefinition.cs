using System.Collections.Generic;
using KernelPanic.Core;
using UnityEngine;

namespace KernelPanic.Data
{
    /// <summary>
    /// Defines immutable card data used to create runtime card instances.
    /// </summary>
    public sealed class CardDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private string description;
        [SerializeField] private string flavorText;
        [SerializeField] private Language language;
        [SerializeField] private Rarity rarity;
        [SerializeField] private int cycleCost;
        [SerializeField] private ResolutionTrack resolutionTrack;
        [SerializeField] private bool distroExclusive;
        [SerializeField] private bool isToken;
        [SerializeField, TextArea] private string designNotes;
        [SerializeField] private List<string> requiresSystem = new();
        [SerializeField] private CardDefinition upgradeTarget;
        [SerializeField] private List<CardEffectDefinition> effects = new();

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public string FlavorText => flavorText;
        public Language Language => language;
        public Rarity Rarity => rarity;
        public int CycleCost => cycleCost;
        public ResolutionTrack ResolutionTrack => resolutionTrack;
        public bool DistroExclusive => distroExclusive;
        public bool IsToken => isToken;
        public string DesignNotes => designNotes;
        public IReadOnlyList<string> RequiresSystem => requiresSystem;
        public CardDefinition UpgradeTarget => upgradeTarget;
        public IReadOnlyList<CardEffectDefinition> Effects => effects;
    }
}
