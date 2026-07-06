using UnityEngine;

namespace KernelPanic.Data
{
    /// <summary>
    /// Defines immutable passive rules and presentation text for a distro.
    /// </summary>
    public sealed class PassiveDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string rulesText;
        [SerializeField] private string flavorText;
        [SerializeField, TextArea] private string designNotes;

        public string Id => id;
        public string Name => displayName;
        public string RulesText => rulesText;
        public string FlavorText => flavorText;
        public string DesignNotes => designNotes;
    }
}
