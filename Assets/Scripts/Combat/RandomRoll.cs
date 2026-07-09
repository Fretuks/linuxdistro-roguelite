using System;

namespace KernelPanic.Combat
{
    /// <summary>
    /// Carries source data for combat rolls so later passives can bias the result.
    /// </summary>
    public readonly struct RollContext
    {
        public RollContext(CombatantState source)
        {
            Source = source;
        }

        public static RollContext None => new(null);
        public CombatantState Source { get; }
    }

    /// <summary>
    /// Single combat randomness chokepoint. Seeded from RunConfig.RunSeed during boot for reproducible runs.
    /// </summary>
    public static class RandomRoll
    {
        private static Random _random = new(0);

        public static void Seed(int seed)
        {
            _random = new Random(seed);
        }

        public static int RollRange(int min, int max, RollContext context)
        {
            if (min > max)
            {
                (min, max) = (max, min);
            }

            // Mint's passive will flip this bool; no passive sets it in this scaffold.
            if (context.Source != null && context.Source.ForceMaxRolls)
            {
                return max;
            }

            return _random.Next(min, max + 1);
        }
    }
}
