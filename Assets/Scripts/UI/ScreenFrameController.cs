using System;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// Binds shared command-style chrome used by terminal sub-screens.
    /// </summary>
    public sealed class ScreenFrameController
    {
        private Label _titleLabel;
        private Label _hintLabel;

        public void Bind(VisualElement panel, string title, string hint, Action onBack)
        {
            _titleLabel = panel.Q<Label>(className: "screen-frame-title");
            _hintLabel = panel.Q<Label>(className: "screen-frame-hint");

            if (_titleLabel != null)
            {
                _titleLabel.text = title;
            }

            if (_hintLabel != null)
            {
                _hintLabel.text = hint;
                _hintLabel.RegisterCallback<ClickEvent>(_ => onBack?.Invoke());
            }
        }

        public void SetHint(string hint)
        {
            if (_hintLabel != null)
            {
                _hintLabel.text = hint;
            }
        }

        public void SetTitle(string title)
        {
            if (_titleLabel != null)
            {
                _titleLabel.text = title;
            }
        }
    }
}
