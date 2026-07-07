using System.Collections.Generic;
using KernelPanic.Data;

namespace KernelPanic.Run
{
    /// <summary>
    /// Provides post-wave reward choice generation contracts.
    /// </summary>
    public sealed class RewardService
    {
        public IReadOnlyList<RewardChoice> GeneratePostWaveChoices(RunConfig runConfig, int waveNumber)
        {
            // TODO: Generate real post-wave rewards once card reward pools and upgrade rules exist.
            return new[] { new RewardChoice(RewardChoiceType.Skip, null) };
        }
    }

    /// <summary>
    /// Represents one selectable reward option after clearing a wave.
    /// </summary>
    public readonly struct RewardChoice
    {
        public RewardChoice(RewardChoiceType type, CardDefinition card)
        {
            Type = type;
            Card = card;
        }

        public RewardChoiceType Type { get; }
        public CardDefinition Card { get; }
    }

    /// <summary>
    /// Identifies the type of reward choice offered after a wave.
    /// </summary>
    public enum RewardChoiceType
    {
        AddCard,
        UpgradeCard,
        Skip
    }
}
