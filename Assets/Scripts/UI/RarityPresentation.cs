using UnityEngine;

namespace KernelPanic.UI
{
    /// <summary>
    /// Shared rarity presentation tokens for pull, shop, collection, and drop UIs.
    /// USS classes are defined in Resources/RarityPresentation.uss.
    /// </summary>
    public static class RarityPresentation
    {
        public static RarityStyle ForStars(int stars)
        {
            return stars switch
            {
                >= 5 => new RarityStyle(5, "5★", "★★★★★", "rarity-5", new Color(1f, 0.75f, 0.25f)),
                4 => new RarityStyle(4, "4★", "★★★★", "rarity-4", new Color(0.35f, 1f, 0.95f)),
                _ => new RarityStyle(3, "3★", "★★★", "rarity-3", new Color(0.5f, 0.62f, 0.54f))
            };
        }
    }

    public readonly struct RarityStyle
    {
        public RarityStyle(int tier, string badge, string stars, string className, Color color)
        {
            Tier = tier;
            Badge = badge;
            Stars = stars;
            ClassName = className;
            Color = color;
        }

        public int Tier { get; }
        public string Badge { get; }
        public string Stars { get; }
        public string ClassName { get; }
        public Color Color { get; }
    }
}
