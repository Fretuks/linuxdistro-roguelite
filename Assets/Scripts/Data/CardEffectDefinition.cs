using KernelPanic.Combat;
using UnityEngine;

namespace KernelPanic.Data
{
    /// <summary>
    /// Defines serialized card effect data that can create a runtime effect.
    /// </summary>
    public abstract class CardEffectDefinition : ScriptableObject
    {
        public abstract ICardEffect CreateRuntimeEffect();
    }
}
