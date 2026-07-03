using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// Fits monospace ASCII art into a fixed UI Toolkit label region.
    /// </summary>
    public sealed class AsciiArtFitter
    {
        private const float ReferenceFontSize = 20f;
        private const float LineHeightRatio = 1.05f;
        private const float MinFontSize = 6f;
        private const float MaxFontSize = 24f;
        private const float MonospaceTolerance = 0.02f;
        private static readonly Dictionary<FontAsset, float> AdvanceRatioByFont = new();
        private static readonly HashSet<FontAsset> WarnedFonts = new();
        private static bool hasDefaultAdvanceRatio;
        private static float defaultAdvanceRatio;

        private readonly Label label;
        private readonly FontAsset font;
        private string art;

        public AsciiArtFitter(Label label, FontAsset font)
        {
            this.label = label;
            this.font = font;
            label.RegisterCallback<GeometryChangedEvent>(_ => Fit());
        }

        public void SetArt(string value)
        {
            art = value;
            Fit();
        }

        private void Fit()
        {
            if (string.IsNullOrWhiteSpace(art))
            {
                return;
            }

            Rect region = label.layout;
            if (!IsUsable(region.width) || !IsUsable(region.height))
            {
                return;
            }

            GetArtDimensions(art, out int rows, out int columns);
            if (rows <= 0 || columns <= 0)
            {
                return;
            }

            float advanceRatio = GetAdvanceRatio();
            float widthFit = region.width / (columns * advanceRatio);
            float heightFit = region.height / (rows * LineHeightRatio);
            float fontSize = Mathf.Clamp(Mathf.Floor(Mathf.Min(widthFit, heightFit)), MinFontSize, MaxFontSize);
            float blockWidth = columns * fontSize * advanceRatio;
            float blockHeight = rows * fontSize * LineHeightRatio;

            label.style.fontSize = fontSize;
            label.style.paddingLeft = Mathf.Max(0f, (region.width - blockWidth) * 0.5f);
            label.style.paddingTop = Mathf.Max(0f, (region.height - blockHeight) * 0.5f);
            label.style.paddingRight = 0f;
            label.style.paddingBottom = 0f;
        }

        private float GetAdvanceRatio()
        {
            if (font == null && hasDefaultAdvanceRatio)
            {
                return defaultAdvanceRatio;
            }

            if (font != null && AdvanceRatioByFont.TryGetValue(font, out float cached))
            {
                return cached;
            }

            float previousFontSize = label.resolvedStyle.fontSize;
            label.style.fontSize = ReferenceFontSize;

            float mWidth = label.MeasureTextSize("MMMMMMMMMM", 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
            float iWidth = label.MeasureTextSize("iiiiiiiiii", 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
            label.style.fontSize = previousFontSize;

            float advanceRatio = mWidth > 0f ? mWidth / (10f * ReferenceFontSize) : 0.6f;
            if (font == null)
            {
                defaultAdvanceRatio = advanceRatio;
                hasDefaultAdvanceRatio = true;
            }
            else
            {
                AdvanceRatioByFont[font] = advanceRatio;
            }

            if (font != null && mWidth > 0f && iWidth > 0f)
            {
                float difference = Mathf.Abs(mWidth - iWidth) / Mathf.Max(mWidth, iWidth);
                if (difference > MonospaceTolerance && WarnedFonts.Add(font))
                {
                    Debug.LogWarning($"ASCII art font '{font.name}' is not monospace; art may misalign.");
                }
            }

            return advanceRatio;
        }

        private static void GetArtDimensions(string value, out int rows, out int columns)
        {
            string normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
            if (normalized.EndsWith("\n", StringComparison.Ordinal))
            {
                normalized = normalized[..^1];
            }

            rows = 0;
            columns = 0;
            string[] lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                rows++;
                columns = Mathf.Max(columns, lines[i].Length);
            }
        }

        private static bool IsUsable(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
