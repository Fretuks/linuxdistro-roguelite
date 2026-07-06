using System;
using System.Collections.Generic;
using KernelPanic.Data;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// Renders the owned-units list and detail readout on the collection screen.
    /// </summary>
    [Serializable]
    public sealed class CollectionScreenController
    {
        private readonly List<VisualElement> rows = new();

        private VisualElement list;
        private VisualElement detail;
        private FontAsset monospaceFont;
        private IReadOnlyList<DistroDefinition> units = Array.Empty<DistroDefinition>();
        private int selectedIndex;

        public void Bind(VisualElement root, FontAsset artFont)
        {
            monospaceFont = artFont;
            list = root.Q<VisualElement>("CollectionList");
            detail = root.Q<VisualElement>("CollectionDetail");
        }

        public void Refresh(IReadOnlyList<DistroDefinition> ownedUnits)
        {
            units = ownedUnits ?? Array.Empty<DistroDefinition>();
            if (list == null || detail == null)
            {
                return;
            }

            rows.Clear();
            list.Clear();

            if (units.Count == 0)
            {
                detail.Clear();
                detail.Add(new Label("no units installed") { name = "CollectionEmptyTitle" });
                detail.Add(new Label("run: curl gacha.sh | sh") { name = "CollectionEmptyHint" });
                return;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, units.Count - 1);
            for (int i = 0; i < units.Count; i++)
            {
                int index = i;
                DistroDefinition unit = units[i];
                VisualElement row = new();
                row.AddToClassList("collection-row");
                row.RegisterCallback<PointerEnterEvent>(_ => SelectUnit(index));
                row.RegisterCallback<ClickEvent>(_ => SelectUnit(index));

                Label name = new(DistroPresentation.DisplayName(unit));
                name.AddToClassList("collection-row-name");
                name.style.color = new StyleColor(unit.AccentColor);

                Label languages = new(DistroPresentation.FormatLanguages(unit));
                languages.AddToClassList("collection-row-languages");

                row.Add(name);
                row.Add(languages);
                list.Add(row);
                rows.Add(row);
            }

            SelectUnit(selectedIndex);
        }

        private void SelectUnit(int index)
        {
            if (units.Count == 0)
            {
                return;
            }

            selectedIndex = Mathf.Clamp(index, 0, units.Count - 1);
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].EnableInClassList("selected", i == selectedIndex);
            }

            RenderDetail(units[selectedIndex]);
        }

        private void RenderDetail(DistroDefinition unit)
        {
            detail.Clear();

            VisualElement readout = new();
            readout.AddToClassList("collection-detail-readout");

            Label artLabel = new();
            DistroArtPresenter.ConfigureArtLabel(artLabel, monospaceFont);
            AsciiArtFitter artFitter = new(artLabel, monospaceFont);
            VisualElement artPlaceholder = DistroArtPresenter.CreatePlaceholder();
            artFitter.SetArt(DistroArtPresenter.Render(artLabel, artPlaceholder, unit));
            readout.Add(artPlaceholder);
            readout.Add(artLabel);

            VisualElement details = new();
            details.AddToClassList("collection-detail-values");

            Label name = new(DistroPresentation.DisplayName(unit));
            name.AddToClassList("collection-detail-name");
            name.style.color = new StyleColor(unit.AccentColor);
            details.Add(name);

            details.Add(BuildDetailLine("lang", DistroPresentation.FormatLanguages(unit)));
            details.Add(BuildDetailLine("passive", string.IsNullOrWhiteSpace(unit.PassiveName) ? "--" : unit.PassiveName));
            details.Add(BuildDetailLine("uptime", unit.BaseUptime.ToString()));
            details.Add(BuildDetailLine("ram", unit.BaseRam.ToString()));
            details.Add(BuildDetailLine("cycles", unit.BaseCyclesPerTurn.ToString()));

            readout.Add(details);
            detail.Add(readout);

            Label description = new(string.IsNullOrWhiteSpace(unit.Description) ? "--" : unit.Description);
            description.AddToClassList("collection-detail-description");
            detail.Add(description);
        }

        private static VisualElement BuildDetailLine(string key, string value)
        {
            VisualElement row = new();
            row.AddToClassList("kv-row");

            Label keyLabel = new(key);
            keyLabel.AddToClassList("kv-key");
            Label valueLabel = new(value);
            valueLabel.AddToClassList("kv-value");

            row.Add(keyLabel);
            row.Add(valueLabel);
            return row;
        }
    }
}
