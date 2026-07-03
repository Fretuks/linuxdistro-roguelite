using KernelPanic.Core;
using UnityEngine;

namespace KernelPanic.Data
{
    /// <summary>
    /// Defines serialized status effect metadata and future stacking behavior.
    /// </summary>
    public sealed class StatusEffectDefinition : ScriptableObject
    {
        [SerializeField] private StatusType statusType;
        [SerializeField] private string stackingBehavior;

        public StatusType StatusType => statusType;
        public string StackingBehavior => stackingBehavior;
    }
}
