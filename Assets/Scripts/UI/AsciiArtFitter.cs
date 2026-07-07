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
        private static bool _hasDefaultAdvanceRatio;
        private static float _defaultAdvanceRatio;

        private readonly Label _label;
        private readonly FontAsset _font;
        private string _art;

        public AsciiArtFitter(Label label, FontAsset font)
        {
            this._label = label;
            this._font = font;
            label.RegisterCallback<GeometryChangedEvent>(_ => Fit());
        }

        public void SetArt(string value)
        {
            _art = value;
            Fit();
        }

        private void Fit()
        {
            if (string.IsNullOrWhiteSpace(_art))
            {
                return;
            }

            Rect region = _label.layout;
            if (!IsUsable(region.width) || !IsUsable(region.height))
            {
                return;
            }

            GetArtDimensions(_art, out int rows, out int columns);
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

            _label.style.fontSize = fontSize;
            _label.style.paddingLeft = Mathf.Max(0f, (region.width - blockWidth) * 0.5f);
            _label.style.paddingTop = Mathf.Max(0f, (region.height - blockHeight) * 0.5f);
            _label.style.paddingRight = 0f;
            _label.style.paddingBottom = 0f;
        }

        private float GetAdvanceRatio()
        {
            if (_font == null && _hasDefaultAdvanceRatio)
            {
                return _defaultAdvanceRatio;
            }

            if (_font != null && AdvanceRatioByFont.TryGetValue(_font, out float cached))
            {
                return cached;
            }

            float previousFontSize = _label.resolvedStyle.fontSize;
            _label.style.fontSize = ReferenceFontSize;

            float mWidth = _label.MeasureTextSize("MMMMMMMMMM", 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
            float iWidth = _label.MeasureTextSize("iiiiiiiiii", 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
            _label.style.fontSize = previousFontSize;

            float advanceRatio = mWidth > 0f ? mWidth / (10f * ReferenceFontSize) : 0.6f;
            if (_font == null)
            {
                _defaultAdvanceRatio = advanceRatio;
                _hasDefaultAdvanceRatio = true;
            }
            else
            {
                AdvanceRatioByFont[_font] = advanceRatio;
            }

            if (_font != null && mWidth > 0f && iWidth > 0f)
            {
                float difference = Mathf.Abs(mWidth - iWidth) / Mathf.Max(mWidth, iWidth);
                if (difference > MonospaceTolerance && WarnedFonts.Add(_font))
                {
                    Debug.LogWarning($"ASCII art font '{_font.name}' is not monospace; art may misalign.");
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
