using System;
using System.Collections.Generic;

namespace KernelPanic.Meta
{
    public static class DistroVersionCatalog
    {
        private static readonly Dictionary<string, string[]> ReleaseLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ubuntu"] = new[] { "20.04", "21.04", "22.04", "23.04", "24.04" },
            ["fedora"] = new[] { "38", "39", "40", "41", "42" },
            ["mint"] = new[] { "21", "21.2", "21.3", "22", "22.1" }
        };

        public static string GetReleaseLabel(string unitId, int version)
        {
            int safeVersion = Math.Max(1, Math.Min(GachaTuning.MaxVersion, version));
            if (!string.IsNullOrWhiteSpace(unitId) && ReleaseLabels.TryGetValue(unitId, out string[] labels) && safeVersion <= labels.Length)
            {
                return labels[safeVersion - 1];
            }

            return $"V{safeVersion}";
        }

        public static string GetEffectSummary(string unitId, int version)
        {
            int safeVersion = Math.Max(1, Math.Min(GachaTuning.MaxVersion, version));
            return (unitId ?? string.Empty).ToLowerInvariant() switch
            {
                "ubuntu" => safeVersion switch
                {
                    1 => "start of turn: apt update chooses the cheaper of top 2 draw cards",
                    2 => "apt update looks at top 3 instead of top 2",
                    3 => "ask ubuntu draws 3 instead of 2",
                    4 => "apt update card costs 1 less this turn",
                    5 => "once per combat, empty hand refills up to RAM",
                    _ => "--"
                },
                "fedora" => safeVersion switch
                {
                    1 => "first card each turn costs 1 less and deals +50%, with crash risk",
                    2 => "first-card damage bonus rises to +75%",
                    3 => "non-crashing first-card plays gain +1 effect this combat; rawhide doubles that growth",
                    4 => "crashes grant +1 Cycle that turn",
                    5 => "first two cards each turn get the discount, bonus, and independent crash rolls",
                    _ => "--"
                },
                "mint" => safeVersion switch
                {
                    1 => "random values roll max; no crits; ignores multiplicative damage buffs",
                    2 => "fixed effect values are treated as +2",
                    3 => "timeshift snapshot restores 2 uptime beyond the snapshot, up to max uptime",
                    4 => "flat +damage can apply; multiplicative crit/mult remains ignored",
                    5 => "timeshift snapshot restores to full uptime if lower",
                    _ => "--"
                },
                _ => safeVersion == 1 ? "base release" : $"V{safeVersion} release effect"
            };
        }
    }
}
