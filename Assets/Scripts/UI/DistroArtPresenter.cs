using KernelPanic.Data;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// Applies terminal-safe rendering rules for distro ASCII art.
    /// </summary>
    public static class DistroArtPresenter
    {
        private static readonly string[][] DashClasses =
        {
            new[] { "dash", "dash-top", "dash-01" },
            new[] { "dash", "dash-top", "dash-02" },
            new[] { "dash", "dash-top", "dash-03" },
            new[] { "dash", "dash-top", "dash-04" },
            new[] { "dash", "dash-top", "dash-05" },
            new[] { "dash", "dash-top", "dash-06" },
            new[] { "dash", "dash-top", "dash-07" },
            new[] { "dash", "dash-top", "dash-08" },
            new[] { "dash", "dash-bottom", "dash-01" },
            new[] { "dash", "dash-bottom", "dash-02" },
            new[] { "dash", "dash-bottom", "dash-03" },
            new[] { "dash", "dash-bottom", "dash-04" },
            new[] { "dash", "dash-bottom", "dash-05" },
            new[] { "dash", "dash-bottom", "dash-06" },
            new[] { "dash", "dash-bottom", "dash-07" },
            new[] { "dash", "dash-bottom", "dash-08" },
            new[] { "dash", "dash-left", "dash-v01" },
            new[] { "dash", "dash-left", "dash-v02" },
            new[] { "dash", "dash-left", "dash-v03" },
            new[] { "dash", "dash-left", "dash-v04" },
            new[] { "dash", "dash-left", "dash-v05" },
            new[] { "dash", "dash-left", "dash-v06" },
            new[] { "dash", "dash-right", "dash-v01" },
            new[] { "dash", "dash-right", "dash-v02" },
            new[] { "dash", "dash-right", "dash-v03" },
            new[] { "dash", "dash-right", "dash-v04" },
            new[] { "dash", "dash-right", "dash-v05" },
            new[] { "dash", "dash-right", "dash-v06" }
        };

        public static VisualElement CreatePlaceholder()
        {
            VisualElement placeholder = new();
            placeholder.AddToClassList("ascii-placeholder");

            for (int i = 0; i < DashClasses.Length; i++)
            {
                VisualElement dash = new();
                for (int classIndex = 0; classIndex < DashClasses[i].Length; classIndex++)
                {
                    dash.AddToClassList(DashClasses[i][classIndex]);
                }

                placeholder.Add(dash);
            }

            return placeholder;
        }

        public static void ConfigureArtLabel(Label label, FontAsset font)
        {
            label.enableRichText = false;
            label.AddToClassList("ascii-art");
            if (font != null)
            {
                label.style.unityFontDefinition = new StyleFontDefinition(font);
            }
        }

        public static string Render(Label label, VisualElement placeholder, DistroDefinition unit)
        {
            string art = TryGetArtText(unit);
            bool hasArt = !string.IsNullOrWhiteSpace(art);

            label.text = hasArt ? art : string.Empty;
            label.EnableInClassList("hidden", !hasArt);
            placeholder.EnableInClassList("hidden", hasArt);
            return hasArt ? art : null;
        }

        private static string TryGetArtText(DistroDefinition unit)
        {
            if (unit == null)
            {
                return null;
            }

            UnityEngine.TextAsset asciiArt = unit.AsciiArt;
            if (asciiArt == null)
            {
                return null;
            }

            try
            {
                return asciiArt.text;
            }
            catch (MissingReferenceException)
            {
                return null;
            }
        }
    }
}
