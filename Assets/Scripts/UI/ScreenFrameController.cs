using System;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// Binds shared command-style chrome used by terminal sub-screens.
    /// </summary>
    public sealed class ScreenFrameController
    {
        private Label titleLabel;
        private Label hintLabel;

        public void Bind(VisualElement panel, string title, string hint, Action onBack)
        {
            titleLabel = panel.Q<Label>(className: "screen-frame-title");
            hintLabel = panel.Q<Label>(className: "screen-frame-hint");

            if (titleLabel != null)
            {
                titleLabel.text = title;
            }

            if (hintLabel != null)
            {
                hintLabel.text = hint;
                hintLabel.RegisterCallback<ClickEvent>(_ => onBack?.Invoke());
            }
        }

        public void SetHint(string hint)
        {
            if (hintLabel != null)
            {
                hintLabel.text = hint;
            }
        }

        public void SetTitle(string title)
        {
            if (titleLabel != null)
            {
                titleLabel.text = title;
            }
        }
    }
}
