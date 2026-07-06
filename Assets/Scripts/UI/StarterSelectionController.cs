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

        private readonly List<VisualElement> cards = new();
        private readonly List<Label> names = new();
        private readonly List<Label> languages = new();
        private readonly List<Label> descriptions = new();

        private VisualElement root;
        private VisualElement modal;
        private VisualElement options;
        private Label confirmLabel;
        private Label errorLabel;
        private DistroDatabase database;
        private Action<DistroDefinition, IReadOnlyList<DistroDefinition>> onConfirmed;

        private IReadOnlyList<DistroDefinition> activeStarters = Array.Empty<DistroDefinition>();
        private int selectedIndex;
        private bool active;
        private bool showingError;
        private bool confirming;
        private IVisualElementScheduledItem closeSchedule;

        public bool IsActive => active;

        public void Bind(VisualElement documentRoot, DistroDatabase distroDatabase, Action<DistroDefinition, IReadOnlyList<DistroDefinition>> onStarterConfirmed)
        {
            root = documentRoot;
            database = distroDatabase;
            onConfirmed = onStarterConfirmed;

            modal = root.Q<VisualElement>("StarterModal");
            options = root.Q<VisualElement>("StarterOptions");
            confirmLabel = root.Q<Label>("StarterConfirmLabel");
            errorLabel = root.Q<Label>("StarterErrorLabel");

            for (int i = 0; i < StarterCount; i++)
            {
                cards.Add(root.Q<VisualElement>($"StarterOption{i}"));
                names.Add(root.Q<Label>($"StarterName{i}"));
                languages.Add(root.Q<Label>($"StarterLanguages{i}"));
                descriptions.Add(root.Q<Label>($"StarterDescription{i}"));
            }

            for (int i = 0; i < cards.Count; i++)
            {
                int index = i;
                cards[i].RegisterCallback<PointerEnterEvent>(_ => Select(index));
                cards[i].RegisterCallback<ClickEvent>(_ =>
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

            activeStarters = starters;
            active = true;
            showingError = false;
            confirming = false;
            errorLabel.AddToClassList("hidden");
            options.RemoveFromClassList("hidden");
            confirmLabel.AddToClassList("hidden");
            modal.RemoveFromClassList("hidden");

            for (int i = 0; i < cards.Count; i++)
            {
                DistroDefinition unit = starters[i];
                names[i].text = DistroPresentation.DisplayName(unit);
                names[i].style.color = new StyleColor(unit.AccentColor);
                languages[i].text = DistroPresentation.FormatLanguages(unit);
                descriptions[i].text = unit.Description;
            }

            Select(0);
            root.Focus();
        }

        public void HandleKeyDown(KeyDownEvent evt)
        {
            if (showingError)
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
                Select(selectedIndex - 1);
                return;
            }

            if (evt.keyCode == KeyCode.RightArrow)
            {
                Select(selectedIndex + 1);
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
                if (confirming)
                {
                    Confirm();
                    return;
                }

                ConfirmIntent();
                return;
            }

            if (evt.keyCode == KeyCode.Y && confirming)
            {
                Confirm();
                return;
            }

            if (evt.keyCode == KeyCode.Escape || evt.keyCode == KeyCode.N)
            {
                confirming = false;
                confirmLabel.AddToClassList("hidden");
            }
        }

        public void PauseSchedules()
        {
            closeSchedule?.Pause();
        }

        private bool TryGetStarters(out IReadOnlyList<DistroDefinition> starters, out string error)
        {
            if (database == null)
            {
                starters = Array.Empty<DistroDefinition>();
                error = "no DistroDatabase is assigned";
                return false;
            }

            if (!database.TryValidate(out error))
            {
                starters = Array.Empty<DistroDefinition>();
                return false;
            }

            IReadOnlyList<DistroDefinition> allDistros = database.AllDistros;
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
            active = true;
            showingError = true;
            confirming = false;
            options.AddToClassList("hidden");
            confirmLabel.AddToClassList("hidden");
            errorLabel.text = $"starter selection unavailable: {error}. press enter to continue.";
            errorLabel.RemoveFromClassList("hidden");
            modal.RemoveFromClassList("hidden");
            root.Focus();
        }

        private void Select(int index)
        {
            if (activeStarters.Count == 0)
            {
                return;
            }

            int slotCount = Mathf.Min(activeStarters.Count, cards.Count);
            selectedIndex = (index + slotCount) % slotCount;
            confirming = false;
            confirmLabel.AddToClassList("hidden");

            for (int i = 0; i < cards.Count; i++)
            {
                bool selected = i == selectedIndex;
                cards[i].EnableInClassList("selected", selected);
                if (i < activeStarters.Count && selected)
                {
                    StyleColor accent = new(activeStarters[i].AccentColor);
                    cards[i].style.borderTopColor = accent;
                    cards[i].style.borderRightColor = accent;
                    cards[i].style.borderBottomColor = accent;
                    cards[i].style.borderLeftColor = accent;
                }
                else
                {
                    cards[i].style.borderTopColor = StyleKeyword.Null;
                    cards[i].style.borderRightColor = StyleKeyword.Null;
                    cards[i].style.borderBottomColor = StyleKeyword.Null;
                    cards[i].style.borderLeftColor = StyleKeyword.Null;
                }
            }
        }

        private void ConfirmIntent()
        {
            if (selectedIndex >= activeStarters.Count)
            {
                return;
            }

            confirming = true;
            confirmLabel.text = $"install {DistroPresentation.DisplayName(activeStarters[selectedIndex])}? this cannot be undone [Y/n]";
            confirmLabel.RemoveFromClassList("hidden");
        }

        private void Confirm()
        {
            if (selectedIndex >= activeStarters.Count)
            {
                return;
            }

            DistroDefinition picked = activeStarters[selectedIndex];
            List<DistroDefinition> remaining = new();
            for (int i = 0; i < activeStarters.Count; i++)
            {
                if (i != selectedIndex)
                {
                    remaining.Add(activeStarters[i]);
                }
            }

            onConfirmed?.Invoke(picked, remaining);

            confirmLabel.text = $"installing {picked.Id} (1/1)... done";
            confirmLabel.RemoveFromClassList("hidden");
            if (UIPreferences.ReducedMotion)
            {
                Close();
                return;
            }

            closeSchedule?.Pause();
            closeSchedule = root.schedule.Execute(Close).StartingIn(600);
        }

        private void Close()
        {
            closeSchedule?.Pause();
            closeSchedule = null;
            active = false;
            confirming = false;
            showingError = false;
            modal.AddToClassList("hidden");
            root.Focus();
        }
    }
}
