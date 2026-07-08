using System;
using System.Collections.Generic;
using KernelPanic.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// Drives the starter-distro selection modal: presents the choices, confirms the pick, and
    /// - if the DistroDatabase isn't usable - shows a dismissible in-UI error instead of leaving
    /// the modal silently hidden.
    /// </summary>
    [Serializable]
    public sealed class StarterSelectionController
    {
        private const int StarterCount = 3;
        private const string ModalOpenClassName = "modal-open";
        private const int ModalTransitionMs = 160;

        private readonly List<VisualElement> _cards = new();
        private readonly List<Label> _names = new();
        private readonly List<Label> _languages = new();
        private readonly List<Label> _descriptions = new();

        private VisualElement _root;
        private VisualElement _modal;
        private VisualElement _options;
        private Label _confirmLabel;
        private Label _errorLabel;
        private DistroDatabase _database;
        private Action<DistroDefinition, IReadOnlyList<DistroDefinition>> _onConfirmed;

        private IReadOnlyList<DistroDefinition> _activeStarters = Array.Empty<DistroDefinition>();
        private int _selectedIndex;
        private bool _active;
        private bool _showingError;
        private bool _confirming;
        private IVisualElementScheduledItem _closeSchedule;
        private IVisualElementScheduledItem _modalOpenSchedule;
        private IVisualElementScheduledItem _modalHideSchedule;

        public bool IsActive => _active;

        public void Bind(VisualElement documentRoot, DistroDatabase distroDatabase, Action<DistroDefinition, IReadOnlyList<DistroDefinition>> onStarterConfirmed)
        {
            _root = documentRoot;
            _database = distroDatabase;
            _onConfirmed = onStarterConfirmed;

            _modal = _root.Q<VisualElement>("StarterModal");
            _options = _root.Q<VisualElement>("StarterOptions");
            _confirmLabel = _root.Q<Label>("StarterConfirmLabel");
            _errorLabel = _root.Q<Label>("StarterErrorLabel");

            for (int i = 0; i < StarterCount; i++)
            {
                _cards.Add(_root.Q<VisualElement>($"StarterOption{i}"));
                _names.Add(_root.Q<Label>($"StarterName{i}"));
                _languages.Add(_root.Q<Label>($"StarterLanguages{i}"));
                _descriptions.Add(_root.Q<Label>($"StarterDescription{i}"));
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                int index = i;
                _cards[i].RegisterCallback<PointerEnterEvent>(_ => Select(index));
                _cards[i].RegisterCallback<ClickEvent>(_ =>
                {
                    Select(index);
                    ConfirmIntent();
                });
            }
        }

        public void ShowIfNeeded(bool starterAlreadyChosen)
        {
            if (starterAlreadyChosen)
            {
                return;
            }

            if (!TryGetStarters(out IReadOnlyList<DistroDefinition> starters, out string error))
            {
                Debug.LogError($"Cannot show starter selection: {error}.");
                ShowConfigError(error);
                return;
            }

            _activeStarters = starters;
            _active = true;
            _showingError = false;
            _confirming = false;
            _errorLabel.AddToClassList("hidden");
            _options.RemoveFromClassList("hidden");
            _confirmLabel.AddToClassList("hidden");
            OpenModal();

            for (int i = 0; i < _cards.Count; i++)
            {
                DistroDefinition unit = starters[i];
                _names[i].text = DistroPresentation.DisplayName(unit);
                _names[i].style.color = new StyleColor(unit.AccentColor);
                _languages[i].text = DistroPresentation.FormatLanguages(unit);
                _descriptions[i].text = unit.Description;
            }

            Select(0);
            _root.Focus();
        }

        public void HandleKeyDown(KeyDownEvent evt)
        {
            if (_showingError)
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter ||
                    evt.keyCode == KeyCode.Escape || evt.keyCode == KeyCode.Space)
                {
                    Close();
                }

                return;
            }

            if (evt.keyCode == KeyCode.LeftArrow)
            {
                Select(_selectedIndex - 1);
                return;
            }

            if (evt.keyCode == KeyCode.RightArrow)
            {
                Select(_selectedIndex + 1);
                return;
            }

            int digitIndex = CommandKeyBindings.GetDigitIndex(evt.keyCode);
            if (digitIndex >= 0 && digitIndex < StarterCount)
            {
                Select(digitIndex);
                return;
            }

            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                if (_confirming)
                {
                    Confirm();
                    return;
                }

                ConfirmIntent();
                return;
            }

            if (evt.keyCode == KeyCode.Y && _confirming)
            {
                Confirm();
                return;
            }

            if (evt.keyCode == KeyCode.Escape || evt.keyCode == KeyCode.N)
            {
                _confirming = false;
                _confirmLabel.AddToClassList("hidden");
            }
        }

        public void PauseSchedules()
        {
            _closeSchedule?.Pause();
            _modalOpenSchedule?.Pause();
            _modalHideSchedule?.Pause();
        }

        private bool TryGetStarters(out IReadOnlyList<DistroDefinition> starters, out string error)
        {
            if (_database == null)
            {
                starters = Array.Empty<DistroDefinition>();
                error = "no DistroDatabase is assigned";
                return false;
            }

            if (!_database.TryValidate(out error))
            {
                starters = Array.Empty<DistroDefinition>();
                return false;
            }

            IReadOnlyList<DistroDefinition> allDistros = _database.AllDistros;
            if (allDistros.Count < StarterCount)
            {
                starters = Array.Empty<DistroDefinition>();
                error = $"DistroDatabase needs at least {StarterCount} distros (found {allDistros.Count})";
                return false;
            }

            starters = allDistros;
            error = null;
            return true;
        }

        private void ShowConfigError(string error)
        {
            _active = true;
            _showingError = true;
            _confirming = false;
            _options.AddToClassList("hidden");
            _confirmLabel.AddToClassList("hidden");
            _errorLabel.text = $"starter selection unavailable: {error}. press enter to continue.";
            _errorLabel.RemoveFromClassList("hidden");
            OpenModal();
            _root.Focus();
        }

        private void OpenModal()
        {
            _modalHideSchedule?.Pause();
            _modal.RemoveFromClassList("hidden");
            _modalOpenSchedule?.Pause();
            _modalOpenSchedule = _modal.schedule.Execute(() => _modal.AddToClassList(ModalOpenClassName)).StartingIn(0);
        }

        private void Select(int index)
        {
            if (_activeStarters.Count == 0)
            {
                return;
            }

            int slotCount = Mathf.Min(_activeStarters.Count, _cards.Count);
            _selectedIndex = (index + slotCount) % slotCount;
            _confirming = false;
            _confirmLabel.AddToClassList("hidden");

            for (int i = 0; i < _cards.Count; i++)
            {
                bool selected = i == _selectedIndex;
                _cards[i].EnableInClassList("selected", selected);
                if (i < _activeStarters.Count && selected)
                {
                    StyleColor accent = new(_activeStarters[i].AccentColor);
                    _cards[i].style.borderTopColor = accent;
                    _cards[i].style.borderRightColor = accent;
                    _cards[i].style.borderBottomColor = accent;
                    _cards[i].style.borderLeftColor = accent;
                }
                else
                {
                    _cards[i].style.borderTopColor = StyleKeyword.Null;
                    _cards[i].style.borderRightColor = StyleKeyword.Null;
                    _cards[i].style.borderBottomColor = StyleKeyword.Null;
                    _cards[i].style.borderLeftColor = StyleKeyword.Null;
                }
            }
        }

        private void ConfirmIntent()
        {
            if (_selectedIndex >= _activeStarters.Count)
            {
                return;
            }

            _confirming = true;
            _confirmLabel.text = $"install {DistroPresentation.DisplayName(_activeStarters[_selectedIndex])}? this cannot be undone [Y/n]";
            _confirmLabel.RemoveFromClassList("hidden");
        }

        private void Confirm()
        {
            if (_selectedIndex >= _activeStarters.Count)
            {
                return;
            }

            DistroDefinition picked = _activeStarters[_selectedIndex];
            List<DistroDefinition> remaining = new();
            for (int i = 0; i < _activeStarters.Count; i++)
            {
                if (i != _selectedIndex)
                {
                    remaining.Add(_activeStarters[i]);
                }
            }

            _onConfirmed?.Invoke(picked, remaining);

            _confirmLabel.text = $"installing {picked.Id} (1/1)... done";
            _confirmLabel.RemoveFromClassList("hidden");
            if (UIPreferences.ReducedMotion)
            {
                Close();
                return;
            }

            _closeSchedule?.Pause();
            _closeSchedule = _root.schedule.Execute(Close).StartingIn(600);
        }

        private void Close()
        {
            _closeSchedule?.Pause();
            _closeSchedule = null;
            _active = false;
            _confirming = false;
            _showingError = false;
            _modalOpenSchedule?.Pause();
            _modal.RemoveFromClassList(ModalOpenClassName);
            int delay = UIPreferences.ReducedMotion ? 0 : ModalTransitionMs;
            _modalHideSchedule?.Pause();
            _modalHideSchedule = _modal.schedule.Execute(() => _modal.AddToClassList("hidden")).StartingIn(delay);
            _root.Focus();
        }
    }
}
