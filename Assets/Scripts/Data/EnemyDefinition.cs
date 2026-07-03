using System.Collections.Generic;
using KernelPanic.Core;
using UnityEngine;

namespace KernelPanic.Data
{
    /// <summary>
    /// Defines enemy stats and language affinities for runtime combat setup.
    /// </summary>
    public sealed class EnemyDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private int baseUptime;
        [SerializeField] private List<Language> resistedLanguages = new();
        [SerializeField] private List<Language> weakLanguages = new();

        public string Id => id;
        public string DisplayName => displayName;
        public int BaseUptime => baseUptime;
        public IReadOnlyList<Language> ResistedLanguages => resistedLanguages;
        public IReadOnlyList<Language> WeakLanguages => weakLanguages;

        // TODO: Model enemy intents once combat actions are defined.
    }
}
